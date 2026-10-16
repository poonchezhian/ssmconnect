//! Makes the servers usable from plain OpenSSH, the way the Windows edition configured PuTTY and WinSCP.
//!
//! SSMConnect writes its own file, ~/.ssh/ssmconnect.conf, with one `Host ssc-<server>` block per SSH server and adds one
//! `Include` line to ~/.ssh/config (a backup is made first). The user's own hosts are never modified. After setup, any tool
//! that reads ssh_config works: `ssh ssc-web-1`, `scp`, `rsync -e ssh`, VS Code Remote-SSH, the file manager's sftp:// ...

use crate::models::TunnelConfig;
use crate::paths;
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

pub const INCLUDE_LINE: &str = "Include ~/.ssh/ssmconnect.conf";
pub const MARKER: &str = "# SSMConnect (managed): the next line loads the servers SSMConnect set up. Remove it to disconnect SSMConnect from ssh.";
const HEADER: &str = "# Written by SSMConnect. Do not edit: this file is rewritten whenever you save servers.\n";

pub fn conf_path() -> PathBuf { paths::ssh_dir().join("ssmconnect.conf") }
pub fn user_config_path() -> PathBuf { paths::ssh_dir().join("config") }

/// A short, shell-friendly name: "Web Server 1" -> "ssc-web-server-1".
pub fn host_alias(name: &str, instance_id: &str) -> String {
    let mut slug = String::new();
    for c in name.to_lowercase().chars() {
        if c.is_ascii_alphanumeric() { slug.push(c); } else if !slug.ends_with('-') { slug.push('-'); }
    }
    let slug: String = slug.trim_matches('-').chars().take(40).collect::<String>().trim_matches('-').to_string();
    format!("ssc-{}", if slug.is_empty() { if instance_id.is_empty() { "server".into() } else { instance_id.to_lowercase() } } else { slug })
}

/// instance_id -> unique alias (what the dashboard shows and `ssh` accepts), even when two servers share a name.
pub fn aliases_for(tunnels: &[TunnelConfig]) -> HashMap<String, String> {
    let mut seen: Vec<String> = vec![];
    let mut out = HashMap::new();
    for t in tunnels.iter().filter(|t| !t.is_rdp()) {
        let mut a = host_alias(&t.name, &t.instance_id);
        if seen.contains(&a) {
            let tail: String = t.instance_id.chars().rev().take(6).collect::<Vec<_>>().into_iter().rev().collect();
            a = format!("{a}-{}", if tail.is_empty() { t.local_port.to_string() } else { tail });
        }
        seen.push(a.clone());
        out.insert(t.instance_id.clone(), a);
    }
    out
}

pub fn alias_of(t: &TunnelConfig, everyone: &[TunnelConfig]) -> String {
    aliases_for(everyone).get(&t.instance_id).cloned().unwrap_or_else(|| host_alias(&t.name, &t.instance_id))
}

fn quote(p: &str) -> String { if p.chars().any(char::is_whitespace) { format!("\"{}\"", p.replace('"', "\\\"")) } else { p.to_string() } }

pub fn render(tunnels: &[TunnelConfig]) -> String {
    let aliases = aliases_for(tunnels);
    let mut out = vec![HEADER.to_string()];
    for t in tunnels.iter().filter(|t| !t.is_rdp() && t.enabled) {
        let alias = &aliases[&t.instance_id];
        out.push(format!("Host {alias}"));
        out.push(format!("    # {} ({}){}", t.name.replace(['\n', '\r'], " "), t.instance_id, if t.environment.is_empty() { String::new() } else { format!(" [{}]", t.environment) }));
        out.push("    HostName 127.0.0.1".into());
        out.push(format!("    Port {}", t.local_port));
        out.push(format!("    User {}", t.user_name));
        if let Some(k) = t.key_file.as_deref().filter(|k| !k.is_empty() && !k.contains(['\n', '\r'])) {
            out.push(format!("    IdentityFile {}", quote(k)));
            out.push("    IdentitiesOnly yes".into());
        }
        // every tunnel is 127.0.0.1:<port>, so each server needs its own identity in known_hosts
        out.push(format!("    HostKeyAlias {alias}-{}", t.instance_id));
        out.push("    StrictHostKeyChecking accept-new".into());
        out.push("    ServerAliveInterval 30".into());
        out.push(String::new());
    }
    out.join("\n")
}

/// Rewrites ssmconnect.conf and makes sure ~/.ssh/config includes it (a .ssmconnect.bak copy is kept).
pub fn apply(tunnels: &[TunnelConfig]) -> std::io::Result<()> {
    use std::os::unix::fs::DirBuilderExt;
    let d = paths::ssh_dir();
    if !d.exists() { fs::DirBuilder::new().mode(0o700).recursive(true).create(&d)?; }
    paths::write_private(&conf_path(), &render(tunnels))?;
    let cfg = user_config_path();
    let text = fs::read(&cfg).map(|b| String::from_utf8_lossy(&b).into_owned()).unwrap_or_default();
    if text.contains(INCLUDE_LINE) { return Ok(()); }
    if cfg.exists() { fs::copy(&cfg, d.join("config.ssmconnect.bak"))?; }
    // Include must come before any Host/Match block to apply to every host
    paths::write_private(&cfg, &format!("{MARKER}\n{INCLUDE_LINE}\n\n{text}"))
}

/// Disconnects SSMConnect from ssh: deletes ssmconnect.conf and the Include line. The user's own hosts stay.
pub fn remove() -> std::io::Result<()> {
    let cfg = user_config_path();
    if let Ok(b) = fs::read(&cfg) {
        let text = String::from_utf8_lossy(&b).into_owned();
        if text.contains(INCLUDE_LINE) {
            fs::copy(&cfg, paths::ssh_dir().join("config.ssmconnect.bak"))?;
            let kept: Vec<&str> = text.lines().filter(|l| l.trim() != INCLUDE_LINE && l.trim() != MARKER).collect();
            let body = kept.join("\n");
            let body = body.trim_start_matches('\n');
            paths::write_private(&cfg, &(body.to_string() + if body.is_empty() { "" } else { "\n" }))?;
        }
    }
    match fs::remove_file(conf_path()) { Err(e) if e.kind() != std::io::ErrorKind::NotFound => Err(e), _ => Ok(()) }
}

fn shell_quote(s: &str) -> String {
    if !s.is_empty() && s.chars().all(|c| c.is_ascii_alphanumeric() || "@%+=:,./-_".contains(c)) { s.to_string() } else { format!("'{}'", s.replace('\'', "'\"'\"'")) }
}

pub fn ssh_command_text(alias: &str) -> String { format!("ssh {}", shell_quote(alias)) }

/// The alias is used as the host so ssh_config supplies the port and key to the file manager.
pub fn sftp_url(user: &str, alias: &str) -> String { format!("sftp://{user}@{alias}/") }
