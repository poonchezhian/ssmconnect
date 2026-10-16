//! Checks the OpenPGP detached signatures Amazon publishes next to its downloads, so a tampered or damaged download is refused.
//!
//! The two public keys are built into the program (`src/keys/`, copied from AWS's own documentation). Each is pinned by its
//! full fingerprint, which is checked when the key is loaded, so even a damaged copy of a key could never be accepted. If AWS
//! one day rotates a key, verification fails with a clear message and a new release of SSMConnect ships the new key.
//!   AWS CLI:                 https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html
//!   Session Manager plugin:  https://docs.aws.amazon.com/systems-manager/latest/userguide/install-plugin-linux-verify-signature.html

use pgp::composed::{Deserializable, DetachedSignature, SignedPublicKey};
use pgp::types::KeyDetails;

pub struct TrustedKey { pub name: &'static str, pub fingerprint: &'static str, pub armored: &'static str }

pub const AWS_CLI: TrustedKey = TrustedKey {
    name: "the AWS CLI Team key",
    fingerprint: "FB5DB77FD5C118B80511ADA8A6310ACC4672475C",
    armored: include_str!("keys/aws-cli.asc"),
};

pub const SESSION_MANAGER_PLUGIN: TrustedKey = TrustedKey {
    name: "the Session Manager plugin signer key",
    fingerprint: "7959637124CE093AD501D47A2C4D4AFF6F6757EE",
    armored: include_str!("keys/session-manager-plugin.asc"),
};

fn hex_upper(b: &[u8]) -> String { b.iter().map(|x| format!("{x:02X}")).collect() }

impl TrustedKey {
    /// The built-in key, after checking that it really has the pinned fingerprint.
    pub fn load(&self) -> Result<SignedPublicKey, String> {
        let (key, _) = SignedPublicKey::from_string(self.armored).map_err(|e| format!("Could not read {}: {e}", self.name))?;
        let got = hex_upper(key.fingerprint().as_bytes());
        if got != self.fingerprint { return Err(format!("{} does not have the expected fingerprint ({got}).", self.name)); }
        Ok(key)
    }
}

/// Ok when `sig` (binary or ASCII-armored) is a valid signature of `data` made by `key`. The error is a plain message.
pub fn verify_detached(data: &[u8], sig: &[u8], key: &TrustedKey) -> Result<(), String> {
    let public = key.load()?;
    let sig = if sig.starts_with(b"-----BEGIN PGP SIGNATURE") {
        DetachedSignature::from_armor_single(sig).map(|s| s.0)
    } else {
        DetachedSignature::from_bytes(sig)
    }.map_err(|_| "The signature file that came with the download is not a valid OpenPGP signature.".to_string())?;
    // the signature may be made by the primary key or by a signing subkey; try them all, all are bound to the pinned fingerprint
    if sig.verify(&public, data).is_ok() { return Ok(()); }
    if public.public_subkeys.iter().any(|sk| sig.verify(&sk.key, data).is_ok()) { return Ok(()); }
    Err(format!("The download does not match its signature from {}. It may be damaged or tampered with, so it was not installed.", key.name))
}
