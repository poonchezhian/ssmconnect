//! How busy each server is: processors, memory and disk size and use, read with a read-only command that Session Manager runs on the
//! server (`AWS-RunShellScript`, or `AWS-RunPowerShellScript` on Windows). Needs permission to run commands through SSM; without it the
//! numbers stay as dashes. Results are kept in usage.json so the dashboard shows them straight away next time.

use crate::models::{AppConfig, TunnelConfig};
use crate::util::{lock, now};
use crate::{applog, aws_cli, paths};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{BTreeMap, HashSet};
use std::sync::Mutex;

static GATE: Mutex<()> = Mutex::new(());
/// Automatic reads happen about every five minutes; a refused account is left alone for an hour.
const FRESH_SECS: f64 = 270.0;
const BACK_OFF_SECS: f64 = 3600.0;

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct Usage {
    pub cpus: u32,
    pub mem_total_mb: u64,
    pub disk_total_gb: u64,
    pub cpu_pct: u32,
    pub mem_pct: u32,
    pub disk_pct: u32,
    pub read_at: f64,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(default)]
pub struct UsageCache {
    /// instance id -> last good reading
    pub servers: BTreeMap<String, Usage>,
    /// account profile -> do not ask again before this time (the role may not run SSM commands)
    pub denied_until: BTreeMap<String, f64>,
}

pub fn load() -> UsageCache {
    std::fs::read_to_string(paths::usage_path()).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or_default()
}

fn save(c: &UsageCache) {
    if let Err(e) = serde_json::to_string(c).map_err(std::io::Error::other).and_then(|t| paths::write_private(&paths::usage_path(), &t)) {
        applog::warn(&format!("Could not save the usage readings: {e}"));
    }
}

const LINUX_SCRIPT: &str = r#"c=$(nproc 2>/dev/null || grep -c ^processor /proc/cpuinfo)
read -r _ u1 n1 s1 i1 w1 q1 r1 _ < /proc/stat; sleep 1; read -r _ u2 n2 s2 i2 w2 q2 r2 _ < /proc/stat
dt=$(( (u2+n2+s2+i2+w2+q2+r2) - (u1+n1+s1+i1+w1+q1+r1) )); di=$(( (i2+w2) - (i1+w1) ))
if [ "$dt" -gt 0 ]; then cpu=$(( 100 * (dt - di) / dt )); else cpu=0; fi
m=$(awk '/MemTotal/{t=$2}/MemAvailable/{a=$2}END{print t, t-a}' /proc/meminfo)
d=$(df -Pk / | awk 'NR==2{print $2, $3}')
echo "SSMUSAGE $c $m $d $cpu""#;

const WINDOWS_SCRIPT: &str = r#"$o=Get-CimInstance Win32_OperatingSystem; $p=Get-CimInstance Win32_Processor; $c=($p|Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum; $cpu=[int]($p|Measure-Object -Property LoadPercentage -Average).Average; $d=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"; "SSMUSAGE $c $($o.TotalVisibleMemorySize) $($o.TotalVisibleMemorySize-$o.FreePhysicalMemory) $([math]::Round($d.Size/1KB)) $([math]::Round(($d.Size-$d.FreeSpace)/1KB)) $cpu""#;

/// Turns the script's `SSMUSAGE cpus mem_total_kb mem_used_kb disk_total_kb disk_used_kb cpu_pct` line into readings.
pub fn parse(output: &str) -> Option<Usage> {
    let line = output.lines().find(|l| l.trim_start().starts_with("SSMUSAGE"))?;
    let n: Vec<u64> = line.split_whitespace().skip(1).map(|x| x.parse().ok()).collect::<Option<_>>()?;
    let [cpus, mem_t, mem_u, disk_t, disk_u, cpu] = n[..] else { return None };
    let pct = |used: u64, total: u64| if total == 0 { 0 } else { ((used as f64 / total as f64) * 100.0).round().min(100.0) as u32 };
    Some(Usage { cpus: cpus as u32, mem_total_mb: mem_t / 1024, disk_total_gb: disk_t / (1024 * 1024), cpu_pct: cpu.min(100) as u32,
                 mem_pct: pct(mem_u, mem_t), disk_pct: pct(disk_u, disk_t), read_at: now() })
}

fn is_denied(text: &str) -> bool {
    let low = text.to_lowercase();
    low.contains("accessdenied") || low.contains("not authorized") || low.contains("unauthorizedoperation") || low.contains("is not authorized to perform")
}

/// Reads one server now. The error is plain language; `denied` is true when the role may not run SSM commands.
pub fn read_one(t: &TunnelConfig) -> Result<Usage, (String, bool)> {
    let (doc, script) = if t.is_rdp() { ("AWS-RunPowerShellScript", WINDOWS_SCRIPT) } else { ("AWS-RunShellScript", LINUX_SCRIPT) };
    let params = serde_json::json!({ "commands": [script] }).to_string();
    let sent = aws_cli::run(&["ssm", "send-command", "--instance-ids", &t.instance_id, "--document-name", doc, "--parameters", &params,
                              "--comment", "SSMConnect usage (read-only)", "--query", "Command.CommandId", "--output", "text"], &t.profile, &t.region, 30);
    if !sent.ok() { return Err((aws_cli::friendly_error(&sent.error, None), is_denied(&sent.error))); }
    let id = sent.output.trim().to_string();
    if id.is_empty() { return Err(("No answer from Session Manager.".into(), false)); }
    for _ in 0..14 {
        std::thread::sleep(std::time::Duration::from_millis(1500));
        let r = aws_cli::run(&["ssm", "get-command-invocation", "--command-id", &id, "--instance-id", &t.instance_id, "--output", "json"], &t.profile, &t.region, 30);
        if !r.ok() { if is_denied(&r.error) { return Err((aws_cli::friendly_error(&r.error, None), true)); } continue; }   // "InvocationDoesNotExist" right after sending
        let v: Value = serde_json::from_str(&r.output).unwrap_or(Value::Null);
        match v.get("Status").and_then(Value::as_str).unwrap_or("") {
            "Success" => return parse(v.get("StandardOutputContent").and_then(Value::as_str).unwrap_or("")).ok_or(("The server's answer was not understood.".to_string(), false)),
            "Pending" | "InProgress" | "Delayed" => continue,
            other => return Err((format!("The read ended as {other}."), false)),
        }
    }
    Err(("The server did not answer in time.".into(), false))
}

/// Reads the servers in `connected` (instance ids). Automatic reads skip production servers (unless the person allowed them), servers read
/// recently and accounts that refused; a manual one (`force`) reads them all. Returns how many were read.
pub fn refresh(tunnels: &[TunnelConfig], connected: &HashSet<String>, cfg: &AppConfig, force: bool) -> usize {
    let mut read = 0;
    for t in tunnels.iter().filter(|t| t.enabled && connected.contains(&t.instance_id)) {
        if !force && t.is_prod() && !cfg.read_usage_on_prod { continue; }
        let snapshot = load();
        if !force {
            if snapshot.denied_until.get(&t.profile).is_some_and(|until| *until > now()) { continue; }
            if snapshot.servers.get(&t.instance_id).is_some_and(|u| now() - u.read_at < FRESH_SECS) { continue; }
        }
        match read_one(t) {
            Ok(u) => { let _g = lock(&GATE); let mut c = load(); c.servers.insert(t.instance_id.clone(), u); c.denied_until.remove(&t.profile); save(&c); read += 1; }
            Err((why, denied)) => {
                applog::info(&format!("Usage of {} not read: {why}", t.name));
                if denied { let _g = lock(&GATE); let mut c = load(); c.denied_until.insert(t.profile.clone(), now() + BACK_OFF_SECS); save(&c); }
            }
        }
    }
    read
}

/// The colour class for a percentage: green under 60, amber 60 to 85, red above.
pub fn level(pct: u32) -> &'static str { if pct >= 85 { "bad" } else if pct >= 60 { "warn" } else { "ok" } }

/// "8 GB" / "512 MB"
pub fn size_text(mb: u64) -> String { if mb >= 1024 { format!("{} GB", (mb as f64 / 1024.0).round() as u64) } else { format!("{mb} MB") } }
