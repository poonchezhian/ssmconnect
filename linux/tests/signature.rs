mod common;
use ssmconnect::signature::{self, TrustedKey};

#[test]
fn built_in_keys_have_the_pinned_fingerprints() {
    signature::AWS_CLI.load().expect("AWS CLI key");
    signature::SESSION_MANAGER_PLUGIN.load().expect("plugin key");
}

#[test]
fn a_key_with_the_wrong_fingerprint_is_refused() {
    // same key material, but pinned to a different fingerprint: must never load
    let wrong = TrustedKey { fingerprint: "0000000000000000000000000000000000000000", ..signature::AWS_CLI };
    assert!(wrong.load().unwrap_err().contains("expected fingerprint"));
}

#[test]
fn garbage_and_empty_signatures_are_refused() {
    assert!(signature::verify_detached(b"data", b"not a signature", &signature::AWS_CLI).is_err());
    assert!(signature::verify_detached(b"data", b"", &signature::SESSION_MANAGER_PLUGIN).is_err());
}

#[test]
fn a_signature_from_the_other_key_is_refused() {
    // a real Amazon signature is only accepted for the key it was made with (needs the network: cargo test -- --ignored)
    if std::env::var("SSMCONNECT_NET_TESTS").is_err() { return; }
    let get = |u: &str| { let mut v = vec![]; std::io::Read::read_to_end(&mut ureq::get(u).call().unwrap().into_reader(), &mut v).unwrap(); v };
    let deb = get("https://s3.amazonaws.com/session-manager-downloads/plugin/latest/ubuntu_64bit/session-manager-plugin.deb");
    let sig = get("https://s3.amazonaws.com/session-manager-downloads/plugin/latest/ubuntu_64bit/session-manager-plugin.deb.sig");
    signature::verify_detached(&deb, &sig, &signature::SESSION_MANAGER_PLUGIN).expect("real plugin package must verify");
    assert!(signature::verify_detached(&deb, &sig, &signature::AWS_CLI).is_err(), "wrong key must not verify");
    let mut tampered = deb.clone();
    tampered[1000] ^= 1;
    assert!(signature::verify_detached(&tampered, &sig, &signature::SESSION_MANAGER_PLUGIN).unwrap_err().contains("may be damaged or tampered"));
}

#[test]
fn the_real_aws_cli_download_verifies() {
    if std::env::var("SSMCONNECT_NET_TESTS").is_err() { return; }
    let get = |u: &str| { let mut v = vec![]; std::io::Read::read_to_end(&mut ureq::get(u).call().unwrap().into_reader(), &mut v).unwrap(); v };
    let zip = get("https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip");
    let sig = get("https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip.sig");
    signature::verify_detached(&zip, &sig, &signature::AWS_CLI).expect("real AWS CLI package must verify");
    let mut tampered = zip.clone();
    let n = tampered.len() / 2;
    tampered[n] ^= 1;
    assert!(signature::verify_detached(&tampered, &sig, &signature::AWS_CLI).is_err());
}
