//! A small rolling log in the user's state directory (Help > Open log file).

use std::fs;
use std::io::Write;
use std::sync::Mutex;

static GATE: Mutex<()> = Mutex::new(());
const MAX_BYTES: u64 = 1_000_000;

fn stamp() -> String {
    let t = crate::util::now();
    let (y, mo, d) = crate::util::local_ymd(t);
    let secs = (t as i64).rem_euclid(86400);
    // wall-clock time of day in local time, via libc
    let tt = t as libc::time_t;
    let mut tm: libc::tm = unsafe { std::mem::zeroed() };
    unsafe { libc::localtime_r(&tt, &mut tm) };
    let _ = secs;
    format!("{y:04}-{mo:02}-{d:02} {:02}:{:02}:{:02}", tm.tm_hour, tm.tm_min, tm.tm_sec)
}

fn write(level: &str, msg: &str) {
    let _g = crate::util::lock(&GATE);
    let path = crate::paths::log_path();
    let _ = path.parent().map(fs::create_dir_all);
    if fs::metadata(&path).map(|m| m.len() > MAX_BYTES).unwrap_or(false) {
        let _ = fs::rename(&path, path.with_extension("log.1"));
    }
    if let Ok(mut f) = fs::OpenOptions::new().create(true).append(true).open(&path) {
        let _ = writeln!(f, "{} {:<5} {}", stamp(), level, msg);
    }
}

pub fn info(msg: &str) { write("INFO", msg) }
pub fn warn(msg: &str) { write("WARN", msg) }
pub fn error(msg: &str) { write("ERROR", msg) }
