//! Preferences: every setting SSMConnect has, in plain words. Nothing here needs a config file or a command line.

use super::app::App;
use super::common::in_background;
use crate::models::AppConfig;
use crate::{aws_cli, config_store, desktop, launchers, paths, prereq_installer, ssh_config};
use adw::prelude::*;
use std::rc::Rc;

/// Loads the saved settings, lets `f` change them, saves, and re-applies what depends on them.
fn change(app: &Rc<App>, f: impl FnOnce(&mut AppConfig)) {
    let mut cfg = config_store::load().unwrap_or_default();
    f(&mut cfg);
    if let Err(e) = config_store::save(&cfg) { app.toast(&format!("Could not save the setting: {e}")); return; }
    let _ = ssh_config::sync(&cfg);   // the strict-identity switch lives in the ssh settings
    app.manager.poke();
    if let Some(w) = app.window.borrow().as_ref() { w.reload(); }
}

fn switch(title: &str, subtitle: &str, on: bool) -> adw::SwitchRow { adw::SwitchRow::builder().title(title).subtitle(subtitle).active(on).build() }

fn combo(title: &str, subtitle: &str, items: &[String], selected: usize) -> adw::ComboRow {
    let refs: Vec<&str> = items.iter().map(String::as_str).collect();
    adw::ComboRow::builder().title(title).subtitle(subtitle).model(&gtk::StringList::new(&refs)).selected(selected as u32).build()
}

pub fn show(app: &Rc<App>, parent: &impl IsA<gtk::Widget>) {
    let cfg = config_store::load().unwrap_or_default();
    let dialog = adw::PreferencesDialog::builder().title("Preferences").build();
    let page = adw::PreferencesPage::new();

    // ---- starting
    let g = adw::PreferencesGroup::builder().title("When SSMConnect starts").build();
    let s = switch("Open my connections automatically", "When this is off, nothing connects until you press Start.", cfg.autostart_connections);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.autostart_connections = on); }); }
    g.add(&s);
    let s = switch("Start SSMConnect when I sign in to my computer", "SSMConnect starts hidden in the background and opens your connections. Turn it off to start it yourself from the application menu.", desktop::autostart_enabled());
    { let a = app.clone(); s.connect_active_notify(move |r| match desktop::set_autostart(r.is_active()) { Ok(()) => {}, Err(e) => a.toast(&format!("Could not change it: {e}")) }); }
    g.add(&s);
    page.add(&g);

    // ---- connecting
    let g = adw::PreferencesGroup::builder().title("Connecting").build();
    let s = switch("Make me confirm each server's identity first", "Recommended for important servers. SSMConnect and ssh refuse to connect to a server until you have confirmed its identity under Server identities. When off, ssh remembers a server's identity the first time automatically (and still refuses if it later changes).", cfg.strict_host_keys);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.strict_host_keys = on); }); }
    g.add(&s);
    let s = switch("Let me type ssh ssc-… in a terminal", "SSMConnect adds one line to your ~/.ssh/config (a backup copy is kept) so ssh, scp, rsync and VS Code can reach your servers by a short name. Turn it off to take that line out again; the Terminal and Files buttons then no longer work, and your own ssh hosts are never changed.", cfg.ssh_integration);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.ssh_integration = on); }); }
    g.add(&s);
    let r = adw::ActionRow::builder().title("Server identities").subtitle("See, confirm or forget the identity of each server.").activatable(true).build();
    r.add_suffix(&gtk::Image::from_icon_name("go-next-symbolic"));
    { let (a, d) = (app.clone(), dialog.clone()); r.connect_activated(move |_| super::identities::show(&a, &d)); }
    g.add(&r);

    let mut terms = vec!["Automatic".to_string()];
    terms.extend(launchers::installed_terminals());
    let sel = terms.iter().position(|t| *t == cfg.terminal).unwrap_or(0);
    let c = combo("Terminal program", "Opened by the Terminal button. Automatic picks one that is installed.", &terms, sel);
    { let (a, terms) = (app.clone(), terms.clone()); c.connect_selected_notify(move |r| { let i = r.selected() as usize; change(&a, |c| c.terminal = if i == 0 { String::new() } else { terms[i].clone() }); }); }
    g.add(&c);
    let mut rdps = vec!["Automatic".to_string()];
    rdps.extend(launchers::installed_rdp_clients());
    let sel = rdps.iter().position(|t| *t == cfg.rdp_client).unwrap_or(0);
    let c = combo("Remote Desktop program", if rdps.len() == 1 { "None is installed. Install Remmina or FreeRDP to open Windows servers." } else { "Opened for Windows servers." }, &rdps, sel);
    { let (a, rdps) = (app.clone(), rdps.clone()); c.connect_selected_notify(move |r| { let i = r.selected() as usize; change(&a, |c| c.rdp_client = if i == 0 { String::new() } else { rdps[i].clone() }); }); }
    g.add(&c);
    let modes = vec!["Automatic".to_string(), "Always the file manager".to_string(), "Always a terminal (sftp command)".to_string()];
    let keys = ["auto", "file-manager", "terminal"];
    let c = combo("Files button", "How the server's files are opened for exchange over SFTP.", &modes, keys.iter().position(|k| *k == cfg.files_mode).unwrap_or(0));
    { let a = app.clone(); c.connect_selected_notify(move |r| { let k = keys[r.selected() as usize].to_string(); change(&a, |c| c.files_mode = k); }); }
    g.add(&c);
    let s = switch("Show how busy my servers are", "Reads processor, memory and disk use of connected servers about every 5 minutes with a read-only command run through Session Manager. Needs permission to run commands; without it the numbers stay as dashes. Servers > Refresh usage reads them now.", cfg.read_usage);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.read_usage = on); }); }
    g.add(&s);
    let s = switch("...also on production servers", "Off by default so nothing runs on production unless you ask (Refresh usage still does).", cfg.read_usage_on_prod);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.read_usage_on_prod = on); }); }
    g.add(&s);
    let p = adw::SpinRow::with_range(1024.0, 64000.0, 1.0);
    p.set_title("First local port for new servers");
    p.set_subtitle("Each server gets its own local port, counting up from here. Servers you already saved keep theirs.");
    p.set_value(f64::from(cfg.first_port));
    { let a = app.clone(); p.connect_value_notify(move |r| { let v = r.value() as u16; change(&a, |c| c.first_port = v); }); }
    g.add(&p);
    page.add(&g);

    // ---- updates and tools
    let g = adw::PreferencesGroup::builder().title("Updates and tools").build();
    let s = switch("Look for a new version once a day", "SSMConnect only tells you. It never installs anything by itself.", cfg.check_updates);
    { let a = app.clone(); s.connect_active_notify(move |r| { let on = r.is_active(); change(&a, |c| c.check_updates = on); }); }
    g.add(&s);
    let r = adw::ActionRow::builder().title("Check for a new version now").activatable(true).build();
    r.add_suffix(&gtk::Image::from_icon_name("go-next-symbolic"));
    { let a = app.clone(); r.connect_activated(move |_| a.gtk.activate_action("check-updates", None)); }
    g.add(&r);
    let have = |o: Option<std::path::PathBuf>| o.map(|p| p.display().to_string());
    let tools = adw::ActionRow::builder().title("AWS tools").subtitle(match (have(aws_cli::find_aws()), have(aws_cli::find_plugin())) {
        (Some(_), Some(_)) => "The AWS CLI and the Session Manager plugin are installed.".to_string(),
        _ => "Something is missing. Press Install to fetch it from Amazon (the signature is checked).".to_string() }).build();
    let b = gtk::Button::builder().label("Install or repair").valign(gtk::Align::Center).build();
    { let a = app.clone(); let d = dialog.clone(); b.connect_clicked(move |btn| {
        btn.set_sensitive(false);
        a.toast("Fetching the AWS tools from Amazon...");
        let (a, btn, d) = (a.clone(), btn.clone(), d.clone());
        in_background(|| prereq_installer::install_missing(&|_| {}), move |res| { btn.set_sensitive(true); match res {
            Ok(done) => { let _ = &d; a.toast(&if done.is_empty() { "Everything was already installed.".to_string() } else { format!("Installed: {}.", done.join(", ")) }); a.manager.poke(); }
            Err(e) => a.toast(&e) } });
    }); }
    tools.add_suffix(&b);
    g.add(&tools);
    page.add(&g);

    // ---- data
    let g = adw::PreferencesGroup::builder().title("Your data").description("SSMConnect keeps your saved servers and settings when it is updated or uninstalled. They are only deleted if you ask for it here.").build();
    for (title, sub, path, file) in [("Open the settings folder", "Your saved servers and settings.", paths::config_dir(), false), ("Open the log file", "What SSMConnect did, for troubleshooting.", paths::log_path(), true)] {
        let r = adw::ActionRow::builder().title(title).subtitle(sub).activatable(true).build();
        r.add_suffix(&gtk::Image::from_icon_name("document-open-symbolic"));
        let a = app.clone();
        r.connect_activated(move |_| if path.exists() { launchers::open_file(&path.to_string_lossy()) } else { a.toast(if file { "There is no log file yet." } else { "There are no saved settings yet." }) });
        g.add(&r);
    }
    let r = adw::ActionRow::builder().title("Delete all my SSMConnect settings…").subtitle("Removes your saved servers, accounts' friendly names, these preferences and the log, and disconnects SSMConnect from ssh. Your AWS logins and your own ssh hosts are never touched.").subtitle_lines(4).activatable(true).build();
    r.add_css_class("error");
    { let a = app.clone(); r.connect_activated(move |_| a.gtk.activate_action("cleanup", None)); }
    g.add(&r);
    page.add(&g);

    dialog.add(&page);
    dialog.present(Some(parent));
}
