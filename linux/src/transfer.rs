//! File transfer between servers (and the engine behind the two-pane copy window). Everything runs through the secure connections that are
//! already open: a command is run on a server with `ssh` (key only, never a password prompt) and files travel as a `tar` stream from one
//! server through this computer to the other, in blocks, so the servers never need to reach each other and no extra program is needed.
//!
//! Safety rules: root is never used (a user named root is refused, and so is any login that turns out to have user id 0), a switched
//! user is reached with `sudo -u` from the default user's key login, and an existing file is only replaced after a warning.

use crate::host;
use std::io::{Read, Write};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};

/// Block size of the copy.
pub const BLOCK: usize = 128 * 1024;

/// Another user reached with sudo from the default user's login.
#[derive(Clone, Debug, PartialEq)]
pub struct Sudo {
    pub user: String,
    /// A private folder on the server (mode 700) holding a helper that gives sudo the password; removed by `close`.
    pub askpass_dir: Option<String>,
}

/// One server, reached through its local tunnel port.
#[derive(Clone, Debug, PartialEq)]
pub struct Remote {
    pub port: u16,
    /// The default login user (the one the saved key belongs to).
    pub user: String,
    pub key: Option<String>,
    /// Where the server's identity is filed in known_hosts.
    pub host_alias: String,
    pub strict: bool,
    pub sudo: Option<Sudo>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Entry { pub name: String, pub is_dir: bool, pub size: u64, pub modified: f64 }

pub fn q(s: &str) -> String { format!("'{}'", s.replace('\'', "'\"'\"'")) }

/// Root is never used, whatever it is spelled like.
pub fn is_root_name(user: &str) -> bool { user.trim().eq_ignore_ascii_case("root") }

fn ssh(r: &Remote, remote_command: &str) -> Command {
    let mut c = host::command("ssh");
    c.args(["-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "-o", "PasswordAuthentication=no", "-o", "KbdInteractiveAuthentication=no",
            "-o", &format!("HostKeyAlias={}", r.host_alias), "-o", &format!("StrictHostKeyChecking={}", if r.strict { "yes" } else { "accept-new" }),
            "-o", "ServerAliveInterval=30", "-p", &r.port.to_string(), "-l", &r.user]);
    if let Some(k) = &r.key { c.args(["-i", k, "-o", "IdentitiesOnly=yes"]); }
    c.arg("127.0.0.1").arg(remote_command);
    c
}

/// The command as it must run on the server: as the default user, or through sudo as the switched user.
pub fn wrap(r: &Remote, cmd: &str) -> String {
    match &r.sudo {
        None => cmd.to_string(),
        Some(s) => {
            let askpass = s.askpass_dir.as_ref().map(|d| format!("SUDO_ASKPASS={} ", q(&format!("{d}/ap")))).unwrap_or_default();
            format!("{askpass}sudo {} -u {} sh -c {}", if s.askpass_dir.is_some() { "-A" } else { "-n" }, q(&s.user), q(cmd))
        }
    }
}

fn nice_error(stderr: &str) -> String {
    let low = stderr.to_lowercase();
    if low.contains("permission denied (publickey") || low.contains("no supported authentication") { return "The server refused the saved key. Open the Terminal button once so SSMConnect can check the login.".into(); }
    if low.contains("connection refused") || low.contains("timed out") || low.contains("connection closed") || low.contains("connection reset") { return "The connection to the server is not open. Wait for the green status on the dashboard.".into(); }
    if low.contains("host key verification failed") || low.contains("identification has changed") { return "The server's identity changed. Check it under File > Server identities.".into(); }
    if low.contains("a password is required") || low.contains("no tty present") || low.contains("sorry, try again") { return "sudo needs the right password for the default user. Type it in the password box and switch again.".into(); }
    if low.contains("not in the sudoers") || low.contains("may not run sudo") || low.contains("is not allowed to execute") { return "The default user is not allowed to switch to that user with sudo.".into(); }
    stderr.lines().rev().find(|l| !l.trim().is_empty()).map(|l| l.trim().to_string()).unwrap_or_else(|| "The server did not accept the command.".into())
}

/// Runs a command on the server; Ok is what it printed.
pub fn run(r: &Remote, cmd: &str) -> Result<String, String> {
    let out = ssh(r, &wrap(r, cmd)).stdin(Stdio::null()).output().map_err(|e| format!("ssh could not be started: {e}"))?;
    if out.status.success() { Ok(String::from_utf8_lossy(&out.stdout).into_owned()) } else { Err(nice_error(&String::from_utf8_lossy(&out.stderr))) }
}

/// Signs in as the default user and returns its home folder. Refuses root.
pub fn connect(r: &Remote) -> Result<String, String> {
    if is_root_name(&r.user) { return Err("File transfer never works as root. Use a normal user such as ubuntu or ec2-user.".into()); }
    let out = run(r, "id -u; printf '%s\\n' \"$HOME\"")?;
    let mut lines = out.lines();
    if lines.next().map(str::trim) == Some("0") { return Err("File transfer never works as root. This login has root rights, so it was refused.".into()); }
    Ok(lines.next().map(|h| h.trim().to_string()).filter(|h| h.starts_with('/')).unwrap_or_else(|| "/".into()))
}

/// Acts as another user through sudo from the default user's login. A password, if given, reaches sudo through a private helper on the server.
pub fn switch_user(r: &Remote, user: &str, password: Option<&str>) -> Result<(Remote, String), String> {
    if is_root_name(user) { return Err("File transfer never works as root. Type another user.".into()); }
    if !crate::validator::is_valid_login(user) { return Err("That user name has characters that are not allowed.".into()); }
    let base = Remote { sudo: None, ..r.clone() };
    connect(&base)?;
    let mut dir = None;
    if let Some(pw) = password.filter(|p| !p.is_empty()) {
        let mut c = ssh(&base, "umask 077; d=$(mktemp -d /tmp/.ssc-XXXXXX) && cat > \"$d/pw\" && printf '#!/bin/sh\\ncat \"%s/pw\"\\n' \"$d\" > \"$d/ap\" && chmod 700 \"$d/ap\" && printf '%s\\n' \"$d\"");
        let mut child = c.stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn().map_err(|e| e.to_string())?;
        child.stdin.take().ok_or("no input")?.write_all(pw.as_bytes()).map_err(|e| e.to_string())?;
        let out = child.wait_with_output().map_err(|e| e.to_string())?;
        if !out.status.success() { return Err(nice_error(&String::from_utf8_lossy(&out.stderr))); }
        dir = Some(String::from_utf8_lossy(&out.stdout).trim().to_string()).filter(|d| d.starts_with("/tmp/"));
    }
    let switched = Remote { sudo: Some(Sudo { user: user.into(), askpass_dir: dir }), ..r.clone() };
    let check = run(&switched, "id -u; printf '%s\\n' \"$HOME\"");
    match check {
        Ok(out) => {
            let mut lines = out.lines();
            if lines.next().map(str::trim) == Some("0") { close(&switched); return Err("That account has root rights, so it was refused. File transfer never works as root.".into()); }
            Ok((switched.clone(), lines.next().map(|h| h.trim().to_string()).filter(|h| h.starts_with('/')).unwrap_or_else(|| "/".into())))
        }
        Err(e) => { close(&switched); Err(e) }
    }
}

/// Removes the private password helper from the server (if there is one).
pub fn close(r: &Remote) {
    if let Some(d) = r.sudo.as_ref().and_then(|s| s.askpass_dir.as_ref()).filter(|d| d.starts_with("/tmp/.ssc-")) {
        let base = Remote { sudo: None, ..r.clone() };
        let _ = run(&base, &format!("rm -rf -- {}", q(d)));
    }
}

/// The folders and files in `dir`, folders first.
pub fn list(r: &Remote, dir: &str) -> Result<Vec<Entry>, String> {
    let out = run(r, &format!("cd -- {} && find . -mindepth 1 -maxdepth 1 -printf '%Y\\t%s\\t%T@\\t%f\\n'", q(dir)))?;
    let mut v: Vec<Entry> = out.lines().filter_map(|l| {
        let f: Vec<&str> = l.splitn(4, '\t').collect();
        (f.len() == 4 && !f[3].is_empty()).then(|| Entry { name: f[3].into(), is_dir: f[0] == "d", size: f[1].parse().unwrap_or(0), modified: f[2].parse().unwrap_or(0.0) })
    }).collect();
    v.sort_by(|a, b| b.is_dir.cmp(&a.is_dir).then(a.name.to_lowercase().cmp(&b.name.to_lowercase())));
    Ok(v)
}

/// Which of `names` already exist in `dir`.
pub fn existing(r: &Remote, dir: &str, names: &[String]) -> Result<Vec<String>, String> {
    let tests: String = names.iter().map(|n| format!("[ -e {} ] && printf '%s\\n' {}; ", q(&format!("{dir}/{n}")), q(n))).collect();
    Ok(run(r, &format!("{tests}true"))?.lines().map(String::from).collect())
}

/// The folder above `dir` ("/" stays "/").
pub fn parent(dir: &str) -> String {
    let t = dir.trim_end_matches('/');
    match t.rfind('/') { Some(0) | None => "/".into(), Some(i) => t[..i].to_string() }
}

pub fn join(dir: &str, name: &str) -> String { if dir.ends_with('/') { format!("{dir}{name}") } else { format!("{dir}/{name}") } }

/// Bytes in the chosen files and folders (for the progress bar).
pub fn total_size(r: &Remote, dir: &str, names: &[String]) -> u64 {
    let list: String = names.iter().map(|n| q(n)).collect::<Vec<_>>().join(" ");
    run(r, &format!("cd -- {} && du -sb -- {list} | awk '{{s+=$1}} END{{print s+0}}'", q(dir))).ok().and_then(|o| o.trim().parse().ok()).unwrap_or(0)
}

/// Copies `names` from `src_dir` on one server to `dst_dir` on another, through this computer in 128 KB blocks. `progress(done, total)` is
/// called as data moves; setting `cancel` stops the copy. Existing files are replaced, so ask the person first (see `existing`).
pub fn copy(src: &Remote, src_dir: &str, names: &[String], dst: &Remote, dst_dir: &str, progress: &dyn Fn(u64, u64), cancel: &AtomicBool) -> Result<(), String> {
    if names.is_empty() { return Ok(()); }
    let total = total_size(src, src_dir, names);
    let list: String = names.iter().map(|n| q(n)).collect::<Vec<_>>().join(" ");
    let mut from = ssh(src, &wrap(src, &format!("tar -C {} -cf - -- {list}", q(src_dir)))).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn().map_err(|e| e.to_string())?;
    let mut to = match ssh(dst, &wrap(dst, &format!("tar -C {} -xf -", q(dst_dir)))).stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::piped()).spawn() {
        Ok(c) => c,
        Err(e) => { let _ = from.kill(); let _ = from.wait(); return Err(e.to_string()); }
    };
    let errs = |c: &mut std::process::Child| { let mut e = c.stderr.take(); std::thread::spawn(move || { let mut s = String::new(); if let Some(x) = e.as_mut() { let _ = x.read_to_string(&mut s); } s }) };
    let (src_err, dst_err) = (errs(&mut from), errs(&mut to));
    let (mut out, mut inp) = (from.stdout.take().ok_or("no output")?, to.stdin.take().ok_or("no input")?);
    let (mut buf, mut done, mut stopped, mut write_failed) = (vec![0u8; BLOCK], 0u64, false, false);
    progress(0, total);
    loop {
        if cancel.load(Ordering::Relaxed) { stopped = true; break; }
        let n = match out.read(&mut buf) { Ok(0) | Err(_) => break, Ok(n) => n };
        if inp.write_all(&buf[..n]).is_err() { write_failed = true; break; }
        done += n as u64;
        progress(done.min(total.max(done)), total.max(done));
    }
    drop(inp);
    if stopped || write_failed { let _ = from.kill(); }
    if stopped { let _ = to.kill(); }
    let (a, b) = (from.wait().map(|s| s.success()).unwrap_or(false), to.wait().map(|s| s.success()).unwrap_or(false));
    let (ea, eb) = (src_err.join().unwrap_or_default(), dst_err.join().unwrap_or_default());
    if stopped { return Err("Stopped.".into()); }
    if !a { return Err(format!("Reading from the first server failed: {}", nice_error(&ea))); }
    if !b { return Err(format!("Writing to the second server failed: {}", nice_error(&eb))); }
    progress(done, done.max(total));
    Ok(())
}
