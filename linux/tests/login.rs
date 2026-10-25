mod common;
use common::Home;
use ssmconnect::login_check::{candidate_users, classify, ensure, forget_refusals, Outcome, Probe};
use ssmconnect::models::*;
use ssmconnect::config_store;

#[test]
fn users_to_try() {
    assert_eq!(candidate_users("ec2-user", "Ubuntu 24.04"), vec!["ec2-user", "ubuntu"]);        // OS known: its own user only
    assert_eq!(candidate_users("bob", "Amazon Linux 2023"), vec!["bob", "ec2-user"]);
    let unknown = candidate_users("ec2-user", "");
    assert!(unknown.contains(&"ubuntu".to_string()) && unknown.contains(&"admin".to_string()) && unknown[0] == "ec2-user");
}

#[test]
fn ssh_errors_in_plain_words() {
    assert_eq!(classify("ec2-user@127.0.0.1: Permission denied (publickey)."), Probe::Refused);
    assert!(matches!(classify("ssh: connect to host 127.0.0.1 port 2201: Connection refused"), Probe::Down(m) if m.contains("did not answer")));
    assert!(matches!(classify("WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!"), Probe::Down(m) if m.contains("identity changed")));
}

#[test]
fn finds_the_user_the_server_accepts_and_saves_it() {
    let h = Home::new();
    // fake ssh: only "ubuntu" with the key is accepted
    h.tool("ssh", "#!/bin/sh\nuser=''; key=''\nwhile [ $# -gt 0 ]; do case \"$1\" in -l) user=$2; shift;; -i) key=$2; shift;; esac; shift; done\n[ \"$user\" = ubuntu ] && [ -n \"$key\" ] && exit 0\necho \"$user@127.0.0.1: Permission denied (publickey).\" >&2; exit 255\n");
    let old_path = std::env::var("PATH").unwrap_or_default();
    std::env::set_var("PATH", format!("{}:{old_path}", h.join(".local/share/ssmconnect/bin").display()));
    let pem = h.join("web.pem");
    std::fs::write(&pem, "-----BEGIN RSA PRIVATE KEY-----\nx\n-----END RSA PRIVATE KEY-----\n").unwrap();
    let t = TunnelConfig { name: "web".into(), instance_id: "i-0abc".into(), profile: "p".into(), region: "eu-west-1".into(), local_port: 2201,
                           user_name: "ec2-user".into(), key_file: Some(pem.to_string_lossy().into_owned()), ..Default::default() };
    let cfg = AppConfig { tunnels: vec![t.clone()], ssh_integration: false, ..Default::default() };
    config_store::save(&cfg).unwrap();
    let out = ensure(&t);
    assert_eq!(out, Outcome::Ready { user: "ubuntu".into(), key: Some(pem.to_string_lossy().into_owned()) });
    assert_eq!(config_store::load().unwrap().tunnels[0].user_name, "ubuntu");
    // nothing fits: a server that refuses everyone asks for a key, and a refused combination is not tried twice
    h.tool("ssh", "#!/bin/sh\necho 'Permission denied (publickey).' >&2; exit 255\n");
    let t2 = TunnelConfig { instance_id: "i-0def".into(), ..t.clone() };
    config_store::save(&AppConfig { tunnels: vec![t2.clone()], ssh_integration: false, ..Default::default() }).unwrap();
    assert_eq!(ensure(&t2), Outcome::NeedKey);
    forget_refusals("i-0def");
    // a server that does not answer is not a login problem
    h.tool("ssh", "#!/bin/sh\necho 'ssh: connect to host 127.0.0.1 port 2201: Connection refused' >&2; exit 255\n");
    assert!(matches!(ensure(&t2), Outcome::Down(_)));
    std::env::set_var("PATH", old_path);
}
