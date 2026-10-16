//! Change one server's login user and key file after setup, without running the whole wizard again.

use super::app::App;
use super::common::message;
use crate::models::TunnelConfig;
use crate::validator::is_valid_login;
use crate::{config_store, key_finder, ssh_config};
use adw::prelude::*;
use gtk::gio;
use std::cell::RefCell;
use std::rc::Rc;

pub fn show(app: &Rc<App>, parent: &impl IsA<gtk::Widget>, t: &TunnelConfig) {
    let heading = format!("{}: login and key", t.name);
    let d = adw::AlertDialog::new(Some(&heading), Some(if t.is_rdp() { "The Windows user to sign in as, and the key file used only to get the Administrator password." } else { "The user SSH logs in as, and the key file that proves it is you." }));
    let b = gtk::Box::new(gtk::Orientation::Vertical, 10);
    let user = gtk::Entry::builder().text(&t.user_name).placeholder_text("Login user, e.g. ubuntu or ec2-user").build();
    b.append(&user);
    let key: Rc<RefCell<Option<String>>> = Rc::new(RefCell::new(t.key_file.clone()));
    let label = gtk::Label::builder().xalign(0.0).wrap(true).build();
    let show_key = { let (label, key) = (label.clone(), key.clone()); move || label.set_text(&key.borrow().as_deref().map(|k| format!("Key file: {k}")).unwrap_or_else(|| "No key file (SSH will use your ssh-agent or ask for a password).".into())) };
    show_key();
    b.append(&label);
    let row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
    let choose = gtk::Button::with_label("Choose a key file…");
    let clear = gtk::Button::with_label("No key file");
    row.append(&choose);
    row.append(&clear);
    b.append(&row);
    let found = gtk::Label::builder().xalign(0.0).wrap(true).build();
    found.add_css_class("dim-label");
    b.append(&found);
    d.set_extra_child(Some(&b));
    d.add_response("cancel", "Cancel");
    d.add_response("save", "Save");
    d.set_response_appearance("save", adw::ResponseAppearance::Suggested);
    d.set_default_response(Some("save"));

    let parent_w: gtk::Widget = parent.clone().upcast();
    {   // choose a file
        let (key, parent_w, show_key) = (key.clone(), parent_w.clone(), show_key.clone());
        choose.connect_clicked(move |_| {
            let dlg = gtk::FileDialog::builder().title("Choose the key file for this server").build();
            if let Some(dir) = dirs_ssh() { dlg.set_initial_folder(Some(&gio::File::for_path(dir))); }
            let (key, show_key) = (key.clone(), show_key.clone());
            let win = parent_w.root().and_downcast::<gtk::Window>();
            dlg.open(win.as_ref(), None::<&gio::Cancellable>, move |res| {
                if let Ok(f) = res { if let Some(p) = f.path() {
                    match key_finder::classify(&p) {
                        Some(_) => { *key.borrow_mut() = Some(p.to_string_lossy().into_owned()); show_key(); }
                        None => { *key.borrow_mut() = Some(p.to_string_lossy().into_owned()); show_key(); }
                    }
                } }
            });
        });
    }
    { let (key, show_key) = (key.clone(), show_key.clone()); clear.connect_clicked(move |_| { *key.borrow_mut() = None; show_key(); }); }
    // suggest keys found on this computer that match this server
    found.set_text("Looking for key files on this computer...");
    { let found = found.clone(); let name = t.name.clone();
      super::common::in_background(|| key_finder::scan_standard(8, &|| false), move |keys| found.set_text(&if keys.is_empty() { String::new() } else { format!("{} key file(s) found in your usual folders (~/.ssh, Downloads...). Use Choose a key file to pick one for {name}.", keys.len()) })); }

    let (app, t) = (app.clone(), t.clone());
    let parent2 = parent_w.clone();
    d.connect_response(None, move |_, r| {
        if r != "save" { return; }
        let u = user.text().trim().to_string();
        if !is_valid_login(&u) { message(&parent2, "Check the login user", "Use letters, digits, dots, dashes or underscores only."); return; }
        let mut cfg = config_store::load().unwrap_or_default();
        if let Some(x) = cfg.tunnels.iter_mut().find(|x| x.instance_id == t.instance_id && x.profile == t.profile) {
            x.user_name = u;
            x.key_file = key.borrow().clone();
        }
        let _ = config_store::save(&cfg);
        let _ = ssh_config::sync(&cfg);
        app.manager.poke();
        if let Some(w) = app.window.borrow().as_ref() { w.reload(); w.toast(&format!("Saved. {} now uses these settings.", t.name)); }
    });
    d.present(Some(parent));
}

fn dirs_ssh() -> Option<std::path::PathBuf> { let p = crate::paths::ssh_dir(); p.is_dir().then_some(p) }
