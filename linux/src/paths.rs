//! Per-user locations following the XDG Base Directory specification. Nothing is shared between users.

use std::fs;
use std::io::Write;
use std::os::unix::fs::{OpenOptionsExt, PermissionsExt};
use std::path::{Path, PathBuf};

pub fn home() -> PathBuf {
    std::env::var_os("HOME").map(PathBuf::from).filter(|p| p.is_absolute()).unwrap_or_else(|| PathBuf::from("/"))
}

fn xdg(var: &str, fallback: &str) -> PathBuf {
    match std::env::var_os(var) {
        Some(v) if Path::new(&v).is_absolute() => PathBuf::from(v),
        _ => home().join(fallback),
    }
}

/// config.json, discovery.json, update-check.json
pub fn config_dir() -> PathBuf { xdg("XDG_CONFIG_HOME", ".config").join("ssmconnect") }
/// status.json and the log
pub fn state_dir() -> PathBuf { xdg("XDG_STATE_HOME", ".local/state").join("ssmconnect") }
/// Tools SSMConnect installed for the user (AWS CLI, Session Manager plugin)
pub fn data_dir() -> PathBuf { xdg("XDG_DATA_HOME", ".local/share").join("ssmconnect") }
pub fn autostart_file() -> PathBuf { xdg("XDG_CONFIG_HOME", ".config").join("autostart").join(format!("{}.desktop", crate::APP_ID)) }
pub fn applications_dir() -> PathBuf { xdg("XDG_DATA_HOME", ".local/share").join("applications") }
pub fn config_path() -> PathBuf { config_dir().join("config.json") }
pub fn status_path() -> PathBuf { state_dir().join("status.json") }
pub fn discovery_path() -> PathBuf { config_dir().join("discovery.json") }
pub fn usage_path() -> PathBuf { state_dir().join("usage.json") }
pub fn log_path() -> PathBuf { state_dir().join("ssmconnect.log") }
pub fn aws_dir() -> PathBuf { home().join(".aws") }
/// An environment setting is followed only while the file it names exists; a leftover value (a removed local emulator) falls back to the standard file.
pub fn aws_config_path() -> PathBuf {
    std::env::var_os("AWS_CONFIG_FILE").filter(|v| !v.is_empty()).map(PathBuf::from).filter(|p| p.exists()).unwrap_or_else(|| aws_dir().join("config"))
}
pub fn aws_credentials_path() -> PathBuf {
    std::env::var_os("AWS_SHARED_CREDENTIALS_FILE").filter(|v| !v.is_empty()).map(PathBuf::from).filter(|p| p.exists()).unwrap_or_else(|| aws_dir().join("credentials"))
}
pub fn ssh_dir() -> PathBuf { home().join(".ssh") }

/// Atomic write with owner-only permissions (these files can name servers and keys).
pub fn write_private(path: &Path, text: &str) -> std::io::Result<()> {
    if let Some(parent) = path.parent() { fs::create_dir_all(parent)?; }
    let tmp = path.with_file_name(format!("{}.tmp", path.file_name().map(|n| n.to_string_lossy()).unwrap_or_default()));
    {
        let mut f = fs::OpenOptions::new().write(true).create(true).truncate(true).mode(0o600).open(&tmp)?;
        f.write_all(text.as_bytes())?;
    }
    fs::set_permissions(&tmp, fs::Permissions::from_mode(0o600))?;
    fs::rename(tmp, path)
}
