//! The dashboard: header (account switcher, status pills, summary, organization chips), the action buttons and one card per server.

use super::app::App;
use super::common::{clear_box, esc, in_background, pill, set_pill};
use crate::models::{AppConfig, State, TunnelConfig, TunnelStatus};
use crate::{aws_cli, aws_profiles as ap, config_store, launchers, sso, ssh_config};
use adw::prelude::*;
use gtk::{gdk, gio, glib};
use std::cell::{Cell, RefCell};
use std::collections::{BTreeMap, HashSet};
use std::rc::{Rc, Weak};

struct ServerRow {
    row: adw::ActionRow,
    dot: gtk::Box,
    buttons: Vec<(&'static str, gtk::Button)>,
    tunnel: TunnelConfig,
}

pub struct Dashboard {
    pub win: adw::ApplicationWindow,
    app: Weak<App>,
    pub cfg: RefCell<AppConfig>,
    rows: RefCell<Vec<ServerRow>>,
    toasts: adw::ToastOverlay,
    stack: gtk::Stack,
    switcher: gtk::MenuButton,
    switch_pop: gtk::Popover,
    switch_box: gtk::Box,
    banner: adw::Banner,
    banner_cb: RefCell<Option<Box<dyn Fn()>>>,
    p_conn: gtk::Label,
    p_signin: gtk::Label,
    p_tools: gtk::Label,
    summary: gtk::Label,
    chips: gtk::FlowBox,
    groups: gtk::Box,
    clip_token: Cell<u32>,
}

impl Dashboard {
    pub fn new(app: &Rc<App>) -> Rc<Dashboard> {
        let win = adw::ApplicationWindow::builder().application(&app.gtk).title("SSMConnect").default_width(860).default_height(720).build();
        let toasts = adw::ToastOverlay::new();
        let view = adw::ToolbarView::new();

        // header
        let header = adw::HeaderBar::new();
        let switcher = gtk::MenuButton::builder().label("Account").tooltip_text("Switch AWS account").build();
        switcher.add_css_class("flat");
        let switch_pop = gtk::Popover::new();
        let switch_box = gtk::Box::new(gtk::Orientation::Vertical, 2);
        switch_pop.set_child(Some(&switch_box));
        switcher.set_popover(Some(&switch_pop));
        header.set_title_widget(Some(&switcher));
        header.pack_end(&gtk::MenuButton::builder().icon_name("open-menu-symbolic").menu_model(&Self::menu()).primary(true).tooltip_text("Main menu").build());
        view.add_top_bar(&header);
        let banner = adw::Banner::builder().revealed(false).build();
        view.add_top_bar(&banner);

        // empty state and main page
        let stack = gtk::Stack::new();
        let empty = adw::StatusPage::builder().icon_name("network-server-symbolic").title("No servers yet")
            .description("Add your AWS account and pick the servers you want to connect to.").build();
        let add = gtk::Button::builder().label("Add servers").halign(gtk::Align::Center).action_name("app.add-servers").build();
        add.add_css_class("suggested-action");
        add.add_css_class("pill");
        empty.set_child(Some(&add));
        stack.add_named(&empty, Some("empty"));

        let scroll = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
        let clamp = adw::Clamp::builder().maximum_size(820).margin_top(12).margin_bottom(24).margin_start(16).margin_end(16).build();
        let main = gtk::Box::new(gtk::Orientation::Vertical, 14);
        clamp.set_child(Some(&main));
        scroll.set_child(Some(&clamp));
        stack.add_named(&scroll, Some("main"));

        let pills = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let (p_conn, p_signin, p_tools) = (pill("Connections"), pill("AWS sign-in"), pill("AWS tools"));
        for p in [&p_conn, &p_signin, &p_tools] { pills.append(p); }
        main.append(&pills);
        let summary = gtk::Label::builder().xalign(0.0).wrap(true).build();
        summary.add_css_class("summary");
        main.append(&summary);
        let chips = gtk::FlowBox::builder().selection_mode(gtk::SelectionMode::None).max_children_per_line(8).column_spacing(6).row_spacing(6).homogeneous(false).build();
        main.append(&chips);
        let bar = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let groups = gtk::Box::new(gtk::Orientation::Vertical, 18);
        main.append(&bar);
        main.append(&groups);
        view.set_content(Some(&stack));
        toasts.set_child(Some(&view));
        win.set_content(Some(&toasts));

        let d = Rc::new(Dashboard { win: win.clone(), app: Rc::downgrade(app), cfg: RefCell::new(AppConfig::default()), rows: RefCell::new(vec![]), toasts, stack, switcher,
            switch_pop, switch_box, banner: banner.clone(), banner_cb: RefCell::new(None), p_conn, p_signin, p_tools, summary, chips, groups, clip_token: Cell::new(0) });

        for (label, icon, css, f) in [("Start", "media-playback-start-symbolic", "suggested-action", Dashboard::start as fn(&Rc<Dashboard>)),
                                      ("Stop", "media-playback-stop-symbolic", "", Dashboard::stop), ("Restart", "view-refresh-symbolic", "", Dashboard::restart)] {
            let b = gtk::Button::new();
            b.set_child(Some(&adw::ButtonContent::builder().icon_name(icon).label(label).build()));
            if !css.is_empty() { b.add_css_class(css); }
            let dd = d.clone();
            b.connect_clicked(move |_| f(&dd));
            bar.append(&b);
        }
        let add = gtk::Button::builder().hexpand(true).halign(gtk::Align::End).action_name("app.add-servers").build();
        add.set_child(Some(&adw::ButtonContent::builder().icon_name("list-add-symbolic").label("Add / change servers").build()));
        bar.append(&add);

        let hide = gio::SimpleAction::new("hide", None);
        let w = win.clone();
        hide.connect_activate(move |_, _| w.set_visible(false));
        win.add_action(&hide);
        let (dd, a) = (d.clone(), Rc::downgrade(app));
        banner.connect_button_clicked(move |b| { b.set_revealed(false); if let Some(cb) = dd.banner_cb.borrow_mut().take() { cb(); } });
        win.connect_close_request(move |w| { w.set_visible(false); if let Some(a) = a.upgrade() { a.note_hidden(); } glib::Propagation::Stop });

        // the tunnel manager reports from its own thread: hop to the main loop through a channel
        let (tx, rx) = async_channel::bounded::<()>(1);
        app.manager.set_on_change(move || { let _ = tx.try_send(()); });
        let dd = d.clone();
        glib::spawn_future_local(async move { while rx.recv().await.is_ok() { dd.refresh_status(); } });
        let dd = d.clone();
        glib::timeout_add_seconds_local(3, move || { dd.refresh_status(); glib::ControlFlow::Continue });
        d.reload();
        d
    }

    fn menu() -> gio::Menu {
        let menu = gio::Menu::new();
        let section = |title: Option<&str>, items: &[(&str, &str)]| {
            let s = gio::Menu::new();
            for (l, a) in items { s.append(Some(l), Some(a)); }
            menu.append_section(title, &s);
        };
        section(Some("File"), &[("Add to application menu", "app.create-launcher"), ("Start when I sign in", "app.autostart"), ("Hide window", "win.hide")]);
        section(Some("AWS"), &[("Add AWS account…", "app.add-account"), ("Manage accounts…", "app.manage-accounts"), ("Sign in again", "app.sign-in-again")]);
        section(Some("Servers"), &[("Scan for new or changed servers", "app.scan"), ("What changed since the last scan", "app.changes")]);
        section(Some("Help"), &[("Check for updates", "app.check-updates"), ("Open log file", "app.open-log"), ("Clean up this user's settings…", "app.cleanup"), ("About SSMConnect", "app.about")]);
        section(None, &[("Quit and close all connections", "app.quit")]);
        menu
    }

    fn app(&self) -> Option<Rc<App>> { self.app.upgrade() }

    // ---- data -------------------------------------------------------------------------------
    pub fn reload(self: &Rc<Self>) {
        let (cfg, warning) = config_store::load_or_recover();
        *self.cfg.borrow_mut() = cfg;
        if let Some(w) = warning { self.show_banner(&w, "OK", None); }
        self.build_switcher();
        self.build_groups();
        self.refresh_status();
    }

    /// (key, label) for each primary account that has saved servers, plus every other one known.
    fn primaries(&self) -> Vec<(String, String)> {
        let everyone = ap::list_profiles();
        let cfg = self.cfg.borrow();
        let mut seen: Vec<(String, String)> = vec![];
        let mut push = |p: &ap::ProfileInfo| {
            let k = ap::primary_key(p);
            if !seen.iter().any(|(x, _)| *x == k) { seen.push((k, ap::primary_label(&cfg, p, &everyone))); }
        };
        for t in &cfg.tunnels { if let Some(p) = everyone.iter().find(|p| p.name == t.profile) { push(p); } }
        for p in everyone.iter().filter(|p| p.kind != ap::Kind::Other) { push(p); }
        seen
    }

    pub fn current_key(&self) -> String {
        let prim = self.primaries();
        let cfg = self.cfg.borrow();
        if prim.iter().any(|(k, _)| *k == cfg.selected_account) { return cfg.selected_account.clone(); }
        // default to the account that has the most saved servers
        let by_name: BTreeMap<String, ap::ProfileInfo> = ap::list_profiles().into_iter().map(|p| (p.name.clone(), p)).collect();
        let mut counts: Vec<(String, usize)> = prim.iter().map(|(k, _)| (k.clone(), 0)).collect();
        for t in &cfg.tunnels {
            if let Some(p) = by_name.get(&t.profile) { let k = ap::primary_key(p); if let Some(c) = counts.iter_mut().find(|(x, _)| *x == k) { c.1 += 1; } }
        }
        counts.into_iter().max_by_key(|(_, n)| *n).map(|(k, _)| k).unwrap_or_default()
    }

    fn build_switcher(self: &Rc<Self>) {
        clear_box(&self.switch_box);
        let (prim, cur) = (self.primaries(), self.current_key());
        self.switcher.set_label(&prim.iter().find(|(k, _)| *k == cur).map(|(_, l)| l.clone()).unwrap_or_else(|| "No account".into()));
        let mut sorted = prim.clone();
        sorted.sort_by_key(|(k, _)| *k != cur);
        for (k, l) in sorted {
            let b = gtk::Button::builder().label(format!("{l}{}", if k == cur { "  ✓" } else { "" })).build();
            b.add_css_class("flat");
            let d = self.clone();
            b.connect_clicked(move |_| d.select_account(&k));
            self.switch_box.append(&b);
        }
        self.switch_box.append(&gtk::Separator::new(gtk::Orientation::Horizontal));
        let add = gtk::Button::builder().label("Add AWS account…").action_name("app.add-account").build();
        add.add_css_class("flat");
        self.switch_box.append(&add);
    }

    fn select_account(self: &Rc<Self>, key: &str) {
        self.switch_pop.popdown();
        { let mut c = self.cfg.borrow_mut(); c.selected_account = key.into(); let _ = config_store::save(&c); }
        self.reload();
    }

    pub fn visible_tunnels(&self) -> Vec<TunnelConfig> {
        let key = self.current_key();
        let by_name: BTreeMap<String, ap::ProfileInfo> = ap::list_profiles().into_iter().map(|p| (p.name.clone(), p)).collect();
        self.cfg.borrow().tunnels.iter()
            .filter(|t| key.is_empty() || by_name.get(&t.profile).map(|p| ap::primary_key(p) == key).unwrap_or(true)).cloned().collect()
    }

    fn build_groups(self: &Rc<Self>) {
        clear_box(&self.groups);
        self.rows.borrow_mut().clear();
        let tunnels = self.visible_tunnels();
        let cfg = self.cfg.borrow().clone();
        self.stack.set_visible_child_name(if cfg.tunnels.is_empty() { "empty" } else { "main" });
        let aliases = ssh_config::aliases_for(&cfg.tunnels);
        let mut by_profile: BTreeMap<String, Vec<&TunnelConfig>> = BTreeMap::new();
        for t in &tunnels { by_profile.entry(t.profile.clone()).or_default().push(t); }
        let multi = by_profile.len() > 1;
        for (profile, mut items) in by_profile {
            let g = adw::PreferencesGroup::new();
            if multi { g.set_title(&esc(&cfg.alias_or(&profile, &profile))); }
            items.sort_by_key(|t| t.name.to_lowercase());
            for t in items {
                let alias = aliases.get(&t.instance_id).cloned().unwrap_or_else(|| ssh_config::host_alias(&t.name, &t.instance_id));
                let sr = self.server_row(t, &alias);
                g.add(&sr.row);
                self.rows.borrow_mut().push(sr);
            }
            self.groups.append(&g);
        }
        // organization chips (greyed when empty)
        clear_chips(&self.chips);
        let everyone = ap::list_profiles();
        let cur = self.current_key();
        let mut counts: BTreeMap<String, usize> = BTreeMap::new();
        for t in &tunnels { *counts.entry(t.profile.clone()).or_default() += 1; }
        let members: Vec<ap::ProfileInfo> = everyone.into_iter().filter(|p| ap::primary_key(p) == cur && p.kind == ap::Kind::Sso).collect();
        if !members.is_empty() {
            let preferred: HashSet<String> = counts.keys().cloned().collect();
            for p in ap::one_each(&members, &preferred, None) {
                let n = counts.get(&p.name).copied().unwrap_or(0);
                let l = gtk::Label::new(Some(&format!("{}  {n}", cfg.alias_or(&p.name, &p.name))));
                l.add_css_class("chip");
                if n == 0 { l.add_css_class("empty"); }
                self.chips.append(&l);
            }
        }
        self.chips.set_visible(self.chips.first_child().is_some());
    }

    fn server_row(self: &Rc<Self>, t: &TunnelConfig, alias: &str) -> ServerRow {
        let row = adw::ActionRow::builder().title(esc(&t.name)).title_lines(1).subtitle_lines(2).build();
        let dot = gtk::Box::builder().valign(gtk::Align::Center).build();
        dot.add_css_class("dot");
        row.add_prefix(&dot);
        if !t.environment.is_empty() {
            let tag = gtk::Label::builder().label(if t.is_prod() { "PROD" } else { &t.environment }).valign(gtk::Align::Center).build();
            tag.add_css_class("tag");
            if t.is_prod() { tag.add_css_class("prod"); }
            row.add_suffix(&tag);
        }
        let mut buttons = vec![];
        let mut action = |icon: &str, label: &'static str, f: Box<dyn Fn(&Rc<Dashboard>, &TunnelConfig, &str)>| {
            let b = gtk::Button::builder().valign(gtk::Align::Center).build();
            b.set_child(Some(&adw::ButtonContent::builder().icon_name(icon).label(label).build()));
            b.add_css_class("flat");
            let (d, t, alias) = (Rc::downgrade(self), t.clone(), alias.to_string());
            b.connect_clicked(move |b| if let Some(d) = d.upgrade() { d.guarded(b, &t, &alias, &f); });
            row.add_suffix(&b);
            buttons.push((label, b));
        };
        if t.is_rdp() {
            action("computer-symbolic", "Remote Desktop", Box::new(|d, t, _| if let Err(e) = launchers::open_rdp(t) { d.toast(&e); }));
            action("dialog-password-symbolic", "Get password", Box::new(|d, t, _| d.get_password(t)));
        } else {
            action("utilities-terminal-symbolic", "Terminal", Box::new(|d, _, a| if let Err(e) = launchers::open_terminal(a) { d.toast(&e); }));
            action("folder-remote-symbolic", "Files", Box::new(|d, t, a| if let Err(e) = launchers::open_files(&t.user_name, a) { d.toast(&e); }));
        }
        // the "more" menu
        let more = gtk::MenuButton::builder().icon_name("view-more-symbolic").valign(gtk::Align::Center).tooltip_text("More").build();
        more.add_css_class("flat");
        let menu = gio::Menu::new();
        menu.append(Some(if t.is_rdp() { "Copy local address" } else { "Copy ssh command" }), Some("row.copy"));
        menu.append(Some(if t.is_rdp() { "Open Remote Desktop address" } else { "Copy sftp address" }), Some("row.copy-alt"));
        more.set_menu_model(Some(&menu));
        row.add_suffix(&more);
        let group = gio::SimpleActionGroup::new();
        let mk = |name: &str, f: Box<dyn Fn(&Rc<Dashboard>)>| {
            let a = gio::SimpleAction::new(name, None);
            let d = Rc::downgrade(self);
            a.connect_activate(move |_, _| if let Some(d) = d.upgrade() { f(&d); });
            group.add_action(&a);
        };
        let (t1, a1) = (t.clone(), alias.to_string());
        mk("copy", Box::new(move |d| { d.copy(&if t1.is_rdp() { format!("127.0.0.1:{}", t1.local_port) } else { ssh_config::ssh_command_text(&a1) }); d.toast("Copied."); }));
        let (t2, a2) = (t.clone(), alias.to_string());
        mk("copy-alt", Box::new(move |d| if t2.is_rdp() { if let Err(e) = launchers::open_rdp(&t2) { d.toast(&e); } } else { d.copy(&ssh_config::sftp_url(&t2.user_name, &a2)); d.toast("Copied."); }));
        row.insert_action_group("row", Some(&group));
        ServerRow { row, dot, buttons, tunnel: t.clone() }
    }

    // Unavailable buttons are dimmed, not disabled, so their hover note still shows and a click explains why.
    fn guarded(self: &Rc<Self>, b: &gtk::Button, t: &TunnelConfig, alias: &str, f: &dyn Fn(&Rc<Dashboard>, &TunnelConfig, &str)) {
        if b.has_css_class("unavailable") { self.toast(&b.tooltip_text().map(|s| s.to_string()).unwrap_or_else(|| "Not available yet.".into())); return; }
        f(self, t, alias);
    }

    fn get_password(self: &Rc<Self>, t: &TunnelConfig) {
        let Some(key) = t.key_file.clone() else { return };
        self.toast("Getting the password...");
        let (d, t) = (self.clone(), t.clone());
        in_background(move || aws_cli::get_windows_password(&t, &key), move |r| match r {
            Ok(pw) => { d.copy_secret(&pw); d.toast("Password copied. It leaves the clipboard in 60 seconds."); }
            Err(e) => d.toast(&e),
        });
    }

    pub fn refresh_status(self: &Rc<Self>) {
        let Some(app) = self.app() else { return };
        let status = app.manager.snapshot();
        let tools = launchers::tools_summary();
        let (mut up, mut n) = (0, 0);
        for sr in self.rows.borrow().iter() {
            let st = status.iter().find(|s| s.instance_id == sr.tunnel.instance_id && s.local_port == sr.tunnel.local_port);
            update_row(sr, st, &tools);
            n += 1;
            up += usize::from(st.is_some_and(|s| s.state == State::Connected));
        }
        let total_up = status.iter().filter(|s| s.state == State::Connected).count();
        let problems = status.iter().filter(|s| matches!(s.state, State::Error | State::Retrying)).count();
        let kind = if n == 0 { "" } else if up == n { "ok" } else if problems > 0 { "err" } else { "warn" };
        set_pill(&self.p_conn, &if n > 0 { format!("{up} of {n} connected") } else { "No connections".into() }, kind);
        let (text, k) = self.sign_in_state();
        set_pill(&self.p_signin, &text, k);
        let have = aws_cli::find_aws().is_some() && aws_cli::find_plugin().is_some();
        set_pill(&self.p_tools, if have { "Tools ready" } else { "Tools missing" }, if have { "ok" } else { "err" });
        let all = self.cfg.borrow().tunnels.len();
        let others = total_up.saturating_sub(up);
        self.summary.set_text(&if all == 0 { "Add your AWS account to get started.".to_string() } else {
            format!("{total_up} of {all} connection(s) open across all accounts.{}", if others > 0 { format!(" {others} belong to other accounts and keep running.") } else { String::new() }) });
    }

    fn sign_in_state(&self) -> (String, &'static str) {
        let by_name: BTreeMap<String, ap::ProfileInfo> = ap::list_profiles().into_iter().map(|p| (p.name.clone(), p)).collect();
        let profs: Vec<&ap::ProfileInfo> = self.visible_tunnels().iter().filter_map(|t| by_name.get(&t.profile)).collect();
        let Some(first) = profs.first() else { return ("Not signed in".into(), "warn") };
        if first.kind != ap::Kind::Sso { return ("Access keys".into(), "ok"); }
        if profs.iter().filter_map(|p| p.sso_session.as_deref()).any(|s| sso::read_access_token(s).is_some()) { ("Signed in".into(), "ok") } else { ("Sign-in needed".into(), "err") }
    }

    // ---- actions ------------------------------------------------------------------------------
    fn write_enabled(&self, on: bool) {
        let mut cfg = config_store::load().unwrap_or_else(|| self.cfg.borrow().clone());
        for t in &mut cfg.tunnels { t.enabled = on; }
        let _ = config_store::save(&cfg);
        *self.cfg.borrow_mut() = cfg;
        if let Some(a) = self.app() { a.manager.poke(); }
    }

    fn start(self: &Rc<Self>) {
        if self.cfg.borrow().tunnels.is_empty() { self.toast("Nothing to start yet. Use Add / change servers."); return; }
        self.write_enabled(true);
        if let Some(a) = self.app() { a.ensure_manager(); }
        self.toast("Opening connections...");
    }

    fn stop(self: &Rc<Self>) { self.write_enabled(false); self.toast("Connections closed."); }

    fn restart(self: &Rc<Self>) {
        self.write_enabled(true);
        if let Some(a) = self.app() { a.ensure_manager(); a.manager.restart_all(); }
        self.toast("Restarting connections...");
    }

    // ---- helpers ------------------------------------------------------------------------------
    pub fn toast(&self, text: &str) { self.toasts.add_toast(adw::Toast::builder().title(text).timeout(4).build()); }

    pub fn copy(&self, text: &str) { if let Some(d) = gdk::Display::default() { d.clipboard().set_text(text); } }

    /// Copies a secret and clears it from the clipboard after 60 seconds (only if nothing newer was copied meanwhile).
    pub fn copy_secret(self: &Rc<Self>, text: &str) {
        let Some(display) = gdk::Display::default() else { return };
        let cb = display.clipboard();
        cb.set_text(text);
        let token = self.clip_token.get() + 1;
        self.clip_token.set(token);
        let d = self.clone();
        glib::timeout_add_seconds_local_once(60, move || if d.clip_token.get() == token { cb.set_text(""); });
    }

    pub fn show_banner(&self, text: &str, button: &str, cb: Option<Box<dyn Fn()>>) {
        self.banner.set_title(text);
        self.banner.set_button_label(Some(button));
        *self.banner_cb.borrow_mut() = cb;
        self.banner.set_revealed(true);
    }
}

fn clear_chips(c: &gtk::FlowBox) { while let Some(ch) = c.first_child() { c.remove(&ch); } }

fn update_row(sr: &ServerRow, st: Option<&TunnelStatus>, tools: &launchers::Tools) {
    let t = &sr.tunnel;
    let state = st.map(|s| s.state).unwrap_or(State::Disabled);
    let up = state == State::Connected;
    for c in ["ok", "work", "err"] { sr.dot.remove_css_class(c); }
    match state { State::Connected => sr.dot.add_css_class("ok"), State::Starting | State::Retrying => sr.dot.add_css_class("work"), State::Error => sr.dot.add_css_class("err"), State::Disabled => {} }
    let what = if t.is_rdp() { "Windows".to_string() } else if t.os_name.is_empty() { "Linux".into() } else { t.os_name.clone() };
    let status = match state { State::Connected => "Connected", State::Starting => "Connecting...", State::Retrying => "Retrying", State::Error => "Problem", State::Disabled => "Off" };
    let mut detail = format!("{what} · local port {} · {status}", t.local_port);
    if let Some(s) = st.filter(|s| matches!(s.state, State::Error | State::Retrying | State::Starting) && !s.message.is_empty()) { detail += &format!("\n{}", s.message); }
    sr.row.set_subtitle(&esc(&detail));
    for (label, b) in &sr.buttons {
        let (ok, why) = if !up { (false, "Waiting for the connection to open (see the status line).".to_string()) } else { match *label {
            "Terminal" => if tools.terminal.is_some() && tools.ssh.is_some() { (true, "Open a terminal and run ssh for this server".into()) } else { (false, "No terminal or ssh program found. Use More > Copy ssh command.".into()) },
            "Files" => if tools.files.is_some() { (true, "Browse files on the server in your file manager (SFTP)".into()) } else { (false, "No file manager found.".into()) },
            "Remote Desktop" => if tools.rdp.is_some() { (true, "Open Remote Desktop to this server".into()) } else { (false, "Install Remmina or FreeRDP (xfreerdp) to use Remote Desktop.".into()) },
            _ => if t.key_file.is_some() { (true, "Decrypt the Administrator password with your key file and copy it (cleared after 60 s)".into()) } else { (false, "Pick this server's key file first (Add / change servers).".into()) },
        } };
        b.set_tooltip_text(Some(&why));
        if ok { b.remove_css_class("unavailable"); } else { b.add_css_class("unavailable"); }
    }
}
