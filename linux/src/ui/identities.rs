//! "Server identities": see, confirm and forget the SSH identity (host key) of each server, without touching `known_hosts` by hand.

use super::app::App;
use super::common::{clear_box, esc, in_background, message};
use crate::known_hosts::{self, HostKey, Trust};
use crate::models::{State, TunnelConfig};
use crate::{config_store, ssh_config};
use adw::prelude::*;
use std::rc::Rc;

/// What we know about one server's identity.
#[derive(Clone)]
struct Row { tunnel: TunnelConfig, alias: String, trust: Trust, saved: Vec<HostKey>, online: bool }

pub fn show(app: &Rc<App>, parent: &impl IsA<gtk::Widget>) {
    let dialog = adw::Dialog::builder().title("Server identities").content_width(680).content_height(600).build();
    let view = adw::ToolbarView::new();
    let header = adw::HeaderBar::new();
    let refresh = gtk::Button::builder().icon_name("view-refresh-symbolic").tooltip_text("Check again").build();
    header.pack_start(&refresh);
    view.add_top_bar(&header);
    let sc = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
    let body = gtk::Box::builder().orientation(gtk::Orientation::Vertical).spacing(14).margin_top(14).margin_bottom(18).margin_start(16).margin_end(16).build();
    sc.set_child(Some(&body));
    view.set_content(Some(&sc));
    dialog.set_child(Some(&view));
    let (a, d, b) = (app.clone(), dialog.clone(), body.clone());
    refresh.connect_clicked(move |_| load(&a, &d, &b));
    load(app, &dialog, &body);
    dialog.present(Some(parent));
}

fn load(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box) {
    clear_box(body);
    let spin = gtk::Spinner::builder().spinning(true).halign(gtk::Align::Center).width_request(32).height_request(32).margin_top(40).build();
    body.append(&spin);
    let cfg = config_store::load().unwrap_or_default();
    let status = app.manager.snapshot();
    let (a, d, b) = (app.clone(), dialog.clone(), body.clone());
    in_background(move || {
        let tunnels: Vec<TunnelConfig> = cfg.tunnels.iter().filter(|t| !t.is_rdp() && t.enabled).cloned().collect();
        std::thread::scope(|s| {
            let hs: Vec<_> = tunnels.iter().map(|t| { let (cfg, status) = (&cfg, &status); s.spawn(move || {
                let alias = ssh_config::host_key_alias(t, &cfg.tunnels);
                let online = status.iter().any(|st| st.instance_id == t.instance_id && st.local_port == t.local_port && st.state == State::Connected);
                let saved = known_hosts::stored(&alias);
                let scanned = if online { known_hosts::scan(t.local_port).ok() } else { None };
                let trust = if saved.is_empty() { Trust::Unknown } else { known_hosts::status(&alias, scanned.as_deref()) };
                Row { tunnel: t.clone(), alias, trust, saved, online }
            }) }).collect();
            hs.into_iter().filter_map(|h| h.join().ok()).collect::<Vec<Row>>()
        })
    }, move |rows| render(&a, &d, &b, rows));
}

fn render(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box, rows: Vec<Row>) {
    clear_box(body);
    let intro = gtk::Label::builder().xalign(0.0).wrap(true).label(
        "Every server has an identity, like a passport, that SSH checks each time you connect. This protects you from connecting to an impostor.\n\n\
         The first time, you confirm it here. If it ever changes, SSH refuses to connect until you have checked with your administrator why.").build();
    intro.add_css_class("dim-label");
    body.append(&intro);
    if rows.is_empty() {
        body.append(&adw::StatusPage::builder().icon_name("network-server-symbolic").title("No Linux servers yet").description("Add servers first (Add / change servers).").build());
        return;
    }
    let g = adw::PreferencesGroup::new();
    for r in rows {
        let (sub, icon) = match r.trust {
            Trust::Trusted => (format!("Confirmed. {}", r.saved.first().map(|k| format!("{} {}", k.nice_type(), k.fingerprint())).unwrap_or_default()), "emblem-ok-symbolic"),
            Trust::Unknown => (if r.online { "Not confirmed yet. SSMConnect can ask the server for its identity now.".to_string() } else { "Not confirmed yet. Open the connection first (its status must be green).".to_string() }, "dialog-information-symbolic"),
            Trust::Changed => ("The server now shows a different identity than the one you confirmed. This happens when a server is rebuilt, but it can also mean someone is intercepting the connection. Ask your administrator before replacing it.".to_string(), "dialog-warning-symbolic"),
        };
        let row = adw::ActionRow::builder().title(esc(&r.tunnel.name)).subtitle(esc(&sub)).subtitle_lines(4).build();
        row.add_prefix(&gtk::Image::from_icon_name(if icon == "emblem-ok-symbolic" { "object-select-symbolic" } else { icon }));
        match r.trust {
            Trust::Unknown => {
                let b = gtk::Button::builder().label("Check and confirm…").valign(gtk::Align::Center).sensitive(r.online).build();
                b.add_css_class("suggested-action");
                let (a, d, bd, r2) = (app.clone(), dialog.clone(), body.clone(), r.clone());
                b.connect_clicked(move |_| confirm_flow(&a, &d, &bd, &r2, false));
                row.add_suffix(&b);
            }
            Trust::Trusted => {
                let b = gtk::Button::builder().label("Forget").valign(gtk::Align::Center).tooltip_text("SSMConnect will ask you to confirm this server again next time").build();
                b.add_css_class("flat");
                let (a, d, bd, r2) = (app.clone(), dialog.clone(), body.clone(), r.clone());
                b.connect_clicked(move |_| match known_hosts::forget(&r2.alias) {
                    Ok(()) => { a.toast("Forgotten. You will be asked to confirm this server again."); a.refresh_trust(); load(&a, &d, &bd); }
                    Err(e) => message(&d, "Could not forget it", &e),
                });
                row.add_suffix(&b);
            }
            Trust::Changed => {
                let b = gtk::Button::builder().label("Review…").valign(gtk::Align::Center).sensitive(r.online).build();
                b.add_css_class("destructive-action");
                let (a, d, bd, r2) = (app.clone(), dialog.clone(), body.clone(), r.clone());
                b.connect_clicked(move |_| confirm_flow(&a, &d, &bd, &r2, true));
                row.add_suffix(&b);
            }
        }
        g.add(&row);
    }
    body.append(&g);
    let note = gtk::Label::builder().xalign(0.0).wrap(true).label("Confirmed identities are saved in ~/.ssh/known_hosts (a copy of the file is kept as known_hosts.ssmconnect.bak). Your own entries there are never changed.").build();
    note.add_css_class("dim-label");
    body.append(&note);
}

/// Asks the server for its identity, shows it, and saves it when the person agrees.
fn confirm_flow(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box, r: &Row, changed: bool) {
    app.toast("Asking the server for its identity...");
    let port = r.tunnel.local_port;
    let (a, d, b, r) = (app.clone(), dialog.clone(), body.clone(), r.clone());
    in_background(move || known_hosts::scan(port), move |res| {
        let keys = match res { Ok(k) => k, Err(e) => { message(&d, "Could not reach the server", &e); return; } };
        let lines: Vec<String> = keys.iter().map(|k| format!("{:<8} {}", k.nice_type(), k.fingerprint())).collect();
        let (heading, body_text, action) = if changed {
            (format!("{} has a new identity", r.tunnel.name),
             format!("The identity this server shows now is different from the one you confirmed earlier:\n\n{}\n\nOnly replace it if you know why it changed (for example the server was rebuilt), and ideally check the fingerprint with your administrator.", lines.join("\n")),
             "Replace with the new identity")
        } else {
            (format!("Trust {}?", r.tunnel.name),
             format!("SSMConnect asked the server for its identity. It shows:\n\n{}\n\nIf your administrator gave you a fingerprint, paste it below and SSMConnect will check it matches. If this is your own company's server, confirming it the first time is normal.", lines.join("\n")),
             "Trust this server")
        };
        let ad = adw::AlertDialog::new(Some(&heading), Some(&body_text));
        let entry = gtk::Entry::builder().placeholder_text("Fingerprint from your administrator (optional), e.g. SHA256:abc…").build();
        ad.set_extra_child(Some(&entry));
        ad.add_response("cancel", "Cancel");
        ad.add_response("trust", action);
        ad.set_response_appearance("trust", if changed { adw::ResponseAppearance::Destructive } else { adw::ResponseAppearance::Suggested });
        ad.set_default_response(Some("cancel"));
        let keys2 = keys.clone();
        let (a2, d2, b2, r2) = (a.clone(), d.clone(), b.clone(), r.clone());
        ad.connect_response(None, move |_, resp| {
            if resp != "trust" { return; }
            let typed = entry.text().trim().to_string();
            if !typed.is_empty() {
                let want = typed.trim_start_matches("SHA256:");
                if !keys2.iter().any(|k| k.fingerprint().trim_start_matches("SHA256:") == want) {
                    message(&d2, "The fingerprint does not match", "The fingerprint you typed is not the one this server shows. Do NOT trust this server: you may not be talking to the server you expect. Check with your administrator.");
                    return;
                }
            }
            let res = if changed { known_hosts::forget(&r2.alias).and_then(|()| known_hosts::trust(&r2.alias, &keys2)) } else { known_hosts::trust(&r2.alias, &keys2) };
            match res {
                Ok(()) => { a2.toast(&format!("{} is confirmed.", r2.tunnel.name)); a2.refresh_trust(); load(&a2, &d2, &b2); }
                Err(e) => message(&d2, "Could not save", &e),
            }
        });
        ad.present(Some(&d));
    });
}
