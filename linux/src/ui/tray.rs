//! The tray icon (StatusNotifierItem): shows how many connections are open and offers Open / Start / Stop / Restart / Quit.
//! KDE, XFCE, Cinnamon, MATE and GNOME (with the AppIndicator extension) show it; where there is no tray the app simply carries on without one.

use super::app::App;
use crate::models::State;
use crate::{applog, APP_ID};
use adw::prelude::*;
use gtk::glib;
use ksni::blocking::TrayMethods;
use std::rc::Rc;
use std::time::Duration;

#[derive(Clone, Copy, Debug)]
enum Cmd { Open, Start, Stop, Restart, Preferences, Quit }

struct Tray {
    tx: async_channel::Sender<Cmd>,
    up: usize,
    total: usize,
    problems: usize,
    running: bool,
}

impl Tray {
    fn summary(&self) -> String {
        if self.total == 0 { "No connections yet".into() }
        else if !self.running { "Connections are stopped".into() }
        else { format!("{} of {} connected", self.up, self.total) }
    }
}

impl ksni::Tray for Tray {
    fn id(&self) -> String { APP_ID.into() }
    fn title(&self) -> String { "SSMConnect".into() }
    fn icon_name(&self) -> String { APP_ID.into() }
    fn category(&self) -> ksni::Category { ksni::Category::ApplicationStatus }
    fn status(&self) -> ksni::Status { if self.problems > 0 { ksni::Status::NeedsAttention } else { ksni::Status::Active } }
    fn attention_icon_name(&self) -> String { APP_ID.into() }
    fn tool_tip(&self) -> ksni::ToolTip {
        ksni::ToolTip { icon_name: APP_ID.into(), icon_pixmap: Vec::new(), title: "SSMConnect".into(), description: self.summary() }
    }
    fn activate(&mut self, _x: i32, _y: i32) { let _ = self.tx.try_send(Cmd::Open); }

    fn menu(&self) -> Vec<ksni::MenuItem<Self>> {
        use ksni::menu::{MenuItem, StandardItem};
        let item = |label: &str, icon: &str, enabled: bool, cmd: Cmd| -> MenuItem<Self> {
            StandardItem { label: label.into(), icon_name: icon.into(), enabled, activate: Box::new(move |t: &mut Self| { let _ = t.tx.try_send(cmd); }), ..Default::default() }.into()
        };
        let has = self.total > 0;
        vec![
            StandardItem { label: self.summary(), enabled: false, ..Default::default() }.into(),
            MenuItem::Separator,
            item("Open SSMConnect", "window-new-symbolic", true, Cmd::Open),
            MenuItem::Separator,
            item("Start connections", "media-playback-start-symbolic", has && !self.running, Cmd::Start),
            item("Stop connections", "media-playback-stop-symbolic", has && self.running, Cmd::Stop),
            item("Restart connections", "view-refresh-symbolic", has && self.running, Cmd::Restart),
            MenuItem::Separator,
            item("Preferences…", "preferences-system-symbolic", true, Cmd::Preferences),
            item("Quit (closes all connections)", "application-exit-symbolic", true, Cmd::Quit),
        ]
    }
}

/// Puts the icon in the notification area and keeps it up to date. Does nothing if the desktop has no tray.
pub fn start(app: &Rc<App>) {
    let (tx, rx) = async_channel::unbounded();
    let handle = match (Tray { tx, up: 0, total: 0, problems: 0, running: false }).spawn() {
        Ok(h) => h,
        Err(e) => { applog::info(&format!("No tray icon on this desktop ({e}); carrying on without one.")); return; }
    };

    let a = app.clone();
    glib::spawn_future_local(async move {
        while let Ok(cmd) = rx.recv().await {
            match cmd {
                Cmd::Open => { a.show_window(); }
                Cmd::Start => a.ensure_manager(),
                Cmd::Stop => { a.manager.stop(); }
                Cmd::Restart => { a.ensure_manager(); a.manager.restart_all(); }
                Cmd::Preferences => { let _ = a.show_window(); a.gtk.activate_action("preferences", None); }
                Cmd::Quit => a.gtk.quit(),
            }
        }
    });

    let a = app.clone();
    glib::timeout_add_local(Duration::from_secs(2), move || {
        let status = a.manager.snapshot();
        let (running, total) = (a.manager.running(), crate::config_store::load().map_or(0, |c| c.tunnels.len()));
        let up = status.iter().filter(|s| s.state == State::Connected).count();
        let problems = status.iter().filter(|s| matches!(s.state, State::Error | State::Retrying)).count();
        if handle.is_closed() { return glib::ControlFlow::Break; }
        handle.update(|t| { t.up = up; t.total = total; t.problems = problems; t.running = running; });
        glib::ControlFlow::Continue
    });
}
