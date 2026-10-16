//! Small helpers: time, strings and a mutex that survives a panicking thread.

use std::sync::{Mutex, MutexGuard};
use std::time::{SystemTime, UNIX_EPOCH};

pub fn now() -> f64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs_f64()).unwrap_or(0.0)
}

pub fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

/// Days since 1970-01-01 for a civil date (Howard Hinnant's algorithm).
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let doy = (153 * (if m > 2 { m - 3 } else { m + 9 }) + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146097 + doe - 719468
}

/// Parses "2026-10-16T12:00:00Z" (also "...UTC", "+00:00" or fractional seconds) to epoch seconds, as UTC.
pub fn parse_iso_utc(s: &str) -> Option<f64> {
    let s = s.trim();
    let (date, time) = s.split_once(['T', ' '])?;
    let mut d = date.split('-').map(|x| x.parse::<i64>().ok());
    let (y, mo, da) = (d.next()??, d.next()??, d.next()??);
    let time = time.trim_end_matches(|c: char| c.is_alphabetic());
    let time = time.split(['+', 'Z']).next().unwrap_or(time);
    let mut t = time.split(':');
    let h: i64 = t.next()?.parse().ok()?;
    let mi: i64 = t.next().unwrap_or("0").parse().ok()?;
    let sec: f64 = t.next().unwrap_or("0").parse().ok()?;
    Some((days_from_civil(y, mo, da) * 86400 + h * 3600 + mi * 60) as f64 + sec)
}

/// (year, month, day) of an epoch time in the user's local time zone.
pub fn local_ymd(epoch: f64) -> (i32, u32, u32) {
    let t = epoch as libc::time_t;
    let mut tm: libc::tm = unsafe { std::mem::zeroed() };
    unsafe { libc::localtime_r(&t, &mut tm) };
    (tm.tm_year + 1900, tm.tm_mon as u32 + 1, tm.tm_mday as u32)
}

pub fn today() -> (i32, u32, u32) { local_ymd(now()) }

pub fn hex(bytes: &[u8]) -> String { bytes.iter().map(|b| format!("{b:02x}")).collect() }

pub fn colon_hex(bytes: &[u8]) -> String { bytes.iter().map(|b| format!("{b:02x}")).collect::<Vec<_>>().join(":") }

pub fn first_line(s: &str) -> &str { s.lines().map(str::trim).find(|l| !l.is_empty()).unwrap_or("") }

pub fn truncate(s: &str, n: usize) -> String { s.chars().take(n).collect() }
