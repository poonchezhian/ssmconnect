//! Running programs on the computer itself when SSMConnect is a Flatpak app.
//!
//! The Flatpak sandbox holds SSMConnect and the AWS tools it downloads, but the terminal, the file manager, the Remote Desktop client and the
//! OpenSSH client belong to the person's desktop. `command` and `which` therefore go through `flatpak-spawn --host` there, and are plain
//! `Command` / PATH lookups everywhere else.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

/// True inside a Flatpak sandbox.
pub fn in_flatpak() -> bool {
    static F: OnceLock<bool> = OnceLock::new();
    *F.get_or_init(|| Path::new("/.flatpak-info").exists())
}

/// A command that runs `program` on the host (in a Flatpak) or here (anywhere else).
pub fn command(program: &str) -> Command {
    if in_flatpak() {
        let mut c = Command::new("flatpak-spawn");
        c.arg("--host");
        if let Some(home) = std::env::var_os("HOME") { let mut d = std::ffi::OsString::from("--directory="); d.push(home); c.arg(d); }
        c.arg(program);
        c
    } else { Command::new(program) }
}

/// Where `name` is installed on the host (a plain PATH search outside a Flatpak). Answers are remembered for a few minutes: the dashboard asks every few seconds.
pub fn which(name: &str) -> Option<PathBuf> {
    if !in_flatpak() { return crate::aws_cli::which(name); }
    static CACHE: OnceLock<Mutex<HashMap<String, (Instant, Option<PathBuf>)>>> = OnceLock::new();
    let cache = CACHE.get_or_init(|| Mutex::new(HashMap::new()));
    if let Some((at, found)) = cache.lock().ok()?.get(name) { if at.elapsed() < Duration::from_secs(300) { return found.clone(); } }
    let out = Command::new("flatpak-spawn").args(["--host", "sh", "-c", "command -v -- \"$1\"", "sh", name]).output().ok();
    let found = out.filter(|o| o.status.success()).map(|o| String::from_utf8_lossy(&o.stdout).trim().to_string()).filter(|p| p.starts_with('/')).map(PathBuf::from);
    if let Ok(mut c) = cache.lock() { c.insert(name.to_string(), (Instant::now(), found.clone())); }
    found
}
