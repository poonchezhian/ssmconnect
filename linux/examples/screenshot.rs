//! Developer tool: opens the dashboard (or a wizard page) against a throw-away HOME with a fake AWS CLI and saves a PNG.
//! Usage: cargo run --example screenshot -- out.png [light|dark] [dashboard|wizard]

use adw::prelude::*;
use gtk::{glib, graphene};
use ssmconnect::models::*;
use ssmconnect::{aws_profiles as ap, config_store};
use std::os::unix::fs::PermissionsExt;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let out = args.first().cloned().expect("output path");
    let dark = args.get(1).is_some_and(|s| s == "dark");
    let what = args.get(2).cloned().unwrap_or_else(|| "dashboard".into());
    let home = std::env::temp_dir().join(format!("ssmconnect-shot-{}", std::process::id()));
    let bin = home.join(".local/share/ssmconnect/bin");
    std::fs::create_dir_all(&bin).unwrap();
    for (n, body) in [("aws", include_str!("../tests/common/fake-aws")), ("session-manager-plugin", "#!/bin/sh\n")] {
        std::fs::write(bin.join(n), body).unwrap();
        std::fs::set_permissions(bin.join(n), std::fs::Permissions::from_mode(0o755)).unwrap();
    }
    std::env::set_var("HOME", &home);
    for v in ["XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"] { std::env::remove_var(v); }

    ap::save_sso_session("acme", "https://acme.awsapps.com/start", "us-east-1", true).unwrap();
    for (n, id) in [("acme-prod", "111111111111"), ("acme-dev", "222222222222"), ("acme-empty", "333333333333")] { ap::save_sso_profile(n, "acme", id, "Admin").unwrap(); }
    let t = |n: &str, i: &str, p: u16, env: &str, prof: &str, os: &str, rdp: bool| TunnelConfig {
        name: n.into(), instance_id: i.into(), profile: prof.into(), region: "ap-south-1".into(), local_port: p, environment: env.into(), os_name: os.into(),
        user_name: if rdp { "Administrator".into() } else { "ubuntu".into() }, protocol: if rdp { "rdp".into() } else { "ssh".into() }, ..Default::default() };
    let mut cfg = AppConfig::default();
    cfg.aliases.extend([("sso:acme.awsapps.com", "Acme Corp"), ("acme-prod", "Production account"), ("acme-dev", "Development account")].map(|(a, b)| (a.to_string(), b.to_string())));
    cfg.tunnels = vec![t("web-prod-1", "i-0123456789abcdef0", 24101, "Production", "acme-prod", "Ubuntu", false), t("db-prod", "i-0123456789abcdef1", 24102, "Production", "acme-prod", "Amazon Linux", false),
                       t("api-dev", "i-0123456789abcdef2", 24103, "Development", "acme-dev", "Debian GNU/Linux", false), t("win-app", "i-0fa1100000000000", 24104, "Test", "acme-dev", "", true)];
    config_store::save(&cfg).unwrap();

    let delay: u64 = std::env::var("SHOT_DELAY").ok().and_then(|v| v.parse().ok()).unwrap_or(5000);
    let hook: Box<dyn Fn(&std::rc::Rc<ssmconnect::ui::app::App>)> = Box::new(move |app| {
        adw::StyleManager::default().set_color_scheme(if dark { adw::ColorScheme::ForceDark } else { adw::ColorScheme::ForceLight });
        if what == "wizard" { app.open_wizard(false); }
        if what == "accounts" { app.open_accounts(); }
        if what == "prefs" { app.gtk.activate_action("preferences", None); }
        if what == "identities" { app.gtk.activate_action("identities", None); }
        let (app, out, what) = (app.clone(), out.clone(), what.clone());
        glib::timeout_add_local_once(std::time::Duration::from_millis(delay), move || {
            let w: gtk::Widget = if what == "wizard" { app.wizard.borrow().as_ref().unwrap().window().clone().upcast() } else { app.window.borrow().as_ref().unwrap().win.clone().upcast() };
            let (width, height) = (w.width() as f32, w.height() as f32);
            let paintable = gtk::WidgetPaintable::new(Some(&w));
            let snap = gtk::Snapshot::new();
            paintable.snapshot(&snap, width as f64, height as f64);
            let node = snap.to_node().expect("render node");
            let tex = w.native().unwrap().renderer().unwrap().render_texture(&node, Some(&graphene::Rect::new(0.0, 0.0, width, height)));
            tex.save_to_png(&out).expect("png");
            println!("saved {out}");
            app.gtk.quit();
        });
    });
    ssmconnect::ui::app::run_with(false, Some(hook));
}
