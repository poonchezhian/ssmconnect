//! Developer tool: drives the real wizard window through all five steps against a fake AWS CLI in a throw-away HOME.
//! Needs a display. Usage: cargo run --example e2e_wizard

use adw::prelude::*;
use glib::ControlFlow;
use gtk::glib;
use ssmconnect::{config_store, key_finder};
use std::cell::{Cell, RefCell};
use std::os::unix::fs::PermissionsExt;
use std::process::Command;
use std::rc::Rc;

fn fail(msg: &str) -> ! { println!("FAIL: {msg}"); std::process::exit(1) }

fn main() {
    let home = std::env::temp_dir().join(format!("ssmconnect-e2e-{}", std::process::id()));
    let bin = home.join(".local/share/ssmconnect/bin");
    std::fs::create_dir_all(&bin).unwrap();
    for (n, body) in [("aws", include_str!("../tests/common/fake-aws")), ("session-manager-plugin", "#!/bin/sh\n")] {
        std::fs::write(bin.join(n), body).unwrap();
        std::fs::set_permissions(bin.join(n), std::fs::Permissions::from_mode(0o755)).unwrap();
    }
    std::env::set_var("HOME", &home);
    for v in ["XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"] { std::env::remove_var(v); }
    let key = home.join("Downloads/prod-key.pem");
    std::fs::create_dir_all(key.parent().unwrap()).unwrap();
    assert!(Command::new("openssl").args(["genrsa", "-traditional", "-out"]).arg(&key).arg("2048").output().unwrap().status.success());
    let mut fps: Vec<String> = key_finder::fingerprints(&key).into_iter().collect();
    fps.sort();
    std::env::set_var("FAKE_FPR", &fps[0]);

    let started = std::time::Instant::now();
    let stage = Rc::new(Cell::new(0u32));
    let driver: Rc<RefCell<Option<Rc<dyn ssmconnect::ui::wizard::WizardDriver>>>> = Rc::new(RefCell::new(None));
    let hook: Box<dyn Fn(&Rc<ssmconnect::ui::app::App>)> = Box::new(move |app| {
        let (app, stage, driver, home) = (app.clone(), stage.clone(), driver.clone(), home.clone());
        glib::timeout_add_local(std::time::Duration::from_millis(250), move || {
            if started.elapsed().as_secs() > 90 { fail(&format!("timed out in stage {}", stage.get())); }
            if driver.borrow().is_none() {
                if let Some(w) = app.wizard.borrow().as_ref() { *driver.borrow_mut() = Some(w.driver()); } else { return ControlFlow::Continue; }
            }
            let d = driver.borrow().clone().unwrap();
            match stage.get() {
                0 if d.step() == 0 && !d.busy() => { println!("1 requirements ok"); d.next(); stage.set(1); }
                1 if d.step() == 1 => { d.set_sign_in("https://acme.awsapps.com/start", "us-east-1", "ap-south-1"); d.next(); stage.set(2); }
                2 if d.step() == 2 && !d.busy() && !d.found().is_empty() => {
                    println!("3 servers: {:?}", d.found());
                    if d.members() != 2 { fail("expected 2 member accounts"); }
                    d.tick_all(); d.next(); stage.set(3);
                }
                3 if d.step() == 3 && !d.busy() && !d.key_rows().is_empty() => {
                    let rows = d.key_rows();
                    println!("4 keys: {rows:?}");
                    if rows.iter().any(|(_, k)| !k.as_deref().is_some_and(|p| p.ends_with("prod-key.pem"))) { fail("key not auto-matched"); }
                    d.next(); stage.set(4);
                }
                4 if d.step() == 4 => { d.next(); stage.set(5); }
                5 => {
                    let Some(cfg) = config_store::load().filter(|c| c.tunnels.len() == 2) else { return ControlFlow::Continue };
                    let ports: Vec<u16> = cfg.tunnels.iter().map(|t| t.local_port).collect();
                    if ports != [2201, 2202] { fail(&format!("unexpected ports {ports:?}")); }
                    let conf = std::fs::read_to_string(home.join(".ssh/ssmconnect.conf")).unwrap_or_default();
                    if !conf.contains("Host ssc-web-prod-1") || conf.contains("win-app") { fail(&format!("ssh config wrong:\n{conf}")); }
                    println!("saved: {:?}", cfg.tunnels.iter().map(|t| (&t.name, &t.protocol, t.local_port, &t.user_name, &t.environment)).collect::<Vec<_>>());
                    println!("ssh config ok");
                    let snap = app.manager.snapshot();
                    if snap.len() == 2 && snap.iter().all(|s| s.state == ssmconnect::models::State::Connected) {
                        println!("tunnels connected ok: {:?}", snap.iter().map(|s| (&s.name, s.state.label())).collect::<Vec<_>>());
                        println!("PASS ({:.1}s)", started.elapsed().as_secs_f64());
                        app.gtk.quit();
                        return ControlFlow::Break;
                    }
                }
                _ => {}
            }
            ControlFlow::Continue
        });
    });
    ssmconnect::ui::app::run_with(false, Some(hook));
}
