//! Installing SSMConnect itself, and the tools it needs.
//!
//! Nothing third-party is bundled. Everything SSMConnect depends on is pulled from its own source, before SSMConnect is installed:
//!   * the AWS CLI and the Session Manager plugin come from Amazon (HTTPS, OpenPGP signature checked, see `prereq_installer`);
//!   * OpenSSH, a Remote Desktop client and GTK 4 / libadwaita come from the distribution's own package repositories,
//!     through its package manager (dnf, apt, zypper or pacman), after asking for permission.
//! SSMConnect's own files (the program, desktop entry, icon, documents) are carried inside the program itself, so the single
//! `.bin` installer is all that has to be downloaded.

use crate::aws_cli::which;
use crate::{applog, paths, prereq_installer, APP_ID};
use std::fs;
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::Command;

const DESKTOP: &str = include_str!("../data/io.github.poonchezhian.SSMConnect.desktop");
const METAINFO: &str = include_str!("../data/io.github.poonchezhian.SSMConnect.metainfo.xml");
const ICON: &[u8] = include_bytes!("../data/icons/hicolor/scalable/apps/io.github.poonchezhian.SSMConnect.svg");
pub const LICENSE: &str = include_str!("../LICENSE");
const NOTICE: &str = include_str!("../NOTICE");
const README: &str = include_str!("../README.md");
const CHANGELOG: &str = include_str!("../CHANGELOG.md");
const MANIFEST: &str = "share/ssmconnect/install-manifest.txt";

// ---- package managers -----------------------------------------------------------------------------
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PackageManager { Dnf, Apt, Zypper, Pacman }

impl PackageManager {
    pub fn name(self) -> &'static str { match self { Self::Dnf => "dnf", Self::Apt => "apt", Self::Zypper => "zypper", Self::Pacman => "pacman" } }

    /// The command (program and arguments) that installs packages without asking further questions.
    pub fn install_command(self, pkgs: &[&str]) -> Vec<String> {
        let mut v: Vec<String> = match self {
            Self::Dnf => vec!["dnf", "install", "-y"],
            Self::Apt => vec!["env", "DEBIAN_FRONTEND=noninteractive", "apt-get", "install", "-y"],
            Self::Zypper => vec!["zypper", "--non-interactive", "install"],
            Self::Pacman => vec!["pacman", "-S", "--noconfirm", "--needed"],
        }.into_iter().map(String::from).collect();
        v.extend(pkgs.iter().map(|p| p.to_string()));
        v
    }
}

/// Which package manager this distribution uses, from /etc/os-release (ID and ID_LIKE), else from what is installed.
pub fn detect_package_manager() -> Option<PackageManager> { pm_from(&std::fs::read_to_string("/etc/os-release").unwrap_or_default(), &|n| which(n).is_some()) }

pub fn pm_from(os_release: &str, have: &dyn Fn(&str) -> bool) -> Option<PackageManager> {
    let mut ids: Vec<String> = vec![];
    for line in os_release.lines() {
        if let Some((k, v)) = line.split_once('=') {
            if k == "ID" || k == "ID_LIKE" { ids.extend(v.trim_matches('"').split_whitespace().map(|s| s.to_lowercase())); }
        }
    }
    for id in &ids {
        match id.as_str() {
            "fedora" | "rhel" | "centos" | "rocky" | "almalinux" | "amzn" | "ol" if have("dnf") => return Some(PackageManager::Dnf),
            "debian" | "ubuntu" | "linuxmint" | "pop" | "raspbian" if have("apt-get") => return Some(PackageManager::Apt),
            "opensuse" | "suse" | "opensuse-leap" | "opensuse-tumbleweed" | "sles" if have("zypper") => return Some(PackageManager::Zypper),
            "arch" | "manjaro" | "endeavouros" if have("pacman") => return Some(PackageManager::Pacman),
            _ => {}
        }
    }
    [("dnf", PackageManager::Dnf), ("apt-get", PackageManager::Apt), ("zypper", PackageManager::Zypper), ("pacman", PackageManager::Pacman)]
        .into_iter().find(|(n, _)| have(n)).map(|(_, p)| p)
}

// ---- dependencies ---------------------------------------------------------------------------------
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Source { Amazon, Distribution }

#[derive(Clone, Debug)]
pub struct Dependency {
    pub id: &'static str,
    pub name: &'static str,
    pub why: &'static str,
    pub required: bool,
    pub source: Source,
    /// Where it was found on this computer, if it was.
    pub found: Option<String>,
}

fn packages(id: &str, pm: PackageManager) -> &'static [&'static str] {
    use PackageManager::*;
    match (id, pm) {
        ("ssh", Dnf) | ("ssh", Zypper) => &["openssh-clients"],
        ("ssh", Apt) => &["openssh-client"],
        ("ssh", Pacman) => &["openssh"],
        ("rdp", Dnf) => &["remmina"],
        ("rdp", Apt) | ("rdp", Zypper) | ("rdp", Pacman) => &["remmina"],
        _ => &[],
    }
}

/// Everything SSMConnect needs or can use, with where each one was found.
pub fn dependencies() -> Vec<Dependency> {
    let p = |n: &str| which(n).map(|p| p.display().to_string());
    vec![
        Dependency { id: "ssh", name: "OpenSSH client", why: "runs the connection to your servers", required: true, source: Source::Distribution, found: p("ssh") },
        Dependency { id: "aws", name: "AWS CLI v2", why: "talks to AWS", required: true, source: Source::Amazon, found: crate::aws_cli::find_aws().map(|x| x.display().to_string()) },
        Dependency { id: "plugin", name: "Session Manager plugin", why: "opens the secure tunnel", required: true, source: Source::Amazon, found: crate::aws_cli::find_plugin().map(|x| x.display().to_string()) },
        Dependency { id: "rdp", name: "Remote Desktop client (Remmina)", why: "only for Windows servers", required: false, source: Source::Distribution,
                     found: crate::launchers::find_rdp_client().map(String::from) },
    ]
}

/// How to get administrator rights for installing system packages.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Elevation { Graphical, Terminal }

fn is_root() -> bool { unsafe { libc::geteuid() == 0 } }

/// `command` prefixed so that it runs as root: nothing when already root, `pkexec` (a password dialog) or `sudo` (a terminal prompt).
pub fn elevated(command: Vec<String>, how: Elevation) -> Result<Vec<String>, String> {
    if is_root() { return Ok(command); }
    let tool = match how { Elevation::Graphical => "pkexec", Elevation::Terminal => "sudo" };
    let tool = if which(tool).is_some() { tool } else if which("sudo").is_some() { "sudo" } else {
        return Err("Administrator rights are needed, but neither pkexec nor sudo was found. Install the packages yourself, then run this installer again.".into());
    };
    let mut v = vec![tool.to_string()];
    v.extend(command);
    Ok(v)
}

/// Installs the given distribution packages with the distribution's own package manager (asks for the password).
pub fn install_system_packages(pkgs: &[&str], how: Elevation, progress: &dyn Fn(String)) -> Result<(), String> {
    if pkgs.is_empty() { return Ok(()); }
    let pm = detect_package_manager().ok_or("This distribution's package manager was not recognised. Install these yourself, then run this installer again: ".to_string() + &pkgs.join(", "))?;
    let cmd = elevated(pm.install_command(pkgs), how)?;
    progress(format!("Installing {} with {}... (you may be asked for your password)", pkgs.join(", "), pm.name()));
    applog::info(&format!("Running: {}", cmd.join(" ")));
    let out = Command::new(&cmd[0]).args(&cmd[1..]).stdin(if how == Elevation::Terminal { std::process::Stdio::inherit() } else { std::process::Stdio::null() })
        .output().map_err(|e| format!("Could not run {}: {e}", cmd[0]))?;
    if out.status.success() { return Ok(()); }
    let code = out.status.code().unwrap_or(-1);
    let tail: String = String::from_utf8_lossy(&out.stderr).lines().rev().take(3).collect::<Vec<_>>().into_iter().rev().collect::<Vec<_>>().join(" ");
    Err(match code {
        126 | 127 if cmd[0] == "pkexec" => "The password was not accepted, or the request was cancelled. Nothing was installed.".into(),
        _ => format!("{} could not install {} (code {code}). {tail}", pm.name(), pkgs.join(", ")),
    })
}

/// Installs what the person chose, in order, before SSMConnect itself: distribution packages first (one password prompt),
/// then Amazon's signed downloads. `chosen` holds dependency ids.
pub fn install_dependencies(chosen: &[&str], how: Elevation, progress: &dyn Fn(String)) -> Result<(), String> {
    let deps = dependencies();
    let missing = |id: &str| deps.iter().find(|d| d.id == id).is_some_and(|d| d.found.is_none());
    if let Some(pm) = detect_package_manager() {
        let mut pkgs: Vec<&str> = vec![];
        for id in chosen.iter().filter(|id| missing(id)) { pkgs.extend(packages(id, pm).iter().copied()); }
        install_system_packages(&pkgs, how, progress)?;
    } else if chosen.iter().any(|id| missing(id) && matches!(*id, "ssh" | "rdp")) {
        return Err("This distribution's package manager was not recognised, so OpenSSH / Remmina must be installed by hand.".into());
    }
    if chosen.contains(&"aws") && missing("aws") { prereq_installer::install_aws_cli(progress)?; }
    if chosen.contains(&"plugin") && missing("plugin") { prereq_installer::install_plugin(progress)?; }
    Ok(())
}

/// What is still missing among the required dependencies.
pub fn missing_required() -> Vec<Dependency> { dependencies().into_iter().filter(|d| d.required && d.found.is_none()).collect() }

// ---- SSMConnect's own files -------------------------------------------------------------------------
fn put(dest: &Path, rel: &str, data: &[u8], mode: u32, written: &mut Vec<String>) -> Result<(), String> {
    let path = dest.join(rel);
    fs::create_dir_all(path.parent().unwrap()).map_err(|e| format!("Could not create {}: {e}", path.parent().unwrap().display()))?;
    let tmp = path.with_extension("ssmconnect-new");
    fs::write(&tmp, data).map_err(|e| format!("Could not write {}: {e}", path.display()))?;
    fs::set_permissions(&tmp, fs::Permissions::from_mode(mode)).map_err(|e| e.to_string())?;
    fs::rename(&tmp, &path).map_err(|e| format!("Could not install {}: {e}", path.display()))?;
    written.push(rel.to_string());
    Ok(())
}

/// Copies SSMConnect into `destdir`+`prefix`: the program (from `source_exe`), desktop entry, AppStream data, icon and documents.
/// With a non-empty `destdir` (building a package) the uninstall list is not written. Returns the files written, relative to the prefix.
pub fn install_files(prefix: &Path, destdir: &Path, source_exe: &Path, progress: &dyn Fn(String)) -> Result<Vec<String>, String> {
    let root = if destdir.as_os_str().is_empty() { prefix.to_path_buf() } else { destdir.join(prefix.strip_prefix("/").unwrap_or(prefix)) };
    let bin = fs::read(source_exe).map_err(|e| format!("Could not read the program to install ({}): {e}", source_exe.display()))?;
    let installed_exe = prefix.join("bin/ssmconnect");
    let desktop = if prefix == Path::new("/usr") { DESKTOP.to_string() } else { DESKTOP.replace("Exec=ssmconnect", &format!("Exec={}", installed_exe.display())) };
    let mut w = vec![];
    progress("Copying the program...".into());
    put(&root, "bin/ssmconnect", &bin, 0o755, &mut w)?;
    progress("Adding it to the application menu...".into());
    put(&root, &format!("share/applications/{APP_ID}.desktop"), desktop.as_bytes(), 0o644, &mut w)?;
    put(&root, &format!("share/metainfo/{APP_ID}.metainfo.xml"), METAINFO.as_bytes(), 0o644, &mut w)?;
    put(&root, &format!("share/icons/hicolor/scalable/apps/{APP_ID}.svg"), ICON, 0o644, &mut w)?;
    put(&root, "share/doc/ssmconnect/README.md", README.as_bytes(), 0o644, &mut w)?;
    put(&root, "share/doc/ssmconnect/CHANGELOG.md", CHANGELOG.as_bytes(), 0o644, &mut w)?;
    put(&root, "share/licenses/ssmconnect/LICENSE", LICENSE.as_bytes(), 0o644, &mut w)?;
    put(&root, "share/licenses/ssmconnect/NOTICE", NOTICE.as_bytes(), 0o644, &mut w)?;
    if destdir.as_os_str().is_empty() {
        // an update: files the previous version had and this one does not are removed; the person's settings are never touched
        if let Some(old) = read_manifest(&root) {
            for rel in old.files.iter().filter(|f| !w.contains(f)) { let _ = fs::remove_file(root.join(rel)); }
            if old.version.as_deref() != Some(crate::VERSION) { backup_settings(old.version.as_deref().unwrap_or("an-earlier-version")); }
        }
        let list = format!("# version {}\n{}\n", crate::VERSION, w.join("\n"));
        put(&root, MANIFEST, list.as_bytes(), 0o644, &mut vec![])?;
        for (cmd, dir) in [("update-desktop-database", "share/applications"), ("gtk-update-icon-cache", "share/icons/hicolor")] {
            if which(cmd).is_some() {
                let mut c = Command::new(cmd);
                if cmd == "gtk-update-icon-cache" { c.args(["-q", "-t"]); }
                let _ = c.arg(root.join(dir)).output();
            }
        }
    }
    applog::info(&format!("Installed SSMConnect {} to {}", crate::VERSION, root.display()));
    Ok(w)
}

/// What an earlier installation wrote down about itself.
pub struct Manifest { pub version: Option<String>, pub files: Vec<String> }

pub fn read_manifest(prefix: &Path) -> Option<Manifest> {
    let text = fs::read_to_string(prefix.join(MANIFEST)).ok()?;
    let version = text.lines().find_map(|l| l.strip_prefix("# version ")).map(|v| v.trim().to_string());
    let files = text.lines().filter(|l| !l.is_empty() && !l.starts_with('#') && !l.contains("..") && !l.starts_with('/')).map(String::from).collect();
    Some(Manifest { version, files })
}

/// Keeps a copy of the saved servers and settings next to them before a new version is installed over an old one.
fn backup_settings(old_version: &str) {
    let cfg = paths::config_path();
    if cfg.exists() {
        let copy = cfg.with_file_name(format!("config.json.before-update-from-{old_version}"));
        if !copy.exists() { let _ = fs::copy(&cfg, &copy); }
    }
}

/// An installation that is already on this computer.
#[derive(Clone, Debug)]
pub struct Existing {
    pub prefix: PathBuf,
    pub version: Option<String>,
    /// Put there by a package manager (dnf, apt ...), which must be the one to update or remove it.
    pub package_managed: bool,
}

/// Looks for an installation made by this installer (in the usual places, or in `prefix`), or by a package.
pub fn existing_install(prefix: Option<&Path>) -> Option<Existing> {
    let candidates: Vec<PathBuf> = prefix.map(|p| vec![p.to_path_buf()]).unwrap_or_else(|| vec![user_prefix(), system_prefix()]);
    for p in candidates {
        if let Some(m) = read_manifest(&p) { return Some(Existing { prefix: p, version: m.version, package_managed: false }); }
    }
    if prefix.is_none() {
        let pkg = Path::new("/usr/bin/ssmconnect");
        if pkg.exists() {
            let version = Command::new(pkg).arg("--version").output().ok().and_then(|o| String::from_utf8_lossy(&o.stdout).split_whitespace().last().map(String::from));
            return Some(Existing { prefix: PathBuf::from("/usr"), version, package_managed: true });
        }
    }
    None
}

/// -1, 0 or 1: is version `a` older than, the same as, or newer than `b` (both like 1.2.3).
pub fn compare_versions(a: &str, b: &str) -> i32 {
    let p = |v: &str| v.trim().trim_start_matches('v').split('.').map(|x| x.parse::<u64>().unwrap_or(0)).collect::<Vec<_>>();
    match p(a).cmp(&p(b)) { std::cmp::Ordering::Less => -1, std::cmp::Ordering::Equal => 0, std::cmp::Ordering::Greater => 1 }
}

/// How many other copies of SSMConnect are running right now (they keep using the old version until restarted).
pub fn running_instances() -> usize {
    let me = std::process::id();
    let Ok(rd) = fs::read_dir("/proc") else { return 0 };
    rd.flatten().filter(|e| e.file_name().to_string_lossy().parse::<u32>().is_ok_and(|pid| pid != me))
        .filter(|e| fs::read_to_string(e.path().join("comm")).is_ok_and(|c| c.trim() == "ssmconnect")).count()
}

/// Removes what `install_files` installed (from its list). The person's settings and the tools in ~/.local/share/ssmconnect stay.
pub fn uninstall(prefix: &Path) -> Result<usize, String> {
    let list = fs::read_to_string(prefix.join(MANIFEST)).map_err(|_| format!("SSMConnect does not seem to be installed in {} (no install list found).", prefix.display()))?;
    let mut n = 0;
    for rel in list.lines().filter(|l| !l.is_empty() && !l.starts_with('#') && !l.contains("..") && !l.starts_with('/')) {
        if fs::remove_file(prefix.join(rel)).is_ok() { n += 1; }
    }
    let _ = fs::remove_file(prefix.join(MANIFEST));
    for d in ["share/ssmconnect", "share/doc/ssmconnect", "share/licenses/ssmconnect"] { let _ = fs::remove_dir(prefix.join(d)); }
    Ok(n)
}

/// Removes the program, and (only when asked) the person's settings and the AWS tools SSMConnect downloaded for them.
/// Settings are kept by default: `remove_settings` and `remove_tools` must be set explicitly.
pub fn remove_everything(prefix: &Path, remove_settings: bool, remove_tools: bool, how: Elevation) -> Result<Vec<String>, String> {
    let mut done = vec![];
    let probe = prefix.join(MANIFEST);
    let writable = fs::OpenOptions::new().append(true).open(&probe).is_ok();
    if writable || is_root() {
        done.push(format!("Removed {} file(s) of the program", uninstall(prefix)?));
    } else {
        let me = std::env::current_exe().map_err(|e| e.to_string())?.to_string_lossy().into_owned();
        let cmd = elevated(vec![me, "--uninstall-files".into(), "--prefix".into(), prefix.to_string_lossy().into_owned()], how)?;
        let out = Command::new(&cmd[0]).args(&cmd[1..]).output().map_err(|e| e.to_string())?;
        if !out.status.success() { return Err(format!("Could not remove the program: {}", String::from_utf8_lossy(&out.stderr).trim())); }
        done.push("Removed the program".into());
    }
    let _ = fs::remove_file(paths::autostart_file());   // it would point at a program that is gone
    if remove_tools { let _ = fs::remove_dir_all(paths::data_dir()); done.push("Removed the AWS CLI and Session Manager plugin that SSMConnect downloaded".into()); }
    if remove_settings { done.extend(crate::desktop::cleanup_user(false)); } else { done.push("Kept your saved servers, settings and log".into()); }
    Ok(done)
}

pub fn user_prefix() -> PathBuf { paths::home().join(".local") }

pub fn system_prefix() -> PathBuf { PathBuf::from("/usr/local") }

pub fn on_path(dir: &Path) -> bool { std::env::var_os("PATH").is_some_and(|p| std::env::split_paths(&p).any(|d| d == dir)) }

/// Installs into a place that needs administrator rights, by running this same program as root in its files-only mode.
pub fn install_files_elevated(prefix: &Path, source_exe: &Path, how: Elevation, progress: &dyn Fn(String)) -> Result<(), String> {
    let me = source_exe.to_string_lossy().into_owned();
    let cmd = elevated(vec![me.clone(), "--install-files".into(), "--prefix".into(), prefix.to_string_lossy().into_owned(), "--source".into(), me], how)?;
    progress("Installing for all users... (you may be asked for your password)".into());
    let out = Command::new(&cmd[0]).args(&cmd[1..]).output().map_err(|e| e.to_string())?;
    if out.status.success() { Ok(()) } else {
        Err(match out.status.code() { Some(126) | Some(127) => "The password was not accepted, or the request was cancelled. Nothing was installed.".into(),
                                      _ => format!("The installation failed: {}", String::from_utf8_lossy(&out.stderr).trim()) })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn package_manager_from_os_release() {
        let all = |_: &str| true;
        assert_eq!(pm_from("ID=fedora\nVERSION_ID=44", &all), Some(PackageManager::Dnf));
        assert_eq!(pm_from("ID=ubuntu\nID_LIKE=debian", &all), Some(PackageManager::Apt));
        assert_eq!(pm_from("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", &all), Some(PackageManager::Apt));
        assert_eq!(pm_from("ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"", &all), Some(PackageManager::Zypper));
        assert_eq!(pm_from("ID=arch", &all), Some(PackageManager::Pacman));
        assert_eq!(pm_from("ID=rocky\nID_LIKE=\"rhel centos fedora\"", &|n| n == "dnf"), Some(PackageManager::Dnf));
        assert_eq!(pm_from("ID=weird", &|n| n == "pacman"), Some(PackageManager::Pacman));   // unknown distribution: fall back to what exists
        assert_eq!(pm_from("ID=weird", &|_| false), None);
    }

    #[test]
    fn install_commands_never_prompt() {
        assert_eq!(PackageManager::Dnf.install_command(&["a", "b"]), ["dnf", "install", "-y", "a", "b"]);
        assert!(PackageManager::Apt.install_command(&["a"]).contains(&"DEBIAN_FRONTEND=noninteractive".to_string()));
        assert!(PackageManager::Zypper.install_command(&["a"]).contains(&"--non-interactive".to_string()));
        assert!(PackageManager::Pacman.install_command(&["a"]).contains(&"--noconfirm".to_string()));
    }

    #[test]
    fn distro_package_names() {
        assert_eq!(packages("ssh", PackageManager::Apt), ["openssh-client"]);
        assert_eq!(packages("ssh", PackageManager::Dnf), ["openssh-clients"]);
        assert_eq!(packages("aws", PackageManager::Dnf), [] as [&str; 0]);   // Amazon's tools never come from a distribution package here
    }
}
