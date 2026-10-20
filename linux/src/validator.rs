//! Checks everything read from config.json before it is used to start a process or written into another tool's config.

use crate::models::{AppConfig, TunnelConfig};
use regex::Regex;
use std::sync::OnceLock;

fn re(cell: &'static OnceLock<Regex>, pat: &str) -> &'static Regex { cell.get_or_init(|| Regex::new(pat).unwrap()) }

pub fn is_valid_instance_id(s: &str) -> bool { static R: OnceLock<Regex> = OnceLock::new(); re(&R, r"^m?i-[0-9a-f]{8,17}$").is_match(s) }
pub fn is_valid_profile(s: &str) -> bool { static R: OnceLock<Regex> = OnceLock::new(); re(&R, r"^[A-Za-z0-9_.@:+=,\- ]{1,128}$").is_match(s) }
pub fn is_valid_region(s: &str) -> bool { static R: OnceLock<Regex> = OnceLock::new(); re(&R, r"^[a-z]{2}(-[a-z]+)+-\d$").is_match(s) }
pub fn is_valid_login(s: &str) -> bool { static R: OnceLock<Regex> = OnceLock::new(); re(&R, r"^[A-Za-z0-9_.\-]{1,64}$").is_match(s) }

pub fn validate_global(cfg: &AppConfig) -> Option<String> {
    if !cfg.profile.trim().is_empty() && !is_valid_profile(&cfg.profile) {
        return Some("The AWS profile name contains characters that are not allowed.".into());
    }
    if !cfg.region.trim().is_empty() && !is_valid_region(&cfg.region) {
        return Some(format!("'{}' does not look like an AWS region (for example ap-south-1).", cfg.region));
    }
    None
}

/// None when the tunnel is fine, otherwise a plain-language reason.
pub fn validate_tunnel(t: &TunnelConfig, everyone: &[TunnelConfig]) -> Option<String> {
    if t.profile.trim().is_empty() { return Some("No AWS account is chosen for this server. Use Add / change servers.".into()); }
    if !is_valid_profile(&t.profile) { return Some("The AWS profile name contains characters that are not allowed.".into()); }
    if !is_valid_region(&t.region) { return Some(format!("'{}' does not look like an AWS region (for example ap-south-1). Use Add / change servers.", t.region)); }
    if !is_valid_instance_id(&t.instance_id) { return Some(format!("'{}' is not a valid EC2 instance ID (it looks like i-0123456789abcdef0).", t.instance_id)); }
    if !(1024..=65535).contains(&t.local_port) { return Some(format!("Local port {} is not allowed. Use a port from 1024 to 65535.", t.local_port)); }
    if t.remote_port == 0 { return Some(format!("Remote port {} is not valid.", t.remote_port)); }
    if everyone.iter().filter(|o| o.enabled && o.local_port == t.local_port).count() > 1 {
        return Some(format!("Local port {} is used by more than one connection.", t.local_port));
    }
    if !is_valid_login(&t.user_name) { return Some("The login user name contains characters that are not allowed.".into()); }
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    fn good() -> TunnelConfig {
        TunnelConfig { name: "a".into(), instance_id: "i-0123456789abcdef0".into(), profile: "p".into(), region: "ap-south-1".into(), local_port: 2201, ..Default::default() }
    }
    #[test]
    fn ok_and_bad() {
        assert!(validate_tunnel(&good(), &[good()]).is_none());
        assert!(validate_tunnel(&TunnelConfig { instance_id: "x; rm -rf".into(), ..good() }, &[]).unwrap().contains("instance"));
        assert!(validate_tunnel(&TunnelConfig { local_port: 80, ..good() }, &[]).unwrap().contains("port"));
        assert!(validate_tunnel(&TunnelConfig { region: "mars".into(), ..good() }, &[]).unwrap().contains("region"));
        assert!(validate_tunnel(&TunnelConfig { user_name: "a b".into(), ..good() }, &[]).unwrap().contains("user name"));
        let b = TunnelConfig { instance_id: "i-0123456789abcdef1".into(), ..good() };
        assert!(validate_tunnel(&good(), &[good(), b]).unwrap().contains("more than one"));
    }
}
