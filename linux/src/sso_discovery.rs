//! Finds the company sign-in URL and region on its own: settings pushed by IT, existing AWS CLI settings, cached sign-in
//! tokens, and finally (clearly labelled) a guess from the company's own domain.

use crate::aws_profiles::host_of;
use crate::paths;
use regex::Regex;
use serde_json::Value;
use std::time::Duration;

#[derive(Clone, Debug, PartialEq)]
pub struct SsoCandidate { pub start_url: String, pub region: String, pub source: String, pub guess: bool }

fn clean(raw: &str) -> String { raw.trim().split('#').next().unwrap_or("").trim_end_matches('/').to_string() }

pub fn is_valid_url(url: &str) -> bool {
    let u = clean(url);
    let re = Regex::new(r"^https://[A-Za-z0-9.\-]+\.(awsapps\.com|amazonaws\.com\.cn)(/start)?/?(#/?)?$|^https://[A-Za-z0-9.\-/]+$").unwrap();
    u.to_lowercase().starts_with("https://") && re.is_match(&u)
}

/// A short, valid name for the sso-session made from the sign-in address (acme.awsapps.com -> acme).
pub fn session_name_for(url: &str) -> String {
    let host = host_of(&clean(url)).unwrap_or_else(|| "sso".into());
    let first = host.split('.').next().unwrap_or("sso");
    let s = Regex::new(r"[^A-Za-z0-9_.\-]+").unwrap().replace_all(first, "-").trim_matches('-').to_string();
    if s.is_empty() { "sso".into() } else { s }
}

struct Found(Vec<SsoCandidate>);

impl Found {
    fn add(&mut self, url: &str, region: &str, source: &str) {
        let url = clean(url);
        if !is_valid_url(&url) { return; }
        let region = region.trim();
        if let Some(c) = self.0.iter_mut().find(|c| c.start_url.eq_ignore_ascii_case(&url)) {
            if c.region.is_empty() && !region.is_empty() { c.region = region.into(); }   // keep the first source, but learn a region
            return;
        }
        self.0.push(SsoCandidate { start_url: url, region: region.into(), source: source.into(), guess: false });
    }
}

pub fn find() -> Vec<SsoCandidate> {
    let mut f = Found(vec![]);
    // 1) pushed by the company's IT team (system-wide, then per user), then environment settings
    for file in [std::path::PathBuf::from("/etc/ssmconnect/sso.json"), paths::config_dir().join("sso.json")] {
        if let Some(v) = std::fs::read_to_string(&file).ok().and_then(|t| serde_json::from_str::<Value>(&t).ok()) {
            f.add(v["startUrl"].as_str().unwrap_or(""), v["region"].as_str().unwrap_or(""), &format!("Set by your IT team ({})", file.display()));
        }
    }
    let env = |k: &str| std::env::var(k).unwrap_or_default();
    f.add(&env("SSMCONNECT_SSO_URL"), &env("SSMCONNECT_SSO_REGION"), "Environment setting");
    f.add(&env("AWS_SSO_START_URL"), &env("AWS_SSO_REGION"), "Environment setting");
    // 2) AWS CLI configuration already on this computer
    for cfg in [paths::aws_dir().join("config"), paths::aws_config_path()] {
        let ini = crate::aws_profiles::read_ini(&cfg);
        for d in ini.values() {
            if let Some(u) = d.get("sso_start_url") { f.add(u, d.get("sso_region").map(String::as_str).unwrap_or(""), "AWS settings already on this computer"); }
        }
    }
    // 3) sign-in tokens cached by the AWS CLI
    if let Ok(rd) = std::fs::read_dir(paths::aws_dir().join("sso/cache")) {
        let mut files: Vec<_> = rd.flatten().map(|e| e.path()).filter(|p| p.extension().is_some_and(|e| e == "json")).collect();
        files.sort();
        for p in files {
            if let Some(v) = std::fs::read_to_string(&p).ok().and_then(|t| serde_json::from_str::<Value>(&t).ok()) {
                f.add(v["startUrl"].as_str().unwrap_or(""), v["region"].as_str().unwrap_or(""), "Earlier AWS sign-in on this computer");
            }
        }
    }
    f.0
}

fn domain_names() -> Vec<String> {
    let mut names: Vec<String> = vec![];
    let mut from_domain = |d: &str| {
        let parts: Vec<String> = d.to_lowercase().split('.').filter(|p| !p.is_empty()).map(String::from).collect();
        if parts.len() >= 2 {
            names.push(parts[parts.len() - 2].clone());
            names.push(parts.join("-"));
            if parts.len() >= 3 { names.push(format!("{}-{}", parts[parts.len() - 3], parts[parts.len() - 2])); }
        }
    };
    if let Ok(d) = std::env::var("DOMAIN") { from_domain(&d); }
    if let Ok(h) = std::fs::read_to_string("/proc/sys/kernel/domainname") { let h = h.trim(); if h != "(none)" { from_domain(h); } }
    if let Ok(mail) = std::env::var("EMAIL") { if let Some((_, d)) = mail.split_once('@') { from_domain(d); } }
    let ok = Regex::new(r"^[a-z0-9][a-z0-9-]{1,62}$").unwrap();
    let mut seen = std::collections::HashSet::new();
    names.into_iter().filter(|n| ok.is_match(n) && seen.insert(n.clone())).collect()
}

/// Tries https://<company>.awsapps.com/start for names taken from this computer's domain and reports the ones that
/// answer. These are guesses and are labelled so; the person confirms by signing in.
pub fn guess_from_domain() -> Vec<SsoCandidate> {
    let agent = ureq::AgentBuilder::new().redirects(0).timeout(Duration::from_secs(6)).build();
    domain_names().into_iter().filter_map(|n| {
        let url = format!("https://{n}.awsapps.com/start");
        let (code, loc) = match agent.get(&url).call() {
            Ok(r) => (r.status(), r.header("Location").unwrap_or("").to_string()),
            Err(ureq::Error::Status(c, r)) => (c, r.header("Location").unwrap_or("").to_string()),
            Err(_) => return None,   // name does not exist or is unreachable from here
        };
        let ok = code == 200 || (matches!(code, 301 | 302 | 303 | 307) && ["awsapps.com", "amazonaws.com", "signin.aws"].iter().any(|x| loc.contains(x)));
        ok.then(|| SsoCandidate { start_url: url, region: String::new(), source: "Guessed from your company domain (please confirm)".into(), guess: true })
    }).collect()
}
