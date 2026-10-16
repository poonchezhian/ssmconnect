//! Command-line interface. With no command the graphical app opens; the commands below work in a terminal or over ssh.

use crate::models::State;
use crate::tunnel_manager::TunnelManager;
use crate::{aws_cli, aws_profiles, config_store, desktop, discovery, launchers, netcheck, prereq_installer, status_store, VERSION};
use std::process::ExitCode;

const HELP: &str = "SSMConnect for Linux: SSH and Remote Desktop to AWS servers over Session Manager (port 22 closed).

Usage: ssmconnect [--background] [COMMAND]

Commands:
  (none)         open the app
  doctor         check the tools SSMConnect needs
  install-tools  install the AWS CLI and Session Manager plugin for this user
  list           list saved servers
  status         show live connection status
  run            open all connections in this terminal until Ctrl+C
  scan           scan every account for new or changed servers
  cleanup [--keep-settings]
                 remove SSMConnect's traces for this user (ssh include, autostart, settings)

Options:
  --background   start hidden and keep connections open (used by 'start when I sign in')
  --version      show the version
  -h, --help     show this help";

fn doctor() -> ExitCode {
    let t = launchers::tools_summary();
    let rows = [
        ("AWS CLI", aws_cli::find_aws().map(|p| p.display().to_string()), "run: ssmconnect install-tools", true),
        ("Session Manager plugin", aws_cli::find_plugin().map(|p| p.display().to_string()), "run: ssmconnect install-tools", true),
        ("ssh", t.ssh, "install the OpenSSH client", true),
        ("Terminal", t.terminal, "optional: needed for the Terminal button", false),
        ("Remote Desktop client", t.rdp, "optional: Remmina or FreeRDP, for Windows servers", false),
        ("File manager (sftp://)", t.files, "optional", false),
    ];
    let mut ok = true;
    for (label, found, hint, required) in rows {
        println!("{}  {:<24} {}", if found.is_some() { "ok     " } else { "MISSING" }, label, found.as_deref().unwrap_or(hint));
        ok &= found.is_some() || !required;
    }
    println!("         AWS profiles             {} found in ~/.aws", aws_profiles::list_profiles().len());
    if ok { ExitCode::SUCCESS } else { ExitCode::from(1) }
}

fn list() -> ExitCode {
    let (cfg, warn) = config_store::load_or_recover();
    if let Some(w) = warn { eprintln!("{w}"); }
    if cfg.tunnels.is_empty() { println!("No servers saved yet. Open SSMConnect and use Add / change servers."); return ExitCode::SUCCESS; }
    let (_, status) = status_store::load();
    for t in &cfg.tunnels {
        let st = status.iter().find(|s| s.instance_id == t.instance_id).map(|s| s.state.label()).unwrap_or("Not running");
        println!("{:<32} {:<15} {}  127.0.0.1:{:<5} {:<10} {}", t.name, if t.environment.is_empty() { "-" } else { &t.environment }, if t.is_rdp() { "rdp" } else { "ssh" }, t.local_port, st, t.instance_id);
    }
    ExitCode::SUCCESS
}

fn status() -> ExitCode {
    let (when, rows) = status_store::load();
    if rows.is_empty() { println!("No connections are running (start SSMConnect or `ssmconnect run`)."); return ExitCode::SUCCESS; }
    println!("Updated {:.0}s ago", crate::util::now() - when);
    for s in rows {
        println!("{:<32} :{:<5} {:<10} {} {}", s.name, s.local_port, s.state.label(), if netcheck::is_listening(s.local_port) { "(port open)" } else { "" }, s.message);
    }
    ExitCode::SUCCESS
}

fn run() -> ExitCode {
    let mgr = std::sync::Arc::new(TunnelManager::new());
    let last = std::sync::Arc::new(std::sync::Mutex::new(Vec::<(String, State, String)>::new()));
    let (m2, l2) = (mgr.clone(), last.clone());
    mgr.set_on_change(move || {
        let now: Vec<_> = m2.snapshot().into_iter().map(|s| (s.name, s.state, s.message)).collect();
        let mut prev = crate::util::lock(&l2);
        for row in now.iter().filter(|r| !prev.contains(r)) { println!("{}  {}: {} - {}", clock(), row.0, row.1.label(), row.2); }
        *prev = now;
    });
    mgr.start();
    println!("SSMConnect: connections are opening. Press Ctrl+C to close them.");
    // Ctrl+C: SIGINT is delivered to the whole foreground group (the sessions end with us because of PDEATHSIG/process groups)
    let stop = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));
    extern "C" fn on_sigint(_: libc::c_int) { STOP.store(true, std::sync::atomic::Ordering::SeqCst); }
    static STOP: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);
    unsafe { libc::signal(libc::SIGINT, on_sigint as *const () as usize); libc::signal(libc::SIGTERM, on_sigint as *const () as usize); }
    let _ = &stop;
    while !STOP.load(std::sync::atomic::Ordering::SeqCst) { std::thread::sleep(std::time::Duration::from_millis(300)); }
    mgr.stop();
    ExitCode::SUCCESS
}

fn clock() -> String {
    let t = crate::util::now() as libc::time_t;
    let mut tm: libc::tm = unsafe { std::mem::zeroed() };
    unsafe { libc::localtime_r(&t, &mut tm) };
    format!("{:02}:{:02}:{:02}", tm.tm_hour, tm.tm_min, tm.tm_sec)
}

fn scan() -> ExitCode {
    let (cfg, _) = config_store::load_or_recover();
    let changes = discovery::run_monitor(&cfg, true, &|| false);
    println!("{}", if changes.is_empty() { "Scan finished. Nothing changed.".to_string() } else { changes.join("\n") });
    ExitCode::SUCCESS
}

fn install_tools() -> ExitCode {
    match prereq_installer::install_missing(&|m| println!("{m}")) {
        Ok(done) => { println!("{}", if done.is_empty() { "Everything is already installed.".to_string() } else { format!("Installed: {}", done.join(", ")) }); ExitCode::SUCCESS }
        Err(e) => { eprintln!("{e}"); ExitCode::from(1) }
    }
}

pub fn main(args: Vec<String>) -> ExitCode {
    let mut background = false;
    let mut rest: Vec<&str> = vec![];
    for a in &args {
        match a.as_str() {
            "--version" | "-V" => { println!("SSMConnect for Linux {VERSION}"); return ExitCode::SUCCESS; }
            "-h" | "--help" | "help" => { println!("{HELP}"); return ExitCode::SUCCESS; }
            "--background" => background = true,
            other => rest.push(other),
        }
    }
    match rest.as_slice() {
        [] => ExitCode::from(crate::ui::run(background) as u8),
        ["doctor"] => doctor(),
        ["install-tools"] => install_tools(),
        ["list"] => list(),
        ["status"] => status(),
        ["run"] => run(),
        ["scan"] => scan(),
        ["cleanup"] => { println!("{}", desktop::cleanup_user(false).join("\n")); ExitCode::SUCCESS }
        ["cleanup", "--keep-settings"] => { println!("{}", desktop::cleanup_user(true).join("\n")); ExitCode::SUCCESS }
        other => { eprintln!("Unknown command: {}\n\n{HELP}", other.join(" ")); ExitCode::from(2) }
    }
}
