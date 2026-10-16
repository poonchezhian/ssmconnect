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
