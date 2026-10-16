//! The five-step setup: Requirements, AWS account, Servers, SSH keys, Save.

use super::app::App;
use super::common::{clear_box, esc, in_background, message, progress_channel};
use crate::models::{AppConfig, DiscoveredServer, TunnelConfig};
use crate::validator::{is_valid_login, is_valid_region};
use crate::{aws_cli, aws_profiles as ap, config_store, discovery, key_finder, launchers, netcheck, prereq_installer, sso, sso_discovery, ssh_config};
use adw::prelude::*;
use gtk::gio;
use std::cell::{Cell, RefCell};
use std::collections::HashSet;
use std::rc::Rc;

const STEPS: [&str; 5] = ["Requirements", "AWS account", "Servers", "SSH keys", "Save"];
const FIRST_PORT: u16 = 2201;

struct KeyRow { profile: String, server: DiscoveredServer, user: adw::EntryRow, combo: adw::ComboRow }

#[derive(Default)]
struct Data {
    cfg: AppConfig,
    primaries: Vec<(String, String)>,
    primary_key: String,
    members: Vec<ap::ProfileInfo>,
    region: String,
    found: Vec<(String, DiscoveredServer)>,
    checks: Vec<(String, String, gtk::CheckButton)>,
    keys: Vec<key_finder::KeyFile>,
    key_rows: Vec<KeyRow>,
}

pub struct Wizard { w: Rc<Inner> }

struct Inner {
    app: Rc<App>,
    win: adw::Window,
    add_account: bool,
    step: Cell<usize>,
    busy: Cell<bool>,
    d: RefCell<Data>,
    stack: gtk::Stack,
    step_label: gtk::Label,
    back: gtk::Button,
    next: gtk::Button,
    status: gtk::Label,
    req_box: gtk::Box,
    install_btn: gtk::Button,
    acct_choice: adw::ComboRow,
    new_box: adw::PreferencesGroup,
    url_row: adw::EntryRow,
    sso_region_row: adw::EntryRow,
    hint: gtk::Label,
    region_row: adw::ComboRow,
    srv_box: gtk::Box,
    key_box: gtk::Box,
    key_note: gtk::Label,
    sum_box: gtk::Box,
}

fn scroller(child: &impl IsA<gtk::Widget>) -> gtk::ScrolledWindow {
    let sc = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
    let clamp = adw::Clamp::builder().maximum_size(640).margin_top(18).margin_bottom(18).margin_start(16).margin_end(16).build();
    clamp.set_child(Some(child));
    sc.set_child(Some(&clamp));
    sc
}

fn page(title: &str, text: &str) -> gtk::Box {
    let b = gtk::Box::new(gtk::Orientation::Vertical, 14);
    let h = gtk::Label::builder().label(title).xalign(0.0).build();
    h.add_css_class("title-2");
    let d = gtk::Label::builder().label(text).xalign(0.0).wrap(true).build();
    d.add_css_class("dim-label");
    b.append(&h);
    b.append(&d);
    b
}

fn dim(text: &str) -> gtk::Label {
    let l = gtk::Label::builder().label(text).xalign(0.0).wrap(true).build();
    l.add_css_class("dim-label");
    l
}

fn combo_text(c: &adw::ComboRow) -> String { c.selected_item().and_downcast::<gtk::StringObject>().map(|o| o.string().to_string()).unwrap_or_default() }

fn set_model(c: &adw::ComboRow, items: &[String]) { let refs: Vec<&str> = items.iter().map(String::as_str).collect(); c.set_model(Some(&gtk::StringList::new(&refs))); }

impl Wizard {
    pub fn new(app: &Rc<App>, parent: &adw::ApplicationWindow, add_account: bool) -> Wizard {
        let win = adw::Window::builder().title("Set up SSMConnect").default_width(760).default_height(700).modal(true).transient_for(parent).build();
        let view = adw::ToolbarView::new();
        let header = adw::HeaderBar::new();
        let step_label = gtk::Label::new(None);
        header.set_title_widget(Some(&step_label));
        view.add_top_bar(&header);
        let stack = gtk::Stack::builder().transition_type(gtk::StackTransitionType::SlideLeftRight).vexpand(true).build();
        let toasts = adw::ToastOverlay::new();
        toasts.set_child(Some(&stack));
        view.set_content(Some(&toasts));
        let bar = gtk::Box::builder().spacing(8).margin_top(8).margin_bottom(8).margin_start(16).margin_end(16).build();
        let back = gtk::Button::with_label("Back");
        let status = gtk::Label::builder().xalign(0.0).hexpand(true).wrap(true).ellipsize(gtk::pango::EllipsizeMode::End).build();
        status.add_css_class("summary");
        let next = gtk::Button::with_label("Next");
        next.add_css_class("suggested-action");
        for w in [back.upcast_ref::<gtk::Widget>(), status.upcast_ref(), next.upcast_ref()] { bar.append(w); }
        view.add_bottom_bar(&bar);
        win.set_content(Some(&view));

        // page 1: requirements
        let p1 = page("What SSMConnect needs", "Two small programs from Amazon open the secure tunnels. SSMConnect can fetch them for you.");
        let req_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        p1.append(&req_box);
        let install_btn = gtk::Button::builder().label("Install what is missing").halign(gtk::Align::Start).build();
        install_btn.add_css_class("suggested-action");
        p1.append(&install_btn);
        p1.append(&dim("Installed for you only, in ~/.local/share/ssmconnect. No administrator rights needed. If your distribution packages them, you can install those instead and click Check again."));
        let again = gtk::Button::builder().label("Check again").halign(gtk::Align::Start).build();
        p1.append(&again);

        // page 2: account
        let p2 = page("Your AWS account", "Sign in once with your company account. Every AWS account in your organization comes along.");
        let g = adw::PreferencesGroup::new();
        let acct_choice = adw::ComboRow::builder().title("Sign-in").build();
        g.add(&acct_choice);
        p2.append(&g);
        let new_box = adw::PreferencesGroup::new();
        let url_row = adw::EntryRow::builder().title("Company sign-in address (like https://yourcompany.awsapps.com/start)").build();
        let sso_region_row = adw::EntryRow::builder().title("Region of the sign-in page (like us-east-1)").text("us-east-1").build();
        new_box.add(&url_row);
        new_box.add(&sso_region_row);
        p2.append(&new_box);
        let hint = dim("");
        p2.append(&hint);
        let keys_link = gtk::Button::builder().label("I use access keys instead").halign(gtk::Align::Start).build();
        keys_link.add_css_class("flat");
        p2.append(&keys_link);
        let g2 = adw::PreferencesGroup::new();
        let region_row = adw::ComboRow::builder().title("Region of your servers").subtitle("Where your EC2 servers live, for example ap-south-1").build();
        g2.add(&region_row);
        p2.append(&g2);

        // page 3: servers
        let p3 = page("Pick your servers", "Tick the servers you want to connect to. Servers marked 'not reachable' have no working SSM agent yet.");
        let srv_box = gtk::Box::new(gtk::Orientation::Vertical, 16);
        p3.append(&srv_box);

        // page 4: keys
        let p4 = page("SSH keys", "Each Linux server needs the key file it was created with. SSMConnect looks for it and checks it against the server. Nothing is copied or uploaded.");
        let key_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        p4.append(&key_box);
        let krow = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let folder = gtk::Button::with_label("Search a folder…");
        krow.append(&folder);
        let key_note = dim("");
        krow.append(&key_note);
        p4.append(&krow);

        // page 5: save
        let p5 = page("Ready", "This is what will be saved. Connections open right away, and ssh gets a short name for each server.");
        let sum_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        p5.append(&sum_box);

        for (i, p) in [&p1, &p2, &p3, &p4, &p5].into_iter().enumerate() { stack.add_named(&scroller(p), Some(&i.to_string())); }

        let inner = Rc::new(Inner { app: app.clone(), win, add_account, step: Cell::new(0), busy: Cell::new(false), d: RefCell::new(Data { cfg: config_store::load().unwrap_or_default(), ..Default::default() }),
            stack, step_label, back: back.clone(), next: next.clone(), status, req_box, install_btn: install_btn.clone(), acct_choice: acct_choice.clone(), new_box, url_row,
            sso_region_row, hint, region_row, srv_box, key_box, key_note, sum_box });
        let me = inner.clone(); back.connect_clicked(move |_| me.go(me.step.get().saturating_sub(1)));
        let me = inner.clone(); next.connect_clicked(move |_| me.on_next());
        let me = inner.clone(); install_btn.connect_clicked(move |_| me.install());
        let me = inner.clone(); again.connect_clicked(move |_| me.enter_requirements());
        let me = inner.clone(); acct_choice.connect_selected_notify(move |_| me.account_changed());
        let me = inner.clone(); keys_link.connect_clicked(move |_| me.access_keys_dialog());
        let me = inner.clone(); folder.connect_clicked(move |_| me.pick_folder());
        inner.go(0);
        Wizard { w: inner }
    }

    pub fn present(&self) { self.w.win.present(); }
    pub fn window(&self) -> &adw::Window { &self.w.win }
    /// For the end-to-end example: drive the wizard like a person would.
    pub fn driver(&self) -> Rc<dyn WizardDriver> { Rc::new(self.w.clone()) }
}

/// What the end-to-end example needs to steer the wizard.
pub trait WizardDriver {
    fn step(&self) -> usize;
    fn busy(&self) -> bool;
    fn next(&self);
    fn set_sign_in(&self, url: &str, sso_region: &str, region: &str);
    fn found(&self) -> Vec<(String, String, bool)>;
    fn tick_all(&self);
    fn key_rows(&self) -> Vec<(String, Option<String>)>;
    fn members(&self) -> usize;
}

impl WizardDriver for Rc<Inner> {
    fn step(&self) -> usize { self.step.get() }
    fn busy(&self) -> bool { self.busy.get() }
    fn next(&self) { self.on_next(); }
    fn set_sign_in(&self, url: &str, sso_region: &str, region: &str) {
        self.url_row.set_text(url);
        self.sso_region_row.set_text(sso_region);
        if let Some(i) = self.d.borrow().primaries.len().checked_add(0) { self.acct_choice.set_selected(i as u32); }
        if let Some(m) = self.region_row.model() {
            for i in 0..m.n_items() { if m.item(i).and_downcast::<gtk::StringObject>().is_some_and(|o| o.string() == region) { self.region_row.set_selected(i); } }
        }
    }
    fn found(&self) -> Vec<(String, String, bool)> { self.d.borrow().found.iter().map(|(p, s)| (p.clone(), s.name.clone(), s.ssm_online)).collect() }
    fn tick_all(&self) { for (_, _, c) in &self.d.borrow().checks { if c.is_sensitive() { c.set_active(true); } } }
    fn key_rows(&self) -> Vec<(String, Option<String>)> { let d = self.d.borrow(); d.key_rows.iter().map(|r| (r.server.name.clone(), self.key_for(&d, r))).collect() }
    fn members(&self) -> usize { self.d.borrow().members.len() }
}

impl Inner {
    // ---- plumbing ------------------------------------------------------------------------------
    fn say(&self, text: &str) { self.status.set_text(text); }

    fn selected(&self) -> Vec<(String, DiscoveredServer)> {
        let d = self.d.borrow();
        d.found.iter().filter(|(p, s)| d.checks.iter().any(|(cp, ci, c)| cp == p && *ci == s.instance_id && c.is_active())).cloned().collect()
    }

    fn go(self: &Rc<Self>, n: usize) {
        let mut n = n.min(STEPS.len() - 1);
        if n == 3 && !self.selected().iter().any(|(_, s)| !s.is_windows()) { n = if self.step.get() < 3 { 4 } else { 2 }; }   // no Linux server ticked: nothing to match a key to
        self.step.set(n);
        self.stack.set_visible_child_name(&n.to_string());
        self.step_label.set_text(&format!("Step {} of {}: {}", n + 1, STEPS.len(), STEPS[n]));
        self.back.set_visible(n > 0);
        self.next.set_label(if n == 4 { "Save and connect" } else { "Next" });
        self.say("");
        self.next.set_sensitive(true);
        match n { 0 => self.enter_requirements(), 1 => self.enter_account(), 2 => self.enter_servers(), 3 => self.enter_keys(), _ => self.enter_save() }
    }

    fn on_next(self: &Rc<Self>) {
        if self.busy.get() { return; }
        match self.step.get() { 0 => self.go(1), 1 => self.leave_account(), 2 => self.go(3), 3 => self.go(4), _ => self.save() }
    }

    fn set_busy(&self, on: bool, text: &str) {
        self.busy.set(on);
        self.next.set_sensitive(!on);
        self.back.set_sensitive(!on);
        self.say(text);
    }

    fn progress(self: &Rc<Self>) -> async_channel::Sender<String> { let me = self.clone(); progress_channel(move |m| me.say(&m)) }

    // ---- 1. requirements -----------------------------------------------------------------------
    fn enter_requirements(self: &Rc<Self>) {
        clear_box(&self.req_box);
        let g = adw::PreferencesGroup::new();
        let t = launchers::tools_summary();
        let items = [("AWS CLI", aws_cli::find_aws().map(|p| p.display().to_string()), true), ("Session Manager plugin", aws_cli::find_plugin().map(|p| p.display().to_string()), true),
                     ("ssh (OpenSSH client)", t.ssh, false), ("Terminal", t.terminal, false), ("Remote Desktop client (for Windows servers)", t.rdp, false)];
        let mut missing_required = false;
        for (name, found, required) in items {
            let row = adw::ActionRow::builder().title(name).subtitle(esc(found.as_deref().unwrap_or(if required { "Missing" } else { "Not found (optional)" }))).build();
            row.add_prefix(&gtk::Image::from_icon_name(if found.is_some() { "object-select-symbolic" } else if required { "dialog-error-symbolic" } else { "dialog-warning-symbolic" }));
            g.add(&row);
            missing_required |= required && found.is_none();
        }
        self.req_box.append(&g);
        self.install_btn.set_visible(missing_required);
        self.next.set_sensitive(!missing_required && !self.busy.get());
        if missing_required { self.say("Install the missing programs to continue."); }
    }

    fn install(self: &Rc<Self>) {
        self.set_busy(true, "Starting...");
        self.install_btn.set_sensitive(false);
        let tx = self.progress();
        let me = self.clone();
        in_background(move || prereq_installer::install_missing(&|m| { let _ = tx.send_blocking(m); }), move |res| {
            me.set_busy(false, "");
            me.install_btn.set_sensitive(true);
            if let Err(e) = res { message(&me.win, "Could not install", &e); }
            me.enter_requirements();
        });
    }

    // ---- 2. account ----------------------------------------------------------------------------
    fn primaries(&self) -> Vec<(String, String)> {
        let everyone = ap::list_profiles();
        let cfg = self.d.borrow().cfg.clone();
        let mut seen: Vec<(String, String)> = vec![];
        for p in everyone.iter().filter(|p| p.kind != ap::Kind::Other) {
            let k = ap::primary_key(p);
            if !seen.iter().any(|(x, _)| *x == k) { seen.push((k, ap::primary_label(&cfg, p, &everyone))); }
        }
        seen
    }

    fn enter_account(self: &Rc<Self>) {
        let cfg = config_store::load().unwrap_or_else(|| self.d.borrow().cfg.clone());
        let prim = self.primaries();
        let mut labels: Vec<String> = prim.iter().map(|(_, l)| l.clone()).collect();
        labels.push("Add a new company sign-in…".into());
        let new_idx = labels.len() - 1;
        let want = { let d = self.d.borrow(); if !d.primary_key.is_empty() { d.primary_key.clone() } else { cfg.selected_account.clone() } };
        let pick = if self.add_account || prim.is_empty() { new_idx } else { prim.iter().position(|(k, _)| *k == want).unwrap_or(0) };
        { let mut d = self.d.borrow_mut(); d.cfg = cfg.clone(); d.primaries = prim; }
        set_model(&self.acct_choice, &labels);
        self.acct_choice.set_selected(pick as u32);
        let mut regions: Vec<String> = ap::COMMON_REGIONS.iter().map(|s| s.to_string()).collect();
        let want = if cfg.region.is_empty() { "us-east-1".to_string() } else { cfg.region.clone() };
        if !regions.contains(&want) { regions.push(want.clone()); }
        regions.sort();
        set_model(&self.region_row, &regions);
        self.region_row.set_selected(regions.iter().position(|r| *r == want).unwrap_or(0) as u32);
        let cand = sso_discovery::find();
        if self.url_row.text().is_empty() {
            if let Some(c) = cand.first() {
                self.url_row.set_text(&c.start_url);
                if !c.region.is_empty() { self.sso_region_row.set_text(&c.region); }
                self.hint.set_text(&format!("Filled in for you. {}.", c.source));
            } else {
                self.hint.set_text("Ask your AWS admin for the sign-in address if you do not know it.");
                let me = self.clone();
                in_background(sso_discovery::guess_from_domain, move |g| if let Some(c) = g.first() { if me.url_row.text().is_empty() { me.url_row.set_text(&c.start_url); me.hint.set_text(&format!("{}.", c.source)); } });
            }
        }
        self.account_changed();
    }

    fn account_changed(&self) {
        let new = self.acct_choice.selected() as usize >= self.d.borrow().primaries.len();
        self.new_box.set_visible(new);
        if self.step.get() == 1 { self.next.set_label(if new { "Sign in" } else { "Next" }); }
    }

    fn leave_account(self: &Rc<Self>) {
        let region = combo_text(&self.region_row);
        if !is_valid_region(&region) { message(&self.win, "Choose a region", "Pick the region where your servers live."); return; }
        self.d.borrow_mut().region = region;
        let sel = self.acct_choice.selected() as usize;
        let existing = self.d.borrow().primaries.get(sel).map(|(k, _)| k.clone());
        match existing { Some(k) => { self.d.borrow_mut().primary_key = k; self.existing_sign_in(); } None => self.new_sign_in() }
    }

    fn members_of(&self, key: &str) -> Vec<ap::ProfileInfo> {
        let group: Vec<ap::ProfileInfo> = ap::list_profiles().into_iter().filter(|p| ap::primary_key(p) == key).collect();
        let preferred: HashSet<String> = self.d.borrow().cfg.tunnels.iter().map(|t| t.profile.clone()).collect();
        ap::one_each(&group, &preferred, None)
    }

    fn existing_sign_in(self: &Rc<Self>) {
        let key = self.d.borrow().primary_key.clone();
        let group: Vec<ap::ProfileInfo> = ap::list_profiles().into_iter().filter(|p| ap::primary_key(p) == key).collect();
        let Some(first) = group.first() else { return };
        if first.kind != ap::Kind::Sso { let m = self.members_of(&key); self.d.borrow_mut().members = m; self.go(2); return; }
        let mut sessions: Vec<String> = vec![];
        for p in &group { if let Some(s) = &p.sso_session { if !sessions.contains(s) { sessions.push(s.clone()); } } }
        self.set_busy(true, "Checking your sign-in...");
        let tx = self.progress();
        let me = self.clone();
        in_background(move || -> Result<(), String> {
            let session = match sessions.iter().find(|s| sso::read_access_token(s).is_some()) {
                Some(s) => s.clone(),
                None => {
                    let _ = tx.send_blocking("Your browser opens: finish signing in there...".into());
                    let r = sso::login(&sessions[0]);
                    if !r.ok() { return Err(aws_cli::friendly_error(&r.error, None)); }
                    sessions[0].clone()
                }
            };
            let (token, region) = (sso::read_access_token(&session), ap::session_info(&session).1);
            if let (Some(token), Some(region)) = (token, region) {   // pick up AWS accounts added to the organization since last time
                let known: HashSet<&str> = group.iter().filter_map(|p| p.account_id.as_deref()).collect();
                let fresh: Vec<sso::SsoAccount> = sso::list_accounts(&token, &region)?.into_iter().filter(|a| !known.contains(a.id.as_str())).collect();
                if !fresh.is_empty() {
                    let role = group.iter().find_map(|p| p.role_name.clone()).unwrap_or_default();
                    sso::add_all_accounts(&session, &token, &region, &role, &fresh, &|m| { let _ = tx.send_blocking(m); });
                }
            }
            Ok(())
        }, move |res| {
            me.set_busy(false, "");
            match res {
                Err(e) => message(&me.win, "Sign-in did not finish", &e),
                Ok(()) => { let m = me.members_of(&key); me.d.borrow_mut().members = m; me.go(2); }
            }
        });
    }

    fn new_sign_in(self: &Rc<Self>) {
        let (url, sso_region) = (self.url_row.text().trim().to_string(), self.sso_region_row.text().trim().to_string());
        if !sso_discovery::is_valid_url(&url) { message(&self.win, "Check the sign-in address", "It should look like https://yourcompany.awsapps.com/start"); return; }
        if !is_valid_region(&sso_region) { message(&self.win, "Check the sign-in region", "Use the AWS region of the sign-in page, for example us-east-1."); return; }
        let url = url.split('#').next().unwrap_or("").trim_end_matches('/').to_string();
        let mut session = sso_discovery::session_name_for(&url);
        if let Some(have) = ap::session_info(&session).0 { if ap::normalise_url(&have) != ap::normalise_url(&url) { session += "-2"; } }
        if let Err(e) = ap::save_sso_session(&session, &url, &sso_region, true) { message(&self.win, "Could not save", &e); return; }
        self.set_busy(true, "Your browser opens: finish signing in there...");
        let tx = self.progress();
        let me = self.clone();
        in_background(move || -> Result<Vec<(String, sso::SsoAccount)>, String> {
            let r = sso::login(&session);
            if !r.ok() { return Err(aws_cli::friendly_error(if r.error.is_empty() { &r.output } else { &r.error }, None)); }
            let token = sso::read_access_token(&session).ok_or("The sign-in finished but AWS did not hand over a token. Please try again.")?;
            let accounts = sso::list_accounts(&token, &sso_region)?;
            if accounts.is_empty() { return Err("You are signed in, but no AWS accounts are assigned to you. Ask your AWS admin.".into()); }
            let made = sso::add_all_accounts(&session, &token, &sso_region, "", &accounts, &|m| { let _ = tx.send_blocking(m); });
            if made.is_empty() { return Err("You have no role in any of the AWS accounts. Ask your AWS admin to assign one.".into()); }
            Ok(made)
        }, move |res| {
            me.set_busy(false, "");
            match res {
                Err(e) => message(&me.win, "Sign-in did not finish", &e),
                Ok(made) => {
                    let key = ap::find(&made[0].0).map(|p| ap::primary_key(&p)).unwrap_or_default();
                    let m = me.members_of(&key);
                    me.say(&format!("Signed in. {} AWS account(s) found.", m.len()));
                    { let mut d = me.d.borrow_mut(); d.primary_key = key; d.members = m; }
                    me.go(2);
                }
            }
        });
    }

    fn access_keys_dialog(self: &Rc<Self>) {
        let d = adw::AlertDialog::new(Some("Use access keys"), Some("Keys are saved only in your own ~/.aws/credentials file."));
        let b = gtk::Box::new(gtk::Orientation::Vertical, 8);
        let name = gtk::Entry::builder().placeholder_text("Name for this account, e.g. my-company").build();
        let kid = gtk::Entry::builder().placeholder_text("Access key ID (AKIA…)").build();
        let secret = gtk::PasswordEntry::builder().placeholder_text("Secret access key").show_peek_icon(true).build();
        b.append(&name); b.append(&kid); b.append(&secret);
        d.set_extra_child(Some(&b));
        d.add_response("cancel", "Cancel");
        d.add_response("save", "Save");
        d.set_response_appearance("save", adw::ResponseAppearance::Suggested);
        let me = self.clone();
        d.connect_response(None, move |_, r| {
            if r != "save" { return; }
            let n = name.text().trim().to_string();
            let res = if ap::is_valid_name(&n) { ap::save_access_keys(&n, kid.text().trim(), secret.text().trim()) } else { Err("Use letters, digits, dashes or dots for the name.".to_string()) };
            if let Err(e) = res { message(&me.win, "Could not save", &e); return; }
            me.enter_account();
            if let Some(p) = ap::find(&n) {
                let key = ap::primary_key(&p);
                if let Some(i) = me.d.borrow().primaries.iter().position(|(k, _)| *k == key) { me.acct_choice.set_selected(i as u32); }
            }
        });
        d.present(Some(&self.win));
    }

    // ---- 3. servers ----------------------------------------------------------------------------
    fn enter_servers(self: &Rc<Self>) {
        clear_box(&self.srv_box);
        { let mut d = self.d.borrow_mut(); d.found.clear(); d.checks.clear(); }
        let spin = gtk::Spinner::builder().spinning(true).halign(gtk::Align::Center).width_request(32).height_request(32).build();
        self.srv_box.append(&spin);
        let (members, region) = { let d = self.d.borrow(); (d.members.clone(), d.region.clone()) };
        self.set_busy(true, &format!("Searching {} AWS account(s) for servers...", members.len()));
        let tx = self.progress();
        let me = self.clone();
        in_background(move || {
            let done = std::sync::atomic::AtomicUsize::new(0);
            let total = members.len();
            let mut out: Vec<(String, Result<Vec<DiscoveredServer>, String>)> = vec![];
            for chunk in members.chunks(4) {
                std::thread::scope(|s| {
                    let hs: Vec<_> = chunk.iter().map(|p| { let (tx, region, done) = (tx.clone(), region.clone(), &done); s.spawn(move || {
                        let r = aws_cli::discover(&p.name, &region);
                        let n = done.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
                        let _ = tx.send_blocking(format!("Searched {n} of {total} AWS account(s)..."));
                        (p.name.clone(), r)
                    }) }).collect();
                    for h in hs { if let Ok(v) = h.join() { out.push(v); } }
                });
            }
            out
        }, move |results| me.servers_done(results));
    }

    fn servers_done(self: &Rc<Self>, results: Vec<(String, Result<Vec<DiscoveredServer>, String>)>) {
        self.set_busy(false, "");
        clear_box(&self.srv_box);
        let (cfg, region) = { let d = self.d.borrow(); (d.cfg.clone(), d.region.clone()) };
        let have: HashSet<(String, String)> = cfg.tunnels.iter().map(|t| (t.profile.clone(), t.instance_id.clone())).collect();
        let configured: HashSet<String> = cfg.tunnels.iter().map(|t| t.instance_id.clone()).collect();
        let (mut skipped, mut any) = (vec![], false);
        for (profile, res) in results {
            let servers = match res { Ok(s) => s, Err(e) => { skipped.push(format!("{}: {e}", cfg.alias_or(&profile, &profile))); continue; } };
            discovery::update(&profile, &region, servers.clone(), &configured, false);
            if servers.is_empty() { continue; }
            let g = adw::PreferencesGroup::new();
            g.set_title(&esc(&cfg.alias_or(&profile, &profile)));
            for s in servers {
                any = true;
                let sub = format!("{} · {}{}", if s.is_windows() { "Windows".to_string() } else if s.os_name.is_empty() { "Linux".into() } else { s.os_name.clone() }, s.private_ip, if s.ssm_online { "" } else { " · not reachable through SSM" });
                let row = adw::ActionRow::builder().title(esc(&s.name)).subtitle(esc(&sub)).build();
                let chk = gtk::CheckButton::builder().valign(gtk::Align::Center).active(have.contains(&(profile.clone(), s.instance_id.clone())) && s.ssm_online).sensitive(s.ssm_online).build();
                row.add_prefix(&chk);
                row.set_activatable_widget(Some(&chk));
                if !s.environment.is_empty() {
                    let tag = gtk::Label::builder().label(if s.is_prod() { "PROD" } else { &s.environment }).valign(gtk::Align::Center).build();
                    tag.add_css_class("tag");
                    if s.is_prod() { tag.add_css_class("prod"); }
                    row.add_suffix(&tag);
                }
                g.add(&row);
                let mut d = self.d.borrow_mut();
                d.checks.push((profile.clone(), s.instance_id.clone(), chk));
                d.found.push((profile.clone(), s));
            }
            self.srv_box.append(&g);
        }
        if !skipped.is_empty() { self.srv_box.append(&dim(&format!("Some AWS accounts could not be searched:\n• {}", skipped.join("\n• ")))); }
        if !any {
            self.srv_box.append(&adw::StatusPage::builder().title("No servers found").icon_name("network-server-symbolic")
                .description("No running servers in this region. Try another region (Back), or check your permissions.").build());
        }
    }

    // ---- 4. keys -------------------------------------------------------------------------------
    fn enter_keys(self: &Rc<Self>) {
        self.set_busy(true, "Looking for key files on this computer...");
        let me = self.clone();
        in_background(|| key_finder::scan_standard(15, &|| false), move |keys| me.keys_found(keys));
    }

    fn keys_found(self: &Rc<Self>, keys: Vec<key_finder::KeyFile>) {
        self.set_busy(false, "");
        { let mut d = self.d.borrow_mut(); for k in keys { if !d.keys.iter().any(|x| x.path == k.path) { d.keys.push(k); } } }
        self.build_key_rows();
    }

    fn pick_folder(self: &Rc<Self>) {
        let dlg = gtk::FileDialog::builder().title("Choose a folder to search for key files").build();
        let me = self.clone();
        dlg.select_folder(Some(&self.win), None::<&gio::Cancellable>, move |res| {
            let Ok(f) = res else { return };
            let Some(path) = f.path() else { return };
            me.set_busy(true, "Searching that folder...");
            let me2 = me.clone();
            in_background(move || key_finder::scan_folder(&path.to_string_lossy(), 30, &|| false), move |keys| me2.keys_found(keys));
        });
    }

    fn build_key_rows(self: &Rc<Self>) {
        clear_box(&self.key_box);
        let sel = self.selected();
        let mut d = self.d.borrow_mut();
        let old: Vec<KeyRow> = std::mem::take(&mut d.key_rows);
        let g = adw::PreferencesGroup::new();
        let mut names = vec!["(no key file: use ssh-agent or ask later)".to_string()];
        names.extend(d.keys.iter().map(|k| k.display()));
        let mut missing = 0;
        let mut rows = vec![];
        for (profile, s) in sel {
            let exp = adw::ExpanderRow::builder().title(esc(&s.name)).build();
            let user = adw::EntryRow::builder().title("Login user").build();
            let prev = old.iter().find(|r| r.profile == profile && r.server.instance_id == s.instance_id);
            let existing = d.cfg.tunnels.iter().find(|t| t.instance_id == s.instance_id);
            user.set_text(&prev.map(|p| p.user.text().to_string()).or_else(|| existing.map(|t| t.user_name.clone())).unwrap_or_else(|| s.suggested_user().to_string()));
            exp.add_row(&user);
            let combo = adw::ComboRow::builder().title(if s.is_windows() { "Key file (only needed to get the password)" } else { "Key file" }).build();
            set_model(&combo, &names);
            let (mut chosen, mut how): (Option<usize>, &str) = (None, "");
            if let Some(p) = prev.filter(|p| p.combo.selected() > 0) { chosen = Some(p.combo.selected() as usize - 1); how = "kept"; }
            else if let Some(kf) = existing.and_then(|t| t.key_file.as_ref()) { chosen = d.keys.iter().position(|k| &k.path == kf); how = "already saved"; }
            if chosen.is_none() {
                let (k, h) = key_finder::find(&d.keys, &s.key_name, &s.key_fingerprint);
                chosen = k.and_then(|k| d.keys.iter().position(|x| x.path == k.path));
                how = h;
            }
            match chosen {
                Some(i) => { combo.set_selected(i as u32 + 1); exp.set_subtitle(&esc(&format!("{} - {how}", d.keys[i].stem()))); }
                None => { missing += 1; exp.set_subtitle(&esc(&format!("No key found for '{}'", if s.key_name.is_empty() { "unknown key" } else { &s.key_name }))); }
            }
            let e2 = exp.clone();
            combo.connect_selected_notify(move |_| e2.set_subtitle(""));
            exp.add_row(&combo);
            exp.set_expanded(chosen.is_none());
            g.add(&exp);
            rows.push(KeyRow { profile, server: s, user, combo });
        }
        d.key_rows = rows;
        drop(d);
        self.key_box.append(&g);
        self.key_note.set_text(&if missing > 0 { format!("{missing} server(s) have no key yet. Choose one, or search a folder.") } else { "All keys found.".into() });
    }

    fn key_for(&self, d: &Data, r: &KeyRow) -> Option<String> {
        let i = r.combo.selected() as usize;
        (i > 0).then(|| d.keys.get(i - 1).map(|k| k.path.clone())).flatten()
    }

    // ---- 5. save -------------------------------------------------------------------------------
    fn enter_save(self: &Rc<Self>) {
        clear_box(&self.sum_box);
        let g = adw::PreferencesGroup::new();
        let sel = self.selected();
        if sel.is_empty() { g.add(&adw::ActionRow::builder().title("No servers ticked").subtitle("Saving will remove this account's saved connections.").build()); }
        let d = self.d.borrow();
        for (profile, s) in sel {
            let r = d.key_rows.iter().find(|r| r.profile == profile && r.server.instance_id == s.instance_id);
            let key = r.and_then(|r| self.key_for(&d, r));
            let sub = if s.is_windows() { if key.is_some() { "Remote Desktop · password via key file".to_string() } else { "Remote Desktop".into() } } else {
                format!("ssh {} · user {}{}", ssh_config::host_alias(&s.name, &s.instance_id), r.map(|r| r.user.text().to_string()).unwrap_or_else(|| s.suggested_user().into()), if key.is_some() { "" } else { " · no key file" }) };
            g.add(&adw::ActionRow::builder().title(esc(&s.name)).subtitle(esc(&sub)).build());
        }
        self.sum_box.append(&g);
    }

    fn save(self: &Rc<Self>) {
        let mut cfg = config_store::load().unwrap_or_else(|| self.d.borrow().cfg.clone());
        let sel = self.selected();
        let d = self.d.borrow();
        let mine: HashSet<&str> = d.members.iter().map(|m| m.name.as_str()).collect();
        let keep: Vec<TunnelConfig> = cfg.tunnels.iter().filter(|t| !mine.contains(t.profile.as_str())).cloned().collect();
        let mut taken: HashSet<u16> = keep.iter().map(|t| t.local_port).collect();
        let mut new: Vec<TunnelConfig> = vec![];
        for (profile, s) in sel {
            let old = cfg.tunnels.iter().find(|t| t.profile == profile && t.instance_id == s.instance_id);
            let r = d.key_rows.iter().find(|r| r.profile == profile && r.server.instance_id == s.instance_id);
            let port = match old { Some(o) if !taken.contains(&o.local_port) => o.local_port, _ => netcheck::free_port(FIRST_PORT, &taken) };
            let user = r.map(|r| r.user.text().trim().to_string()).filter(|u| !u.is_empty()).unwrap_or_else(|| s.suggested_user().to_string());
            if !is_valid_login(&user) { message(&self.win, "Check the login user", &format!("'{user}' is not a valid user name for {}.", s.name)); return; }
            new.push(TunnelConfig { name: s.name.clone(), instance_id: s.instance_id.clone(), profile: profile.clone(), region: d.region.clone(), local_port: port,
                remote_port: if s.is_windows() { 3389 } else { 22 }, protocol: if s.is_windows() { "rdp".into() } else { "ssh".into() }, user_name: user,
                environment: s.environment.clone(), key_file: r.and_then(|r| self.key_for(&d, r)), enabled: true, os_name: s.os_name.clone() });
            taken.insert(port);
        }
        let saved = new.len();
        cfg.tunnels = keep.into_iter().chain(new).collect();
        if let Some(m) = d.members.first() { cfg.profile = m.name.clone(); }
        cfg.region = d.region.clone();
        cfg.selected_account = d.primary_key.clone();
        let members = d.members.clone();
        let region = d.region.clone();
        drop(d);
        let res = config_store::save(&cfg).map_err(|e| e.to_string()).and_then(|()| {
            for m in members.iter().filter(|m| m.region.is_none()) { ap::save_region(&m.name, &region)?; }
            ssh_config::apply(&cfg.tunnels).map_err(|e| e.to_string())
        });
        if let Err(e) = res { message(&self.win, "Could not save", &e); return; }
        self.app.ensure_manager();
        if let Some(w) = self.app.window.borrow().as_ref() { w.reload(); w.toast(&format!("Saved {saved} server(s). Opening connections...")); }
        self.win.close();
    }
}
