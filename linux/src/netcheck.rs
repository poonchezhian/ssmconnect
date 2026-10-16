//! Which local TCP ports are in use, read from /proc/net (no extra tools, works the same on every distribution).

use std::collections::HashSet;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

fn parse(path: &str, v6: bool) -> Vec<(IpAddr, u16)> {
    let Ok(text) = std::fs::read_to_string(path) else { return vec![] };
    text.lines().skip(1).filter_map(|line| {
        let f: Vec<&str> = line.split_whitespace().collect();
        if f.len() < 4 || f[3] != "0A" { return None; }   // 0A = LISTEN
        let (addr, port) = f[1].split_once(':')?;
        let port = u16::from_str_radix(port, 16).ok()?;
        let ip = if v6 {
            if addr.len() != 32 { return None; }
            let mut b = [0u8; 16];
            for w in 0..4 {   // four little-endian 32-bit words
                let word = u32::from_str_radix(&addr[w * 8..w * 8 + 8], 16).ok()?;
                b[w * 4..w * 4 + 4].copy_from_slice(&word.to_le_bytes());
            }
            IpAddr::V6(Ipv6Addr::from(b))
        } else {
            IpAddr::V4(Ipv4Addr::from(u32::from_str_radix(addr, 16).ok()?.to_le_bytes()))
        };
        Some((ip, port))
    }).collect()
}

pub fn listeners() -> Vec<(IpAddr, u16)> {
    let mut v = parse("/proc/net/tcp", false);
    v.extend(parse("/proc/net/tcp6", true));
    v
}

pub fn is_listening(port: u16) -> bool { listeners().iter().any(|(_, p)| *p == port) }

pub fn is_loopback(ip: &IpAddr) -> bool {
    match ip {
        IpAddr::V4(a) => a.is_loopback(),
        IpAddr::V6(a) => a.is_loopback() || a.to_ipv4_mapped().is_some_and(|m| m.is_loopback()),
    }
}

/// True when something listens on the port on an address other than this computer's own loopback.
pub fn is_exposed(port: u16) -> bool { listeners().iter().any(|(a, p)| *p == port && !is_loopback(a)) }

/// The first port from `start` that no program is using and no saved connection has.
pub fn free_port(start: u16, taken: &HashSet<u16>) -> u16 {
    let used: HashSet<u16> = listeners().into_iter().map(|(_, p)| p).chain(taken.iter().copied()).collect();
    let mut port = start.max(1024);
    while used.contains(&port) && port < 65535 { port += 1; }
    port
}
