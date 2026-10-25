//! Thin, safe wrapper around the AWS CLI. Every value is validated elsewhere and the CLI is always started with an
//! argument list, never a command string.

use crate::aws_profiles::COMMON_REGIONS;
use crate::models::{DiscoveredServer, TunnelConfig};
use crate::{classifier, paths, util};
use serde_json::Value;
use std::io::Read;
use std::os::unix::fs::PermissionsExt;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

#[derive(Clone, Debug, Default)]
pub struct CliResult { pub exit_code: i32, pub output: String, pub error: String }

impl CliResult { pub fn ok(&self) -> bool { self.exit_code == 0 } }

fn search_dirs() -> Vec<PathBuf> {
    let mut v = vec![paths::data_dir().join("bin"), paths::home().join(".local/bin")];
    v.extend(["/usr/local/aws-cli/v2/current/bin", "/usr/local/sessionmanagerplugin/bin", "/usr/local/bin"].map(PathBuf::from));
    if let Some(p) = std::env::var_os("PATH") { v.extend(std::env::split_paths(&p)); }
    v
}

pub fn search_path() -> std::ffi::OsString { std::env::join_paths(search_dirs()).unwrap_or_default() }

pub fn is_executable(p: &std::path::Path) -> bool {
    p.is_file() && std::fs::metadata(p).map(|m| m.permissions().mode() & 0o111 != 0).unwrap_or(false)
}

/// First executable called `name` in the usual places (the user's own tools first), or None.
pub fn which(name: &str) -> Option<PathBuf> { search_dirs().into_iter().map(|d| d.join(name)).find(|p| is_executable(p)) }

pub fn find_aws() -> Option<PathBuf> { which("aws") }
pub fn find_plugin() -> Option<PathBuf> { which("session-manager-plugin") }

pub fn apply_env(cmd: &mut Command) { apply_env_for(cmd, "", None) }

/// What to do to the environment of an AWS CLI call so that settings left behind by a local AWS emulator (floci, LocalStack...)
/// cannot redirect the sign-in or the connections to a local address. Returns (variables to remove, variables to set).
pub fn env_plan(env: &[(String, String)], profile: &str, session: Option<&str>) -> (Vec<String>, Vec<(String, String)>) {
    let (mut remove, mut set) = (vec![], vec![("AWS_IGNORE_CONFIGURED_ENDPOINT_URLS".to_string(), "true".to_string())]);   // also endpoint_url in the AWS files
    remove.push("AWS_PROFILE".into());   // the profile is always passed explicitly
    for (k, _) in env {
        let up = k.to_ascii_uppercase();
        let loose = ["AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_SECURITY_TOKEN", "AWS_DEFAULT_PROFILE"];
        if up.starts_with("AWS_ENDPOINT_URL") || (!profile.trim().is_empty() && loose.contains(&up.as_str())) { remove.push(k.clone()); }
    }
    let standard = paths::aws_dir();
    for (var, file, creds) in [("AWS_CONFIG_FILE", "config", false), ("AWS_SHARED_CREDENTIALS_FILE", "credentials", true)] {
        let Some((_, custom)) = env.iter().find(|(k, v)| k == var && !v.is_empty()) else { continue };
        let standard_file = standard.join(file);
        if std::path::Path::new(custom) == standard_file { continue; }
        if !std::path::Path::new(custom).is_file() { remove.push(var.into()); continue; }   // a leftover value naming a file that is gone
        let mut headers: Vec<String> = vec![];
        if !profile.trim().is_empty() {
            if creds { headers.push(profile.into()); } else { headers.push(format!("profile {profile}")); if profile == "default" { headers.push("default".into()); } }
        }
        if let (false, Some(sess)) = (creds, session) { headers.push(format!("sso-session {sess}")); }
        if headers.is_empty() { continue; }
        let has = |path: &std::path::Path| std::fs::read_to_string(path).map(|t| t.lines().any(|l| {
            let l = l.trim();
            l.starts_with('[') && l.ends_with(']') && headers.iter().any(|h| h == l[1..l.len() - 1].trim())
        })).unwrap_or(false);
        if !has(std::path::Path::new(custom)) && has(&standard_file) { set.push((var.into(), standard_file.to_string_lossy().into_owned())); }
    }
    (remove, set)
}

pub fn apply_env_for(cmd: &mut Command, profile: &str, session: Option<&str>) {
    cmd.env("PATH", search_path()).env("AWS_PAGER", "");
    let env: Vec<(String, String)> = std::env::vars().collect();
    let (remove, set) = env_plan(&env, profile, session);
    let mut ignored: Vec<&String> = remove.iter().filter(|k| env.iter().any(|(e, _)| e == *k) && *k != "AWS_PROFILE").collect();
    ignored.sort();
    for k in &remove { cmd.env_remove(k); }
    for (k, v) in &set { cmd.env(k, v); }
    if !ignored.is_empty() && !LOGGED.swap(true, std::sync::atomic::Ordering::Relaxed) {
        crate::applog::warn(&format!("Ignoring AWS settings found in the environment so they cannot redirect the sign-in: {}.", ignored.iter().map(|s| s.as_str()).collect::<Vec<_>>().join(", ")));
    }
}

static LOGGED: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

pub fn build_args(args: &[&str], profile: &str, region: &str) -> Result<Vec<String>, String> {
    let aws = find_aws().ok_or("AWS CLI is not installed.")?;
    let mut v = vec![aws.to_string_lossy().into_owned()];
    v.extend(args.iter().map(|s| s.to_string()));
    if !profile.trim().is_empty() { v.push("--profile".into()); v.push(profile.into()); }
    if !region.trim().is_empty() { v.push("--region".into()); v.push(region.into()); }
    Ok(v)
}

pub fn run(args: &[&str], profile: &str, region: &str, timeout_s: u64) -> CliResult {
    let cmdline = match build_args(args, profile, region) { Ok(c) => c, Err(e) => return CliResult { exit_code: -1, output: String::new(), error: e } };
    let mut cmd = Command::new(&cmdline[0]);
    cmd.args(&cmdline[1..]).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped());
    let session = args.iter().position(|a| *a == "--sso-session").and_then(|i| args.get(i + 1)).copied();
    apply_env_for(&mut cmd, profile, session);
    let mut child = match cmd.spawn() { Ok(c) => c, Err(e) => return CliResult { exit_code: -1, output: String::new(), error: e.to_string() } };
    let (mut so, mut se) = (child.stdout.take().unwrap(), child.stderr.take().unwrap());
    let t_out = std::thread::spawn(move || { let mut b = Vec::new(); let _ = so.read_to_end(&mut b); b });
    let t_err = std::thread::spawn(move || { let mut b = Vec::new(); let _ = se.read_to_end(&mut b); b });
    let start = Instant::now();
    let status = loop {
        match child.try_wait() {
            Ok(Some(s)) => break Some(s),
            Ok(None) if start.elapsed() > Duration::from_secs(timeout_s) => { let _ = child.kill(); let _ = child.wait(); break None; }
            Ok(None) => std::thread::sleep(Duration::from_millis(40)),
            Err(_) => break None,
        }
    };
    let out = String::from_utf8_lossy(&t_out.join().unwrap_or_default()).trim().to_string();
    let err = String::from_utf8_lossy(&t_err.join().unwrap_or_default()).trim().to_string();
    match status {
        Some(s) => CliResult { exit_code: s.code().unwrap_or(-1), output: out, error: err },
        None => CliResult { exit_code: -1, output: out, error: "Timed out waiting for the AWS CLI.".into() },
    }
}

/// Opens the browser sign-in for an SSO profile.
pub fn sso_login(profile: &str) -> CliResult { sign_in(&["sso", "login"], profile, "") }

/// True for an address a sign-in may open: https, on an AWS sign-in host or the company's own sign-in address.
pub fn is_sign_in_link(url: &str, company_host: Option<&str>) -> bool {
    let Some(rest) = url.strip_prefix("https://") else { return false };
    let host = rest.split(['/', '?', '#', ':']).next().unwrap_or("").to_ascii_lowercase();
    if host.is_empty() || url.chars().any(|c| c.is_whitespace() || c.is_control()) { return false; }
    let on = |d: &str| host == d || host.ends_with(&format!(".{d}"));
    ["amazonaws.com", "awsapps.com", "amazon.com", "aws.amazon.com", "amazonaws.com.cn", "aws"].iter().any(|d| on(d))
        || company_host.is_some_and(|c| !c.is_empty() && host == c.to_ascii_lowercase())
}

fn first_link(text: &str) -> Option<String> { text.split_whitespace().find(|w| w.starts_with("https://")).map(String::from) }

/// The browser sign-in: the AWS CLI is asked for the sign-in address (`--no-browser`) and the page is opened in the person's own
/// browser as a new tab, so a sign-in they already have is reused. If the CLI gives no address within a few seconds, it is run the
/// standard way and opens the browser itself.
pub fn sign_in(args: &[&str], profile: &str, session: &str) -> CliResult {
    let mut a: Vec<&str> = args.to_vec();
    a.push("--no-browser");
    let Ok(cmdline) = build_args(&a, profile, "") else { return run(args, profile, "", 180) };
    let company_host = if session.is_empty() { None } else { crate::aws_profiles::session_info(session).0
        .and_then(|u| u.strip_prefix("https://").map(|r| r.split(['/', ':']).next().unwrap_or("").to_string())) };
    let mut cmd = Command::new(&cmdline[0]);
    cmd.args(&cmdline[1..]).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped());
    apply_env_for(&mut cmd, profile, if session.is_empty() { None } else { Some(session) });
    let Ok(mut child) = cmd.spawn() else { return run(args, profile, "", 180) };
    crate::applog::info(&format!("Sign-in started{}.", if session.is_empty() { String::new() } else { format!(" for session '{session}'") }));
    let (tx, rx) = std::sync::mpsc::channel::<(bool, String)>();
    for (is_err, pipe) in [(false, child.stdout.take().map(|p| Box::new(p) as Box<dyn Read + Send>)), (true, child.stderr.take().map(|p| Box::new(p) as Box<dyn Read + Send>))] {
        let (tx, Some(mut pipe)) = (tx.clone(), pipe) else { continue };
        std::thread::spawn(move || {
            let mut buf = [0u8; 1024];
            while let Ok(n) = pipe.read(&mut buf) { if n == 0 { break; } let _ = tx.send((is_err, String::from_utf8_lossy(&buf[..n]).into_owned())); }
        });
    }
    drop(tx);
    let (mut out, mut err) = (String::new(), String::new());
    let (start, mut opened) = (Instant::now(), false);
    let status = loop {
        while let Ok((is_err, chunk)) = rx.try_recv() { if is_err { err.push_str(&chunk) } else { out.push_str(&chunk) } }
        if !opened {
            if let Some(link) = first_link(&out).or_else(|| first_link(&err)) {
                if is_sign_in_link(&link, company_host.as_deref()) { crate::launchers::open_url(&link); crate::applog::info("Sign-in page opened in the browser."); }
                else { crate::applog::warn("The AWS CLI gave a sign-in address that is not an AWS or company address; it was not opened."); let _ = child.kill(); let _ = child.wait();
                       return CliResult { exit_code: -1, output: String::new(), error: "The sign-in address did not look like an AWS sign-in page, so it was not opened.".into() }; }
                opened = true;
            }
        }
        match child.try_wait() {
            Ok(Some(s)) => break Some(s),
            Ok(None) if !opened && start.elapsed() > Duration::from_secs(10) => {
                crate::applog::warn("The AWS CLI gave no sign-in address; letting it open the browser itself.");
                let _ = child.kill(); let _ = child.wait();
                return run(args, profile, "", 180);
            }
            Ok(None) if start.elapsed() > Duration::from_secs(180) => { let _ = child.kill(); let _ = child.wait(); break None; }
            Ok(None) => std::thread::sleep(Duration::from_millis(100)),
            Err(_) => break None,
        }
    };
    std::thread::sleep(Duration::from_millis(60));
    while let Ok((is_err, chunk)) = rx.try_recv() { if is_err { err.push_str(&chunk) } else { out.push_str(&chunk) } }
    match status {
        Some(s) => CliResult { exit_code: s.code().unwrap_or(-1), output: out.trim().into(), error: err.trim().into() },
        None => CliResult { exit_code: -1, output: out.trim().into(), error: "Timed out waiting for the sign-in.".into() },
    }
}

/// Checks the sign-in. STS needs some region, so a neutral one is used when none is chosen yet.
pub fn who_am_i(profile: &str, region: &str) -> CliResult {
    run(&["sts", "get-caller-identity", "--output", "json"], profile, if region.is_empty() { "us-east-1" } else { region }, 30)
}

/// Region names this account can use; falls back to the common list when AWS cannot be asked.
pub fn list_regions(profile: &str) -> Vec<String> {
    let r = run(&["ec2", "describe-regions", "--query", "Regions[].RegionName", "--output", "json"], profile, "us-east-1", 30);
    if r.ok() {
        if let Ok(Value::Array(a)) = serde_json::from_str::<Value>(&r.output) {
            let mut names: Vec<String> = a.iter().filter_map(|x| x.as_str().map(String::from)).filter(|s| !s.is_empty()).collect();
            names.sort();
            if !names.is_empty() { return names; }
        }
    }
    let mut v: Vec<String> = COMMON_REGIONS.iter().map(|s| s.to_string()).collect();
    v.sort();
    v
}

fn cell(row: &Value, i: usize) -> String { row.get(i).and_then(Value::as_str).unwrap_or("").to_string() }
fn json_rows(s: &str) -> Vec<Value> { match serde_json::from_str::<Value>(if s.is_empty() { "[]" } else { s }) { Ok(Value::Array(a)) => a, _ => vec![] } }

/// Lists running EC2 instances and marks which ones SSM can actually reach. The error is a plain message.
pub fn discover(profile: &str, region: &str) -> Result<Vec<DiscoveredServer>, String> {
    let ssm = run(&["ssm", "describe-instance-information", "--query", "InstanceInformationList[].[InstanceId,PingStatus,PlatformType,PlatformName]", "--output", "json"], profile, region, 60);
    if !ssm.ok() { return Err(friendly_error(&ssm.error, None)); }
    let ec2 = run(&["ec2", "describe-instances", "--filters", "Name=instance-state-name,Values=running", "--query",
        "Reservations[].Instances[].[InstanceId,PrivateIpAddress,Tags[?Key=='Name']|[0].Value,KeyName,Tags]", "--output", "json"], profile, region, 60);
    if !ec2.ok() { return Err(friendly_error(&ec2.error, None)); }

    // optional: without ec2:DescribeKeyPairs, key files are matched by name
    let mut prints = std::collections::HashMap::new();
    let kp = run(&["ec2", "describe-key-pairs", "--query", "KeyPairs[].[KeyName,KeyFingerprint]", "--output", "json"], profile, region, 30);
    if kp.ok() { for row in json_rows(&kp.output) { prints.insert(cell(&row, 0), cell(&row, 1)); } }

    let managed: std::collections::HashMap<String, (bool, String, String)> = json_rows(&ssm.output).iter()
        .map(|r| (cell(r, 0), (cell(r, 1) == "Online", cell(r, 2), cell(r, 3)))).collect();
    let mut result = vec![];
    for row in json_rows(&ec2.output) {
        let iid = cell(&row, 0);
        let (online, plat, os_name) = managed.get(&iid).cloned().unwrap_or_default();
        let name = { let n = cell(&row, 2); if n.trim().is_empty() { iid.clone() } else { n.trim().to_string() } };
        let tags: Vec<(String, String)> = row.get(4).and_then(Value::as_array).map(|a| a.iter().filter_map(|t| {
            let k = t.get("Key")?.as_str()?.to_string();
            Some((k, t.get("Value").and_then(Value::as_str).unwrap_or("").to_string()))
        }).collect()).unwrap_or_default();
        let key_name = cell(&row, 3);
        result.push(DiscoveredServer {
            environment: classifier::classify(&tags, &name), name, profile: profile.into(), private_ip: cell(&row, 1),
            platform: if plat.is_empty() { "Linux".into() } else { plat }, key_fingerprint: prints.get(&key_name).cloned().unwrap_or_default(),
            key_name, ssm_online: online, os_name, instance_id: iid,
        });
    }
    result.sort_by_key(|s| (!s.ssm_online, s.name.to_lowercase()));
    Ok(result)
}

/// Turns raw AWS CLI / Session Manager errors into a plain-language status a developer can act on.
pub fn friendly_error(raw: &str, local_port: Option<u16>) -> String {
    let low = raw.to_lowercase();
    let has = |xs: &[&str]| xs.iter().any(|x| low.contains(&x.to_lowercase()));
    if has(&["TargetNotConnected"]) {
        return "Server is offline in SSM: the SSM agent is not running or the server has no SSM instance role. Check it in AWS Systems Manager > Fleet Manager.".into();
    }
    if has(&["InvalidInstanceId"]) || (has(&["is not valid"]) && has(&["instance"])) {
        return "That server ID was not found in this AWS account and region. Check the region, or run setup again.".into();
    }
    if has(&["RegisterClient"]) || (has(&["InvalidRequestException"]) && has(&["sso"])) {
        return "AWS did not accept the company sign-in URL or its region. Check the URL (like https://yourcompany.awsapps.com/start) and the region of the sign-in page with your AWS admin.".into();
    }
    if has(&["Token has expired", "ExpiredToken", "sso login", "SSO session", "SSO token", "The security token included in the request is expired"]) {
        return "Your AWS sign-in has expired. Sign in again: AWS > Sign in again in the dashboard.".into();
    }
    if has(&["Unable to locate credentials", "NoCredentialProviders"]) {
        return "No AWS credentials found for this account. Use AWS > Sign in again, or add the account with AWS > Add AWS account.".into();
    }
    if has(&["could not be found"]) && has(&["profile"]) {
        return "That AWS account profile does not exist on this computer. Open AWS > Add AWS account and add it again.".into();
    }
    if has(&["ssm:StartSession"]) || (has(&["not authorized to perform"]) && has(&["ssm"])) {
        return "Your AWS role is not allowed to start SSM sessions (needs ssm:StartSession on this server). Ask your AWS admin.".into();
    }
    if has(&["UnauthorizedOperation", "AccessDenied"]) {
        return "Your AWS role does not have permission for this action. Ask your AWS admin (needs ec2:DescribeInstances, ssm:DescribeInstanceInformation, ssm:StartSession).".into();
    }
    if has(&["SessionManagerPlugin is not found"]) || (has(&["session-manager-plugin"]) && has(&["not found", "no such file"])) {
        return "The Session Manager plugin is missing. Click Add / change servers to install it.".into();
    }
    if has(&["address already in use", "bind: address"]) {
        let w = local_port.map(|p| format!("Local port {p}")).unwrap_or_else(|| "The local port".into());
        return format!("{w} is already used by another program. Run setup again to pick a free port.");
    }
    if has(&["Could not connect to the endpoint", "Connect timeout", "Name or service not known", "getaddrinfo", "Temporary failure in name resolution"]) {
        return "Cannot reach AWS. Check your internet connection, VPN or proxy, and the region name.".into();
    }
    if has(&["ThrottlingException", "Rate exceeded"]) { return "AWS is rate-limiting requests. SSMConnect will retry automatically.".into(); }
    let first = util::first_line(raw);
    if first.is_empty() { "AWS CLI returned an unknown error.".into() } else { first.to_string() }
}

/// Decrypts a Windows server's Administrator password with the key pair's .pem file (AWS stores it encrypted).
pub fn get_windows_password(t: &TunnelConfig, pem_file: &str) -> Result<String, String> {
    let r = run(&["ec2", "get-password-data", "--instance-id", &t.instance_id, "--priv-launch-key", pem_file, "--query", "PasswordData", "--output", "text"],
                &t.profile, &t.region, 60);
    if !r.ok() { return Err(friendly_error(&r.error, None)); }
    let pw = r.output.trim().to_string();
    if pw.is_empty() || pw == "None" {
        return Err("AWS has no generated password for this server (it may use a domain login or a password you set). Use the password you know.".into());
    }
    Ok(pw)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn friendly_errors() {
        assert!(friendly_error("An error occurred (TargetNotConnected)", None).contains("offline in SSM"));
        assert!(friendly_error("Token has expired and refresh failed", None).contains("expired"));
        assert!(friendly_error("bind: address already in use", Some(2201)).contains("2201"));
    }
}
