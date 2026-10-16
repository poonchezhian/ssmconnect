mod common;
use common::Home;
use ssmconnect::aws_profiles as ap;
use ssmconnect::models::*;
use ssmconnect::{config_store, key_finder, ssh_config};
use std::collections::HashSet;
use std::process::Command;

#[test]
fn sso_roundtrip_and_preservation() {
    let h = Home::new();
    let cfg = h.join(".aws/config");
    std::fs::create_dir_all(cfg.parent().unwrap()).unwrap();
    std::fs::write(&cfg, "[default]\nregion = eu-west-1\n\n[profile mine]\noutput = text\n").unwrap();
    ap::save_sso_session("acme", "https://acme.awsapps.com/start", "us-east-1", true).unwrap();
    ap::save_sso_profile("prod-1111", "acme", "111111111111", "Admin").unwrap();
    ap::save_sso_profile("dev-2222", "acme", "222222222222", "Admin").unwrap();
    let profs = ap::list_profiles();
    let names: HashSet<_> = profs.iter().map(|p| p.name.as_str()).collect();
    assert_eq!(names, HashSet::from(["default", "mine", "prod-1111", "dev-2222"]));
    let prod = profs.iter().find(|p| p.name == "prod-1111").unwrap();
    assert_eq!(prod.kind, ap::Kind::Sso);
    assert_eq!(prod.start_url.as_deref(), Some("https://acme.awsapps.com/start"));
    assert_eq!(profs.iter().find(|p| p.name == "mine").unwrap().kind, ap::Kind::Other);
    assert_eq!(ap::primary_key(prod), "sso:acme.awsapps.com");
    let dev = profs.iter().find(|p| p.name == "dev-2222").unwrap();
    assert_eq!(ap::same_sign_in(dev, &profs, &HashSet::new()).len(), 2);
    assert!(std::fs::read_to_string(&cfg).unwrap().contains("output = text"));
    ap::remove_profile("dev-2222").unwrap();
    let after = std::fs::read_to_string(&cfg).unwrap();
    assert!(!after.contains("dev-2222") && after.contains("[profile mine]"));
    assert!(h.join(".aws/config.ssmconnect.bak").exists());
}

#[test]
fn access_keys_private_and_validated() {
    use std::os::unix::fs::PermissionsExt;
    let h = Home::new();
    assert!(ap::save_access_keys("k", "nope", "x").is_err());
    ap::save_access_keys("k", "AKIAABCDEFGHIJKLMNOP", &"a".repeat(40)).unwrap();
    assert_eq!(std::fs::metadata(h.join(".aws/credentials")).unwrap().permissions().mode() & 0o777, 0o600);
    assert_eq!(ap::find("k").unwrap().kind, ap::Kind::Keys);
}

#[test]
fn alias_and_name_helpers() {
    let _h = Home::new();
    let p = ProfileInfoBuilder::sso();
    let mut cfg = AppConfig::default();
    cfg.aliases.insert("sso:acme.awsapps.com".into(), "Work".into());
    assert_eq!(ap::primary_label(&cfg, &p, &[p.clone()]), "Work");
    assert_eq!(ap::primary_name(&p), "Acme");
    assert_eq!(ap::profile_name_for("Prod Acct!", "123456789012", &HashSet::from(["Prod-Acct".to_string()])), "Prod-Acct-9012");
}

struct ProfileInfoBuilder;
impl ProfileInfoBuilder {
    fn sso() -> ap::ProfileInfo {
        ap::ProfileInfo { sso_session: Some("Acme".into()), start_url: Some("https://Acme.awsapps.com/start/".into()), ..ap::ProfileInfo::new("x", ap::Kind::Sso) }
    }
}

#[test]
fn config_roundtrip_and_recovery() {
    let h = Home::new();
    let cfg = AppConfig { profile: "p".into(), region: "ap-south-1".into(), tunnels: vec![TunnelConfig { name: "w".into(), instance_id: "i-1".into(), local_port: 2201, ..Default::default() }], ..Default::default() };
    config_store::save(&cfg).unwrap();
    let back = config_store::load().unwrap();
    assert_eq!(back.tunnels[0].name, "w");
    assert_eq!(back.tunnels[0].profile, "p");   // inherited from the old single-account setting
    std::fs::write(h.join(".config/ssmconnect/config.json"), "{broken").unwrap();
    let (got, warning) = config_store::load_or_recover();
    assert!(got.tunnels.is_empty());
    assert!(warning.unwrap().contains("damaged file was kept"));
}

#[test]
fn reads_settings_written_by_the_python_edition() {
    let h = Home::new();
    std::fs::create_dir_all(h.join(".config/ssmconnect")).unwrap();
    std::fs::write(h.join(".config/ssmconnect/config.json"), r#"{"profile":"p","region":"ap-south-1","tunnels":[{"name":"w","instance_id":"i-0123456789abcdef0","profile":"p","region":"ap-south-1","local_port":2201,"remote_port":22,"protocol":"ssh","user_name":"ubuntu","environment":"Production","key_file":null,"enabled":true,"os_name":"Ubuntu"}],"aliases":{"a":"b"},"selected_account":"","autostart_connections":true}"#).unwrap();
    let c = config_store::load().unwrap();
    assert_eq!((c.tunnels[0].user_name.as_str(), c.tunnels[0].local_port, c.aliases["a"].as_str()), ("ubuntu", 2201, "b"));
}

#[test]
fn ssh_config_apply_remove_keeps_user_hosts() {
    let h = Home::new();
    let ssh = h.join(".ssh");
    std::fs::create_dir_all(&ssh).unwrap();
    std::fs::write(ssh.join("config"), "Host mine\n    HostName example.com\n").unwrap();
    let t = |n: &str, id: &str, port, rdp: bool| TunnelConfig { name: n.into(), instance_id: id.into(), local_port: port, protocol: if rdp { "rdp".into() } else { "ssh".into() }, ..Default::default() };
    let ts = vec![TunnelConfig { user_name: "ubuntu".into(), key_file: Some("/k/my key.pem".into()), ..t("Web Server 1", "i-aaaaaaaa", 2201, false) },
                  t("web server 1", "i-bbbbbbbb", 2202, false), t("Win", "i-cccccccc", 2203, true)];
    ssh_config::apply(&ts, false).unwrap();
    ssh_config::apply(&ts, false).unwrap();   // idempotent
    let text = std::fs::read_to_string(ssh.join("config")).unwrap();
    assert_eq!(text.matches(ssh_config::INCLUDE_LINE).count(), 1);
    assert!(text.starts_with("# SSMConnect"));
    let conf = std::fs::read_to_string(ssh.join("ssmconnect.conf")).unwrap();
    assert!(conf.contains("Host ssc-web-server-1\n"));
    assert!(conf.contains("IdentityFile \"/k/my key.pem\""));
    assert!(!conf.contains("Win"));
    assert_eq!(ssh_config::alias_of(&ts[1], &ts), "ssc-web-server-1-bbbbbb");
    ssh_config::remove().unwrap();
    assert_eq!(std::fs::read_to_string(ssh.join("config")).unwrap(), "Host mine\n    HostName example.com\n");
    assert!(!ssh.join("ssmconnect.conf").exists());
    if Command::new("which").arg("ssh").output().map(|o| o.status.success()).unwrap_or(false) {
        ssh_config::apply(&ts, false).unwrap();
        let out = Command::new("ssh").args(["-F"]).arg(ssh.join("config")).args(["-G", "ssc-web-server-1"]).output().unwrap();
        let s = String::from_utf8_lossy(&out.stdout);
        assert!(s.contains("\nport 2201\n") && s.contains("\nuser ubuntu\n"), "{s}");
    }
}

#[test]
fn key_fingerprint_matches_openssl() {
    let h = Home::new();
    let key = h.join("k.pem");
    assert!(Command::new("openssl").args(["genrsa", "-traditional", "-out"]).arg(&key).arg("2048").output().unwrap().status.success());
    let fps = key_finder::fingerprints(&key);
    let pipe = |args: &[&str], stdin: &[u8]| -> String {
        use std::io::Write;
        let mut c = Command::new("openssl").args(args).stdin(std::process::Stdio::piped()).stdout(std::process::Stdio::piped()).spawn().unwrap();
        c.stdin.take().unwrap().write_all(stdin).unwrap();
        let out = c.wait_with_output().unwrap();
        String::from_utf8_lossy(&out.stdout).split("= ").nth(1).unwrap_or("").trim().to_string()
    };
    let der = Command::new("openssl").args(["pkcs8", "-topk8", "-nocrypt", "-outform", "DER", "-in"]).arg(&key).output().unwrap().stdout;
    assert!(fps.contains(&pipe(&["sha1", "-c"], &der)));
    let publ = Command::new("openssl").args(["rsa", "-pubout", "-outform", "DER", "-in"]).arg(&key).output().unwrap().stdout;
    assert!(fps.contains(&pipe(&["md5", "-c"], &publ)));
    let p8 = h.join("k8.pem");
    assert!(Command::new("openssl").args(["pkcs8", "-topk8", "-nocrypt", "-in"]).arg(&key).arg("-out").arg(&p8).output().unwrap().status.success());
    assert_eq!(key_finder::fingerprints(&p8), fps);   // PKCS#8 form of the same key gives the same fingerprints
}

#[test]
fn key_matching_and_classify() {
    let h = Home::new();
    let k = |p: &str, fp: &[&str]| key_finder::KeyFile { path: p.into(), format: "pem".into(), fingerprints: fp.iter().map(|s| s.to_string()).collect() };
    let (a, b, c) = (k("/x/a.pem", &["aa:bb"]), k("/x/prod-key.pem", &[]), k("/x/prod-key (1).pem", &[]));
    assert_eq!(key_finder::find(&[a.clone(), b.clone()], "other", "aa:bb").0, Some(&a));
    assert_eq!(key_finder::find(&[b.clone()], "prod-key", "").1, "matched by name");
    assert_eq!(key_finder::find(&[c], "prod-key", "").1, "similar name - please check");
    assert!(key_finder::find(&[a], "zzz", "ff").0.is_none());
    let pem = h.join("a.pem");
    std::fs::write(&pem, format!("-----BEGIN RSA PRIVATE KEY-----\n{}\n-----END RSA PRIVATE KEY-----\n", "A".repeat(200))).unwrap();
    assert_eq!(key_finder::classify(&pem).as_deref(), Some("pem"));
    let txt = h.join("a.txt");
    std::fs::copy(&pem, &txt).unwrap();
    assert!(key_finder::classify(&txt).is_none());
}

#[test]
fn server_user_suggestion() {
    assert_eq!(DiscoveredServer { os_name: "Ubuntu".into(), ..Default::default() }.suggested_user(), "ubuntu");
    assert_eq!(DiscoveredServer { platform: "Windows".into(), ..Default::default() }.suggested_user(), "Administrator");
    assert_eq!(DiscoveredServer { os_name: "Amazon Linux".into(), ..Default::default() }.suggested_user(), "ec2-user");
}

#[test]
fn iso_time_and_token_expiry() {
    use ssmconnect::util::parse_iso_utc;
    assert_eq!(parse_iso_utc("1970-01-02T00:00:00Z"), Some(86400.0));
    assert!(parse_iso_utc("2099-01-01T00:00:00UTC").unwrap() > ssmconnect::util::now());
    assert!(parse_iso_utc("garbage").is_none());
}

#[test]
fn sftp_problems_are_explained_in_plain_words() {
    use ssmconnect::launchers::sftp_problem;
    assert!(sftp_problem("Failed to mount: Permission denied (publickey).").unwrap().contains("refused the login"));
    assert!(sftp_problem("ssh: connect to host 127.0.0.1 port 2201: Connection refused").unwrap().contains("not open"));
    assert!(sftp_problem("Host key verification failed.").unwrap().contains("host key"));
    // "this desktop cannot mount SFTP" is not a problem with the server: the caller falls back to a file manager or the sftp client
    assert!(sftp_problem("Operation not supported / no handler for sftp").is_none());
}
