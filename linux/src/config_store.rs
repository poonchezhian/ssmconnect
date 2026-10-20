//! Per-user settings in ~/.config/ssmconnect.

use crate::models::AppConfig;
use crate::{applog, paths};
use std::fs;

/// The saved config, an empty one if none exists yet, or None if the file is unreadable.
pub fn load() -> Option<AppConfig> {
    let path = paths::config_path();
    if !path.exists() { return Some(AppConfig::default()); }
    let mut cfg: AppConfig = serde_json::from_str(&fs::read_to_string(&path).ok()?).ok()?;
    for t in &mut cfg.tunnels {   // settings from before per-server accounts: copy the default onto each tunnel
        if t.profile.is_empty() { t.profile = cfg.profile.clone(); }
        if t.region.is_empty() { t.region = cfg.region.clone(); }
    }
    Some(cfg)
}

/// Like `load`, but a damaged file is copied aside first so nothing is silently lost. Returns (config, warning).
pub fn load_or_recover() -> (AppConfig, Option<String>) {
    if let Some(c) = load() { return (c, None); }
    let path = paths::config_path();
    let mut warning = String::from("Your saved settings file could not be read, so SSMConnect is starting with an empty list.");
    let copy = path.with_file_name(format!("config.corrupt-{}.json", crate::util::now() as u64));
    if fs::copy(&path, &copy).is_ok() { warning += &format!(" The damaged file was kept as {}.", copy.display()); }
    applog::warn(&warning);
    (AppConfig::default(), Some(warning))
}

pub fn save(cfg: &AppConfig) -> std::io::Result<()> {
    let path = paths::config_path();
    if path.exists() {
        let _ = fs::copy(&path, path.with_file_name("config.json.bak"));
    }
    paths::write_private(&path, &serde_json::to_string_pretty(cfg).map_err(std::io::Error::other)?)
}
