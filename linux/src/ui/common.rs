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

pub fn pill(text: &str) -> gtk::Label {
    let l = gtk::Label::new(Some(text));
    l.add_css_class("pill");
    l
}

pub fn set_pill(l: &gtk::Label, text: &str, kind: &str) {
    l.set_text(text);
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
