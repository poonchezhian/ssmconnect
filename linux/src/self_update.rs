//! "Update now": downloads the newer installer from the project's GitHub release, checks it, closes this copy, runs the installer over it and starts the new version.
//!
//! The installer is accepted only if its OpenPGP signature is valid for the built-in SSMConnect release key *and* its SHA-256 equals the one in the release's
//! `SHA256SUMS.txt`. Only a copy installed by the installer in the person's own `~/.local` is updated this way; installs made by a package manager or for all
//! users are left to dnf/apt (or the installer run by hand), and the person is sent to the download page instead.

use crate::update_checker::UpdateInfo;
use crate::{applog, installer, paths, signature};
use sha2::{Digest, Sha256};
use std::io::Read;
use std::os::unix::fs::PermissionsExt;
use std::os::unix::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;

const ALLOWED: &str = "https://github.com/poonchezhian/ssmconnect/releases/download/";

/// The folder this copy of SSMConnect was installed into, when the app can update it by itself.
pub fn updatable_prefix() -> Option<PathBuf> {
    let e = installer::existing_install(None)?;
    let exe = std::env::current_exe().ok()?;
    (!e.package_managed && e.prefix == installer::user_prefix() && exe.starts_with(&e.prefix)).then_some(e.prefix)
}

fn get(url: &str, what: &str, progress: &dyn Fn(String)) -> Result<Vec<u8>, String> {
    if !url.starts_with(ALLOWED) { return Err("Refusing to download from an unexpected address.".into()); }
    let agent = ureq::AgentBuilder::new().timeout_connect(Duration::from_secs(30)).timeout_read(Duration::from_secs(60)).user_agent("SSMConnect-Linux").build();
    let resp = agent.get(url).call().map_err(|e| format!("Could not download {what}: {e}"))?;
    let total: usize = resp.header("Content-Length").and_then(|v| v.parse().ok()).unwrap_or(0);
    let (mut r, mut buf, mut chunk) = (resp.into_reader(), Vec::new(), vec![0u8; 256 * 1024]);
    loop {
        let n = r.read(&mut chunk).map_err(|e| format!("Could not download {what}: {e}"))?;
        if n == 0 { break; }
        buf.extend_from_slice(&chunk[..n]);
        if total > 0 { progress(format!("Downloading {what}... {}%", buf.len() * 100 / total)); }
    }
    Ok(buf)
}

/// The SHA-256 listed for `name` in a `sha256sum`-style file.
pub fn sum_for(sums: &str, name: &str) -> Option<String> {
    sums.lines().find_map(|l| { let (h, n) = l.split_once(char::is_whitespace)?; (n.trim().trim_start_matches('*') == name).then(|| h.to_ascii_lowercase()) })
}

/// Downloads and checks the installer for `info`; returns the file, ready to run.
pub fn download(info: &UpdateInfo, progress: &dyn Fn(String)) -> Result<PathBuf, String> {
    let name = format!("ssmconnect-{}-linux-{}.bin", info.version, std::env::consts::ARCH);
    let url = |n: &str| info.asset(n).ok_or_else(|| format!("This release has no {n}, so it cannot be installed from here."));
    let (bin_url, sig_url, sums_url) = (url(&name)?, url(&format!("{name}.asc"))?, url("SHA256SUMS.txt")?);
    let data = get(bin_url, "the new version", progress)?;
    progress("Checking the signature...".into());
    let sig = get(sig_url, "the signature", &|_| {})?;
    signature::verify_detached(&data, &sig, &signature::SSMCONNECT_RELEASES).inspect_err(|e| applog::error(&format!("Update check failed for {bin_url}: {e}")))?;
    let sums = String::from_utf8_lossy(&get(sums_url, "the checksums", &|_| {})?).into_owned();
    let want = sum_for(&sums, &name).ok_or("The release's checksum list does not mention the installer, so it was not installed.")?;
    let got = crate::util::hex(&Sha256::digest(&data));
    if got != want { return Err("The download does not match the published checksum, so it was not installed.".into()); }
    applog::info(&format!("Update {} downloaded and verified (signature and SHA-256).", info.version));
    let dir = paths::state_dir().join("update");
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let file = dir.join(&name);
    std::fs::write(&file, &data).map_err(|e| e.to_string())?;
    std::fs::set_permissions(&file, std::fs::Permissions::from_mode(0o755)).map_err(|e| e.to_string())?;
    Ok(file)
}

/// Starts the hand-over in the background: waits for this process to exit, runs the installer in update mode (it keeps servers and settings), starts the
/// new version and removes the downloaded file. The caller quits the app right after this returns.
pub fn apply(installer_file: &Path, prefix: &Path) -> Result<(), String> {
    let script = r#"while kill -0 "$1" 2>/dev/null; do sleep 0.5; done
"$2" --text --yes --update-only --prefix "$3" >>"$5" 2>&1
rm -f "$2"
setsid "$4" >/dev/null 2>&1 &"#;
    let log = paths::log_path();
    Command::new("sh").arg("-c").arg(script).arg("sh")
        .arg(std::process::id().to_string()).arg(installer_file).arg(prefix).arg(prefix.join("bin/ssmconnect")).arg(&log)
        .stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null()).process_group(0)
        .spawn().map(|_| ()).map_err(|e| format!("Could not start the update: {e}"))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn finds_the_sum_for_a_file() {
        let sums = "AAAA  other.deb\nBb12 *ssmconnect-1.0.0-linux-x86_64.bin\n";
        assert_eq!(sum_for(sums, "ssmconnect-1.0.0-linux-x86_64.bin").as_deref(), Some("bb12"));
        assert_eq!(sum_for(sums, "missing"), None);
    }
    #[test]
    fn only_project_release_addresses_are_downloaded() {
        assert!(get("https://example.com/x.bin", "x", &|_| {}).unwrap_err().contains("unexpected"));
    }

    /// Needs the internet (`SSMCONNECT_NET_TESTS=1`): the real 0.5.1 release downloads, verifies against the built-in key and the published checksum, and a damaged copy is refused.
    #[test]
    fn real_release_is_verified() {
        if std::env::var_os("SSMCONNECT_NET_TESTS").is_none() { return; }
        let base = "https://github.com/poonchezhian/ssmconnect/releases/download/linux-v0.5.1/";
        let name = "ssmconnect-0.5.1-linux-x86_64.bin";
        let assets = [name.to_string(), format!("{name}.asc"), "SHA256SUMS.txt".to_string()].into_iter().map(|n| (n.clone(), format!("{base}{n}"))).collect();
        let info = UpdateInfo { version: "0.5.1".into(), tag: "linux-v0.5.1".into(), notes: String::new(), page_url: String::new(), assets };
        let file = download(&info, &|_| {}).unwrap();
        assert!(std::fs::metadata(&file).unwrap().len() > 1_000_000);
        let (data, sig) = (std::fs::read(&file).unwrap(), get(&format!("{base}{name}.asc"), "sig", &|_| {}).unwrap());
        let mut bad = data.clone(); bad[100] ^= 1;
        assert!(signature::verify_detached(&bad, &sig, &signature::SSMCONNECT_RELEASES).is_err());
        let _ = std::fs::remove_file(file);
    }
}
