//! Remembers what discovery found (discovery.json) so the server list is not lost between runs, works out what changed
//! since the previous scan, and runs the once-a-day passive rescan.

use crate::models::{AppConfig, DiscoveredServer};
use crate::util::{self, lock, now};
use crate::{applog, aws_cli, aws_profiles as ap, paths, sso};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::sync::Mutex;

static GATE: Mutex<()> = Mutex::new(());

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(default)]
pub struct ScanRecord {
    pub profile: String,
    pub region: String,
    /// last successful listing (epoch seconds)
    pub scanned: f64,
    /// last try; a failed try (expired sign-in) is repeated after an hour, not every minute
    pub attempted: f64,
    pub last_error: String,
    pub servers: Vec<DiscoveredServer>,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(default)]
pub struct DiscoveryState { pub scans: Vec<ScanRecord>, pub pending_changes: Vec<String>, pub changes_found: f64 }

pub fn load() -> DiscoveryState {
    let _g = lock(&GATE);
    load_unlocked()
}

fn load_unlocked() -> DiscoveryState {
    std::fs::read_to_string(paths::discovery_path()).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or_default()
}

fn save(s: &DiscoveryState) {
    if let Err(e) = serde_json::to_string_pretty(s).map_err(std::io::Error::other).and_then(|t| paths::write_private(&paths::discovery_path(), &t)) {
        applog::warn(&format!("Could not save the server cache: {e}"));
    }
}

fn same(r: &ScanRecord, profile: &str, region: &str) -> bool { r.profile.eq_ignore_ascii_case(profile) && r.region.eq_ignore_ascii_case(region) }

pub fn get(profile: &str, region: &str) -> Option<ScanRecord> { load().scans.into_iter().find(|r| same(r, profile, region)) }

/// Stores a successful scan. Returns the differences from the previous scan (none for an account's first scan).
pub fn update(profile: &str, region: &str, servers: Vec<DiscoveredServer>, configured: &HashSet<String>, record_changes: bool) -> Vec<String> {
    let _g = lock(&GATE);
    let mut st = load_unlocked();
    let idx = st.scans.iter().position(|r| same(r, profile, region));
    let changes = match idx.map(|i| &st.scans[i]) {
        Some(old) if old.scanned > 0.0 => diff(&old.servers, &servers, profile, region, configured),
        _ => vec![],
    };
    let i = idx.unwrap_or_else(|| { st.scans.push(ScanRecord { profile: profile.into(), region: region.into(), ..Default::default() }); st.scans.len() - 1 });
    let r = &mut st.scans[i];
    r.servers = servers; r.scanned = now(); r.attempted = now(); r.last_error.clear();
    if record_changes && !changes.is_empty() { st.pending_changes.extend(changes.clone()); st.changes_found = now(); }
    save(&st);
    changes
}

/// Drops everything remembered about an account (used when the account is removed).
pub fn forget(profile: &str) {
    let _g = lock(&GATE);
    let mut st = load_unlocked();
    let before = st.scans.len();
    st.scans.retain(|r| !r.profile.eq_ignore_ascii_case(profile));
    if st.scans.len() != before { save(&st); }
}

pub fn record_failure(profile: &str, region: &str, error: &str) {
    let _g = lock(&GATE);
    let mut st = load_unlocked();
    let i = st.scans.iter().position(|r| same(r, profile, region))
        .unwrap_or_else(|| { st.scans.push(ScanRecord { profile: profile.into(), region: region.into(), ..Default::default() }); st.scans.len() - 1 });
    st.scans[i].attempted = now();
    st.scans[i].last_error = error.into();
    save(&st);
}

pub fn take_pending() -> Vec<String> {
    let _g = lock(&GATE);
    let mut st = load_unlocked();
    let out = std::mem::take(&mut st.pending_changes);
    st.changes_found = 0.0;
    save(&st);
    out
}

pub fn diff(before: &[DiscoveredServer], after: &[DiscoveredServer], profile: &str, region: &str, configured: &HashSet<String>) -> Vec<String> {
    let acct = format!("[{profile} / {region}] ");
    let tag = |s: &DiscoveredServer| if configured.contains(&s.instance_id) { " (you have a connection for it)" } else { "" };
    let mut c = vec![];
    for s in after.iter().filter(|s| !before.iter().any(|b| b.instance_id == s.instance_id)) {
        c.push(format!("{acct}New server: {} ({}, {}){}", s.name, s.instance_id, s.platform, if s.ssm_online { "" } else { ", not reachable through SSM yet" }));
    }
    for s in before.iter().filter(|s| !after.iter().any(|a| a.instance_id == s.instance_id)) {
        c.push(format!("{acct}No longer running: {} ({}){}", s.name, s.instance_id, tag(s)));
    }
    for n in after {
        let Some(o) = before.iter().find(|b| b.instance_id == n.instance_id) else { continue };
        if o.ssm_online != n.ssm_online {
            c.push(format!("{acct}{}: {}{}", n.name, if n.ssm_online { "is now reachable through SSM" } else { "can no longer be reached through SSM" }, tag(n)));
        }
        if o.key_name != n.key_name {
            c.push(format!("{acct}{}: key pair changed from '{}' to '{}'. The saved key file may no longer work{}", n.name, o.key_name, n.key_name, tag(n)));
        }
        if o.name != n.name { c.push(format!("{acct}Server renamed: '{}' is now '{}'{}", o.name, n.name, tag(n))); }
        if o.private_ip != n.private_ip { c.push(format!("{acct}{}: private IP changed from {} to {}", n.name, o.private_ip, n.private_ip)); }
        if !o.os_name.is_empty() && !n.os_name.is_empty() && !o.os_name.eq_ignore_ascii_case(&n.os_name) {
            c.push(format!("{acct}{}: operating system changed from {} to {}. The login user may differ{}", n.name, o.os_name, n.os_name, tag(n)));
        }
    }
    c
}

// ---- the once-a-day passive rescan ------------------------------------------------------------
/// True when this account has not been scanned today, and no failed try happened within the last hour.
pub fn is_due(r: Option<&ScanRecord>) -> bool {
    let Some(r) = r else { return true };
    if r.scanned > 0.0 && util::local_ymd(r.scanned) == util::today() { return false; }
    if !r.last_error.is_empty() && now() - r.attempted < 3600.0 { return false; }
    true
}

/// The accounts to watch: those with saved connections, the one last used in setup, and every other AWS account under
/// the same company sign-in.
pub fn watched_accounts(cfg: &AppConfig) -> Vec<(String, String)> {
    let mut base: Vec<(String, String)> = cfg.tunnels.iter().filter(|t| !t.profile.trim().is_empty() && !t.region.trim().is_empty()).map(|t| (t.profile.clone(), t.region.clone())).collect();
    if !cfg.profile.trim().is_empty() && !cfg.region.trim().is_empty() { base.push((cfg.profile.clone(), cfg.region.clone())); }
    let mut out = base.clone();
    let everyone = ap::list_profiles();
    let preferred: HashSet<String> = cfg.tunnels.iter().map(|t| t.profile.clone()).collect();
    for (profile, region) in &base {
        if let Some(p) = everyone.iter().find(|x| &x.name == profile).filter(|p| p.kind == ap::Kind::Sso) {
            for m in ap::same_sign_in(p, &everyone, &preferred) { out.push((m.name, region.clone())); }
        }
    }
    let mut seen = HashSet::new();
    out.retain(|x| seen.insert(x.clone()));
    out
}

static LAST_SYNC: Mutex<Option<(i32, u32, u32)>> = Mutex::new(None);
static NEED_SIGN_IN: Mutex<Vec<String>> = Mutex::new(Vec::new());

pub fn need_sign_in() -> Vec<String> { lock(&NEED_SIGN_IN).clone() }

/// Scans every watched account that is due (all when `force`). Never prompts and never opens a browser: an expired
/// sign-in is skipped and tried again later. Returns the new differences.
pub fn run_monitor(cfg: &AppConfig, force: bool, cancelled: &dyn Fn() -> bool) -> Vec<String> {
    let mut found = vec![];
    if force || *lock(&LAST_SYNC) != Some(util::today()) {
        let sync = sso::sync_members();
        if sync.need_sign_in.is_empty() { *lock(&LAST_SYNC) = Some(util::today()); }
        *lock(&NEED_SIGN_IN) = sync.need_sign_in;
        found.extend(sync.added.into_iter().map(|a| format!("New AWS account found under your sign-in: {a}")));
    }
    let configured: HashSet<String> = cfg.tunnels.iter().map(|t| t.instance_id.clone()).collect();
    for (profile, region) in watched_accounts(cfg) {
        if cancelled() { break; }
        if !force && !is_due(get(&profile, &region).as_ref()) { continue; }
        match aws_cli::discover(&profile, &region) {
            Ok(servers) => {
                let n = servers.len();
                found.extend(update(&profile, &region, servers, &configured, true));
                applog::info(&format!("Server scan {profile}/{region}: {n} servers."));
            }
            Err(e) => { record_failure(&profile, &region, &e); applog::warn(&format!("Server scan {profile}/{region} skipped: {e}")); }
        }
    }
    found
}
