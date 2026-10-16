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
pub fn open_terminal(alias: &str) -> Result<(), String> {
    let (exe, args) = find_terminal().ok_or("No terminal program was found. Install one (for example GNOME Terminal), or use 'Copy ssh command'.")?;
    let script = format!("ssh {}; status=$?; if [ $status -ne 0 ]; then echo; read -r -p 'ssh ended with an error. Press Enter to close.' _; fi", sh_quote(alias));
    applog::info(&format!("Opening terminal: {exe} {} bash -c ...", args.join(" ")));
    spawn(Command::new(&exe).args(&args).arg("bash").arg("-c").arg(script))
}

/// Browse the server in the desktop file manager (sftp://). ssh_config supplies the port and key.
pub fn open_files(user: &str, alias: &str) -> Result<(), String> {
    let url = format!("sftp://{user}@{alias}/");
    if which("gio").is_some() { return spawn(Command::new("gio").args(["open", &url])); }
    if which("xdg-open").is_some() { return spawn(Command::new("xdg-open").arg(&url)); }
    Err("No file manager was found to open the server's files.".into())
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
