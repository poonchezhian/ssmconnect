//! The dashboard: header (account switcher, status pills, summary, organization chips), the action buttons and one card per server.

use super::app::App;
use super::common::{clear_box, confirm, in_background, pill, set_pill};
use crate::models::{AppConfig, State, TunnelConfig, TunnelStatus};
use crate::{usage, aws_cli, aws_profiles as ap, config_store, known_hosts, launchers, login_check, sso, ssh_config};
use adw::prelude::*;
use gtk::{gdk, gio, glib};
use std::cell::{Cell, RefCell};
use std::collections::{BTreeMap, HashSet};
use std::rc::{Rc, Weak};

struct ServerRow {
    card: gtk::Box,
    line: gtk::Label,
    status: gtk::Label,
    /// processor, memory, disk
    bars: [gtk::Label; 3],
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
    switch_title: gtk::Label,
    switch_avatar: gtk::Label,
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
    /// alias -> has its identity been confirmed (filled in the background)
    trusted: RefCell<BTreeMap<String, bool>>,
    /// last readings of processor, memory and disk use (usage.json)
    usage: RefCell<usage::UsageCache>,
    usage_busy: Cell<bool>,
    /// "all", "connected", "attention" or "prod"
    filter: RefCell<String>,
    search: RefCell<String>,
}

impl Dashboard {
    pub fn new(app: &Rc<App>) -> Rc<Dashboard> {
        let win = adw::ApplicationWindow::builder().application(&app.gtk).title("SSMConnect").default_width(1040).default_height(760).build();
        let toasts = adw::ToastOverlay::new();
        let view = adw::ToolbarView::new();
        super::common::ssm_style(&view);

        // top: the menu bar (File, AWS, Servers, Help) in the window's title bar, then the teal band with the logo and the account switcher
        let header = adw::HeaderBar::new();
        header.add_css_class("ssm-top");
        header.set_show_title(false);
        header.pack_start(&gtk::PopoverMenuBar::from_model(Some(&Self::menu())));
        view.add_top_bar(&header);

        let band = gtk::Box::new(gtk::Orientation::Horizontal, 14);
        band.add_css_class("ssm-band");
        let logo = gtk::Image::from_icon_name(crate::APP_ID);
        logo.set_pixel_size(46);
        band.append(&logo);
        let titles = gtk::Box::new(gtk::Orientation::Vertical, 2);
        titles.set_valign(gtk::Align::Center);
        titles.set_hexpand(true);
        let t1 = gtk::Label::builder().label("SSMConnect").xalign(0.0).build();
        t1.add_css_class("band-title");
        let t2 = gtk::Label::builder().label("Your AWS servers, one click away. Secure SSH and Remote Desktop through SSM, with no open ports.").xalign(0.0).wrap(true).build();
        t2.add_css_class("band-sub");
        titles.append(&t1);
        titles.append(&t2);
        band.append(&titles);
        let switch_avatar = gtk::Label::new(Some("+"));
        switch_avatar.add_css_class("avatar-mint");
        let switch_title = gtk::Label::builder().label("Account").xalign(0.0).ellipsize(gtk::pango::EllipsizeMode::End).max_width_chars(22).build();
        switch_title.add_css_class("acct-name");
        let kicker = gtk::Label::builder().label("AWS ACCOUNT").xalign(0.0).build();
        kicker.add_css_class("acct-kicker");
        let chip_text = gtk::Box::new(gtk::Orientation::Vertical, 0);
        chip_text.set_hexpand(true);
        chip_text.append(&kicker);
        chip_text.append(&switch_title);
        let chip = gtk::Box::new(gtk::Orientation::Horizontal, 10);
        chip.append(&switch_avatar);
        chip.append(&chip_text);
        chip.append(&gtk::Image::from_icon_name("pan-down-symbolic"));
        let switcher = gtk::MenuButton::builder().child(&chip).tooltip_text("Switch AWS account").valign(gtk::Align::Center).always_show_arrow(false).build();
        switcher.add_css_class("acct-chip");
        let switch_pop = gtk::Popover::new();
        let switch_box = gtk::Box::new(gtk::Orientation::Vertical, 2);
        switch_pop.set_child(Some(&switch_box));
        switcher.set_popover(Some(&switch_pop));
        band.append(&switcher);
        view.add_top_bar(&band);
        let banner = adw::Banner::builder().revealed(false).build();
        banner.add_css_class("ssm-banner");
        view.add_top_bar(&banner);

        // empty state and main page
        let stack = gtk::Stack::new();
        let empty = adw::StatusPage::builder().icon_name("network-server-symbolic").title("No servers yet")
            .description("Add your AWS account and pick the servers you want to connect to.").build();
        stack.add_named(&empty, Some("empty"));

        let scroll = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
        let clamp = adw::Clamp::builder().maximum_size(1180).tightening_threshold(900).margin_top(14).margin_bottom(20).margin_start(22).margin_end(22).build();
        let main = gtk::Box::new(gtk::Orientation::Vertical, 10);
        clamp.set_child(Some(&main));
        scroll.set_child(Some(&clamp));
        stack.add_named(&scroll, Some("main"));

        let pills = gtk::Box::new(gtk::Orientation::Horizontal, 10);
        pills.set_homogeneous(true);
        let (p_conn, p_signin, p_tools) = (pill("Connections"), pill("AWS sign-in"), pill("AWS tools"));
        for p in [&p_conn, &p_signin, &p_tools] { pills.append(p); }
        main.append(&pills);
        // show only some servers: by state, and by what is typed in the search box
        let find = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let filter_box = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        filter_box.add_css_class("linked");
        let mut first: Option<gtk::ToggleButton> = None;
        let filter_buttons: Vec<(String, gtk::ToggleButton)> = [("all", "All"), ("connected", "Connected"), ("attention", "Needs attention"), ("prod", "PROD only")].iter().map(|(id, label)| {
            let b = gtk::ToggleButton::builder().label(*label).active(*id == "all").build();
            if let Some(f) = &first { b.set_group(Some(f)); } else { first = Some(b.clone()); }
            filter_box.append(&b);
            (id.to_string(), b)
        }).collect();
        find.append(&filter_box);
        let search = gtk::SearchEntry::builder().placeholder_text("Search servers").hexpand(true).halign(gtk::Align::End).width_request(240).build();
        find.append(&search);
        main.append(&find);
        let summary = gtk::Label::builder().xalign(0.0).wrap(true).margin_top(4).build();
        summary.add_css_class("ssm-summary");
        main.append(&summary);
        let chips = gtk::FlowBox::builder().selection_mode(gtk::SelectionMode::None).max_children_per_line(12).column_spacing(8).row_spacing(6).homogeneous(false).build();
        main.append(&chips);
        let groups = gtk::Box::new(gtk::Orientation::Vertical, 12);
        main.append(&groups);
        // the colour key: what the card colours and the use labels mean
        let legend = gtk::Box::new(gtk::Orientation::Horizontal, 14);
        legend.add_css_class("legend");
        legend.set_margin_top(8);
        for (cls, text) in [("green", "Connected"), ("amber", "Connecting"), ("red", "Problem"), ("grey", "Stopped")] {
            let l = gtk::Label::builder().use_markup(true).label(format!("<span foreground=\"{}\">\u{25CF}</span>  {text}", match cls { "green" => "#22aa50", "amber" => "#f0aa14", "red" => "#cd2832", _ => "#969696" })).build();
            legend.append(&l);
        }
        let l = gtk::Label::new(Some("CPU, memory, disk:  under 60%  \u{00B7}  60 to 85%  \u{00B7}  over 85%"));
        l.set_hexpand(true); l.set_halign(gtk::Align::End);
        legend.append(&l);
        main.append(&legend);
        view.set_content(Some(&stack));

        // the bottom bar: connection controls on the left, the main action on the right
        let bar = gtk::Box::new(gtk::Orientation::Horizontal, 10);
        bar.add_css_class("ssm-bar");
        view.add_bottom_bar(&bar);
        toasts.set_child(Some(&view));
        win.set_content(Some(&toasts));

        let d = Rc::new(Dashboard { win: win.clone(), app: Rc::downgrade(app), cfg: RefCell::new(AppConfig::default()), rows: RefCell::new(vec![]), toasts, stack, switch_title, switch_avatar,
            switch_pop, switch_box, banner: banner.clone(), banner_cb: RefCell::new(None), p_conn, p_signin, p_tools, summary, chips, groups, clip_token: Cell::new(0), trusted: RefCell::new(BTreeMap::new()), usage: RefCell::new(usage::load()), usage_busy: Cell::new(false), filter: RefCell::new("all".into()), search: RefCell::new(String::new()) });

        for (label, css, f) in [("Start", "go", Dashboard::start as fn(&Rc<Dashboard>)), ("Stop", "halt", Dashboard::stop), ("Restart", "plain", Dashboard::restart)] {
            let b = gtk::Button::builder().label(label).width_request(124).build();
            b.add_css_class(css);
            let dd = d.clone();
            b.connect_clicked(move |_| f(&dd));
            bar.append(&b);
        }
        let add = gtk::Button::builder().label("Add / change servers…").hexpand(true).halign(gtk::Align::End).width_request(250).action_name("app.add-servers").build();
        add.add_css_class("main");
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
        let dd = d.clone();
        glib::timeout_add_seconds_local(300, move || { dd.refresh_usage(false); glib::ControlFlow::Continue });
        let dd = d.clone();
        glib::timeout_add_seconds_local_once(25, move || dd.refresh_usage(false));
        for (id, b) in filter_buttons {
            let dd = d.clone();
            b.connect_toggled(move |b| if b.is_active() { *dd.filter.borrow_mut() = id.clone(); dd.apply_filter(); });
        }
        let dd = d.clone();
        search.connect_search_changed(move |e| { *dd.search.borrow_mut() = e.text().trim().to_lowercase(); dd.apply_filter(); });
        d.reload();
        d
    }

    /// Shows only the servers that match the chosen filter and the search text; an account heading disappears when none of its servers do.
    fn apply_filter(self: &Rc<Self>) {
        let Some(app) = self.app() else { return };
        let (filter, search) = (self.filter.borrow().clone(), self.search.borrow().clone());
        let status = app.manager.snapshot();
        let rows = self.rows.borrow();
        let shown: Vec<bool> = rows.iter().map(|sr| {
            let t = &sr.tunnel;
            let state = status.iter().find(|s| s.instance_id == t.instance_id && s.local_port == t.local_port).map(|s| s.state);
            let by_state = match filter.as_str() {
                "connected" => state == Some(State::Connected),
                "attention" => matches!(state, Some(State::Error | State::Retrying | State::Starting)),
                "prod" => t.is_prod(),
                _ => true,
            };
            let by_text = search.is_empty() || [&t.name, &t.instance_id, &t.environment, &t.os_name, &t.profile].iter().any(|f| f.to_lowercase().contains(&search));
            if let Some(cell) = sr.card.parent() { cell.set_visible(by_state && by_text); }
            by_state && by_text
        }).collect();
        // a group is judged from the results above, not from the widgets: a hidden group makes everything in it report "not visible"
        let mut g = self.groups.first_child();
        while let Some(group) = g {
            g = group.next_sibling();
            let any = rows.iter().zip(&shown).any(|(sr, show)| *show && sr.card.is_ancestor(&group));
            group.set_visible(any || rows.is_empty());
        }
    }

    /// Looks up (off the main thread) which Linux servers have a confirmed identity, and offers to review the ones that do not.
    pub fn reload_trust(self: &Rc<Self>) {
        let cfg = self.cfg.borrow().clone();
        let d = self.clone();
        in_background(move || cfg.tunnels.iter().filter(|t| !t.is_rdp() && t.enabled).map(|t| {
            let a = ssh_config::host_key_alias(t, &cfg.tunnels);
            let ok = !known_hosts::stored(&a).is_empty();
            (a, ok)
        }).collect::<Vec<_>>(), move |list| {
            let pending = list.iter().filter(|(_, ok)| !ok).count();
            *d.trusted.borrow_mut() = list.into_iter().collect();
            d.refresh_status();
            if pending > 0 {
                let (me, strict) = (d.clone(), d.cfg.borrow().strict_host_keys);
                d.show_banner(&format!("{pending} server(s) have not been confirmed yet. {}", if strict { "You asked to confirm each server first, so they cannot be used until then." } else { "Confirming lets SSH be sure it talks to the right server." }), "Review",
                    Some(Box::new(move || if let Some(a) = me.app() { a.gtk.activate_action("identities", None); })));
            }
        });
    }

    fn menu() -> gio::Menu {
        let bar = gio::Menu::new();
        let sub = |title: &str, items: &[(&str, &str)]| {
            let m = gio::Menu::new();
            for (l, a) in items { m.append(Some(l), Some(a)); }
            bar.append_submenu(Some(title), &m);
        };
        sub("_File", &[("Preferences", "app.preferences"), ("Add to application menu", "app.create-launcher"), ("Hide window", "win.hide"), ("Quit and close all connections", "app.quit")]);
        sub("_AWS", &[("Add AWS account…", "app.add-account"), ("Manage accounts…", "app.manage-accounts"), ("Sign in again", "app.sign-in-again")]);
        sub("_Servers", &[("Copy files between servers…", "app.file-transfer"), ("Server identities…", "app.identities"), ("Scan for new or changed servers", "app.scan"), ("Refresh usage", "app.refresh-usage"), ("What changed since the last scan", "app.changes")]);
        sub("_Help", &[("Check for updates", "app.check-updates"), ("Open log file", "app.open-log"), ("Clean up this user's settings…", "app.cleanup"), ("About SSMConnect", "app.about")]);
        bar
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
        self.reload_trust();
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
        let title = prim.iter().find(|(k, _)| *k == cur).map(|(_, l)| l.clone());
        self.switch_title.set_text(title.as_deref().unwrap_or("Add an AWS account"));
        self.switch_avatar.set_text(&initial(title.as_deref()));
        let mut sorted = prim.clone();
        sorted.sort_by_key(|(k, _)| *k != cur);
        for (k, l) in sorted {
            let b = gtk::Button::new();
            let row = gtk::Box::new(gtk::Orientation::Horizontal, 10);
            let av = gtk::Label::new(Some(&initial(Some(&l))));
            av.add_css_class("avatar-soft");
            if k == cur { av.add_css_class("avatar-current"); }
            row.append(&av);
            row.append(&gtk::Label::builder().label(&l).xalign(0.0).hexpand(true).build());
            b.set_child(Some(&row));
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
            let g = gtk::Box::new(gtk::Orientation::Vertical, 8);
            if multi {
                let h = gtk::Label::builder().label(cfg.alias_or(&profile, &profile)).xalign(0.0).margin_top(6).build();
                h.add_css_class("ghead");
                g.append(&h);
            }
            let cards = gtk::FlowBox::builder().selection_mode(gtk::SelectionMode::None).homogeneous(true).column_spacing(12).row_spacing(12)
                .min_children_per_line(1).max_children_per_line(6).build();
            cards.add_css_class("cards");
            items.sort_by_key(|t| t.name.to_lowercase());
            for t in items {
                let alias = aliases.get(&t.instance_id).cloned().unwrap_or_else(|| ssh_config::host_alias(&t.name, &t.instance_id));
                let sr = self.server_row(t, &alias);
                cards.append(&sr.card);
                self.rows.borrow_mut().push(sr);
            }
            g.append(&cards);
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
            let k = gtk::Label::new(Some("Organization accounts"));
            k.add_css_class("org-kicker");
            self.chips.append(&k);
            for p in ap::one_each(&members, &preferred, None) {
                let n = counts.get(&p.name).copied().unwrap_or(0);
                let l = gtk::Label::new(Some(&format!("{}  {n}", cfg.alias_or(&p.name, &p.name))));
                l.add_css_class("org-chip");
                if n == 0 { l.add_css_class("empty"); }
                self.chips.append(&l);
            }
        }
        self.chips.set_visible(self.chips.first_child().is_some());
    }

    fn server_row(self: &Rc<Self>, t: &TunnelConfig, alias: &str) -> ServerRow {
        let card = gtk::Box::new(gtk::Orientation::Vertical, 10);
        card.add_css_class("scard");
        card.add_css_class("grey");
        card.set_size_request(300, -1);
        // top: operating-system badge, name, environment tag, second line
        let top = gtk::Box::new(gtk::Orientation::Horizontal, 12);
        top.append(&super::common::os_logo(&t.os_name, t.is_rdp()));
        let text = gtk::Box::new(gtk::Orientation::Vertical, 3);
        text.set_hexpand(true);
        let name_row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let name = gtk::Label::builder().label(&t.name).xalign(0.0).hexpand(true).ellipsize(gtk::pango::EllipsizeMode::End).build();
        name.add_css_class("card-name");
        name_row.append(&name);
        if !t.environment.is_empty() {
            let env = if t.is_prod() { "PROD".to_string() } else { t.environment.to_uppercase() };
            let tag = gtk::Label::builder().label(env).valign(gtk::Align::Center).build();
            tag.add_css_class("tag");
            if t.is_prod() { tag.add_css_class("prod"); }
            name_row.append(&tag);
        }
        let line = gtk::Label::builder().xalign(0.0).ellipsize(gtk::pango::EllipsizeMode::End).build();
        line.add_css_class("card-line");
        text.append(&name_row);
        text.append(&line);
        top.append(&text);
        card.append(&top);
        // use of processor, memory and disk (dashes until read)
        let usage_row = gtk::Box::new(gtk::Orientation::Horizontal, 6);
        let bars = ["CPU", "Memory", "Disk"].map(|n| {
            let l = gtk::Label::builder().label(format!("{n} -")).build();
            l.add_css_class("ubar");
            l.set_tooltip_text(Some("Reading... Needs permission to run commands through Session Manager."));
            usage_row.append(&l);
            l
        });
        card.append(&usage_row);
        // bottom: status pill on the left, the action buttons on the right
        let bottom = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let status = gtk::Label::builder().label("●  Stopped").valign(gtk::Align::Center).build();
        status.add_css_class("status");
        bottom.append(&status);
        let spacer = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        spacer.set_hexpand(true);
        bottom.append(&spacer);
        card.append(&bottom);
        let mut buttons = vec![];
        let mut action = |label: &'static str, f: Box<dyn Fn(&Rc<Dashboard>, &TunnelConfig, &str)>| {
            let b = gtk::Button::builder().label(label).valign(gtk::Align::Center).build();
            b.add_css_class("abtn");
            let (d, t, alias) = (Rc::downgrade(self), t.clone(), alias.to_string());
            b.connect_clicked(move |b| if let Some(d) = d.upgrade() { d.guarded(b, &t, &alias, &f); });
            bottom.append(&b);
            buttons.push((label, b));
        };
        if t.is_rdp() {
            action("Remote Desktop", Box::new(|d, t, _| if let Err(e) = launchers::open_rdp(t) { d.toast(&e); }));
            action("Get password", Box::new(|d, t, _| d.get_password(t)));
        } else {
            action("Terminal", Box::new(|d, t, a| { let a = a.to_string(); d.checked(t, move |d| if let Err(e) = launchers::open_terminal(&a) { d.toast(&e); }); }));
            action("Files", Box::new(|d, t, a| { let (t2, a) = (t.clone(), a.to_string()); d.checked(t, move |d| d.open_files(&t2, &a)); }));
        }
        // double-click anywhere else on the card does the main thing
        let first = buttons.first().map(|(_, b)| b.clone());
        let dbl = gtk::GestureClick::new();
        dbl.connect_pressed(move |_, n, _, _| if n == 2 { if let Some(b) = &first { b.emit_clicked(); } });
        card.add_controller(dbl);
        // the "more" menu
        let more = gtk::MenuButton::builder().icon_name("view-more-symbolic").valign(gtk::Align::Center).tooltip_text("More").build();
        more.add_css_class("flat");
        more.add_css_class("abtn-more");
        let menu = gio::Menu::new();
        menu.append(Some(if t.is_rdp() { "Copy local address" } else { "Copy ssh command" }), Some("row.copy"));
        menu.append(Some(if t.is_rdp() { "Open Remote Desktop address" } else { "Copy sftp address" }), Some("row.copy-alt"));
        if !t.is_rdp() { menu.append(Some("Open sftp in a terminal"), Some("row.sftp")); menu.append(Some("Server identity…"), Some("row.identity")); }
        menu.append(Some("Change login user or key file…"), Some("row.settings"));
        more.set_menu_model(Some(&menu));
        bottom.append(&more);
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
        mk("identity", Box::new(|d| if let Some(a) = d.app() { a.gtk.activate_action("identities", None); }));
        let t4 = t.clone();
        mk("settings", Box::new(move |d| if let Some(a) = d.app() { super::server_settings::show(&a, &d.win, &t4); }));
        let a3 = alias.to_string();
        let t5 = t.clone();
        mk("sftp", Box::new(move |d| { let a3 = a3.clone(); d.checked(&t5, move |d| if let Err(e) = launchers::open_sftp_terminal(&a3) { d.toast(&e); }); }));
        card.insert_action_group("row", Some(&group));
        ServerRow { card, line, status, bars, buttons, tunnel: t.clone() }
    }

    // Unavailable buttons are dimmed, not disabled, so their hover note still shows and a click explains why.
    fn guarded(self: &Rc<Self>, b: &gtk::Button, t: &TunnelConfig, alias: &str, f: &dyn Fn(&Rc<Dashboard>, &TunnelConfig, &str)) {
        if b.has_css_class("unavailable") { self.toast(&b.tooltip_text().map(|s| s.to_string()).unwrap_or_else(|| "Not available yet.".into())); return; }
        if self.cfg.borrow().strict_host_keys && !t.is_rdp() {
            let key = ssh_config::host_key_alias(t, &self.cfg.borrow().tunnels);
            if !self.trusted.borrow().get(&key).copied().unwrap_or(false) {
                self.toast("Confirm this server's identity first: More > Server identity…");
                return;
            }
        }
        f(self, t, alias);
    }

    /// Checks that the server accepts the login (right user, right key) before a tool opens, and repairs it when it can. If the server does not
    /// answer, the person may still open the tool; if no key fits, they are asked for one.
    fn checked(self: &Rc<Self>, t: &TunnelConfig, then: impl Fn(&Rc<Dashboard>) + Clone + 'static) {
        self.toast("Checking the login...");
        let (t2, d) = (t.clone(), self.clone());
        in_background(move || login_check::ensure(&t2), {
            let t = t.clone();
            move |out| match out {
                login_check::Outcome::Ready { user, .. } => { if user != t.user_name { d.reload(); } then(&d); }
                login_check::Outcome::Down(why) => { let (d2, then) = (d.clone(), then.clone()); confirm(&d.win, "The server did not answer", &format!("{why}\n\nOpen the tool anyway?"), "Open anyway", false, move || then(&d2)); }
                login_check::Outcome::NeedKey => {
                    let (d2, t2, then) = (d.clone(), t.clone(), then.clone());
                    confirm(&d.win, "Which key file is this?", &format!("None of the key files on this computer lets you in to {}. Pick the key file you use for it.", t.name), "Choose a key file", false, move || {
                        let dlg = gtk::FileDialog::builder().title("Choose the key file for this server").build();
                        let ssh = crate::paths::ssh_dir();
                        if ssh.is_dir() { dlg.set_initial_folder(Some(&gio::File::for_path(ssh))); }
                        let (d3, t3, then) = (d2.clone(), t2.clone(), then.clone());
                        dlg.open(Some(&d2.win), None::<&gio::Cancellable>, move |res| if let Some(p) = res.ok().and_then(|f| f.path()) {
                            login_check::save(&t3, &t3.user_name, Some(&p.to_string_lossy()));
                            login_check::forget_refusals(&t3.instance_id);
                            let mut fresh = t3.clone();
                            fresh.key_file = Some(p.to_string_lossy().into_owned());
                            d3.reload();
                            d3.checked(&fresh, then);
                        });
                    });
                }
            }
        });
    }

    /// The "Files" button: logs in over SFTP and opens the server's folders (a few seconds, so off the main thread), telling the person what happens.
    fn open_files(self: &Rc<Self>, t: &TunnelConfig, alias: &str) {
        self.toast("Opening the server's files over SFTP...");
        let (user, alias, d) = (t.user_name.clone(), alias.to_string(), self.clone());
        in_background(move || launchers::open_files(&user, &alias), move |r| if let Err(e) = r { d.toast(&e); });
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

    /// Reads processor, memory and disk use of the connected servers in the background. Automatic reads follow the Preferences; a manual one
    /// (`force`) reads every connected server, production included.
    pub fn refresh_usage(self: &Rc<Self>, force: bool) {
        let Some(app) = self.app() else { return };
        let cfg = self.cfg.borrow().clone();
        if (!force && !cfg.read_usage) || self.usage_busy.get() { if force { self.toast("Already reading..."); } return; }
        let connected: std::collections::HashSet<String> = app.manager.snapshot().iter().filter(|s| s.state == State::Connected).map(|s| s.instance_id.clone()).collect();
        if connected.is_empty() { if force { self.toast("No server is connected yet."); } return; }
        self.usage_busy.set(true);
        if force { self.toast("Reading how busy the servers are..."); }
        let (tunnels, d) = (cfg.tunnels.clone(), self.clone());
        in_background(move || usage::refresh(&tunnels, &connected, &cfg, force), move |n| {
            d.usage_busy.set(false);
            *d.usage.borrow_mut() = usage::load();
            d.refresh_status();
            if force { d.toast(&if n > 0 { format!("Read {n} server(s).") } else { "Nothing could be read. Your AWS role may not be allowed to run commands through Session Manager.".to_string() }); }
        });
    }

    pub fn refresh_status(self: &Rc<Self>) {
        let Some(app) = self.app() else { return };
        let status = app.manager.snapshot();
        let tools = launchers::tools_summary();
        let (mut up, mut n) = (0, 0);
        for sr in self.rows.borrow().iter() {
            let st = status.iter().find(|s| s.instance_id == sr.tunnel.instance_id && s.local_port == sr.tunnel.local_port);
            update_row(sr, st, &tools, self.usage.borrow().servers.get(&sr.tunnel.instance_id));
            n += 1;
            up += usize::from(st.is_some_and(|s| s.state == State::Connected));
        }
        self.apply_filter();
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

fn initial(name: Option<&str>) -> String {
    name.and_then(|n| n.trim().chars().next()).map(|c| c.to_uppercase().to_string()).unwrap_or_else(|| "+".into())
}

fn update_row(sr: &ServerRow, st: Option<&TunnelStatus>, tools: &launchers::Tools, use_: Option<&usage::Usage>) {
    let t = &sr.tunnel;
    let state = st.map(|s| s.state).unwrap_or(State::Disabled);
    let up = state == State::Connected;
    for c in ["green", "amber", "red", "grey"] { sr.card.remove_css_class(c); }
    for c in ["ok", "warn", "err"] { sr.status.remove_css_class(c); }
    let (level, kind, text) = match state {
        State::Connected => ("green", "ok", "Connected"),
        State::Starting => ("amber", "warn", "Connecting..."),
        State::Retrying => ("amber", "warn", "Retrying"),
        State::Error => ("red", "err", "Problem"),
        State::Disabled => ("grey", "", "Stopped"),
    };
    sr.card.add_css_class(level);
    if !kind.is_empty() { sr.status.add_css_class(kind); }
    sr.status.set_text(&format!("●  {text}"));
    let what = if t.is_rdp() { "Windows".to_string() } else if t.os_name.is_empty() { "Linux".into() } else { t.os_name.clone() };
    // second line: the operating system and port, or - when something is not right - what is wrong
    let problem = st.filter(|s| matches!(s.state, State::Error | State::Retrying | State::Starting) && !s.message.is_empty());
    sr.line.remove_css_class("bad");
    match problem {
        Some(s) => { sr.line.set_text(&s.message); if state == State::Error { sr.line.add_css_class("bad"); } }
        None => {
            let size = use_.filter(|u| u.cpus > 0).map(|u| format!("  -  {} vCPU, {} memory, {} disk", u.cpus, usage::size_text(u.mem_total_mb), format!("{} GB", u.disk_total_gb))).unwrap_or_default();
            sr.line.set_text(&format!("{what}{size}  -  port {}", t.local_port));
        }
    }
    let readings = use_.map(|u| [("CPU", u.cpu_pct), ("Memory", u.mem_pct), ("Disk", u.disk_pct)]);
    for (i, l) in sr.bars.iter().enumerate() {
        for c in ["ok", "warn", "bad"] { l.remove_css_class(c); }
        match readings {
            Some(r) => { l.set_text(&format!("{} {}%", r[i].0, r[i].1)); l.add_css_class(usage::level(r[i].1)); l.set_tooltip_text(Some(&format!("{} in use, read {} minute(s) ago.", r[i].0, ((crate::util::now() - use_.map(|u| u.read_at).unwrap_or(0.0)) / 60.0).max(0.0) as u64))); }
            None => { l.set_text(&format!("{} -", ["CPU", "Memory", "Disk"][i])); l.set_tooltip_text(Some("Not read yet. Reading needs a connected server and permission to run commands through Session Manager (Servers > Refresh usage).")); }
        }
    }
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
