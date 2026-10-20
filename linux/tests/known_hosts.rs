mod common;
use common::Home;
use ssmconnect::known_hosts::{self, HostKey, Trust};
use std::process::{Child, Command};

/// A throw-away sshd on a free port (skipped when sshd is not installed).
struct Sshd { child: Child, port: u16 }
impl Drop for Sshd { fn drop(&mut self) { let _ = self.child.kill(); let _ = self.child.wait(); } }

fn start_sshd(h: &Home) -> Option<Sshd> {
    let sshd = ["/usr/sbin/sshd", "/usr/bin/sshd", "/usr/local/sbin/sshd"].into_iter().find(|p| std::path::Path::new(p).exists())?;
    let port = std::net::TcpListener::bind("127.0.0.1:0").ok()?.local_addr().ok()?.port();
    let dir = h.join("sshd");
    std::fs::create_dir_all(&dir).ok()?;
    for (t, f) in [("ed25519", "ssh_host_ed25519_key"), ("rsa", "ssh_host_rsa_key")] {
        assert!(Command::new("ssh-keygen").args(["-q", "-t", t, "-N", "", "-f"]).arg(dir.join(f)).status().ok()?.success());
    }
    std::fs::write(dir.join("sshd_config"), format!("Port {port}\nListenAddress 127.0.0.1\nHostKey {d}/ssh_host_ed25519_key\nHostKey {d}/ssh_host_rsa_key\nUsePAM no\nStrictModes no\nPidFile {d}/pid\n", d = dir.display())).ok()?;
    let child = Command::new(sshd).arg("-D").arg("-f").arg(dir.join("sshd_config")).arg("-E").arg(dir.join("log")).spawn().ok()?;
    for _ in 0..50 { if std::net::TcpStream::connect(("127.0.0.1", port)).is_ok() { return Some(Sshd { child, port }); } std::thread::sleep(std::time::Duration::from_millis(100)); }
    None
}

#[test]
fn trust_and_forget_a_real_servers_identity() {
    let h = Home::new();
    let Some(srv) = start_sshd(&h) else { eprintln!("sshd not available: skipped"); return };
    let alias = "ssc-test-i-0123456789abcdef0";
    assert_eq!(known_hosts::status(alias, None), Trust::Unknown);

    let keys = known_hosts::scan(srv.port).expect("scan");
    assert!(keys.len() >= 2, "{keys:?}");
    // the fingerprint we show is the one ssh-keygen shows
    let ed = keys.iter().find(|k| k.key_type == "ssh-ed25519").unwrap();
    let mut c = Command::new("ssh-keygen").args(["-lf", "-"]).stdin(std::process::Stdio::piped()).stdout(std::process::Stdio::piped()).spawn().unwrap();
    std::io::Write::write_all(c.stdin.as_mut().unwrap(), format!("x {} {}\n", ed.key_type, ed.b64).as_bytes()).unwrap();
    let shown = String::from_utf8_lossy(&c.wait_with_output().unwrap().stdout).to_string();
    assert!(shown.contains(&ed.fingerprint()), "{shown} vs {}", ed.fingerprint());

    // existing entries of the user survive, and a copy is kept
    std::fs::create_dir_all(h.join(".ssh")).unwrap();
    std::fs::write(h.join(".ssh/known_hosts"), "github.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl\n").unwrap();
    known_hosts::trust(alias, &keys).unwrap();
    let text = std::fs::read_to_string(h.join(".ssh/known_hosts")).unwrap();
    assert!(text.contains("github.com ssh-ed25519") && text.contains(&format!("{alias} ssh-ed25519 ")));
    assert!(h.join(".ssh/known_hosts.ssmconnect.bak").exists());
    known_hosts::trust(alias, &keys).unwrap();   // trusting twice adds nothing
    assert_eq!(std::fs::read_to_string(h.join(".ssh/known_hosts")).unwrap(), text);
    assert_eq!(known_hosts::status(alias, Some(&keys)), Trust::Trusted);

    // a different identity under the same name is reported as changed
    let other = HostKey { key_type: "ssh-ed25519".into(), b64: "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl".into() };
    assert_eq!(known_hosts::status(alias, Some(&[other])), Trust::Changed);

    known_hosts::forget(alias).unwrap();
    assert_eq!(known_hosts::status(alias, None), Trust::Unknown);
    assert!(std::fs::read_to_string(h.join(".ssh/known_hosts")).unwrap().contains("github.com"));   // the user's other entries stay
    assert!(known_hosts::scan(1).is_err());
}

#[test]
fn fingerprint_of_a_known_key() {
    // the well-known GitHub ed25519 host key and its published fingerprint
    let k = HostKey { key_type: "ssh-ed25519".into(), b64: "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl".into() };
    assert_eq!(k.fingerprint(), "SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU");
    assert_eq!(k.nice_type(), "ED25519");
}
