mod common;
use common::{wait_for, Home, FAKE_AWS};
use ssmconnect::models::*;
use ssmconnect::tunnel_manager::TunnelManager;
use ssmconnect::{config_store, netcheck, status_store};

fn free_port() -> u16 { std::net::TcpListener::bind("127.0.0.1:0").unwrap().local_addr().unwrap().port() }

fn tunnel(id: &str, port: u16) -> TunnelConfig {
    TunnelConfig { name: "web".into(), instance_id: id.into(), profile: "p".into(), region: "ap-south-1".into(), local_port: port, ..Default::default() }
}

fn setup() -> (Home, TunnelManager) {
    let h = Home::new();
    h.tool("aws", FAKE_AWS);
    h.tool("session-manager-plugin", "#!/bin/sh\n");
    let m = TunnelManager::with(Box::new(config_store::load), std::time::Duration::from_millis(200));
    (h, m)
}

fn save(ts: Vec<TunnelConfig>) { config_store::save(&AppConfig { tunnels: ts, ..Default::default() }).unwrap(); }
fn state(m: &TunnelManager) -> Option<State> { m.snapshot().first().map(|s| s.state) }

#[test]
fn connects_and_stops_cleanly() {
    let (_h, m) = setup();
    let port = free_port();
    save(vec![tunnel("i-0123456789abcdef0", port)]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Connected)), "{:?}", m.snapshot());
    assert!(netcheck::is_listening(port));
    assert!(!netcheck::is_exposed(port));
    assert_eq!(status_store::load().1[0].state, State::Connected);
    m.stop();
    assert!(wait_for(10, || !netcheck::is_listening(port)));
}

#[test]
fn friendly_error_for_offline_server() {
    let (_h, m) = setup();
    save(vec![tunnel("i-0fa1100000000000", free_port())]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Error)));
    assert!(m.snapshot()[0].message.contains("offline in SSM"));
    m.stop();
}

#[test]
fn refuses_port_used_by_another_program() {
    let (_h, m) = setup();
    let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = l.local_addr().unwrap().port();
    save(vec![tunnel("i-0123456789abcdef0", port)]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Error)));
    assert!(m.snapshot()[0].message.contains("already used"));
    m.stop();
}

#[test]
fn config_change_reconciles() {
    let (_h, m) = setup();
    let port = free_port();
    save(vec![tunnel("i-0123456789abcdef0", port)]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Connected)));
    save(vec![TunnelConfig { enabled: false, ..tunnel("i-0123456789abcdef0", port) }]);
    m.poke();
    assert!(wait_for(10, || state(&m) == Some(State::Disabled)));
    assert!(wait_for(10, || !netcheck::is_listening(port)));
    m.stop();
}

#[test]
fn invalid_config_reported_not_started() {
    let (_h, m) = setup();
    save(vec![tunnel("oops", free_port())]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Error)));
    assert!(m.snapshot()[0].message.contains("not a valid EC2 instance"));
    m.stop();
}
