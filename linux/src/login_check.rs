//! Before a terminal or the file manager opens, checks that the server accepts the login: finds the key that belongs to the server,
//! tries the login users that make sense for it, and saves what works. A green status only means the secure tunnel is up; without this
//! check a wrong user or key shows up later as "Permission denied" in a window that closes by itself.

use crate::models::{AppConfig, DiscoveredServer, TunnelConfig};
use crate::{applog, config_store, discovery, host, key_finder, ssh_config};
use std::process::Stdio;
use std::sync::Mutex;

#[derive(Clone, Debug, PartialEq)]
pub enum Outcome {
    /// This user (and key, when one is needed) is accepted; the settings were saved.
    Ready { user: String, key: Option<String> },
    /// No key on this computer fits; the person has to pick one.
    NeedKey,
    /// The server did not answer, or its identity changed: not a login problem. The text is plain language.
    Down(String),
}

#[derive(Clone, Debug, PartialEq)]
pub enum Probe { Accepted, Refused, Down(String) }

/// Combinations the server already refused in this run, so a second click does not guess them again.
static REFUSED: Mutex<Vec<(String, String, String)>> = Mutex::new(Vec::new());

/// The login users to try, best guess first. When the operating system is known only its own user is tried (plus the saved one).
pub fn candidate_users(saved: &str, os_name: &str) -> Vec<String> {
    let known = DiscoveredServer { os_name: os_name.into(), ..Default::default() };
    let mut v: Vec<String> = vec![];
    let mut add = |u: &str| if !u.is_empty() && !v.iter().any(|x| x == u) { v.push(u.to_string()); };
    add(saved);
    let n = os_name.to_lowercase();
    let recognised = ["ubuntu", "debian", "centos", "fedora", "rocky", "amazon", "red hat", "rhel", "suse", "alma", "oracle"].iter().any(|k| n.contains(k));
    add(known.suggested_user());
    if !recognised { for u in ["ec2-user", "ubuntu", "admin", "centos", "fedora", "rocky", "opc"] { add(u); } }
    v
}

/// What ssh's error text means for the person.
pub fn classify(stderr: &str) -> Probe {
    let low = stderr.to_lowercase();
    if low.contains("remote host identification has changed") || (low.contains("host key") && low.contains("verification failed")) {
        return Probe::Down("The server's identity changed since last time. If you rebuilt the server, forget it under File > Server identities.".into());
    }
    if low.contains("permission denied") || low.contains("too many authentication failures") || low.contains("no supported authentication") { return Probe::Refused; }
    if low.contains("connection refused") || low.contains("closed by remote") || low.contains("connection reset") || low.contains("timed out") || low.contains("kex_exchange") || low.contains("banner exchange") {
        return Probe::Down("The server did not answer through the secure connection. Wait for the green status, or press Restart.".into());
    }
    Probe::Down(format!("The login could not be checked: {}", stderr.lines().last().unwrap_or("no answer").trim()))
}

/// One try: log in as `user` with `key` and run `true`. Never asks for a password.
pub fn probe(t: &TunnelConfig, alias_for_host_key: &str, strict: bool, user: &str, key: Option<&str>) -> Probe {
    let mut c = host::command("ssh");
    c.args(["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "PasswordAuthentication=no", "-o", "KbdInteractiveAuthentication=no", "-o", "NumberOfPasswordPrompts=0"])
        .args(["-o", &format!("HostKeyAlias={alias_for_host_key}"), "-o", &format!("StrictHostKeyChecking={}", if strict { "yes" } else { "accept-new" })])
        .args(["-p", &t.local_port.to_string(), "-l", user]);
    if let Some(k) = key { c.args(["-i", k, "-o", "IdentitiesOnly=yes"]); }
    c.arg("127.0.0.1").arg("true").stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::piped());
    match c.output() {
        Ok(o) if o.status.success() => Probe::Accepted,
        Ok(o) => classify(&String::from_utf8_lossy(&o.stderr)),
        Err(e) => Probe::Down(format!("ssh could not be started: {e}")),
    }
}

/// The key file for this server: the saved one while it still exists, else the one on this computer that belongs to it
/// (matched by the key pair's fingerprint or name from the last scan).
pub fn resolve_key(t: &TunnelConfig) -> Option<String> {
    if let Some(k) = t.key_file.as_deref().filter(|k| !k.is_empty() && std::path::Path::new(k).is_file()) { return Some(k.to_string()); }
    let server = discovery::load().scans.into_iter().filter(|r| r.profile.eq_ignore_ascii_case(&t.profile))
        .flat_map(|r| r.servers).find(|s| s.instance_id == t.instance_id)?;
    if server.key_name.is_empty() && server.key_fingerprint.is_empty() { return None; }
    let keys = key_finder::scan_standard(8, &|| false);
    key_finder::find(&keys, &server.key_name, &server.key_fingerprint).0.map(|k| k.path.clone())
}

/// Checks (and, when needed, repairs) the login for one SSH server. Takes a few seconds: call it off the main thread.
pub fn ensure(t: &TunnelConfig) -> Outcome {
    let cfg = config_store::load().unwrap_or_default();
    let alias = ssh_config::host_key_alias(t, &cfg.tunnels);
    let key = resolve_key(t);
    let os = discovery::load().scans.into_iter().filter(|r| r.profile.eq_ignore_ascii_case(&t.profile)).flat_map(|r| r.servers)
        .find(|s| s.instance_id == t.instance_id).map(|s| s.os_name).filter(|n| !n.is_empty()).unwrap_or_else(|| t.os_name.clone());
    let key_id = key.clone().unwrap_or_default();
    for user in candidate_users(&t.user_name, &os) {
        if REFUSED.lock().map(|r| r.iter().any(|(i, u, k)| *i == t.instance_id && *u == user && *k == key_id)).unwrap_or(false) { continue; }
        match probe(t, &alias, cfg.strict_host_keys, &user, key.as_deref()) {
            Probe::Accepted => {
                applog::info(&format!("Login check: {} accepts user '{user}'.", t.name));
                if user != t.user_name || key != t.key_file { save(t, &user, key.as_deref()); }
                return Outcome::Ready { user, key };
            }
            Probe::Refused => { if let Ok(mut r) = REFUSED.lock() { r.push((t.instance_id.clone(), user.clone(), key_id.clone())); } }
            Probe::Down(why) => { applog::warn(&format!("Login check for {}: {why}", t.name)); return Outcome::Down(why); }
        }
    }
    Outcome::NeedKey
}

/// Keeps the working user and key, and rewrites the ssh entries. Other settings changed meanwhile are kept (the config is read again).
pub fn save(t: &TunnelConfig, user: &str, key: Option<&str>) {
    let mut cfg: AppConfig = config_store::load().unwrap_or_default();
    if let Some(x) = cfg.tunnels.iter_mut().find(|x| x.instance_id == t.instance_id && x.profile == t.profile) {
        x.user_name = user.into();
        if key.is_some() { x.key_file = key.map(String::from); }
    }
    if config_store::save(&cfg).is_ok() { let _ = ssh_config::sync(&cfg); }
}

/// The person picked a key file: forget earlier refusals for this server so the new key is tried.
pub fn forget_refusals(instance_id: &str) { if let Ok(mut r) = REFUSED.lock() { r.retain(|(i, _, _)| i != instance_id); } }


