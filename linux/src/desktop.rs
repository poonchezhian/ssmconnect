//! Desktop integration: the launcher entry, 'start when I sign in', and the full clean-up used by Help > Clean up.

use crate::{paths, ssh_config, APP_ID};
use std::fs;
use std::path::PathBuf;

fn exec_line(extra: &str) -> String {
    let exe = crate::aws_cli::which("ssmconnect").or_else(|| std::env::current_exe().ok()).unwrap_or_else(|| PathBuf::from("ssmconnect"));
    format!("{} {}", exe.display(), extra).trim().to_string()
}

fn entry(extra: &str) -> String {
    format!("[Desktop Entry]\nType=Application\nName=SSMConnect\nComment=SSH and Remote Desktop to AWS servers over Session Manager\nExec={}\nIcon={APP_ID}\n\
             Terminal=false\nCategories=Network;RemoteAccess;\nStartupNotify=true\nKeywords=aws;ssm;ssh;rdp;tunnel;\n", exec_line(extra))
}

pub fn autostart_enabled() -> bool { paths::autostart_file().exists() }

pub fn set_autostart(on: bool) -> std::io::Result<()> {
    let f = paths::autostart_file();
    if on {
        fs::create_dir_all(f.parent().unwrap())?;
        fs::write(&f, entry("--background") + "X-GNOME-Autostart-enabled=true\n")
    } else {
        match fs::remove_file(&f) { Err(e) if e.kind() != std::io::ErrorKind::NotFound => Err(e), _ => Ok(()) }
    }
}

/// Adds SSMConnect to the application menu for this user (useful when run from a tarball or a source checkout).
pub fn create_launcher() -> std::io::Result<PathBuf> {
    let f = paths::applications_dir().join(format!("{APP_ID}.desktop"));
    fs::create_dir_all(f.parent().unwrap())?;
    fs::write(&f, entry(""))?;
    Ok(f)
}

/// Removes only the current user's SSMConnect traces: the ssh include, autostart, launcher and (optionally kept) settings.
/// The user's own ssh hosts, AWS profiles and servers in AWS are never touched. Returns what was done.
pub fn cleanup_user(keep_settings: bool) -> Vec<String> {
    let mut done = vec![];
    match ssh_config::remove() { Ok(()) => done.push("Disconnected SSMConnect from your ssh configuration".to_string()), Err(e) => done.push(format!("Could not update your ssh configuration: {e}")) }
    for f in [paths::autostart_file(), paths::applications_dir().join(format!("{APP_ID}.desktop"))] {
        if f.exists() && fs::remove_file(&f).is_ok() { done.push(format!("Removed {}", f.display())); }
    }
    if !keep_settings {
        for d in [paths::config_dir(), paths::state_dir()] { let _ = fs::remove_dir_all(d); }
        done.push("Removed your SSMConnect settings and log".into());
    }
    done
}
