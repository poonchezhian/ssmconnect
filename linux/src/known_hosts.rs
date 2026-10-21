//! Each server has an identity (its SSH host key). SSH remembers it in ~/.ssh/known_hosts the first time it connects and refuses to
//! connect later if it changes, which protects against someone sitting in the middle. This module lets the person look at, confirm,
//! and forget those identities from a window, instead of editing `known_hosts` by hand.
//!
//! Every SSMConnect tunnel is 127.0.0.1:<port>, so the address cannot tell two servers apart. Each server is therefore filed under its
//! own name (`HostKeyAlias` in the ssh settings SSMConnect writes, see `ssh_config`).

use crate::{applog, paths};
use base64::Engine;
use sha2::{Digest, Sha256};
use std::path::PathBuf;
use std::process::Stdio;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HostKey { pub key_type: String, pub b64: String }

impl HostKey {
    /// The fingerprint ssh itself shows: SHA256 of the key, base64 without padding.
    pub fn fingerprint(&self) -> String {
        match base64::engine::general_purpose::STANDARD.decode(&self.b64) {
            Ok(blob) => format!("SHA256:{}", base64::engine::general_purpose::STANDARD_NO_PAD.encode(Sha256::digest(&blob))),
            Err(_) => "(unreadable key)".into(),
        }
    }
    /// A short name for the person: ED25519, ECDSA, RSA.
    pub fn nice_type(&self) -> &'static str {
        match self.key_type.as_str() { "ssh-ed25519" => "ED25519", "ssh-rsa" => "RSA", t if t.starts_with("ecdsa-") => "ECDSA", _ => "other" }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Trust {
    /// SSH has never seen this server's identity.
    Unknown,
    /// An identity is saved (and, when the server could be asked, it still matches).
    Trusted,
    /// The server now presents a different identity from the one saved.
    Changed,
}

pub fn path() -> PathBuf { paths::ssh_dir().join("known_hosts") }

fn parse_key_line(line: &str) -> Option<HostKey> {
    let f: Vec<&str> = line.split_whitespace().collect();
    // "<host> <type> <base64> [comment]", also with a leading "@cert-authority"/"@revoked" marker that we ignore
    if f.len() < 3 || f[0].starts_with('#') || f[0].starts_with('@') { return None; }
    f[1].starts_with("ssh-").then_some(()).or_else(|| f[1].starts_with("ecdsa-").then_some(()))?;
    Some(HostKey { key_type: f[1].into(), b64: f[2].into() })
}

/// The identities saved for this name (works with hashed known_hosts files too, because ssh-keygen does the lookup).
pub fn stored(alias: &str) -> Vec<HostKey> {
    let p = path();
    if !p.exists() { return vec![]; }
    let Ok(out) = crate::host::command("ssh-keygen").args(["-F", alias, "-f"]).arg(&p).stdin(Stdio::null()).stderr(Stdio::null()).output() else { return vec![] };
    String::from_utf8_lossy(&out.stdout).lines().filter_map(parse_key_line).collect()
}

/// Asks the server (through its open tunnel) for its identity.
pub fn scan(port: u16) -> Result<Vec<HostKey>, String> {
    let out = crate::host::command("ssh-keyscan").args(["-T", "10", "-t", "ed25519,ecdsa,rsa", "-p", &port.to_string(), "127.0.0.1"]).stdin(Stdio::null()).output()
        .map_err(|e| format!("Could not run ssh-keyscan ({e}). Is the OpenSSH client installed?"))?;
    let keys: Vec<HostKey> = String::from_utf8_lossy(&out.stdout).lines().filter_map(parse_key_line).collect();
    if keys.is_empty() { Err("The server did not answer. Wait until its status is green (Connected), then try again.".into()) } else { Ok(keys) }
}

/// Compares what is saved with what the server shows now (`scanned`, when it could be asked).
pub fn status(alias: &str, scanned: Option<&[HostKey]>) -> Trust {
    let saved = stored(alias);
    if saved.is_empty() { return Trust::Unknown; }
    match scanned {
        Some(now) if !now.iter().any(|k| saved.contains(k)) => Trust::Changed,
        _ => Trust::Trusted,
    }
}

fn backup() {
    let p = path();
    if p.exists() { let _ = std::fs::copy(&p, p.with_file_name("known_hosts.ssmconnect.bak")); }
}

/// Saves the server's identity under its own name. A copy of known_hosts is kept as known_hosts.ssmconnect.bak.
pub fn trust(alias: &str, keys: &[HostKey]) -> Result<(), String> {
    if alias.is_empty() || alias.contains(char::is_whitespace) { return Err("That server name cannot be used.".into()); }
    let p = path();
    let existing = std::fs::read_to_string(&p).unwrap_or_default();
    backup();
    let mut text = existing;
    if !text.is_empty() && !text.ends_with('\n') { text.push('\n'); }
    let saved = stored(alias);
    for k in keys.iter().filter(|k| !saved.contains(k)) { text += &format!("{alias} {} {}\n", k.key_type, k.b64); }
    paths::write_private(&p, &text).map_err(|e| format!("Could not save to {}: {e}", p.display()))?;
    applog::info(&format!("Trusted the identity of {alias}"));
    Ok(())
}

/// Removes the saved identity of this server (ssh will ask again, or SSMConnect will, next time).
pub fn forget(alias: &str) -> Result<(), String> {
    let p = path();
    if !p.exists() { return Ok(()); }
    backup();
    let out = crate::host::command("ssh-keygen").args(["-R", alias, "-f"]).arg(&p).stdin(Stdio::null()).output().map_err(|e| format!("Could not run ssh-keygen ({e})."))?;
    let _ = std::fs::remove_file(p.with_file_name("known_hosts.old"));   // ssh-keygen leaves its own copy; ours is kept as known_hosts.ssmconnect.bak
    if out.status.success() { applog::info(&format!("Forgot the identity of {alias}")); Ok(()) } else { Err("Could not remove the saved identity.".into()) }
}
