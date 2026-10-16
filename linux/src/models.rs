//! Plain data shared by the engine and the interface. JSON field names are the same as in the Python edition (0.0.x),
//! so settings saved by it are read as they are.

use crate::classifier;
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

fn yes() -> bool { true }
fn ssh_port() -> u16 { 22 }
fn ssh_proto() -> String { "ssh".into() }
fn ec2_user() -> String { "ec2-user".into() }
fn linux() -> String { "Linux".into() }

/// One SSH-over-SSM tunnel: local 127.0.0.1:local_port -> instance:remote_port.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct TunnelConfig {
    pub name: String,
    pub instance_id: String,
    /// AWS profile (account) this server is reached with
    pub profile: String,
    pub region: String,
    pub local_port: u16,
    #[serde(default = "ssh_port")]
    pub remote_port: u16,
    /// "ssh" for Linux servers, "rdp" for Windows servers
    #[serde(default = "ssh_proto")]
    pub protocol: String,
    #[serde(default = "ec2_user")]
    pub user_name: String,
    /// Production, Staging ... from the server's AWS tags
    pub environment: String,
    pub key_file: Option<String>,
    #[serde(default = "yes")]
    pub enabled: bool,
    pub os_name: String,
}

impl Default for TunnelConfig {
    fn default() -> Self {
        Self { name: String::new(), instance_id: String::new(), profile: String::new(), region: String::new(), local_port: 0, remote_port: 22,
               protocol: "ssh".into(), user_name: "ec2-user".into(), environment: String::new(), key_file: None, enabled: true, os_name: String::new() }
    }
}

impl TunnelConfig {
    pub fn is_rdp(&self) -> bool { self.protocol.eq_ignore_ascii_case("rdp") }
    pub fn is_prod(&self) -> bool { classifier::is_prod(&self.environment) }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct AppConfig {
    /// Account and region last chosen in setup (only pre-selects lists)
    pub profile: String,
    pub region: String,
    pub tunnels: Vec<TunnelConfig>,
    /// Friendly account names
    pub aliases: BTreeMap<String, String>,
    /// Primary-account key shown on the dashboard
    pub selected_account: String,
    #[serde(default = "yes")]
    pub autostart_connections: bool,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self { profile: String::new(), region: String::new(), tunnels: vec![], aliases: BTreeMap::new(), selected_account: String::new(), autostart_connections: true }
    }
}

impl AppConfig {
    pub fn alias_or(&self, key: &str, fallback: &str) -> String {
        self.aliases.get(key).filter(|v| !v.trim().is_empty()).cloned().unwrap_or_else(|| fallback.to_string())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct DiscoveredServer {
    pub name: String,
    pub profile: String,
    pub environment: String,
    pub instance_id: String,
    pub private_ip: String,
    #[serde(default = "linux")]
    pub platform: String,
    pub key_name: String,
    pub key_fingerprint: String,
    pub ssm_online: bool,
    pub os_name: String,
}

impl Default for DiscoveredServer {
    fn default() -> Self {
        Self { name: String::new(), profile: String::new(), environment: String::new(), instance_id: String::new(), private_ip: String::new(),
               platform: "Linux".into(), key_name: String::new(), key_fingerprint: String::new(), ssm_online: false, os_name: String::new() }
    }
}

impl DiscoveredServer {
    pub fn is_windows(&self) -> bool { self.platform.to_lowercase().contains("windows") }
    pub fn is_prod(&self) -> bool { classifier::is_prod(&self.environment) }
    /// The login user that distribution's cloud images use by default.
    pub fn suggested_user(&self) -> &'static str {
        if self.is_windows() { return "Administrator"; }
        let n = self.os_name.to_lowercase();
        for (needle, user) in [("ubuntu", "ubuntu"), ("debian", "admin"), ("centos", "centos"), ("fedora", "fedora"), ("rocky", "rocky")] {
            if n.contains(needle) { return user; }
        }
        "ec2-user" // Amazon Linux, Red Hat, SUSE, AlmaLinux, Oracle Linux and unknown
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum State { Starting, Connected, Retrying, Error, Disabled }

impl State {
    pub fn label(self) -> &'static str {
        match self { State::Starting => "Starting", State::Connected => "Connected", State::Retrying => "Retrying", State::Error => "Error", State::Disabled => "Disabled" }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct TunnelStatus {
    pub name: String,
    pub instance_id: String,
    pub local_port: u16,
    pub state: State,
    pub message: String,
    pub since: f64,
}
