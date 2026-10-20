//! The application: single instance, keeps connections running while the window is hidden, daily background scan and update check.

use super::common::{confirm, in_background, message};
use super::window::Dashboard;
use crate::tunnel_manager::TunnelManager;
use crate::{self_update, aws_cli, aws_profiles as ap, config_store, desktop, discovery, launchers, paths, update_checker, APP_ID, VERSION};
use adw::prelude::*;
use gtk::{gdk, gio, glib};
use std::cell::{Cell, RefCell};
use std::rc::Rc;
use std::sync::Arc;

static SIGNALLED: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

pub struct App {
    pub gtk: adw::Application,
    pub manager: Arc<TunnelManager>,
    pub window: RefCell<Option<Rc<Dashboard>>>,
    background: bool,
    shown_once: Cell<bool>,
    told_hidden: Cell<bool>,
    pub wizard: RefCell<Option<super::wizard::Wizard>>,
    on_ready: RefCell<Option<Box<dyn Fn(&Rc<App>)>>>,
}

pub fn run(background: bool) -> i32 { run_with(background, None) }

/// Like `run`; `on_ready` is called once the window is up (used by the screenshot and end-to-end example programs).
pub fn run_with(background: bool, on_ready: Option<Box<dyn Fn(&Rc<App>)>>) -> i32 {
    // the example programs run beside a real SSMConnect that may be open, so they must not hand over to it
    let flags = if on_ready.is_some() { gio::ApplicationFlags::NON_UNIQUE } else { gio::ApplicationFlags::FLAGS_NONE };
    let gtk_app = adw::Application::builder().application_id(APP_ID).flags(flags).build();
    let app = Rc::new(App { gtk: gtk_app.clone(), manager: Arc::new(TunnelManager::new()), window: RefCell::new(None), background,
                            shown_once: Cell::new(false), told_hidden: Cell::new(false), wizard: RefCell::new(None), on_ready: RefCell::new(on_ready) });
    let a = app.clone();
    gtk_app.connect_startup(move |_| a.startup());
    let a = app.clone();
    gtk_app.connect_activate(move |_| a.activate());
    let a = app.clone();
    gtk_app.connect_shutdown(move |_| a.manager.stop());
    i32::from(gtk_app.run_with_args::<&str>(&[]).get())
}

impl App {
    fn startup(self: &Rc<Self>) {
        gtk::Window::set_default_icon_name(APP_ID);
        if let Some(display) = gdk::Display::default() {
            let theme = gtk::IconTheme::for_display(&display);
            let dev = concat!(env!("CARGO_MANIFEST_DIR"), "/data/icons");
            if std::path::Path::new(dev).is_dir() { theme.add_search_path(dev); }
            let css = gtk::CssProvider::new();
            css.load_from_string(include_str!("style.css"));
            gtk::style_context_add_provider_for_display(&display, &css, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
        }
        self.actions();
        if config_store::load().is_some_and(|c| !c.tunnels.is_empty() && c.autostart_connections) { self.manager.start(); }
        // logging out, shutting down or `kill` must close the connections cleanly, like Quit does
        extern "C" fn on_signal(_: libc::c_int) { SIGNALLED.store(true, std::sync::atomic::Ordering::SeqCst); }
        for sig in [libc::SIGTERM, libc::SIGHUP, libc::SIGINT] { unsafe { libc::signal(sig, on_signal as *const () as usize); } }
        let app = self.gtk.clone();
        glib::timeout_add_local(std::time::Duration::from_millis(500), move || {
            if SIGNALLED.load(std::sync::atomic::Ordering::SeqCst) { app.quit(); glib::ControlFlow::Break } else { glib::ControlFlow::Continue }
        });
        if self.on_ready.borrow().is_none() { super::tray::start(self); }   // the example programs run beside a real SSMConnect: no second icon
        std::mem::forget(self.gtk.hold());   // the app lives until Quit, even with the window hidden
        let a = self.clone();
        glib::timeout_add_seconds_local_once(8, move || a.background_jobs());
    }

    fn activate(self: &Rc<Self>) {
        if self.background && !self.shown_once.get() { self.shown_once.set(true); return; }   // started at login: stay hidden until asked
        self.show_window();
        let hook = self.on_ready.borrow_mut().take();
        if let Some(f) = hook { let me = self.clone(); glib::timeout_add_local_once(std::time::Duration::from_millis(500), move || f(&me)); }
    }

    pub fn show_window(self: &Rc<Self>) -> Rc<Dashboard> {
        if self.window.borrow().is_none() {
            let d = Dashboard::new(self);
            *self.window.borrow_mut() = Some(d);
            if config_store::load().is_some_and(|c| c.tunnels.is_empty()) {
                let a = self.clone();
                glib::idle_add_local_once(move || a.open_wizard(false));
            }
        }
        let d = self.window.borrow().clone().unwrap();
        d.win.set_visible(true);
        d.win.present();
        d
    }

    /// Re-reads which servers have a confirmed identity (after the person confirmed or forgot one).
    pub fn refresh_trust(&self) { if let Some(w) = self.window.borrow().as_ref() { w.reload_trust(); } }

    pub fn ensure_manager(&self) { self.manager.start(); self.manager.poke(); }

    pub fn toast(&self, text: &str) { if let Some(w) = self.window.borrow().as_ref() { w.toast(text); } }

    pub fn note_hidden(&self) {
        if self.told_hidden.replace(true) { return; }
        let n = gio::Notification::new("SSMConnect is still running");
        n.set_body(Some("Your connections stay open. Open SSMConnect again from the application menu, or choose Quit to close them."));
        self.gtk.send_notification(Some("hidden"), &n);
    }

    fn add_action(self: &Rc<Self>, name: &str, f: impl Fn(&Rc<App>) + 'static) {
        let a = gio::SimpleAction::new(name, None);
        let me = self.clone();
        a.connect_activate(move |_, _| f(&me));
        self.gtk.add_action(&a);
    }

    fn actions(self: &Rc<Self>) {
        self.add_action("quit", |a| a.gtk.quit());
        self.add_action("add-servers", |a| a.open_wizard(false));
        self.add_action("add-account", |a| a.open_wizard(true));
        self.add_action("manage-accounts", |a| a.open_accounts());
        self.add_action("sign-in-again", |a| a.sign_in_again());
        self.add_action("preferences", |a| { let w = a.show_window(); super::preferences::show(a, &w.win); });
        self.add_action("identities", |a| { let w = a.show_window(); super::identities::show(a, &w.win); });
        self.add_action("scan", |a| a.scan_now());
        self.add_action("changes", |a| a.show_changes());
        self.add_action("check-updates", |a| a.check_updates(true));
        self.add_action("open-log", |a| if paths::log_path().exists() { launchers::open_file(&paths::log_path().to_string_lossy()) } else { a.toast("There is no log file yet.") });
        self.add_action("cleanup", |a| a.cleanup());
        self.add_action("about", |a| a.about());
        self.add_action("create-launcher", |a| match desktop::create_launcher() { Ok(p) => a.toast(&format!("Added: {}", p.display())), Err(e) => a.toast(&e.to_string()) });
        self.gtk.set_accels_for_action("app.quit", &["<Primary>q"]);
    }

    pub fn open_wizard(self: &Rc<Self>, add_account: bool) {
        let win = self.show_window();
        let w = super::wizard::Wizard::new(self, &win.win, add_account);
        w.present();
        *self.wizard.borrow_mut() = Some(w);
    }

    pub fn open_accounts(self: &Rc<Self>) {
        let win = self.show_window();
        super::accounts::show(self, &win.win);
    }

    fn sign_in_again(self: &Rc<Self>) {
        let win = self.show_window();
        let profs = ap::list_profiles();
        let cfg = win.cfg.borrow().clone();
        let todo: Vec<&ap::ProfileInfo> = win.visible_tunnels().iter().filter_map(|t| profs.iter().find(|p| p.name == t.profile && p.kind == ap::Kind::Sso)).collect();
        let Some(p) = todo.first() else { self.toast("This account does not use a company sign-in."); return };
        let _ = cfg;
        self.toast("Your browser opens to sign in...");
        let (name, me) = (p.name.clone(), self.clone());
        in_background(move || aws_cli::sso_login(&name), move |r| {
            if r.ok() { me.toast("Signed in."); me.manager.poke(); } else { me.toast(&aws_cli::friendly_error(&r.error, None)); }
        });
    }

    fn scan_now(self: &Rc<Self>) {
        let cfg = config_store::load().unwrap_or_default();
        self.toast("Scanning your accounts...");
        let me = self.clone();
        in_background(move || discovery::run_monitor(&cfg, true, &|| false), move |changes| {
            me.toast(&if changes.is_empty() { "Scan finished. Nothing changed.".to_string() } else { format!("{} change(s) found. See Servers > What changed.", changes.len()) });
        });
    }

    fn show_changes(self: &Rc<Self>) {
        let changes = discovery::take_pending();
        let win = self.show_window();
        message(&win.win, "What changed since the last scan", &if changes.is_empty() { "Nothing has changed since you last looked.".to_string() } else { changes.iter().map(|c| format!("• {c}")).collect::<Vec<_>>().join("\n") });
    }

    fn check_updates(self: &Rc<Self>, manual: bool) {
        if !manual && !config_store::load().is_some_and(|c| c.check_updates) { return; }
        let me = self.clone();
        in_background(move || update_checker::check(manual), move |info| match info {
            Some(i) if manual => {
                let win = me.show_window();
                let (url, notes) = (i.page_url.clone(), i.notes.chars().take(600).collect::<String>());
                match self_update::updatable_prefix() {
                    Some(prefix) => {
                        let app = me.clone();
                        confirm(&win.win, &format!("SSMConnect {} is available", i.version),
                            &format!("You have {VERSION}.\n\n{notes}\n\nUpdate now? SSMConnect downloads the new version, checks its signature, closes (your connections close with it), updates itself and opens again. Your servers and settings are kept."),
                            "Update now", false, move || app.update_now(i.clone(), prefix.clone()));
                    }
                    None => confirm(&win.win, &format!("SSMConnect {} is available", i.version), &format!("You have {VERSION}.\n\n{notes}\n\nOpen the download page?"), "Open page", false, move || launchers::open_url(&url)),
                }
            }
            Some(i) if update_checker::should_notify(&i.version) => {
                let n = gio::Notification::new(&format!("SSMConnect {} is available", i.version));
                n.set_body(Some("Choose Help > Check for updates to install it."));
                me.gtk.send_notification(Some("update"), &n);
            }
            None if manual => me.toast(&format!("You have the latest version ({VERSION}).")),
            _ => {}
        });
    }

    /// Downloads and checks the new installer, then hands over to it and quits (the installer starts the new version).
    fn update_now(self: &Rc<Self>, info: update_checker::UpdateInfo, prefix: std::path::PathBuf) {
        self.toast("Downloading the update...");
        let tx = super::common::progress_channel({ let me = self.clone(); move |m| me.toast(&m) });
        let (me, url) = (self.clone(), info.page_url.clone());
        in_background(move || self_update::download(&info, &|m| { let _ = tx.send_blocking(m); }), move |r| {
            let win = me.show_window();
            match r.and_then(|file| self_update::apply(&file, &prefix)) {
                Ok(()) => me.gtk.quit(),
                Err(e) => confirm(&win.win, "The update could not be installed", &format!("{e}\n\nNothing was changed. Open the download page to update by hand?"), "Open page", false, move || launchers::open_url(&url)),
            }
        });
    }

    fn cleanup(self: &Rc<Self>) {
        let win = self.show_window();
        let me = self.clone();
        confirm(&win.win, "Remove SSMConnect's settings for this user?",
            "This disconnects SSMConnect from ssh, removes 'start when I sign in', the menu entry, your saved servers and the log. Your own ssh hosts, AWS profiles \
             and everything in AWS stay untouched. SSMConnect then closes.\n\nTo remove the program itself, use your package manager (for example: sudo dnf remove ssmconnect).",
            "Remove and quit", true, move || { desktop::cleanup_user(false); me.gtk.quit(); });
    }

    fn about(self: &Rc<Self>) {
        let win = self.show_window();
        let d = adw::AboutDialog::builder().application_name("SSMConnect").application_icon(APP_ID).version(VERSION).developer_name("Poonchezhian Periasamy")
            .license_type(gtk::License::Apache20).website("https://github.com/poonchezhian/ssmconnect").issue_url("https://github.com/poonchezhian/ssmconnect/issues")
            .comments("SSH and Remote Desktop to AWS servers over Session Manager, with port 22 closed.").build();
        d.present(Some(&win.win));
    }

    fn background_jobs(self: &Rc<Self>) {
        if let Some(cfg) = config_store::load().filter(|c| !c.tunnels.is_empty()) {
            let me = self.clone();
            in_background(move || discovery::run_monitor(&cfg, false, &|| false), move |changes| {
                if !changes.is_empty() {
                    let n = gio::Notification::new("SSMConnect found changes in your servers");
                    n.set_body(Some(&format!("{} change(s). Open Servers > What changed since the last scan.", changes.len())));
                    me.gtk.send_notification(Some("scan"), &n);
                }
            });
        }
        self.check_updates(false);
    }
}
