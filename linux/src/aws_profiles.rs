//! Reads and writes AWS profiles directly in ~/.aws/config and ~/.aws/credentials, so profiles can be listed and created
//! without the AWS CLI and without any interactive `aws configure`. Only the sections SSMConnect touches are rewritten;
//! everything else is preserved and a .ssmconnect.bak copy is kept.

use crate::models::AppConfig;
use crate::paths;
use regex::Regex;
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fs;
use std::io::Write;
use std::os::unix::fs::OpenOptionsExt;
use std::path::Path;

pub const COMMON_REGIONS: [&str; 25] = [
    "us-east-1", "us-east-2", "us-west-1", "us-west-2", "ap-south-1", "ap-south-2", "ap-southeast-1", "ap-southeast-2", "ap-southeast-3",
    "ap-northeast-1", "ap-northeast-2", "ap-northeast-3", "ap-east-1", "ca-central-1", "eu-central-1", "eu-central-2", "eu-west-1",
    "eu-west-2", "eu-west-3", "eu-north-1", "eu-south-1", "me-south-1", "me-central-1", "sa-east-1", "af-south-1",
];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind { Sso, Keys, Other }

#[derive(Clone, Debug, PartialEq)]
pub struct ProfileInfo {
    pub name: String,
    pub kind: Kind,
    pub region: Option<String>,
    pub account_id: Option<String>,
    pub role_name: Option<String>,
    pub sso_session: Option<String>,
    pub start_url: Option<String>,
}

impl ProfileInfo {
    pub fn new(name: &str, kind: Kind) -> Self {
        Self { name: name.into(), kind, region: None, account_id: None, role_name: None, sso_session: None, start_url: None }
    }
}

pub fn is_valid_name(name: &str) -> bool { Regex::new(r"^[A-Za-z0-9_.@\-]{1,64}$").unwrap().is_match(name) }

type Ini = BTreeMap<String, HashMap<String, String>>;

// ---- reading ---------------------------------------------------------------------------------
pub fn read_ini(path: &Path) -> Ini {
    let mut out: Ini = BTreeMap::new();
    let Ok(text) = fs::read(path) else { return out };
    let text = String::from_utf8_lossy(&text);
    let mut cur: Option<String> = None;
    for raw in text.lines() {
        let line = raw.trim();
        if line.is_empty() || line.starts_with('#') || line.starts_with(';') { continue; }
        if line.starts_with('[') && line.ends_with(']') {
            let name = line[1..line.len() - 1].trim().to_string();
            out.entry(name.clone()).or_default();
            cur = Some(name);
        } else if let (Some(sec), Some((k, v))) = (&cur, line.split_once('=')) {
            out.get_mut(sec).unwrap().insert(k.trim().to_lowercase(), v.trim().to_string());
        }
    }
    out
}

fn get(d: &HashMap<String, String>, k: &str) -> Option<String> { d.get(k).filter(|v| !v.is_empty()).cloned() }

pub fn list_profiles() -> Vec<ProfileInfo> {
    let cfg = read_ini(&paths::aws_config_path());
    let cred = read_ini(&paths::aws_credentials_path());
    let mut names: Vec<String> = vec![];
    for s in cfg.keys() {
        if s == "default" { names.push("default".into()); } else if let Some(n) = s.strip_prefix("profile ") { names.push(n.trim().to_string()); }
    }
    for s in cred.keys() { if !names.contains(s) { names.push(s.clone()); } }
    names.sort_by_key(|n| n.to_lowercase());
    names.dedup();
    let empty = HashMap::new();
    names.into_iter().map(|n| {
        let c = cfg.get(&if n == "default" { "default".to_string() } else { format!("profile {n}") }).unwrap_or(&empty);
        let k = cred.get(&n).unwrap_or(&empty);
        let session = get(c, "sso_session");
        let mut start = get(c, "sso_start_url");
        if let (Some(s), None) = (&session, &start) { if let Some(ss) = cfg.get(&format!("sso-session {s}")) { start = get(ss, "sso_start_url"); } }
        let kind = if session.is_some() || start.is_some() { Kind::Sso }
            else if get(k, "aws_access_key_id").is_some() || get(c, "aws_access_key_id").is_some() { Kind::Keys } else { Kind::Other };
        ProfileInfo { name: n, kind, region: get(c, "region"), account_id: get(c, "sso_account_id"), role_name: get(c, "sso_role_name"), sso_session: session, start_url: start }
    }).collect()
}

pub fn find(name: &str) -> Option<ProfileInfo> { list_profiles().into_iter().find(|p| p.name == name) }

/// The sign-in address reduced to its host, so nicknames differing in letter case or a trailing slash count as one.
pub fn normalise_url(url: &str) -> String {
    let u = url.trim();
    if u.is_empty() { return String::new(); }
    match host_of(u) { Some(h) => h.to_lowercase(), None => u.trim_end_matches(['/', '#']).to_lowercase() }
}

pub fn host_of(url: &str) -> Option<String> {
    let rest = url.split_once("://")?.1;
    let host = rest.split(['/', '?', '#']).next()?.rsplit('@').next()?.split(':').next()?;
    (!host.is_empty()).then(|| host.to_string())
}

/// Identifies the primary account (one company sign-in, or one access-key login) a profile belongs to.
pub fn primary_key(p: &ProfileInfo) -> String {
    if p.kind != Kind::Sso { return format!("key:{}", p.name); }
    let host = normalise_url(p.start_url.as_deref().unwrap_or(""));
    format!("sso:{}", if host.is_empty() { p.sso_session.clone().unwrap_or_else(|| p.name.clone()).to_lowercase() } else { host })
}

pub fn legacy_keys(p: &ProfileInfo) -> Vec<String> {
    if p.kind != Kind::Sso { return vec![]; }
    let mut v = vec![];
    if let Some(s) = p.sso_session.as_deref().filter(|s| !s.is_empty()) { v.push(format!("sso:{s}")); }
    if let Some(u) = p.start_url.as_deref().filter(|s| !s.is_empty()) { v.push(format!("sso:{u}")); }
    v.push(format!("sso:{}", p.name));
    v
}

/// A short friendly name for the primary account: the sign-in's nickname, or the company part of its address.
pub fn primary_name(p: &ProfileInfo) -> String {
    if p.kind != Kind::Sso { return p.name.clone(); }
    if let Some(s) = p.sso_session.as_deref().filter(|s| !s.trim().is_empty()) { return s.to_string(); }
    p.start_url.as_deref().and_then(host_of).map(|h| h.split('.').next().unwrap_or("").to_string()).filter(|s| !s.is_empty()).unwrap_or_else(|| p.name.clone())
}

pub fn primary_label(cfg: &AppConfig, p: &ProfileInfo, everyone: &[ProfileInfo]) -> String {
    let key = primary_key(p);
    if let Some(a) = cfg.aliases.get(&key).filter(|a| !a.trim().is_empty()) { return a.clone(); }
    for m in everyone.iter().filter(|x| primary_key(x) == key).chain(std::iter::once(p)) {
        for k in legacy_keys(m) {
            if let Some(b) = cfg.aliases.get(&k).filter(|b| !b.trim().is_empty()) { return b.clone(); }
        }
    }
    primary_name(p)
}

/// One profile per AWS account: of several pointing at the same account keep the one with saved connections,
/// then the chosen one, then the shortest name.
pub fn one_each(group: &[ProfileInfo], preferred: &HashSet<String>, favourite: Option<&str>) -> Vec<ProfileInfo> {
    let mut sets: Vec<(String, Vec<&ProfileInfo>)> = vec![];
    for x in group {
        let k = match x.account_id.as_deref() { Some(a) if !a.is_empty() => format!("a:{a}"), _ => format!("n:{}", x.name) };
        match sets.iter_mut().find(|(sk, _)| *sk == k) { Some((_, v)) => v.push(x), None => sets.push((k, vec![x])) }
    }
    let mut best: Vec<ProfileInfo> = sets.into_iter().filter_map(|(_, s)| {
        s.into_iter().min_by_key(|x| (!preferred.contains(&x.name), favourite != Some(x.name.as_str()), x.name.len(), x.name.to_lowercase())).cloned()
    }).collect();
    best.sort_by_key(|x| x.name.to_lowercase());
    best
}

/// The saved profiles sharing this profile's company sign-in (the other AWS accounts reachable with it).
pub fn same_sign_in(p: &ProfileInfo, everyone: &[ProfileInfo], preferred: &HashSet<String>) -> Vec<ProfileInfo> {
    if p.kind != Kind::Sso { return vec![p.clone()]; }
    let key = primary_key(p);
    let mut group: Vec<ProfileInfo> = everyone.iter().filter(|x| primary_key(x) == key).cloned().collect();
    if !group.iter().any(|x| x.name == p.name) { group.push(p.clone()); }
    one_each(&group, preferred, Some(&p.name))
}

/// The address and region a named company sign-in (sso-session) is set up with.
pub fn session_info(session: &str) -> (Option<String>, Option<String>) {
    read_ini(&paths::aws_config_path()).get(&format!("sso-session {session}")).map(|d| (get(d, "sso_start_url"), get(d, "sso_region"))).unwrap_or((None, None))
}

pub fn profile_name_for(account_name: &str, account_id: &str, taken: &HashSet<String>) -> String {
    let slug = Regex::new(r"[^A-Za-z0-9_.@\-]+").unwrap().replace_all(account_name, "-").trim_matches('-').to_string();
    let mut slug = if slug.is_empty() { format!("account-{account_id}") } else { slug };
    if slug.len() > 56 { slug = slug[..56].trim_matches('-').to_string(); }
    let low: HashSet<String> = taken.iter().map(|t| t.to_lowercase()).collect();
    if low.contains(&slug.to_lowercase()) {
        let tail = if account_id.len() >= 4 { &account_id[account_id.len() - 4..] } else { account_id };
        format!("{slug}-{tail}")
    } else { slug }
}

// ---- writing ---------------------------------------------------------------------------------
pub fn save_sso_session(session: &str, start_url: &str, sso_region: &str, with_scopes: bool) -> Result<(), String> {
    let mut v = vec![("sso_start_url", start_url), ("sso_region", sso_region)];
    if with_scopes { v.push(("sso_registration_scopes", "sso:account:access")); }
    upsert(&paths::aws_config_path(), &format!("sso-session {session}"), &v, if with_scopes { &[] } else { &["sso_registration_scopes"] })
}

pub fn save_sso_profile(profile: &str, session: &str, account_id: &str, role_name: &str) -> Result<(), String> {
    upsert(&paths::aws_config_path(), &format!("profile {profile}"),
           &[("sso_session", session), ("sso_account_id", account_id), ("sso_role_name", role_name), ("output", "json")], &[])
}

pub fn save_access_keys(profile: &str, key_id: &str, secret: &str) -> Result<(), String> {
    if !Regex::new(r"^[A-Z0-9]{16,128}$").unwrap().is_match(key_id) {
        return Err("That does not look like an AWS access key ID (20 letters and digits, starting with AKIA or ASIA).".into());
    }
    if !Regex::new(r"^[A-Za-z0-9/+=]{30,128}$").unwrap().is_match(secret) {
        return Err("That does not look like an AWS secret access key (about 40 characters).".into());
    }
    upsert(&paths::aws_credentials_path(), profile, &[("aws_access_key_id", key_id), ("aws_secret_access_key", secret)], &[])?;
    upsert(&paths::aws_config_path(), &format!("profile {profile}"), &[("output", "json")], &[])
}

pub fn save_region(profile: &str, region: &str) -> Result<(), String> {
    upsert(&paths::aws_config_path(), &if profile == "default" { "default".to_string() } else { format!("profile {profile}") }, &[("region", region)], &[])
}

/// Removes one account profile (and, for access keys, its stored keys). A copy of each file is kept as .ssmconnect.bak.
pub fn remove_profile(profile: &str) -> Result<(), String> {
    delete_section(&paths::aws_config_path(), &if profile == "default" { "default".to_string() } else { format!("profile {profile}") })?;
    delete_section(&paths::aws_credentials_path(), profile)
}

/// Call only when no remaining profile uses it.
pub fn remove_sso_session(session: &str) -> Result<(), String> { delete_section(&paths::aws_config_path(), &format!("sso-session {session}")) }

fn backup(path: &Path) {
    if let Ok(data) = fs::read(path) {
        let bak = path.with_file_name(format!("{}.ssmconnect.bak", path.file_name().unwrap().to_string_lossy()));
        let _ = fs::OpenOptions::new().write(true).create(true).truncate(true).mode(0o600).open(&bak).and_then(|mut f| f.write_all(&data));
    }
}

fn write(path: &Path, lines: &[String]) -> Result<(), String> {
    paths::write_private(path, &(lines.join("\n") + "\n")).map_err(|e| format!("Could not write {}: {e}", path.display()))
}

fn read_lines(path: &Path) -> Vec<String> {
    fs::read(path).map(|b| String::from_utf8_lossy(&b).lines().map(String::from).collect()).unwrap_or_default()
}

fn section_end(lines: &[String], start: usize) -> usize {
    let mut e = start + 1;
    while e < lines.len() && !lines[e].trim_start().starts_with('[') { e += 1; }
    e
}

fn delete_section(path: &Path, section: &str) -> Result<(), String> {
    if !path.exists() { return Ok(()); }
    let mut lines = read_lines(path);
    let header = format!("[{section}]");
    let Some(start) = lines.iter().position(|l| l.trim() == header) else { return Ok(()) };
    backup(path);
    let end = section_end(&lines, start);
    lines.drain(start..end);
    while start > 0 && start < lines.len() && lines[start - 1].trim().is_empty() && lines[start].trim().is_empty() { lines.remove(start); }
    write(path, &lines)
}

fn key_of(line: &str) -> Option<String> { line.split_once('=').map(|(k, _)| k.trim().to_lowercase()) }

fn upsert(path: &Path, section: &str, values: &[(&str, &str)], remove: &[&str]) -> Result<(), String> {
    if values.iter().any(|(_, v)| v.contains('\n') || v.contains('\r')) { return Err("Line breaks are not allowed in AWS settings.".into()); }
    let mut lines = read_lines(path);
    if path.exists() { backup(path); }
    let header = format!("[{section}]");
    match lines.iter().position(|l| l.trim() == header) {
        None => {
            if lines.last().is_some_and(|l| !l.trim().is_empty()) { lines.push(String::new()); }
            lines.push(header);
            for (k, v) in values { lines.push(format!("{k} = {v}")); }
        }
        Some(start) => {
            let mut end = section_end(&lines, start);
            let rm: Vec<String> = remove.iter().map(|r| r.to_lowercase()).collect();
            let mut i = end;
            while i > start + 1 {
                i -= 1;
                if key_of(&lines[i]).is_some_and(|k| rm.contains(&k)) { lines.remove(i); end -= 1; }
            }
            for (k, v) in values {
                let text = format!("{k} = {v}");
                match (start + 1..end).find(|&i| key_of(&lines[i]).is_some_and(|x| x == k.to_lowercase())) {
                    Some(i) => lines[i] = text,
                    None => { lines.insert(end, text); end += 1; }
                }
            }
        }
    }
    write(path, &lines)
}
