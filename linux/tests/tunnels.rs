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

#[test]
fn a_leftover_session_from_an_earlier_run_is_cleared_away() {
    use std::os::unix::process::CommandExt;
    let (_h, m) = setup();
    let port = free_port();
    // stands in for a Session Manager plugin that outlived the SSMConnect that started it: right name, right arguments, holding the port, other process group
    let code = format!("import socket,time\ns=socket.socket();s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('127.0.0.1',{port}));s.listen(1);time.sleep(120)");
    let params = format!("{{\"Target\": \"i-0123456789abcdef0\", \"DocumentName\": \"AWS-StartPortForwardingSession\", \"Parameters\": {{\"portNumber\": [\"22\"], \"localPortNumber\": [\"{port}\"]}}}}");
    // started directly (no shell: `exec -a` does not exist in dash, the sh of Debian and Ubuntu) with the program name set to the plugin's
    let mut orphan = std::process::Command::new("python3").arg0("session-manager-plugin").arg("-c").arg(code).arg("x").arg(params).process_group(0).spawn().unwrap();
    assert!(wait_for(10, || netcheck::is_listening(port)));
    save(vec![tunnel("i-0123456789abcdef0", port)]);
    m.start();
    assert!(wait_for(20, || state(&m) == Some(State::Connected)), "{:?}", m.snapshot());   // the orphan was ended and a fresh session took the port
    assert!(orphan.try_wait().map(|s| s.is_some()).unwrap_or(false) || wait_for(5, || orphan.try_wait().map(|s| s.is_some()).unwrap_or(false)));
    m.stop();
    let _ = orphan.kill();
}

#[test]
fn another_programs_port_is_never_touched() {
    // only Session Manager sessions for that exact port are ended; anything else holding it is respected
    let (_h, m) = setup();
    let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = l.local_addr().unwrap().port();
    save(vec![tunnel("i-0123456789abcdef0", port)]);
    m.start();
    assert!(wait_for(15, || state(&m) == Some(State::Error)));
    assert!(m.snapshot()[0].message.contains("already used"));
    assert!(std::net::TcpStream::connect(("127.0.0.1", port)).is_ok());
    m.stop();
}
