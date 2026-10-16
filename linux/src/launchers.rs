//! Opens the tools a person connects with: a terminal running ssh, the file manager over sftp://, a Remote Desktop client,
//! plus detection of what is installed so the dashboard only offers what works.

use crate::aws_cli::which;
use crate::models::TunnelConfig;
use crate::applog;
use std::os::unix::process::CommandExt;
use std::process::{Command, Stdio};

// (executable, arguments placed before the command to run). The first one found is used.
const TERMINALS: [(&str, &[&str]); 15] = [
    ("ptyxis", &["--new-window", "--"]), ("kgx", &["--"]), ("gnome-terminal", &["--"]), ("konsole", &["-e"]), ("xfce4-terminal", &["-x"]),
    ("mate-terminal", &["-x"]), ("lxterminal", &["-e"]), ("tilix", &["-e"]), ("kitty", &[]), ("alacritty", &["-e"]), ("wezterm", &["start", "--"]),
    ("foot", &[]), ("terminator", &["-x"]), ("xterm", &["-e"]), ("x-terminal-emulator", &["-e"]),
];
const RDP_CLIENTS: [&str; 5] = ["xfreerdp3", "xfreerdp", "sdl-freerdp3", "sdl-freerdp", "remmina"];

pub fn find_terminal() -> Option<(String, Vec<String>)> {
    if let Ok(pref) = std::env::var("TERMINAL") {
        if let Some(exe) = pref.split_whitespace().next().filter(|e| which(e).is_some()) {
            let base = std::path::Path::new(exe).file_name().map(|b| b.to_string_lossy().into_owned()).unwrap_or_default();
            let args = TERMINALS.iter().find(|(n, _)| *n == base).map(|(_, a)| a.to_vec()).unwrap_or_else(|| vec!["-e"]);
            return Some((exe.to_string(), args.into_iter().map(String::from).collect()));
        }
    }
    TERMINALS.iter().find(|(n, _)| which(n).is_some()).map(|(n, a)| (n.to_string(), a.iter().map(|s| s.to_string()).collect()))
}

pub fn find_rdp_client() -> Option<&'static str> { RDP_CLIENTS.iter().copied().find(|c| which(c).is_some()) }

#[derive(Clone, Debug, Default)]
pub struct Tools { pub ssh: Option<String>, pub terminal: Option<String>, pub rdp: Option<String>, pub files: Option<String> }

pub fn tools_summary() -> Tools {
    let p = |n: &str| which(n).map(|p| p.to_string_lossy().into_owned());
    Tools { ssh: p("ssh"), terminal: find_terminal().map(|t| t.0), rdp: find_rdp_client().map(String::from), files: p("xdg-open").or_else(|| p("gio")) }
}

fn spawn(cmd: &mut Command) -> Result<(), String> {
    cmd.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null()).process_group(0).spawn().map(|_| ()).map_err(|e| e.to_string())
}

fn sh_quote(s: &str) -> String { format!("'{}'", s.replace('\'', "'\"'\"'")) }

/// Runs `ssh <alias>` in the person's terminal. The terminal stays open if the connection fails, so errors can be read.
pub fn open_terminal(alias: &str) -> Result<(), String> { open_in_terminal("ssh", alias) }

/// Runs the plain `sftp <alias>` client in a terminal: works everywhere OpenSSH does, whatever file manager is installed.
pub fn open_sftp_terminal(alias: &str) -> Result<(), String> { open_in_terminal("sftp", alias) }

fn open_in_terminal(program: &str, alias: &str) -> Result<(), String> {
    let (exe, args) = find_terminal().ok_or("No terminal program was found. Install one (for example GNOME Terminal), or use 'Copy ssh command'.")?;
    let script = format!("{program} {}; status=$?; if [ $status -ne 0 ]; then echo; read -r -p '{program} ended with an error. Press Enter to close.' _; fi", sh_quote(alias));
    applog::info(&format!("Opening terminal: {exe} {} bash -c ({program} {alias})", args.join(" ")));
    spawn(Command::new(&exe).args(&args).arg("bash").arg("-c").arg(script))
}

/// Runs a command to the end (at most `secs`), returning (success, stderr text).
fn run_limited(cmd: &mut Command, secs: u64) -> (bool, String) {
    use std::io::Read;
    let Ok(mut child) = cmd.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::piped()).spawn() else { return (false, "could not start".into()) };
    let mut err = child.stderr.take().unwrap();
    let reader = std::thread::spawn(move || { let mut s = String::new(); let _ = err.read_to_string(&mut s); s });
    let start = std::time::Instant::now();
    let ok = loop {
        match child.try_wait() {
            Ok(Some(st)) => break st.success(),
            Ok(None) if start.elapsed().as_secs() >= secs => { let _ = child.kill(); let _ = child.wait(); break false; }
            Ok(None) => std::thread::sleep(std::time::Duration::from_millis(50)),
            Err(_) => break false,
        }
    };
    (ok, reader.join().unwrap_or_default())
}

/// A plain-language reason from what `gio mount` printed, or None when the problem is "this computer cannot do SFTP in the file manager".
pub fn sftp_problem(stderr: &str) -> Option<String> {
    let low = stderr.to_lowercase();
    if low.contains("permission denied") || low.contains("authentication") {
        return Some("The server refused the login. Check the login user and the key file (Add / change servers).".into());
    }
    if low.contains("connection refused") || low.contains("connection closed") || low.contains("timed out") || low.contains("connection reset") {
        return Some("The connection to the server is not open. Wait for the green status, or press Restart.".into());
    }
    if low.contains("host key") && low.contains("verification failed") || low.contains("remote host identification has changed") {
        return Some("The server's identity changed since last time (its host key). If you rebuilt the server, remove its old entry from ~/.ssh/known_hosts.".into());
    }
    None
}

/// Opens the server's files for exchange over SFTP. Takes a few seconds (it logs in first), so call it off the main thread.
/// Order: mount it in the desktop (GNOME / Cinnamon / MATE / Xfce through GVFS) and open it in the file manager; otherwise the file manager's
/// own SFTP (Dolphin, Thunar, Nemo, Caja, Nautilus); otherwise the `sftp` client in a terminal.
pub fn open_files(user: &str, alias: &str) -> Result<(), String> {
    let url = format!("sftp://{user}@{alias}/");
    let kde = std::env::var("XDG_CURRENT_DESKTOP").is_ok_and(|d| d.to_uppercase().contains("KDE"));
    if kde && which("dolphin").is_some() { return spawn(Command::new("dolphin").arg(&url)); }   // KIO speaks SFTP itself
    if which("gio").is_some() {
        // `gio open sftp://...` alone does nothing ("not mounted"), so log in first, then open the mounted folder
        let (ok, err) = run_limited(Command::new("gio").args(["mount", &url]), 60);
        if !ok && !err.to_lowercase().contains("already mounted") {
            applog::warn(&format!("gio mount {url}: {err}"));
            if let Some(p) = sftp_problem(&err) { return Err(p); }
        } else {
            let (opened, err2) = run_limited(Command::new("gio").args(["open", &url]), 20);
            if opened { return Ok(()); }
            applog::warn(&format!("gio open {url}: {err2}"));
        }
    }
    for fm in ["dolphin", "thunar", "nemo", "caja", "nautilus", "pcmanfm"] {
        if which(fm).is_some() { return spawn(Command::new(fm).arg(&url)); }
    }
    open_sftp_terminal(alias)
}

/// Opens Remote Desktop to a Windows server through its tunnel (127.0.0.1:<local port>).
pub fn open_rdp(t: &TunnelConfig) -> Result<(), String> {
    let client = find_rdp_client().ok_or("No Remote Desktop client was found. Install Remmina or FreeRDP (xfreerdp), then try again.")?;
    let user = if t.user_name.trim().is_empty() { "Administrator" } else { t.user_name.trim() };
    let mut cmd = Command::new(client);
    if client == "remmina" { cmd.args(["-c", &format!("rdp://{user}@127.0.0.1:{}", t.local_port)]); }
    else { cmd.args([format!("/v:127.0.0.1:{}", t.local_port), format!("/u:{user}"), "/cert:tofu".into(), "/dynamic-resolution".into(), "+clipboard".into()]); }
    spawn(&mut cmd)
}

pub fn open_url(url: &str) {
    if !(url.starts_with("https://") || url.starts_with("http://")) { return; }
    let _ = if which("xdg-open").is_some() { spawn(Command::new("xdg-open").arg(url)) } else { spawn(Command::new("gio").args(["open", url])) };
}

pub fn open_file(path: &str) { if which("xdg-open").is_some() { let _ = spawn(Command::new("xdg-open").arg(path)); } }
