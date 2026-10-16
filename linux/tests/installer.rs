mod common;
use common::Home;
use ssmconnect::installer;
use std::path::Path;

fn fake_program(h: &Home) -> std::path::PathBuf {
    let p = h.join("src-ssmconnect");
    std::fs::write(&p, b"#!/bin/sh\necho hi\n").unwrap();
    p
}

#[test]
fn installs_and_uninstalls_from_the_manifest() {
    let h = Home::new();
    let prefix = h.join("prefix");
    let written = installer::install_files(&prefix, Path::new(""), &fake_program(&h), &|_| {}).unwrap();
    assert!(written.contains(&"bin/ssmconnect".to_string()));
    for f in ["bin/ssmconnect", "share/applications/io.github.poonchezhian.SSMConnect.desktop", "share/metainfo/io.github.poonchezhian.SSMConnect.metainfo.xml",
              "share/icons/hicolor/scalable/apps/io.github.poonchezhian.SSMConnect.svg", "share/licenses/ssmconnect/LICENSE", "share/licenses/ssmconnect/NOTICE",
              "share/doc/ssmconnect/README.md", "share/ssmconnect/install-manifest.txt"] {
        assert!(prefix.join(f).exists(), "{f} missing");
    }
    let desktop = std::fs::read_to_string(prefix.join("share/applications/io.github.poonchezhian.SSMConnect.desktop")).unwrap();
    assert!(desktop.contains(&format!("Exec={}/bin/ssmconnect", prefix.display())), "{desktop}");   // a menu entry must not depend on PATH
    use std::os::unix::fs::PermissionsExt;
    assert_eq!(std::fs::metadata(prefix.join("bin/ssmconnect")).unwrap().permissions().mode() & 0o777, 0o755);
    let removed = installer::uninstall(&prefix).unwrap();
    assert!(removed >= 8);
    assert!(!prefix.join("bin/ssmconnect").exists() && !prefix.join("share/applications/io.github.poonchezhian.SSMConnect.desktop").exists());
    assert!(installer::uninstall(&prefix).is_err());   // nothing left to remove
}

#[test]
fn package_staging_uses_destdir_and_writes_no_install_list() {
    let h = Home::new();
    let dest = h.join("pkgroot");
    installer::install_files(Path::new("/usr"), &dest, &fake_program(&h), &|_| {}).unwrap();
    assert!(dest.join("usr/bin/ssmconnect").exists());
    assert!(!dest.join("usr/share/ssmconnect/install-manifest.txt").exists());   // the package manager owns the file list
    let desktop = std::fs::read_to_string(dest.join("usr/share/applications/io.github.poonchezhian.SSMConnect.desktop")).unwrap();
    assert!(desktop.contains("Exec=ssmconnect\n"));
}

#[test]
fn uninstall_ignores_unsafe_manifest_lines() {
    let h = Home::new();
    let prefix = h.join("prefix");
    std::fs::create_dir_all(prefix.join("share/ssmconnect")).unwrap();
    let victim = h.join("victim.txt");
    std::fs::write(&victim, "keep").unwrap();
    std::fs::write(prefix.join("share/ssmconnect/install-manifest.txt"), "../../victim.txt\n/etc/passwd\n").unwrap();
    installer::uninstall(&prefix).unwrap();
    assert!(victim.exists());
}

#[test]
fn nothing_third_party_is_embedded() {
    // the program carries SSMConnect's own files and Amazon's *public keys* only; the tools themselves are always fetched
    let deps = installer::dependencies();
    assert!(deps.iter().filter(|d| d.id == "aws" || d.id == "plugin").all(|d| d.source == installer::Source::Amazon));
    assert!(deps.iter().filter(|d| d.id == "ssh" || d.id == "rdp").all(|d| d.source == installer::Source::Distribution));
}

#[test]
fn updating_keeps_settings_and_removes_files_the_new_version_dropped() {
    let h = Home::new();
    let prefix = h.join("prefix");
    // an older installation, with an extra file the new version no longer ships, and the person's settings
    installer::install_files(&prefix, Path::new(""), &fake_program(&h), &|_| {}).unwrap();
    std::fs::write(prefix.join("share/obsolete.txt"), "x").unwrap();
    let mut m = std::fs::read_to_string(prefix.join("share/ssmconnect/install-manifest.txt")).unwrap();
    m = m.replace(&format!("# version {}", ssmconnect::VERSION), "# version 0.0.1") + "share/obsolete.txt\n";
    std::fs::write(prefix.join("share/ssmconnect/install-manifest.txt"), m).unwrap();
    std::fs::create_dir_all(h.join(".config/ssmconnect")).unwrap();
    std::fs::write(h.join(".config/ssmconnect/config.json"), "{\"profile\":\"mine\"}").unwrap();
    std::fs::create_dir_all(h.join(".aws")).unwrap();
    std::fs::write(h.join(".aws/config"), "[default]\n").unwrap();

    assert_eq!(installer::existing_install(Some(&prefix)).unwrap().version.as_deref(), Some("0.0.1"));
    installer::install_files(&prefix, Path::new(""), &fake_program(&h), &|_| {}).unwrap();

    assert!(!prefix.join("share/obsolete.txt").exists());
    assert_eq!(installer::existing_install(Some(&prefix)).unwrap().version.as_deref(), Some(ssmconnect::VERSION));
    assert_eq!(std::fs::read_to_string(h.join(".config/ssmconnect/config.json")).unwrap(), "{\"profile\":\"mine\"}");   // settings untouched
    assert!(h.join(".config/ssmconnect/config.json.before-update-from-0.0.1").exists());                              // and a copy made first
    assert!(h.join(".aws/config").exists());
}

#[test]
fn removing_keeps_the_persons_data_unless_asked() {
    let h = Home::new();
    let prefix = h.join("prefix");
    let setup = || {
        installer::install_files(&prefix, Path::new(""), &fake_program(&h), &|_| {}).unwrap();
        std::fs::create_dir_all(h.join(".config/ssmconnect")).unwrap();
        std::fs::write(h.join(".config/ssmconnect/config.json"), "{}").unwrap();
        std::fs::create_dir_all(h.join(".local/share/ssmconnect/bin")).unwrap();
        std::fs::write(h.join(".local/share/ssmconnect/bin/aws"), "x").unwrap();
    };
    setup();
    installer::remove_everything(&prefix, false, false, installer::Elevation::Terminal).unwrap();
    assert!(!prefix.join("bin/ssmconnect").exists());
    assert!(h.join(".config/ssmconnect/config.json").exists() && h.join(".local/share/ssmconnect/bin/aws").exists());

    setup();
    installer::remove_everything(&prefix, false, true, installer::Elevation::Terminal).unwrap();      // tools only
    assert!(h.join(".config/ssmconnect/config.json").exists() && !h.join(".local/share/ssmconnect").exists());

    setup();
    installer::remove_everything(&prefix, true, false, installer::Elevation::Terminal).unwrap();      // settings only
    assert!(!h.join(".config/ssmconnect").exists() && h.join(".local/share/ssmconnect/bin/aws").exists());
}

#[test]
fn version_order() {
    assert_eq!(installer::compare_versions("0.2.1", "0.3.0"), -1);
    assert_eq!(installer::compare_versions("0.10.0", "0.9.9"), 1);
    assert_eq!(installer::compare_versions("1.0.0", "v1.0.0"), 0);
}
