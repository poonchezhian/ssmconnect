"""Manage accounts: one collapsed line per sign-in, expandable to its organization accounts; rename and remove."""

from __future__ import annotations

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk  # noqa: E402

from .. import aws_profiles as ap, config_store, discovery, ssh_config
from .common import confirm


class AccountsDialog(Adw.Dialog):
    def __init__(self, app):
        super().__init__(title="Manage accounts", content_width=620, content_height=560)
        self.app = app
        view = Adw.ToolbarView()
        view.add_top_bar(Adw.HeaderBar())
        sc = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, vexpand=True)
        self.box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12, margin_top=12, margin_bottom=12, margin_start=16, margin_end=16)
        sc.set_child(self.box)
        view.set_content(sc)
        self.set_child(view)
        self.build()

    def build(self) -> None:
        while (c := self.box.get_first_child()):
            self.box.remove(c)
        cfg = config_store.load() or self.app.window.cfg
        everyone = ap.list_profiles()
        groups: dict[str, list[ap.ProfileInfo]] = {}
        for p in everyone:
            if p.kind != ap.OTHER:
                groups.setdefault(ap.primary_key(p), []).append(p)
        if not groups:
            self.box.append(Adw.StatusPage(title="No accounts yet", icon_name="avatar-default-symbolic",
                                           description="Use AWS > Add AWS account to sign in."))
            return
        counts: dict[str, int] = {}
        for t in cfg.tunnels:
            counts[t.profile] = counts.get(t.profile, 0) + 1
        g = Adw.PreferencesGroup()
        for key, group in groups.items():
            members = ap.one_each(group, set(counts))
            label = ap.primary_label(cfg, group[0], everyone)
            n = sum(counts.get(p.name, 0) for p in group)
            exp = Adw.ExpanderRow(title=GLib.markup_escape_text(label),
                                  subtitle=GLib.markup_escape_text(
                                      (f"{len(members)} AWS account(s)" if group[0].kind == ap.SSO else "Access keys") + f" · {n} saved server(s)"))
            for b in (self._btn("Remove", lambda _b, k=key, grp=group, lb=label: self._remove(k, grp, lb), destructive=True),
                      self._btn("Rename", lambda _b, k=key, lb=label: self._rename(k, lb))):
                exp.add_suffix(b)
            if group[0].kind == ap.SSO:
                for m in members:
                    row = Adw.ActionRow(title=GLib.markup_escape_text(cfg.alias_or(m.name, m.name)),
                                        subtitle=GLib.markup_escape_text(f"AWS account {m.account_id or '?'} · {counts.get(m.name, 0)} saved server(s)"))
                    row.add_suffix(self._btn("Rename", lambda _b, k=m.name, lb=cfg.alias_or(m.name, m.name): self._rename(k, lb)))
                    exp.add_row(row)
            g.add(exp)
        self.box.append(g)

    @staticmethod
    def _btn(label: str, fn, destructive: bool = False) -> Gtk.Button:
        b = Gtk.Button(label=label, valign=Gtk.Align.CENTER)
        b.add_css_class("flat")
        if destructive:
            b.add_css_class("destructive-action")
        b.connect("clicked", fn)
        return b

    def _rename(self, key: str, current: str) -> None:
        d = Adw.AlertDialog(heading="Friendly name", body="Shown in SSMConnect only. The AWS profile name does not change. Leave empty to go back to the original.")
        entry = Gtk.Entry(text=current, activates_default=True)
        d.set_extra_child(entry)
        d.add_response("cancel", "Cancel")
        d.add_response("ok", "Save")
        d.set_response_appearance("ok", Adw.ResponseAppearance.SUGGESTED)
        d.set_default_response("ok")

        def resp(_d, r):
            if r != "ok":
                return
            cfg = config_store.load() or self.app.window.cfg
            name = entry.get_text().strip()
            if name:
                cfg.aliases[key] = name
            else:
                cfg.aliases.pop(key, None)
            config_store.save(cfg)
            self.build()
            self.app.window.reload()
        d.connect("response", resp)
        d.present(self)

    def _remove(self, key: str, group: list[ap.ProfileInfo], label: str) -> None:
        confirm(self, f"Remove '{label}'?",
                "This deletes SSMConnect's saved connections for this account and its login from ~/.aws. Your own ssh hosts and the servers "
                "in AWS are not touched.", "Remove", lambda: self._do_remove(key, group), destructive=True)

    def _do_remove(self, key: str, group: list[ap.ProfileInfo]) -> None:
        cfg = config_store.load() or self.app.window.cfg
        names = {p.name for p in group}
        sessions = {p.sso_session for p in group if p.sso_session}
        cfg.tunnels = [t for t in cfg.tunnels if t.profile not in names]
        cfg.aliases = {k: v for k, v in cfg.aliases.items() if k != key and k not in names}
        if cfg.profile in names:
            cfg.profile = ""
        if cfg.selected_account == key:
            cfg.selected_account = ""
        config_store.save(cfg)
        for n in names:
            ap.remove_profile(n)
            discovery.forget(n)
        remaining = {p.sso_session for p in ap.list_profiles() if p.sso_session}
        for s in sessions - remaining:
            ap.remove_sso_session(s)
        ssh_config.apply(cfg.tunnels)
        self.app.manager.poke()
        self.build()
        self.app.window.reload()
