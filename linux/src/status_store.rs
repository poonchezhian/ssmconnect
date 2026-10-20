//! status.json: the live tunnel state, written by the tunnel manager so other tools (and `ssmconnect status`) can read it.

use crate::models::TunnelStatus;
use crate::paths;
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Default)]
struct File { updated: f64, tunnels: Vec<TunnelStatus> }

pub fn save(tunnels: &[TunnelStatus]) {
    let f = File { updated: crate::util::now(), tunnels: tunnels.to_vec() };
    if let Ok(text) = serde_json::to_string_pretty(&f) { let _ = paths::write_private(&paths::status_path(), &text); }
}

pub fn load() -> (f64, Vec<TunnelStatus>) {
    std::fs::read_to_string(paths::status_path()).ok().and_then(|t| serde_json::from_str::<File>(&t).ok()).map(|f| (f.updated, f.tunnels)).unwrap_or((0.0, vec![]))
}
