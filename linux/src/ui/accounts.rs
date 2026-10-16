//! Manage accounts: one collapsed line per sign-in, expandable to its organization accounts; rename and remove.

use super::app::App;
use super::common::{clear_box, confirm, esc};
use crate::{aws_profiles as ap, config_store, discovery, ssh_config};
use adw::prelude::*;
use std::collections::{BTreeMap, HashSet};
use std::rc::Rc;

pub fn show(app: &Rc<App>, parent: &adw::ApplicationWindow) {
    let dialog = adw::Dialog::builder().title("Manage accounts").content_width(620).content_height(560).build();
    let view = adw::ToolbarView::new();
    view.add_top_bar(&adw::HeaderBar::new());
    let sc = gtk::ScrolledWindow::builder().hscrollbar_policy(gtk::PolicyType::Never).vexpand(true).build();
    let body = gtk::Box::builder().orientation(gtk::Orientation::Vertical).spacing(12).margin_top(12).margin_bottom(12).margin_start(16).margin_end(16).build();
    sc.set_child(Some(&body));
    view.set_content(Some(&sc));
    dialog.set_child(Some(&view));
    build(app, &dialog, &body);
    dialog.present(Some(parent));
}

fn small_button(label: &str, destructive: bool) -> gtk::Button {
    let b = gtk::Button::builder().label(label).valign(gtk::Align::Center).build();
    b.add_css_class("flat");
    if destructive { b.add_css_class("destructive-action"); }
    b
}

fn build(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box) {
    clear_box(body);
    let cfg = config_store::load().unwrap_or_default();
    let everyone = ap::list_profiles();
    let mut groups: BTreeMap<String, Vec<ap::ProfileInfo>> = BTreeMap::new();
    for p in everyone.iter().filter(|p| p.kind != ap::Kind::Other) { groups.entry(ap::primary_key(p)).or_default().push(p.clone()); }
    if groups.is_empty() {
        body.append(&adw::StatusPage::builder().title("No accounts yet").icon_name("avatar-default-symbolic").description("Use AWS > Add AWS account to sign in.").build());
        return;
    }
    let mut counts: BTreeMap<String, usize> = BTreeMap::new();
    for t in &cfg.tunnels { *counts.entry(t.profile.clone()).or_default() += 1; }
    let preferred: HashSet<String> = counts.keys().cloned().collect();
    let g = adw::PreferencesGroup::new();
    for (key, group) in groups {
        let members = ap::one_each(&group, &preferred, None);
        let label = ap::primary_label(&cfg, &group[0], &everyone);
        let n: usize = group.iter().map(|p| counts.get(&p.name).copied().unwrap_or(0)).sum();
        let sub = format!("{} · {n} saved server(s)", if group[0].kind == ap::Kind::Sso { format!("{} AWS account(s)", members.len()) } else { "Access keys".into() });
        let exp = adw::ExpanderRow::builder().title(esc(&label)).subtitle(esc(&sub)).build();
        let (rm, rn) = (small_button("Remove", true), small_button("Rename", false));
        exp.add_suffix(&rm);
        exp.add_suffix(&rn);
        { let (app, dialog, body, k, l) = (app.clone(), dialog.clone(), body.clone(), key.clone(), label.clone()); rn.connect_clicked(move |_| rename(&app, &dialog, &body, &k, &l)); }
        { let (app, dialog, body, grp, l, k) = (app.clone(), dialog.clone(), body.clone(), group.clone(), label.clone(), key.clone());
          rm.connect_clicked(move |_| {
            let (a, d, b, grp, k) = (app.clone(), dialog.clone(), body.clone(), grp.clone(), k.clone());
            confirm(&dialog, &format!("Remove '{l}'?"), "This deletes SSMConnect's saved connections for this account and its login from ~/.aws. Your own ssh hosts and the servers in AWS are not touched.",
                    "Remove", true, move || do_remove(&a, &d, &b, &k, &grp));
          }); }
        if group[0].kind == ap::Kind::Sso {
            for m in &members {
                let name = cfg.alias_or(&m.name, &m.name);
                let row = adw::ActionRow::builder().title(esc(&name)).subtitle(esc(&format!("AWS account {} · {} saved server(s)", m.account_id.as_deref().unwrap_or("?"), counts.get(&m.name).copied().unwrap_or(0)))).build();
                let b = small_button("Rename", false);
                { let (app, dialog, body, k) = (app.clone(), dialog.clone(), body.clone(), m.name.clone()); let nm = name.clone(); b.connect_clicked(move |_| rename(&app, &dialog, &body, &k, &nm)); }
                row.add_suffix(&b);
                exp.add_row(&row);
            }
        }
        g.add(&exp);
    }
    body.append(&g);
}

fn rename(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box, key: &str, current: &str) {
    let d = adw::AlertDialog::new(Some("Friendly name"), Some("Shown in SSMConnect only. The AWS profile name does not change. Leave empty to go back to the original."));
    let entry = gtk::Entry::builder().text(current).activates_default(true).build();
    d.set_extra_child(Some(&entry));
    d.add_response("cancel", "Cancel");
    d.add_response("ok", "Save");
    d.set_response_appearance("ok", adw::ResponseAppearance::Suggested);
    d.set_default_response(Some("ok"));
    let (app, dialog2, body, key) = (app.clone(), dialog.clone(), body.clone(), key.to_string());
    d.connect_response(None, move |_, r| {
        if r != "ok" { return; }
        let mut cfg = config_store::load().unwrap_or_default();
        let name = entry.text().trim().to_string();
        if name.is_empty() { cfg.aliases.remove(&key); } else { cfg.aliases.insert(key.clone(), name); }
        let _ = config_store::save(&cfg);
        build(&app, &dialog2, &body);
        if let Some(w) = app.window.borrow().as_ref() { w.reload(); }
    });
    d.present(Some(dialog));
}

fn do_remove(app: &Rc<App>, dialog: &adw::Dialog, body: &gtk::Box, key: &str, group: &[ap::ProfileInfo]) {
    let mut cfg = config_store::load().unwrap_or_default();
    let names: HashSet<&str> = group.iter().map(|p| p.name.as_str()).collect();
    let sessions: HashSet<&str> = group.iter().filter_map(|p| p.sso_session.as_deref()).collect();
    cfg.tunnels.retain(|t| !names.contains(t.profile.as_str()));
    cfg.aliases.retain(|k, _| k != key && !names.contains(k.as_str()));
    if names.contains(cfg.profile.as_str()) { cfg.profile.clear(); }
    if cfg.selected_account == key { cfg.selected_account.clear(); }
    let _ = config_store::save(&cfg);
    for n in &names { let _ = ap::remove_profile(n); discovery::forget(n); }
    let remaining: HashSet<String> = ap::list_profiles().into_iter().filter_map(|p| p.sso_session).collect();
    for s in sessions.iter().filter(|s| !remaining.contains(**s)) { let _ = ap::remove_sso_session(s); }
    let _ = ssh_config::sync(&cfg);
    app.manager.poke();
    build(app, dialog, body);
    if let Some(w) = app.window.borrow().as_ref() { w.reload(); }
}
