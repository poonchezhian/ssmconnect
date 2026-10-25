//! "Copy files between servers": two panes, each one a server. Pick a server on each side, open folders, select files or folders and copy
//! them across. The data passes through this computer, so the servers never need to reach each other. One organization (company sign-in)
//! is linked at a time, root is never used, and an existing file is only replaced after a warning. The work is in `crate::transfer`.

use super::app::App;
use super::common::{confirm, in_background, message};
use crate::models::{State, TunnelConfig};
use crate::transfer::{self as tx, Entry, Remote};
use crate::{aws_profiles as ap, config_store, login_check, ssh_config};
use adw::prelude::*;
use std::cell::RefCell;
use std::rc::Rc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

const PICK: &str = "Select the server to connect";

#[derive(Default)]
struct PaneState {
    servers: Vec<TunnelConfig>,
    tunnel: Option<TunnelConfig>,
    /// The default user's login (kept so "Switch user" can start again from it).
    base: Option<Remote>,
    remote: Option<Remote>,
    dir: String,
    entries: Vec<Entry>,
    /// Incremented on every server change so a late answer for an earlier choice is ignored.
    token: u32,
}

struct Pane {
    root: gtk::Box,
    picker: gtk::DropDown,
    user: gtk::Entry,
    password: gtk::PasswordEntry,
    switch: gtk::Button,
    status: gtk::Label,
    path: gtk::Label,
    up: gtk::Button,
    list: gtk::ListBox,
    st: RefCell<PaneState>,
}

fn human(n: u64) -> String {
    let (mut v, mut u) = (n as f64, 0);
    while v >= 1024.0 && u < 4 { v /= 1024.0; u += 1; }
    if u == 0 { format!("{n} B") } else { format!("{v:.1} {}", ["B", "KB", "MB", "GB", "TB"][u]) }
}

fn day(epoch: f64) -> String { let (y, m, d) = crate::util::local_ymd(epoch); format!("{y:04}-{m:02}-{d:02}") }

impl Pane {
    fn new(title: &str) -> Rc<Pane> {
        let root = gtk::Box::new(gtk::Orientation::Vertical, 8);
        root.set_hexpand(true);
        root.add_css_class("tpane");
        let head = gtk::Label::builder().label(title).xalign(0.0).build();
        head.add_css_class("card-name");
        root.append(&head);
        let picker = gtk::DropDown::from_strings(&[PICK]);
        root.append(&picker);
        let who = gtk::Box::new(gtk::Orientation::Horizontal, 6);
        let user = gtk::Entry::builder().placeholder_text("Switch to user").hexpand(true).build();
        let password = gtk::PasswordEntry::builder().placeholder_text("sudo password").show_peek_icon(true).width_chars(14).build();
        let switch = gtk::Button::builder().label("Switch user").build();
        switch.add_css_class("abtn");
        who.append(&user); who.append(&password); who.append(&switch);
        root.append(&who);
        let status = gtk::Label::builder().xalign(0.0).wrap(true).label("Pick a server above.").build();
        status.add_css_class("card-line");
        root.append(&status);
        let nav = gtk::Box::new(gtk::Orientation::Horizontal, 6);
        let up = gtk::Button::builder().icon_name("go-up-symbolic").tooltip_text("Up one folder").sensitive(false).build();
        let path = gtk::Label::builder().xalign(0.0).hexpand(true).ellipsize(gtk::pango::EllipsizeMode::Start).selectable(true).build();
        nav.append(&up); nav.append(&path);
        root.append(&nav);
        let list = gtk::ListBox::builder().selection_mode(gtk::SelectionMode::Multiple).activate_on_single_click(false).build();
        list.add_css_class("boxed-list");
        let sc = gtk::ScrolledWindow::builder().vexpand(true).hscrollbar_policy(gtk::PolicyType::Never).min_content_height(260).child(&list).build();
        root.append(&sc);
        Rc::new(Pane { root, picker, user, password, switch, status, path, up, list, st: RefCell::new(PaneState::default()) })
    }

    fn say(&self, text: &str, bad: bool) {
        self.status.set_text(text);
        if bad { self.status.add_css_class("bad"); } else { self.status.remove_css_class("bad"); }
    }

    /// Lists the servers this side can pick, and forgets the current choice.
    fn set_servers(self: &Rc<Self>, servers: Vec<TunnelConfig>) {
        self.leave();
        let names: Vec<String> = std::iter::once(PICK.to_string()).chain(servers.iter().map(|t| format!("{}  -  {}", t.name, t.profile))).collect();
        let refs: Vec<&str> = names.iter().map(String::as_str).collect();
        self.picker.set_model(Some(&gtk::StringList::new(&refs)));
        self.st.borrow_mut().servers = servers;
        self.say(if self.st.borrow().servers.is_empty() { "No connected Linux server in this organization. Open the connections on the dashboard first." } else { "Pick a server above." }, false);
    }

    /// Lets go of the current server (and removes the password helper from it).
    fn leave(&self) {
        let (remote, _) = { let mut s = self.st.borrow_mut(); s.token += 1; let r = s.remote.take(); s.base = None; s.tunnel = None; s.dir.clear(); s.entries.clear(); (r, ()) };
        if let Some(r) = remote { std::thread::spawn(move || tx::close(&r)); }
        while let Some(c) = self.list.first_child() { self.list.remove(&c); }
        self.path.set_text("");
        self.up.set_sensitive(false);
    }

    fn connect_to(self: &Rc<Self>, t: TunnelConfig) {
        self.leave();
        let token = self.st.borrow().token;
        self.say("Signing in with the saved key...", false);
        let (me, strict, all) = (self.clone(), config_store::load().unwrap_or_default().strict_host_keys, config_store::load().unwrap_or_default().tunnels);
        let t2 = t.clone();
        in_background(move || {
            match login_check::ensure(&t2) {
                login_check::Outcome::Ready { user, key } => {
                    let r = Remote { port: t2.local_port, user, key, host_alias: ssh_config::host_key_alias(&t2, &all), strict, sudo: None };
                    tx::connect(&r).and_then(|home| tx::list(&r, &home).map(|l| (r.clone(), home, l)))
                }
                login_check::Outcome::NeedKey => Err("No key file fits this server yet. Open its Terminal button once to pick the key.".to_string()),
                login_check::Outcome::Down(why) => Err(why),
            }
        }, move |res| {
            if me.st.borrow().token != token { return; }
            match res {
                Ok((r, home, entries)) => {
                    me.say(&format!("Signed in as {}.", r.user), false);
                    { let mut s = me.st.borrow_mut(); s.tunnel = Some(t); s.base = Some(r.clone()); s.remote = Some(r); s.dir = home; s.entries = entries; }
                    me.show();
                }
                Err(e) => me.say(&e, true),
            }
        });
    }

    fn switch_user(self: &Rc<Self>) {
        let (user, pw) = (self.user.text().trim().to_string(), self.password.text().to_string());
        let Some(base) = self.st.borrow().base.clone() else { self.say("Pick a server first.", true); return };
        if user.is_empty() { self.say("Type the user to act as.", true); return; }
        if tx::is_root_name(&user) { self.say("File transfer never works as root. Type another user.", true); return; }
        let token = self.st.borrow().token;
        self.say(&format!("Switching to {user}..."), false);
        let (me, old) = (self.clone(), self.st.borrow().remote.clone());
        in_background(move || { if let Some(o) = old.filter(|o| o.sudo.is_some()) { tx::close(&o); } tx::switch_user(&base, &user, Some(&pw)).and_then(|(r, home)| tx::list(&r, &home).map(|l| (r, home, l))) }, move |res| {
            if me.st.borrow().token != token { return; }
            match res {
                Ok((r, home, entries)) => {
                    me.say(&format!("Acting as {} (through sudo).", r.sudo.as_ref().map(|s| s.user.as_str()).unwrap_or("")), false);
                    me.password.set_text("");
                    { let mut s = me.st.borrow_mut(); s.remote = Some(r); s.dir = home; s.entries = entries; }
                    me.show();
                }
                Err(e) => me.say(&e, true),
            }
        });
    }

    fn go(self: &Rc<Self>, dir: String) {
        let Some(r) = self.st.borrow().remote.clone() else { return };
        let token = self.st.borrow().token;
        self.say(&format!("Opening {dir}..."), false);
        let me = self.clone();
        let d2 = dir.clone();
        in_background(move || tx::list(&r, &d2), move |res| {
            if me.st.borrow().token != token { return; }
            match res {
                Ok(entries) => { { let mut s = me.st.borrow_mut(); s.dir = dir; s.entries = entries; } me.say("", false); me.show(); }
                Err(e) => me.say(&e, true),
            }
        });
    }

    fn reload(self: &Rc<Self>) { let d = self.st.borrow().dir.clone(); if !d.is_empty() { self.go(d); } }

    /// Draws the current folder.
    fn show(self: &Rc<Self>) {
        while let Some(c) = self.list.first_child() { self.list.remove(&c); }
        let s = self.st.borrow();
        self.path.set_text(&s.dir);
        self.up.set_sensitive(s.dir != "/" && !s.dir.is_empty());
        for e in &s.entries {
            let row = gtk::Box::new(gtk::Orientation::Horizontal, 10);
            row.set_margin_top(4); row.set_margin_bottom(4); row.set_margin_start(8); row.set_margin_end(8);
            row.append(&gtk::Image::from_icon_name(if e.is_dir { "folder-symbolic" } else { "text-x-generic-symbolic" }));
            let name = gtk::Label::builder().label(&e.name).xalign(0.0).hexpand(true).ellipsize(gtk::pango::EllipsizeMode::Middle).build();
            row.append(&name);
            let info = gtk::Label::builder().label(format!("{}   {}", if e.is_dir { String::new() } else { human(e.size) }, day(e.modified))).build();
            info.add_css_class("card-line");
            row.append(&info);
            self.list.append(&row);
        }
    }

    fn selected(&self) -> Vec<String> {
        let s = self.st.borrow();
        self.list.selected_rows().iter().filter_map(|r| s.entries.get(r.index() as usize)).map(|e| e.name.clone()).collect()
    }
}

pub fn show(app: &Rc<App>, parent: &impl IsA<gtk::Widget>, current_org: &str) {
    let dialog = adw::Dialog::builder().title("Copy files between servers").content_width(1080).content_height(720).build();
    let view = adw::ToolbarView::new();
    super::common::dialog_frame(&view, &dialog, "Copy files between servers", "Pick a server on each side, select files or folders, and copy. The data passes through this computer.", None);

    let cfg = config_store::load().unwrap_or_default();
    let status = app.manager.snapshot();
    let by_name: std::collections::BTreeMap<String, ap::ProfileInfo> = ap::list_profiles().into_iter().map(|p| (p.name.clone(), p)).collect();
    let everyone: Vec<ap::ProfileInfo> = by_name.values().cloned().collect();
    // connected Linux servers, grouped by company sign-in (organization)
    let connected: Vec<(String, String, TunnelConfig)> = cfg.tunnels.iter().filter(|t| !t.is_rdp() && t.enabled)
        .filter(|t| status.iter().any(|s| s.instance_id == t.instance_id && s.local_port == t.local_port && s.state == State::Connected))
        .map(|t| match by_name.get(&t.profile) { Some(p) => (ap::primary_key(p), ap::primary_label(&cfg, p, &everyone), t.clone()), None => (t.profile.clone(), t.profile.clone(), t.clone()) }).collect();
    let mut orgs: Vec<(String, String)> = vec![];
    for (k, l, _) in &connected { if !orgs.iter().any(|(x, _)| x == k) { orgs.push((k.clone(), l.clone())); } }

    let body = gtk::Box::new(gtk::Orientation::Vertical, 10);
    body.set_margin_top(12); body.set_margin_bottom(12); body.set_margin_start(16); body.set_margin_end(16);
    view.set_content(Some(&body));
    dialog.set_child(Some(&view));
    if orgs.is_empty() {
        body.append(&adw::StatusPage::builder().icon_name("folder-remote-symbolic").title("No server is connected").description("Open your connections on the dashboard (Start), then come back. Only connected Linux servers can be used here.").vexpand(true).build());
        dialog.present(Some(parent));
        return;
    }
    let org_row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
    org_row.append(&gtk::Label::new(Some("Organization")));
    let labels: Vec<&str> = orgs.iter().map(|(_, l)| l.as_str()).collect();
    let org_pick = gtk::DropDown::from_strings(&labels);
    org_pick.set_selected(orgs.iter().position(|(k, _)| k == current_org).unwrap_or(0) as u32);
    org_row.append(&org_pick);
    let hint = gtk::Label::builder().label("Both sides list servers of this organization only.").xalign(0.0).hexpand(true).build();
    hint.add_css_class("dim-label");
    org_row.append(&hint);
    body.append(&org_row);

    let (left, right) = (Pane::new("From"), Pane::new("To"));
    let middle = gtk::Box::new(gtk::Orientation::Vertical, 8);
    middle.set_valign(gtk::Align::Center);
    let to_right = gtk::Button::builder().label("Copy  \u{2192}").tooltip_text("Copy the selected items from the left server to the folder shown on the right").build();
    let to_left = gtk::Button::builder().label("\u{2190}  Copy").tooltip_text("Copy the selected items from the right server to the folder shown on the left").build();
    for b in [&to_right, &to_left] { b.add_css_class("main"); }
    middle.append(&to_right); middle.append(&to_left);
    let panes = gtk::Box::new(gtk::Orientation::Horizontal, 12);
    panes.set_vexpand(true);
    panes.append(&left.root); panes.append(&middle); panes.append(&right.root);
    body.append(&panes);

    let foot = gtk::Box::new(gtk::Orientation::Horizontal, 10);
    let progress = gtk::ProgressBar::builder().hexpand(true).valign(gtk::Align::Center).show_text(true).build();
    let stop = gtk::Button::builder().label("Stop").sensitive(false).build();
    stop.add_css_class("halt");
    foot.append(&progress); foot.append(&stop);
    body.append(&foot);

    let cancel = Arc::new(AtomicBool::new(false));
    let busy = Rc::new(std::cell::Cell::new(false));
    let fill = {
        let (left, right, orgs, connected) = (left.clone(), right.clone(), orgs.clone(), connected.clone());
        move |i: usize| {
            let key = orgs.get(i).map(|(k, _)| k.clone()).unwrap_or_default();
            let servers: Vec<TunnelConfig> = connected.iter().filter(|(k, _, _)| *k == key).map(|(_, _, t)| t.clone()).collect();
            left.set_servers(servers.clone()); right.set_servers(servers);
        }
    };
    fill(org_pick.selected() as usize);
    { let fill = fill.clone(); org_pick.connect_selected_notify(move |d| fill(d.selected() as usize)); }

    for p in [&left, &right] {
        let me = p.clone();
        p.picker.connect_selected_notify(move |d| {
            let i = d.selected() as usize;
            if i == 0 { me.leave(); me.say("Pick a server above.", false); return; }
            let t = me.st.borrow().servers.get(i - 1).cloned();
            if let Some(t) = t { me.connect_to(t); }
        });
        let me = p.clone();
        p.switch.connect_clicked(move |_| me.switch_user());
        let me = p.clone();
        p.password.connect_activate(move |_| me.switch_user());
        let me = p.clone();
        p.user.connect_activate(move |_| me.switch_user());
        let me = p.clone();
        p.up.connect_clicked(move |_| { let d = tx::parent(&me.st.borrow().dir); me.go(d); });
        let me = p.clone();
        p.list.connect_row_activated(move |_, row| {
            let e = me.st.borrow().entries.get(row.index() as usize).cloned();
            if let Some(e) = e.filter(|e| e.is_dir) { let d = tx::join(&me.st.borrow().dir, &e.name); me.go(d); }
        });
    }
    { let c = cancel.clone(); stop.connect_clicked(move |b| { c.store(true, Ordering::Relaxed); b.set_sensitive(false); }); }

    // one copy, from one side to the other
    let start = {
        let (progress, stop, cancel, busy, dialog) = (progress.clone(), stop.clone(), cancel.clone(), busy.clone(), dialog.clone());
        Rc::new(move |from: Rc<Pane>, to: Rc<Pane>| {
            if busy.get() { return; }
            let (names, src, dst) = (from.selected(), from.st.borrow().remote.clone(), to.st.borrow().remote.clone());
            let (Some(src), Some(dst)) = (src, dst) else { message(&dialog, "Pick both servers", "Choose a server on each side first."); return };
            if names.is_empty() { message(&dialog, "Nothing selected", "Select the files or folders to copy on the side you copy from."); return; }
            let (sdir, ddir) = (from.st.borrow().dir.clone(), to.st.borrow().dir.clone());
            let same_place = from.st.borrow().tunnel.as_ref().map(|t| t.instance_id.clone()) == to.st.borrow().tunnel.as_ref().map(|t| t.instance_id.clone())
                && src.sudo.as_ref().map(|s| &s.user) == dst.sudo.as_ref().map(|s| &s.user) && sdir == ddir;
            if same_place { message(&dialog, "Same folder", "The source and the destination are the same folder on the same server. Choose another folder on the right."); return; }
            let (progress, stop, cancel, busy, dialog, to2) = (progress.clone(), stop.clone(), cancel.clone(), busy.clone(), dialog.clone(), to.clone());
            // first find out what would be replaced
            let (d2, n2, dd2) = (dst.clone(), names.clone(), ddir.clone());
            in_background(move || tx::existing(&d2, &dd2, &n2), move |found| {
                let run = {
                    let (names, src, dst, sdir, ddir, progress, stop, cancel, busy, to2) = (names.clone(), src.clone(), dst.clone(), sdir.clone(), ddir.clone(), progress.clone(), stop.clone(), cancel.clone(), busy.clone(), to2.clone());
                    move || {
                        busy.set(true); cancel.store(false, Ordering::Relaxed); stop.set_sensitive(true);
                        progress.set_fraction(0.0); progress.set_text(Some("Starting..."));
                        let (ptx, prx) = async_channel::bounded::<(u64, u64)>(8);
                        let (p2, s2, b2, to3) = (progress.clone(), stop.clone(), busy.clone(), to2.clone());
                        gtk::glib::spawn_future_local(async move { while let Ok((d, t)) = prx.recv().await { p2.set_fraction(if t == 0 { 0.0 } else { d as f64 / t as f64 }); p2.set_text(Some(&format!("{} of {}", human(d), human(t)))); } });
                        let (c2, names, src, dst, sdir, ddir) = (cancel.clone(), names.clone(), src.clone(), dst.clone(), sdir.clone(), ddir.clone());
                        let (p3, s3, b3) = (progress.clone(), s2.clone(), b2.clone());
                        in_background(move || tx::copy(&src, &sdir, &names, &dst, &ddir, &|d, t| { let _ = ptx.try_send((d, t)); }, &c2), move |res| {
                            b3.set(false); s3.set_sensitive(false);
                            match res {
                                Ok(()) => { p3.set_fraction(1.0); p3.set_text(Some("Done")); }
                                Err(e) => { p3.set_text(Some(&e)); }
                            }
                            to3.reload();
                        });
                    }
                };
                match found {
                    Ok(f) if !f.is_empty() => {
                        let shown = f.iter().take(8).cloned().collect::<Vec<_>>().join("\n") + if f.len() > 8 { "\n..." } else { "" };
                        confirm(&dialog, "Replace existing files?", &format!("These already exist in the destination folder and will be replaced:\n\n{shown}"), "Replace", true, run);
                    }
                    Ok(_) => run(),
                    Err(e) => message(&dialog, "Could not look at the destination", &e),
                }
            });
        })
    };
    { let (s, l, r) = (start.clone(), left.clone(), right.clone()); to_right.connect_clicked(move |_| s(l.clone(), r.clone())); }
    { let (s, l, r) = (start.clone(), left.clone(), right.clone()); to_left.connect_clicked(move |_| s(r.clone(), l.clone())); }

    // closing the window removes the password helpers from the servers
    { let (l, r, c) = (left.clone(), right.clone(), cancel.clone()); dialog.connect_closed(move |_| { c.store(true, Ordering::Relaxed); l.leave(); r.leave(); }); }
    dialog.present(Some(parent));
}
