//! Small helpers shared by the windows.

use adw::prelude::*;
use gtk::glib;

/// Runs `work` on a thread; `done` is called with its result on the GTK main loop.
pub fn in_background<T: Send + 'static>(work: impl FnOnce() -> T + Send + 'static, done: impl FnOnce(T) + 'static) {
    let (tx, rx) = async_channel::bounded(1);
    std::thread::spawn(move || { let _ = tx.send_blocking(work()); });
    glib::spawn_future_local(async move { if let Ok(v) = rx.recv().await { done(v); } });
}

/// A progress line sender for background work: send plain messages, they appear on the GTK main loop through `on_msg`.
pub fn progress_channel(on_msg: impl Fn(String) + 'static) -> async_channel::Sender<String> {
    let (tx, rx) = async_channel::unbounded::<String>();
    glib::spawn_future_local(async move { while let Ok(m) = rx.recv().await { on_msg(m); } });
    tx
}

/// A status pill across the dashboard: a dot and a short text, as in the Windows edition.
pub fn pill(text: &str) -> gtk::Label {
    let l = gtk::Label::builder().label(format!("●  {text}")).hexpand(true).build();
    l.add_css_class("spill");
    l
}

pub fn set_pill(l: &gtk::Label, text: &str, kind: &str) {
    l.set_text(&format!("●  {text}"));
    for k in ["ok", "warn", "err"] { l.remove_css_class(k); }
    if !kind.is_empty() { l.add_css_class(kind); }
}

pub fn clear_box(b: &impl IsA<gtk::Widget>) {
    while let Some(c) = b.first_child() { c.unparent(); }
}

pub fn confirm(parent: &impl IsA<gtk::Widget>, heading: &str, body: &str, action: &str, destructive: bool, on_yes: impl Fn() + 'static) {
    let d = adw::AlertDialog::new(Some(heading), Some(body));
    d.add_response("cancel", "Cancel");
    d.add_response("yes", action);
    d.set_response_appearance("yes", if destructive { adw::ResponseAppearance::Destructive } else { adw::ResponseAppearance::Suggested });
    d.set_default_response(Some("cancel"));
    d.connect_response(None, move |_, r| { if r == "yes" { on_yes(); } });
    d.present(Some(parent));
}

pub fn message(parent: &impl IsA<gtk::Widget>, heading: &str, body: &str) {
    let d = adw::AlertDialog::new(Some(heading), Some(body));
    d.add_response("ok", "OK");
    d.present(Some(parent));
}

pub fn esc(s: &str) -> String { glib::markup_escape_text(s).to_string() }

/// The round operating-system badge on a server card: a blue tile of four squares for Windows, a lilac prompt (>_) for Linux.
pub fn os_avatar(windows: bool) -> gtk::DrawingArea {
    let a = gtk::DrawingArea::builder().content_width(38).content_height(38).valign(gtk::Align::Start).build();
    a.set_draw_func(move |_, cr, w, h| {
        let (bg, fg) = if windows { ((214., 232., 252.), (10., 70., 150.)) } else { ((236., 226., 250.), (88., 40., 150.)) };
        let (w, h) = (f64::from(w), f64::from(h));
        let (cx, cy) = (w / 2., h / 2.);
        cr.set_source_rgb(bg.0 / 255., bg.1 / 255., bg.2 / 255.);
        cr.arc(cx, cy, w.min(h) / 2., 0., std::f64::consts::TAU);
        let _ = cr.fill();
        cr.set_source_rgb(fg.0 / 255., fg.1 / 255., fg.2 / 255.);
        if windows {
            let (q, gap) = (7., 2.);
            for (dx, dy) in [(-q - gap / 2., -q - gap / 2.), (gap / 2., -q - gap / 2.), (-q - gap / 2., gap / 2.), (gap / 2., gap / 2.)] { cr.rectangle(cx + dx, cy + dy, q, q); }
            let _ = cr.fill();
        } else {
            cr.set_line_width(2.2);
            cr.set_line_cap(gtk::cairo::LineCap::Round);
            cr.set_line_join(gtk::cairo::LineJoin::Round);
            cr.move_to(cx - 8., cy - 6.); cr.line_to(cx - 2., cy); cr.line_to(cx - 8., cy + 6.);
            let _ = cr.stroke();
            cr.move_to(cx + 1., cy + 6.5); cr.line_to(cx + 8., cy + 6.5);
            let _ = cr.stroke();
        }
    });
    a
}

/// Gives a window's content the shared look (see style.css), following the system's light or dark preference.
pub fn ssm_style(root: &impl IsA<gtk::Widget>) {
    root.add_css_class("ssm");
    let sm = adw::StyleManager::default();
    let r = root.clone().upcast::<gtk::Widget>();
    let apply = move |sm: &adw::StyleManager| if sm.is_dark() { r.add_css_class("dark") } else { r.remove_css_class("dark") };
    apply(&sm);
    sm.connect_dark_notify(apply);
}

/// The teal band at the top of a window: logo, a title and a line below it, and (for the setup wizard) the step dots on the right.
pub struct Band {
    pub root: gtk::Box,
    title: gtk::Label,
    sub: gtk::Label,
    dots: gtk::DrawingArea,
    step: std::rc::Rc<std::cell::Cell<usize>>,
}

impl Band {
    pub fn new(steps: usize) -> Band {
        let root = gtk::Box::new(gtk::Orientation::Horizontal, 14);
        root.add_css_class("ssm-band");
        let logo = gtk::Image::from_icon_name(crate::APP_ID);
        logo.set_pixel_size(46);
        root.append(&logo);
        let titles = gtk::Box::new(gtk::Orientation::Vertical, 2);
        titles.set_valign(gtk::Align::Center);
        titles.set_hexpand(true);
        let title = gtk::Label::builder().xalign(0.0).build();
        title.add_css_class("band-title");
        let sub = gtk::Label::builder().xalign(0.0).wrap(true).build();
        sub.add_css_class("band-sub");
        titles.append(&title);
        titles.append(&sub);
        root.append(&titles);
        let step = std::rc::Rc::new(std::cell::Cell::new(0usize));
        let dots = gtk::DrawingArea::builder().content_width(34 * steps.max(1) as i32).content_height(24).valign(gtk::Align::Center).visible(steps > 0).build();
        let st = step.clone();
        dots.set_draw_func(move |_, cr, w, h| {
            let (gap, r, cy) = (34.0, 7.0, f64::from(h) / 2.0);
            let x0 = f64::from(w) - gap * (steps as f64 - 1.0) - r - 4.0;
            for i in 0..steps {
                let cx = x0 + gap * i as f64;
                if i + 1 < steps {
                    cr.set_source_rgba(1.0, 1.0, 1.0, if i < st.get() { 0.67 } else { 0.27 });
                    cr.set_line_width(2.0);
                    cr.move_to(cx + r, cy); cr.line_to(cx + gap - r, cy);
                    let _ = cr.stroke();
                }
                if i < st.get() {
                    cr.set_source_rgb(110. / 255., 231. / 255., 183. / 255.);
                    cr.arc(cx, cy, r, 0., std::f64::consts::TAU);
                    let _ = cr.fill();
                } else if i == st.get() {
                    cr.set_source_rgb(1.0, 1.0, 1.0);
                    cr.arc(cx, cy, r + 2.0, 0., std::f64::consts::TAU);
                    let _ = cr.fill();
                    cr.set_source_rgb(13. / 255., 148. / 255., 136. / 255.);
                    cr.arc(cx, cy, 3.5, 0., std::f64::consts::TAU);
                    let _ = cr.fill();
                } else {
                    cr.set_source_rgba(1.0, 1.0, 1.0, 0.47);
                    cr.set_line_width(2.0);
                    cr.arc(cx, cy, r, 0., std::f64::consts::TAU);
                    let _ = cr.stroke();
                }
            }
        });
        root.append(&dots);
        Band { root, title, sub, dots, step }
    }

    pub fn set(&self, title: &str, sub: &str, step: usize) {
        self.title.set_text(title);
        self.sub.set_text(sub);
        self.step.set(step);
        self.dots.queue_draw();
    }
}

/// The shared frame of the small dialogs (accounts, server identities): teal title bar, band with a title and a line below, a bottom bar with a *Done* button.
pub fn dialog_frame(view: &adw::ToolbarView, dialog: &adw::Dialog, title: &str, sub: &str, header_extra: Option<&gtk::Button>) {
    ssm_style(view);
    let header = adw::HeaderBar::new();
    header.add_css_class("ssm-top");
    header.set_show_title(false);
    if let Some(b) = header_extra { header.pack_start(b); }
    view.add_top_bar(&header);
    let band = Band::new(0);
    band.set(title, sub, 0);
    view.add_top_bar(&band.root);
    let bar = gtk::Box::new(gtk::Orientation::Horizontal, 10);
    bar.add_css_class("ssm-bar");
    let done = gtk::Button::builder().label("Done").hexpand(true).halign(gtk::Align::End).width_request(110).build();
    done.add_css_class("main");
    let d = dialog.clone();
    done.connect_clicked(move |_| { d.close(); });
    bar.append(&done);
    view.add_bottom_bar(&bar);
}
