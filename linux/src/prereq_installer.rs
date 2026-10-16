//! Installs the AWS CLI v2 and the Session Manager plugin for the current user only, with no administrator rights.
//!
//! Both come straight from Amazon over HTTPS (awscli.amazonaws.com and the session-manager-downloads bucket) and are placed in
//! ~/.local/share/ssmconnect, with links in ~/.local/share/ssmconnect/bin. If your distribution packages them, prefer your
//! package manager; SSMConnect uses whichever it finds first.
//!
//! Every download is checked against the OpenPGP signature Amazon publishes next to it (`<file>.sig`), with public keys built
//! into the program and pinned by fingerprint (see `signature.rs`). A download whose signature is missing or does not match
//! is refused and nothing is installed.

use crate::signature::{self, TrustedKey};
use crate::{applog, aws_cli, paths};
use std::io::{Cursor, Read, Write};
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

const ALLOWED: [&str; 2] = ["https://awscli.amazonaws.com/", "https://s3.amazonaws.com/session-manager-downloads/"];

fn arch() -> Result<&'static str, String> {
    match std::env::consts::ARCH {
        "x86_64" => Ok("x86_64"),
        "aarch64" => Ok("aarch64"),
        other => Err(format!("This computer's processor ({other}) is not supported by Amazon's Linux downloads.")),
    }
}

pub fn aws_cli_url() -> Result<String, String> { Ok(format!("https://awscli.amazonaws.com/awscli-exe-linux-{}.zip", arch()?)) }

pub fn plugin_url() -> Result<String, String> {
    let folder = if arch()? == "x86_64" { "ubuntu_64bit" } else { "ubuntu_arm64" };
    Ok(format!("https://s3.amazonaws.com/session-manager-downloads/plugin/latest/{folder}/session-manager-plugin.deb"))
}

fn download(url: &str, progress: &dyn Fn(String), what: &str) -> Result<Vec<u8>, String> {
    if !ALLOWED.iter().any(|a| url.starts_with(a)) { return Err("Refusing to download from an unexpected address.".into()); }
    progress(format!("Downloading {what}..."));
    let agent = ureq::AgentBuilder::new().timeout_connect(Duration::from_secs(30)).timeout_read(Duration::from_secs(60)).user_agent("SSMConnect-Linux").build();
    let resp = agent.get(url).call().map_err(|e| e.to_string())?;
    let total: usize = resp.header("Content-Length").and_then(|v| v.parse().ok()).unwrap_or(0);
    let mut r = resp.into_reader();
    let (mut buf, mut chunk) = (Vec::new(), vec![0u8; 256 * 1024]);
    loop {
        let n = r.read(&mut chunk).map_err(|e| e.to_string())?;
        if n == 0 { break; }
        buf.extend_from_slice(&chunk[..n]);
        if total > 0 { progress(format!("Downloading {what}... {}%", buf.len() * 100 / total)); }
    }
    Ok(buf)
}

/// Downloads `url` and its `.sig`, and returns the data only if the signature checks out against `key`.
pub fn download_verified(url: &str, key: &TrustedKey, progress: &dyn Fn(String), what: &str) -> Result<Vec<u8>, String> {
    let data = download(url, progress, what)?;
    progress(format!("Checking Amazon's signature on {what}..."));
    let sig = download(&format!("{url}.sig"), &|_| {}, "the signature").map_err(|e| format!("Could not get Amazon's signature for {what} ({e}), so it was not installed."))?;
    signature::verify_detached(&data, &sig, key).inspect_err(|e| applog::error(&format!("Signature check failed for {url}: {e}")))?;
    applog::info(&format!("Signature of {url} verified ({}).", key.fingerprint));
    Ok(data)
}

fn extract_zip(data: &[u8], dest: &Path) -> Result<(), String> {
    let mut z = zip::ZipArchive::new(Cursor::new(data)).map_err(|e| e.to_string())?;
    for i in 0..z.len() {
        let mut f = z.by_index(i).map_err(|e| e.to_string())?;
        let rel = f.enclosed_name().ok_or("The download contains an unsafe path.")?;   // rejects ../ and absolute paths
        let target = dest.join(rel);
        if f.is_dir() { std::fs::create_dir_all(&target).map_err(|e| e.to_string())?; continue; }
        if let Some(p) = target.parent() { std::fs::create_dir_all(p).map_err(|e| e.to_string())?; }
        let mode = f.unix_mode().unwrap_or(0o644);
        let mut out = std::fs::File::create(&target).map_err(|e| e.to_string())?;
        std::io::copy(&mut f, &mut out).map_err(|e| e.to_string())?;
        std::fs::set_permissions(&target, std::fs::Permissions::from_mode(mode & 0o755 | 0o600)).map_err(|e| e.to_string())?;
    }
    Ok(())
}

fn temp_dir(tag: &str) -> Result<PathBuf, String> {
    let d = paths::data_dir().join(format!(".tmp-{tag}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&d);
    std::fs::create_dir_all(&d).map_err(|e| e.to_string())?;
    Ok(d)
}

pub fn install_aws_cli(progress: &dyn Fn(String)) -> Result<(), String> {
    let data_dir = paths::data_dir();
    let bin = data_dir.join("bin");
    std::fs::create_dir_all(&bin).map_err(|e| e.to_string())?;
    let data = download_verified(&aws_cli_url()?, &signature::AWS_CLI, progress, "the AWS CLI")?;
    progress("Installing the AWS CLI...".into());
    let tmp = temp_dir("awscli")?;
    let res = (|| {
        extract_zip(&data, &tmp)?;
        let out = Command::new(tmp.join("aws/install")).arg("-i").arg(data_dir.join("aws-cli")).arg("-b").arg(&bin).arg("--update").output().map_err(|e| e.to_string())?;
        if !out.status.success() {
            let msg = String::from_utf8_lossy(if out.stderr.is_empty() { &out.stdout } else { &out.stderr }).trim().chars().take(300).collect::<String>();
            return Err(format!("The AWS CLI installer failed: {msg}"));
        }
        Ok(())
    })();
    let _ = std::fs::remove_dir_all(&tmp);
    res?;
    applog::info(&format!("Installed the AWS CLI to {}", data_dir.join("aws-cli").display()));
    Ok(())
}

/// The data.tar.* member of a .deb (an `ar` archive), without dpkg.
fn deb_data(deb: &[u8]) -> Result<(String, &[u8]), String> {
    if !deb.starts_with(b"!<arch>\n") { return Err("The plugin download is not a valid package.".into()); }
    let mut pos = 8;
    while pos + 60 <= deb.len() {
        let name = String::from_utf8_lossy(&deb[pos..pos + 16]).trim().trim_end_matches('/').to_string();
        let size: usize = String::from_utf8_lossy(&deb[pos + 48..pos + 58]).trim().parse().map_err(|_| "The plugin package is damaged.")?;
        let body = deb.get(pos + 60..pos + 60 + size).ok_or("The plugin package is damaged.")?;
        if name.starts_with("data.tar") { return Ok((name, body)); }
        pos += 60 + size + (size % 2);
    }
    Err("The plugin package has no program files in it.".into())
}

fn find_file(dir: &Path, name: &str) -> Option<PathBuf> {
    for e in std::fs::read_dir(dir).ok()?.flatten() {
        let p = e.path();
        if e.file_type().ok()?.is_dir() { if let Some(f) = find_file(&p, name) { return Some(f); } }
        else if p.file_name().is_some_and(|n| n == name) { return Some(p); }
    }
    None
}

pub fn install_plugin(progress: &dyn Fn(String)) -> Result<(), String> {
    let data_dir = paths::data_dir();
    let deb = download_verified(&plugin_url()?, &signature::SESSION_MANAGER_PLUGIN, progress, "the Session Manager plugin")?;
    progress("Installing the Session Manager plugin...".into());
    let (name, body) = deb_data(&deb)?;
    let tmp = temp_dir("plugin")?;
    let res = (|| {
        let tarball = tmp.join(&name);
        std::fs::File::create(&tarball).and_then(|mut f| f.write_all(body)).map_err(|e| e.to_string())?;
        let out = Command::new("tar").arg("-xf").arg(&tarball).arg("-C").arg(&tmp).output().map_err(|e| format!("Could not unpack the plugin ({e}). Is `tar` installed?"))?;
        if !out.status.success() { return Err("Could not unpack the plugin package.".to_string()); }
        let src = find_file(&tmp.join("usr"), "session-manager-plugin").ok_or("The plugin package did not contain the plugin program.")?;
        let target = data_dir.join("plugin/bin/session-manager-plugin");
        std::fs::create_dir_all(target.parent().unwrap()).map_err(|e| e.to_string())?;
        let part = target.with_extension("tmp");
        std::fs::copy(&src, &part).map_err(|e| e.to_string())?;
        std::fs::set_permissions(&part, std::fs::Permissions::from_mode(0o755)).map_err(|e| e.to_string())?;
        std::fs::rename(&part, &target).map_err(|e| e.to_string())?;
        let link = data_dir.join("bin/session-manager-plugin");
        std::fs::create_dir_all(link.parent().unwrap()).map_err(|e| e.to_string())?;
        let _ = std::fs::remove_file(&link);
        std::os::unix::fs::symlink(&target, &link).map_err(|e| e.to_string())?;
        applog::info(&format!("Installed the Session Manager plugin to {}", target.display()));
        Ok(())
    })();
    let _ = std::fs::remove_dir_all(&tmp);
    res
}

/// Installs whatever is missing. Returns what was installed; the error is a plain message.
pub fn install_missing(progress: &dyn Fn(String)) -> Result<Vec<&'static str>, String> {
    let mut done = vec![];
    let wrap = |e: String| format!("Could not install the missing components ({e}). Check your internet connection, or install them with your package manager.");
    if aws_cli::find_aws().is_none() { install_aws_cli(progress).map_err(wrap)?; done.push("AWS CLI"); }
    if aws_cli::find_plugin().is_none() { install_plugin(progress).map_err(wrap)?; done.push("Session Manager plugin"); }
    Ok(done)
}
