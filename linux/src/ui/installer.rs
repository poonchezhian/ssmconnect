//! The installation wizard: Welcome, Requirements (fetched from their own sources), Where to install, Done.
//! Everything SSMConnect depends on is installed before SSMConnect itself.

use super::common::{clear_box, esc, in_background, message, progress_channel};
use crate::installer::{self, Dependency, Elevation, Source};
use crate::{APP_ID, VERSION};
use adw::prelude::*;
use gtk::gio;
use std::cell::{Cell, RefCell};
use std::path::PathBuf;
use std::rc::Rc;

struct Inner {
    win: adw::ApplicationWindow,
    stack: gtk::Stack,
    title: gtk::Label,
    back: gtk::Button,
    next: gtk::Button,
    status: gtk::Label,
    spinner: gtk::Spinner,
    dep_box: gtk::Box,
    checks: RefCell<Vec<(Dependency, gtk::CheckButton)>>,
    user_radio: gtk::CheckButton,
    system_radio: gtk::CheckButton,
    path_hint: gtk::Label,
    done_text: gtk::Label,
    step: Cell<usize>,
    busy: Cell<bool>,
    prefix: RefCell<Option<PathBuf>>,
    source: PathBuf,
    installed_to: RefCell<Option<PathBuf>>,
}

const STEPS: [&str; 4] = ["Welcome", "Requirements", "Where to install", "Done"];

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

fn scroller(child: &impl IsA<gtk::Widget>) -> gtk::ScrolledWindow {
    let sc = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
    let clamp = adw::Clamp::builder().maximum_size(620).margin_top(18).margin_bottom(18).margin_start(16).margin_end(16).build();
    clamp.set_child(Some(child));
    sc.set_child(Some(&clamp));
    sc
}

pub fn run(prefix: Option<PathBuf>, source: Option<PathBuf>) -> i32 { run_with(prefix, source, None) }

/// Like `run`; `on_ready` gets the window and a function that jumps to a step (used by the screenshot example).
pub fn run_with(prefix: Option<PathBuf>, source: Option<PathBuf>, on_ready: Option<Box<dyn Fn(&adw::ApplicationWindow, &dyn Fn(usize))>>) -> i32 {
    let on_ready = Rc::new(on_ready);
    let app = adw::Application::builder().application_id(format!("{APP_ID}.Installer")).flags(gio::ApplicationFlags::NON_UNIQUE).build();
    let (prefix, source) = (prefix, source.or_else(|| std::env::current_exe().ok()).unwrap_or_default());
    app.connect_startup(|_| {
        if let Some(d) = gtk::gdk::Display::default() {
            let css = gtk::CssProvider::new();
            css.load_from_string(include_str!("style.css"));
            gtk::style_context_add_provider_for_display(&d, &css, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
        }
    });
    app.connect_activate(move |app| {
        let w = Inner::build(app, prefix.clone(), source.clone());
        w.win.present();
        if on_ready.is_some() {
            let (w2, hook) = (w.clone(), on_ready.clone());
            gtk::glib::timeout_add_local_once(std::time::Duration::from_millis(400), move || {
                if let Some(h) = hook.as_ref() { let w3 = w2.clone(); h(&w2.win, &move |n| w3.go(n)); }
            });
        }
        std::mem::forget(w);   // lives as long as the window; the process ends when it closes
    });
    i32::from(app.run_with_args::<&str>(&[]).get())
}

impl Inner {
    fn build(app: &adw::Application, prefix: Option<PathBuf>, source: PathBuf) -> Rc<Inner> {
        let win = adw::ApplicationWindow::builder().application(app).title("Install SSMConnect").default_width(720).default_height(640).build();
        let view = adw::ToolbarView::new();
        let header = adw::HeaderBar::new();
        let title = gtk::Label::new(None);
        header.set_title_widget(Some(&title));
        view.add_top_bar(&header);
        let stack = gtk::Stack::builder().transition_type(gtk::StackTransitionType::SlideLeftRight).vexpand(true).build();
        view.set_content(Some(&stack));
        let bar = gtk::Box::builder().spacing(8).margin_top(8).margin_bottom(8).margin_start(16).margin_end(16).build();
        let back = gtk::Button::with_label("Back");
        let spinner = gtk::Spinner::new();
        let status = gtk::Label::builder().xalign(0.0).hexpand(true).wrap(true).ellipsize(gtk::pango::EllipsizeMode::End).build();
        status.add_css_class("summary");
        let next = gtk::Button::with_label("Next");
        next.add_css_class("suggested-action");
        for w in [back.upcast_ref::<gtk::Widget>(), spinner.upcast_ref(), status.upcast_ref(), next.upcast_ref()] { bar.append(w); }
        view.add_bottom_bar(&bar);
        win.set_content(Some(&view));

        // 1. welcome
        let p1 = page(&format!("Welcome to SSMConnect {VERSION}"), "SSH and Remote Desktop to your AWS servers, with port 22 closed.\n\n\
            SSMConnect is free, open-source software (Apache License 2.0). It does not carry any third-party tools inside it. This installer first fetches \
            what SSMConnect needs from each tool's own source, and only then installs SSMConnect:\n\
            •  AWS CLI and Session Manager plugin: from Amazon, with Amazon's signature checked\n\
            •  OpenSSH and a Remote Desktop client: from your distribution's own package repositories");
        let lic = gtk::TextView::builder().editable(false).cursor_visible(false).wrap_mode(gtk::WrapMode::Word).monospace(true).build();
        lic.buffer().set_text(installer::LICENSE);
        let exp = gtk::Expander::builder().label("Licence").child(&gtk::ScrolledWindow::builder().child(&lic).min_content_height(220).build()).build();
        p1.append(&exp);

        // 2. requirements
        let p2 = page("Requirements", "These are fetched before SSMConnect is installed. Ticked items are installed for you.");
        let dep_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        p2.append(&dep_box);
        p2.append(&dim_label(&format!("Packages from your distribution are installed with {} and need your password.",
            installer::detect_package_manager().map(|p| p.name()).unwrap_or("your package manager"))));

        // 3. location
        let p3 = page("Where to install", "Choose who SSMConnect is installed for.");
        let user_radio = gtk::CheckButton::with_label(&format!("Just for me  ({}/bin)  - no password needed", installer::user_prefix().display()));
        let system_radio = gtk::CheckButton::with_label(&format!("For all users  ({}/bin)  - asks for your password", installer::system_prefix().display()));
        system_radio.set_group(Some(&user_radio));
        user_radio.set_active(true);
        p3.append(&user_radio);
        p3.append(&system_radio);
        let path_hint = dim_label("");
        p3.append(&path_hint);

        // 4. done
        let p4 = page("All done", "");
        let done_text = dim_label("");
        p4.append(&done_text);
        let open_btn = gtk::Button::builder().label("Open SSMConnect").halign(gtk::Align::Start).build();
        open_btn.add_css_class("suggested-action");
        p4.append(&open_btn);

        for (i, p) in [&p1, &p2, &p3, &p4].into_iter().enumerate() { stack.add_named(&scroller(p), Some(&i.to_string())); }
        let me = Rc::new(Inner { win, stack, title, back: back.clone(), next: next.clone(), status, spinner, dep_box, checks: RefCell::new(vec![]), user_radio, system_radio,
            path_hint, done_text, step: Cell::new(0), busy: Cell::new(false), prefix: RefCell::new(prefix), source, installed_to: RefCell::new(None) });
        let m = me.clone(); back.connect_clicked(move |_| m.go(m.step.get().saturating_sub(1)));
        let m = me.clone(); next.connect_clicked(move |_| m.on_next());
        let m = me.clone(); open_btn.connect_clicked(move |_| m.open_installed());
        me.go(0);
        me
    }

    fn say(&self, t: &str) { self.status.set_text(t); }

    fn busy(&self, on: bool, text: &str) {
        self.busy.set(on);
        self.spinner.set_spinning(on);
        self.next.set_sensitive(!on);
        self.back.set_sensitive(!on);
        self.say(text);
    }

    fn go(self: &Rc<Self>, n: usize) {
        self.step.set(n);
        self.stack.set_visible_child_name(&n.to_string());
        self.title.set_text(&format!("Step {} of {}: {}", n + 1, STEPS.len(), STEPS[n]));
        self.back.set_visible(n > 0 && n < 3);
        self.next.set_visible(n < 3);
        self.next.set_label(match n { 1 => "Install requirements", 2 => "Install SSMConnect", _ => "Next" });
        self.next.set_sensitive(true);
        self.say("");
        if n == 1 { self.show_deps(); }
        if n == 2 { self.show_location(); }
    }

    fn on_next(self: &Rc<Self>) {
        if self.busy.get() { return; }
        match self.step.get() { 0 => self.go(1), 1 => self.install_deps(), 2 => self.install_ssmconnect(), _ => {} }
    }

    fn show_deps(self: &Rc<Self>) {
        clear_box(&self.dep_box);
        self.checks.borrow_mut().clear();
        let g = adw::PreferencesGroup::new();
        for d in installer::dependencies() {
            let origin = match d.source { Source::Amazon => "from Amazon, signature checked", Source::Distribution => "from your distribution" };
            let sub = match &d.found {
                Some(p) => format!("Already installed · {p}"),
                None => format!("{} · {origin}", d.why),
            };
            let row = adw::ActionRow::builder().title(esc(&format!("{}{}", d.name, if d.required { "" } else { "  (optional)" }))).subtitle(esc(&sub)).build();
            let chk = gtk::CheckButton::builder().valign(gtk::Align::Center).active(d.found.is_none() && d.required).sensitive(d.found.is_none() && !d.required).build();
            if d.found.is_some() { chk.set_active(true); }
            row.add_prefix(&chk);
            row.set_activatable_widget(Some(&chk));
            g.add(&row);
            self.checks.borrow_mut().push((d, chk));
        }
        self.dep_box.append(&g);
        let nothing = self.checks.borrow().iter().all(|(d, c)| d.found.is_some() || !c.is_active());
        self.next.set_label(if nothing { "Next" } else { "Install requirements" });
    }

    fn install_deps(self: &Rc<Self>) {
        let chosen: Vec<&'static str> = self.checks.borrow().iter().filter(|(d, c)| d.found.is_none() && c.is_active()).map(|(d, _)| d.id).collect();
        if chosen.is_empty() { self.go(2); return; }
        self.busy(true, "Starting...");
        let tx = { let me = self.clone(); progress_channel(move |m| me.say(&m)) };
        let me = self.clone();
        in_background(move || installer::install_dependencies(&chosen, Elevation::Graphical, &|m| { let _ = tx.send_blocking(m); }), move |res| {
            me.busy(false, "");
            match res {
                Err(e) => { message(&me.win, "Could not install the requirements", &e); me.show_deps(); }
                Ok(()) => {
                    let left = installer::missing_required();
                    if left.is_empty() { me.go(2); } else {
                        message(&me.win, "Something is still missing", &format!("Still missing: {}.", left.iter().map(|d| d.name).collect::<Vec<_>>().join(", ")));
                        me.show_deps();
                    }
                }
            }
        });
    }

    fn show_location(&self) {
        if let Some(p) = self.prefix.borrow().as_ref() {
            // a location given on the command line is used as is
            self.user_radio.set_sensitive(false);
            self.system_radio.set_sensitive(false);
            self.path_hint.set_text(&format!("Installing to {} (set on the command line).", p.display()));
        } else {
            let bin = installer::user_prefix().join("bin");
            self.path_hint.set_text(&if installer::on_path(&bin) { String::new() } else { format!("{} is not on your PATH yet; the application menu entry works regardless.", bin.display()) });
        }
    }

    fn install_ssmconnect(self: &Rc<Self>) {
        let (prefix, system) = match self.prefix.borrow().clone() {
            Some(p) => (p, false),
            None => if self.system_radio.is_active() { (installer::system_prefix(), true) } else { (installer::user_prefix(), false) },
        };
        self.busy(true, "Installing SSMConnect...");
        let (source, tx) = (self.source.clone(), { let me = self.clone(); progress_channel(move |m| me.say(&m)) });
        let p2 = prefix.clone();
        let me = self.clone();
        in_background(move || {
            let writable = !system && std::fs::create_dir_all(&p2).is_ok();
            if writable { installer::install_files(&p2, std::path::Path::new(""), &source, &|m| { let _ = tx.send_blocking(m); }).map(|_| ()) }
            else { installer::install_files_elevated(&p2, &source, Elevation::Graphical, &|m| { let _ = tx.send_blocking(m); }) }
        }, move |res| {
            me.busy(false, "");
            match res {
                Err(e) => message(&me.win, "Could not install SSMConnect", &e),
                Ok(()) => {
                    let bin = prefix.join("bin");
                    me.done_text.set_text(&format!("SSMConnect {VERSION} is installed in {}.\n\nOpen it from your application menu{}. The first time, it guides you through adding your AWS account.\n\
                        To remove it later: ssmconnect uninstall --prefix {}",
                        prefix.display(), if installer::on_path(&bin) { " or by running `ssmconnect`".to_string() } else { format!(", or run {}", bin.join("ssmconnect").display()) }, prefix.display()));
                    *me.installed_to.borrow_mut() = Some(bin.join("ssmconnect"));
                    me.go(3);
                }
            }
        });
    }

    fn open_installed(&self) {
        if let Some(exe) = self.installed_to.borrow().as_ref() {
            let _ = std::process::Command::new(exe).spawn();
            self.win.close();
        }
    }
}

fn dim_label(text: &str) -> gtk::Label {
    let l = gtk::Label::builder().label(text).xalign(0.0).wrap(true).build();
    l.add_css_class("dim-label");
    l
}
