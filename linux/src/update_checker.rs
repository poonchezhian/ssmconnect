//! Looks for a newer Linux release on the project's GitHub Releases page (once a day). It only tells the person: the app is
//! installed by the package manager or the tarball, so upgrading is done the same way. Tags are `linux-vMAJOR.MINOR.PATCH`.

use crate::{paths, util, VERSION};
use serde_json::Value;
use std::time::Duration;

pub const REPO: &str = "poonchezhian/ssmconnect";
pub const TAG_PREFIX: &str = "linux-v";
pub const RELEASES_PAGE: &str = "https://github.com/poonchezhian/ssmconnect/releases";
const INTERVAL: f64 = 24.0 * 3600.0;

#[derive(Clone, Debug, PartialEq)]
pub struct UpdateInfo { pub version: String, pub tag: String, pub notes: String, pub page_url: String }

pub fn parse(v: &str) -> Option<(u32, u32, u32)> {
    let mut it = v.trim().trim_start_matches('v').split('.');
    let r = (it.next()?.parse().ok()?, it.next()?.parse().ok()?, it.next()?.parse().ok()?);
    it.next().is_none().then_some(r)
}

fn state_path() -> std::path::PathBuf { paths::config_dir().join("update-check.json") }
fn load() -> Value { std::fs::read_to_string(state_path()).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or(Value::Object(Default::default())) }
fn store(v: &Value) { let _ = paths::write_private(&state_path(), &v.to_string()); }

pub fn due() -> bool { util::now() - load().get("last_check").and_then(Value::as_f64).unwrap_or(0.0) >= INTERVAL }

/// True the first time a given version is found, so the notification appears once per version.
pub fn should_notify(version: &str) -> bool {
    let mut st = load();
    if st.get("notified").and_then(Value::as_str) == Some(version) { return false; }
    st["notified"] = Value::String(version.into());
    store(&st);
    true
}

pub fn newest_from(releases: &[Value]) -> Option<UpdateInfo> {
    let mut best: Option<((u32, u32, u32), &Value)> = None;
    for r in releases {
        let tag = r.get("tag_name").and_then(Value::as_str).unwrap_or("");
        if !tag.starts_with(TAG_PREFIX) || r.get("draft").and_then(Value::as_bool) == Some(true) { continue; }   // 0.x releases are pre-releases on purpose
        if let Some(v) = parse(&tag[TAG_PREFIX.len()..]) { if best.as_ref().is_none_or(|(b, _)| v > *b) { best = Some((v, r)); } }
    }
    best.map(|((a, b, c), r)| UpdateInfo {
        version: format!("{a}.{b}.{c}"), tag: r["tag_name"].as_str().unwrap_or("").into(),
        notes: util::truncate(r.get("body").and_then(Value::as_str).unwrap_or(""), 1500),
        page_url: r.get("html_url").and_then(Value::as_str).unwrap_or(RELEASES_PAGE).into(),
    })
}

/// The newer release, or None (up to date, offline, or checked less than a day ago).
pub fn check(force: bool) -> Option<UpdateInfo> {
    if !force && !due() { return None; }
    let agent = ureq::AgentBuilder::new().timeout(Duration::from_secs(15)).user_agent("SSMConnect-Linux").build();
    let text = agent.get(&format!("https://api.github.com/repos/{REPO}/releases?per_page=30")).set("Accept", "application/vnd.github+json").call().ok()?.into_string().ok()?;
    let releases: Vec<Value> = serde_json::from_str(&text).ok()?;
    let mut st = load();
    st["last_check"] = Value::from(util::now());
    store(&st);
    let info = newest_from(&releases)?;
    (parse(&info.version)? > parse(VERSION)?).then_some(info)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn newest_linux_release_only() {
        let rel: Vec<Value> = serde_json::from_str(r#"[{"tag_name":"v1.0.42","html_url":"w"},{"tag_name":"linux-v0.0.9","prerelease":true,"html_url":"a"},
            {"tag_name":"linux-v0.0.10","prerelease":true,"html_url":"b","body":"n"},{"tag_name":"linux-v9.9.9","draft":true}]"#).unwrap();
        let i = newest_from(&rel).unwrap();
        assert_eq!((i.version.as_str(), i.page_url.as_str()), ("0.0.10", "b"));
        assert!(newest_from(&rel[..1]).is_none());
        assert_eq!(parse("linux-v1.2.3"), None);
        assert_eq!(parse("1.2.3"), Some((1, 2, 3)));
    }
}
