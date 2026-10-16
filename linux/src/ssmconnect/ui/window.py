"""The dashboard: header (account switcher, status pills, summary, organization chips), the action buttons and one card per server."""

from __future__ import annotations

import time

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
gi.require_version("Gdk", "4.0")
from gi.repository import Adw, GLib, Gdk, Gio, Gtk  # noqa: E402

from .. import aws_cli, aws_profiles, config_store, discovery, key_finder, launchers, sso, ssh_config
from ..models import AppConfig, TunnelConfig, TunnelStates, TunnelStatus
from .common import in_background, message, pill, plain_error, set_pill


class ServerRow(Adw.ActionRow):
    """One server: status dot, name, type tag, details, and the actions that make sense for its kind."""

    def __init__(self, win: "Dashboard", t: TunnelConfig, alias: str):
        super().__init__()
        self.win, self.t, self.alias = win, t, alias
        self.dot = Gtk.Box(valign=Gtk.Align.CENTER)
        self.dot.add_css_class("dot")
        self.add_prefix(self.dot)
        self.set_title(GLib.markup_escape_text(t.name))
        self.set_title_lines(1)
        self.set_subtitle_lines(2)

        if t.environment:
            tag = Gtk.Label(label="PROD" if t.is_prod else t.environment, valign=Gtk.Align.CENTER)
            tag.add_css_class("tag")
            if t.is_prod:
                tag.add_css_class("prod")
            self.add_suffix(tag)

        self.buttons: list[Gtk.Button] = []
        if t.is_rdp:
            self._action("computer-symbolic", "Remote Desktop", self._rdp)
            self._action("dialog-password-symbolic", "Get password", self._password)
        else:
            self._action("utilities-terminal-symbolic", "Terminal", self._terminal)
            self._action("folder-remote-symbolic", "Files", self._files)
        more = Gtk.MenuButton(icon_name="view-more-symbolic", valign=Gtk.Align.CENTER, tooltip_text="More")
        more.add_css_class("flat")
        menu = Gio.Menu()
        menu.append("Copy ssh command" if not t.is_rdp else "Copy local address", "row.copy")
        menu.append("Open Remote Desktop address" if t.is_rdp else "Copy sftp address", "row.copy-alt")
        more.set_menu_model(menu)
        self.add_suffix(more)
        group = Gio.SimpleActionGroup()
        for name, fn in (("copy", self._copy), ("copy-alt", self._copy_alt)):
            a = Gio.SimpleAction.new(name, None)
            a.connect("activate", lambda _a, _p, f=fn: f())
            group.add_action(a)
        self.insert_action_group("row", group)

    def _action(self, icon: str, label: str, fn) -> None:
        b = Gtk.Button(valign=Gtk.Align.CENTER)
        b.set_child(Adw.ButtonContent(icon_name=icon, label=label))
        b.add_css_class("flat")
        b.connect("clicked", lambda _b: fn())
        self.buttons.append(b)
        self.add_suffix(b)

    # Unavailable buttons are dimmed, not disabled, so their hover note still shows and a click explains why.
    def refresh(self, st: TunnelStatus | None, tools: dict) -> None:
        state = st.state if st else TunnelStates.DISABLED
        up = state == TunnelStates.CONNECTED
        for c in ("ok", "work", "err"):
            self.dot.remove_css_class(c)
        self.dot.add_css_class({TunnelStates.CONNECTED: "ok", TunnelStates.STARTING: "work", TunnelStates.RETRYING: "work",
                                TunnelStates.ERROR: "err"}.get(state, ""))
        what = "Windows" if self.t.is_rdp else (self.t.os_name or "Linux")
        status = {"Connected": "Connected", "Starting": "Connecting...", "Retrying": "Retrying", "Error": "Problem",
                  "Disabled": "Off"}.get(state, state)
        detail = f"{what} · local port {self.t.local_port} · {status}"
        if state in (TunnelStates.ERROR, TunnelStates.RETRYING, TunnelStates.STARTING) and st and st.message:
            detail += f"\n{st.message}"
        self.set_subtitle(GLib.markup_escape_text(detail))
        for b in self.buttons:
            ok, why = self._available(b, up, tools)
            b.set_tooltip_text(why)
            (b.remove_css_class if ok else b.add_css_class)("unavailable")

    def _available(self, b: Gtk.Button, up: bool, tools: dict) -> tuple[bool, str]:
        label = b.get_child().get_label()
        if not up:
            return False, "Waiting for the connection to open (see the status line)."
        if label == "Terminal":
            return (True, f"Open a terminal and run ssh {self.alias}") if tools["terminal"] and tools["ssh"] else \
                (False, "No terminal or ssh program found. Use More > Copy ssh command.")
        if label == "Files":
            return (True, "Browse files on the server in your file manager (SFTP)") if tools["files"] else (False, "No file manager found.")
        if label == "Remote Desktop":
            return (True, "Open Remote Desktop to this server") if tools["rdp"] else \
                (False, "Install Remmina or FreeRDP (xfreerdp) to use Remote Desktop.")
        if label == "Get password":
            return (True, "Decrypt the Administrator password with your key file and copy it (cleared after 60 s)") if self.t.key_file else \
                (False, "Pick this server's key file first (Add / change servers).")
        return True, ""

    def _guard(self, label: str, fn) -> None:
        b = next(b for b in self.buttons if b.get_child().get_label() == label)
        if b.has_css_class("unavailable"):
            self.win.toast(b.get_tooltip_text() or "Not available yet.")
            return
        try:
            fn()
        except (RuntimeError, OSError) as ex:
            self.win.toast(str(ex))

    def _terminal(self):
        self._guard("Terminal", lambda: launchers.open_terminal(self.alias))

    def _files(self):
        self._guard("Files", lambda: launchers.open_files(self.t.user_name, self.alias))

    def _rdp(self):
        self._guard("Remote Desktop", lambda: launchers.open_rdp(self.t))

    def _password(self):
        def go():
            key = self.t.key_file or ""

            def work():
                return aws_cli.get_windows_password(self.t, key)

            def done(pw, err):
                if err:
                    self.win.toast(plain_error(err))
                    return
                self.win.copy_secret(pw)
                self.win.toast("Password copied. It leaves the clipboard in 60 seconds.")
            self.win.toast("Getting the password...")
            in_background(work, done)
        self._guard("Get password", go)

    def _copy(self):
        self.win.copy(f"127.0.0.1:{self.t.local_port}" if self.t.is_rdp else ssh_config.ssh_command_text(self.alias))
        self.win.toast("Copied.")

    def _copy_alt(self):
        if self.t.is_rdp:
            self._rdp()
        else:
            self.win.copy(ssh_config.sftp_url(self.t.user_name, self.alias))
            self.win.toast("Copied.")


class Dashboard(Adw.ApplicationWindow):
    def __init__(self, app):
        super().__init__(application=app, title="SSMConnect", default_width=860, default_height=720)
        self.app = app
        self.cfg: AppConfig = AppConfig()
        self.rows: dict[str, ServerRow] = {}
        self._sig = ""
        self._clip_token = 0

        self.toasts = Adw.ToastOverlay()
        view = Adw.ToolbarView()
        view.add_top_bar(self._header())
        self.banner = Adw.Banner(revealed=False)
        self.banner.connect("button-clicked", lambda _b: self._banner_action())
        self._banner_cb = None
        view.add_top_bar(self.banner)

        self.stack = Gtk.Stack()
        self.empty = Adw.StatusPage(icon_name="network-server-symbolic", title="No servers yet",
                                    description="Add your AWS account and pick the servers you want to connect to.")
        b = Gtk.Button(label="Add servers", halign=Gtk.Align.CENTER)
        b.add_css_class("suggested-action")
        b.add_css_class("pill")
        b.set_action_name("app.add-servers")
        self.empty.set_child(b)
        self.stack.add_named(self.empty, "empty")
        self.stack.add_named(self._main(), "main")
        view.set_content(self.stack)
        self.toasts.set_child(view)
        self.set_content(self.toasts)

        self.connect("close-request", self._on_close)
        app.manager.on_change = lambda: GLib.idle_add(self.refresh_status)
        self.reload()
        GLib.timeout_add_seconds(3, self._tick)

    # ---- construction -------------------------------------------------------------------
    def _header(self) -> Adw.HeaderBar:
        bar = Adw.HeaderBar()
        self.switcher = Gtk.MenuButton(label="Account", tooltip_text="Switch AWS account")
        self.switcher.add_css_class("flat")
        self.switch_pop = Gtk.Popover()
        self.switch_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=2)
        self.switch_pop.set_child(self.switch_box)
        self.switcher.set_popover(self.switch_pop)
        bar.set_title_widget(self.switcher)

        menu = Gio.Menu()
        f = Gio.Menu()
        f.append("Add to application menu", "app.create-launcher")
        f.append("Start when I sign in", "app.autostart")
        f.append("Hide window", "win.hide")
        menu.append_section("File", f)
        a = Gio.Menu()
        a.append("Add AWS account…", "app.add-account")
        a.append("Manage accounts…", "app.manage-accounts")
        a.append("Sign in again", "app.sign-in-again")
        menu.append_section("AWS", a)
        s = Gio.Menu()
        s.append("Scan for new or changed servers", "app.scan")
        s.append("What changed since the last scan", "app.changes")
        menu.append_section("Servers", s)
        h = Gio.Menu()
        h.append("Check for updates", "app.check-updates")
        h.append("Open log file", "app.open-log")
        h.append("Clean up this user's settings…", "app.cleanup")
        h.append("About SSMConnect", "app.about")
        menu.append_section("Help", h)
        q = Gio.Menu()
        q.append("Quit and close all connections", "app.quit")
        menu.append_section(None, q)
        mb = Gtk.MenuButton(icon_name="open-menu-symbolic", menu_model=menu, primary=True, tooltip_text="Main menu")
        bar.pack_end(mb)
        hide = Gio.SimpleAction.new("hide", None)
        hide.connect("activate", lambda *_: self.set_visible(False))
        self.add_action(hide)
        return bar

    def _main(self) -> Gtk.Widget:
        scroll = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, vexpand=True)
        clamp = Adw.Clamp(maximum_size=820, margin_top=12, margin_bottom=24, margin_start=16, margin_end=16)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
        clamp.set_child(box)
        scroll.set_child(clamp)

        pills = Gtk.Box(spacing=8)
        self.p_conn, self.p_signin, self.p_tools = pill("Connections"), pill("AWS sign-in"), pill("AWS tools")
        for p in (self.p_conn, self.p_signin, self.p_tools):
            pills.append(p)
        box.append(pills)
        self.summary = Gtk.Label(xalign=0, wrap=True)
        self.summary.add_css_class("summary")
        box.append(self.summary)
        self.chips = Gtk.FlowBox(selection_mode=Gtk.SelectionMode.NONE, max_children_per_line=8, column_spacing=6, row_spacing=6,
                                 homogeneous=False)
        box.append(self.chips)

        bar = Gtk.Box(spacing=8)
        for label, icon, fn, css in (("Start", "media-playback-start-symbolic", self._start, "suggested-action"),
                                     ("Stop", "media-playback-stop-symbolic", self._stop, ""),
                                     ("Restart", "view-refresh-symbolic", self._restart, "")):
            b = Gtk.Button()
            b.set_child(Adw.ButtonContent(icon_name=icon, label=label))
            if css:
                b.add_css_class(css)
            b.connect("clicked", lambda _b, f=fn: f())
            bar.append(b)
        add = Gtk.Button(hexpand=True, halign=Gtk.Align.END)
        add.set_child(Adw.ButtonContent(icon_name="list-add-symbolic", label="Add / change servers"))
        add.set_action_name("app.add-servers")
        bar.append(add)
        box.append(bar)

        self.groups = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=18)
        box.append(self.groups)
        return scroll

    # ---- data ---------------------------------------------------------------------------
    def reload(self) -> None:
        self.cfg, warning = config_store.load_or_recover()
        if warning:
            self.show_banner(warning, "OK", None)
        self._build_switcher()
        self._build_groups()
        self.refresh_status()

    def _profiles(self) -> list[aws_profiles.ProfileInfo]:
        return aws_profiles.list_profiles()

    def _primaries(self) -> list[tuple[str, str]]:
        """(key, label) for each primary account that has saved servers, plus every other one known."""
        everyone = self._profiles()
        seen: dict[str, str] = {}
        by_name = {p.name: p for p in everyone}
        for t in self.cfg.tunnels:
            p = by_name.get(t.profile)
            if p:
                seen.setdefault(aws_profiles.primary_key(p), aws_profiles.primary_label(self.cfg, p, everyone))
        for p in everyone:
            if p.kind != aws_profiles.OTHER:
                seen.setdefault(aws_profiles.primary_key(p), aws_profiles.primary_label(self.cfg, p, everyone))
        return list(seen.items())

    def current_key(self) -> str:
        prim = self._primaries()
        keys = [k for k, _ in prim]
        if self.cfg.selected_account in keys:
            return self.cfg.selected_account
        # default to the account that has the most saved servers
        counts = {k: 0 for k in keys}
        by_name = {p.name: p for p in self._profiles()}
        for t in self.cfg.tunnels:
            if t.profile in by_name:
                counts[aws_profiles.primary_key(by_name[t.profile])] = counts.get(aws_profiles.primary_key(by_name[t.profile]), 0) + 1
        return max(counts, key=counts.get) if counts else ""

    def _build_switcher(self) -> None:
        while (c := self.switch_box.get_first_child()):
            self.switch_box.remove(c)
        prim, cur = self._primaries(), self.current_key()
        label = next((l for k, l in prim if k == cur), "No account")
        self.switcher.set_label(label)
        for k, l in sorted(prim, key=lambda kv: kv[0] != cur):
            b = Gtk.Button(label=l + ("  ✓" if k == cur else ""), halign=Gtk.Align.FILL)
            b.add_css_class("flat")
            b.connect("clicked", lambda _b, key=k: self._select_account(key))
            self.switch_box.append(b)
        add = Gtk.Button(label="Add AWS account…")
        add.add_css_class("flat")
        add.set_action_name("app.add-account")
        self.switch_box.append(Gtk.Separator())
        self.switch_box.append(add)

    def _select_account(self, key: str) -> None:
        self.switch_pop.popdown()
        self.cfg.selected_account = key
        config_store.save(self.cfg)
        self.reload()

    def _visible_tunnels(self) -> list[TunnelConfig]:
        key = self.current_key()
        by_name = {p.name: p for p in self._profiles()}
        return [t for t in self.cfg.tunnels if not key or (t.profile in by_name and aws_profiles.primary_key(by_name[t.profile]) == key)
                or t.profile not in by_name]

    def _build_groups(self) -> None:
        while (c := self.groups.get_first_child()):
            self.groups.remove(c)
        self.rows.clear()
        tunnels = self._visible_tunnels()
        self.stack.set_visible_child_name("main" if self.cfg.tunnels else "empty")
        aliases = ssh_config.aliases_for(self.cfg.tunnels)
        by_profile: dict[str, list[TunnelConfig]] = {}
        for t in tunnels:
            by_profile.setdefault(t.profile, []).append(t)
        multi = len(by_profile) > 1
        for profile, items in sorted(by_profile.items(), key=lambda kv: kv[0].lower()):
            g = Adw.PreferencesGroup()
            if multi:
                g.set_title(GLib.markup_escape_text(self.cfg.alias_or(profile, profile)))
            for t in sorted(items, key=lambda x: x.name.lower()):
                row = ServerRow(self, t, aliases.get(t.instance_id, ssh_config.host_alias(t.name, t.instance_id)))
                self.rows[t.instance_id + str(t.local_port)] = row
                g.add(row)
            self.groups.append(g)
        # organization chips (greyed when empty)
        while (c := self.chips.get_first_child()):
            self.chips.remove(c)
        everyone = self._profiles()
        cur = self.current_key()
        counts: dict[str, int] = {}
        for t in tunnels:
            counts[t.profile] = counts.get(t.profile, 0) + 1
        members = [p for p in everyone if aws_profiles.primary_key(p) == cur and p.kind == aws_profiles.SSO]
        if members:
            for p in aws_profiles.one_each(members, set(counts)):
                n = counts.get(p.name, 0)
                lbl = Gtk.Label(label=f"{self.cfg.alias_or(p.name, p.name)}  {n}")
                lbl.add_css_class("chip")
                if not n:
                    lbl.add_css_class("empty")
                self.chips.append(lbl)
        self.chips.set_visible(self.chips.get_first_child() is not None)

    def refresh_status(self) -> bool:
        status = {(s.instance_id, s.local_port): s for s in self.app.manager.snapshot()}
        tools = launchers.tools_summary()
        up = n = 0
        for row in self.rows.values():
            st = status.get((row.t.instance_id, row.t.local_port))
            row.refresh(st, tools)
            n += 1
            up += bool(st and st.state == TunnelStates.CONNECTED)
        everything = [s for s in status.values()]
        total_up = sum(1 for s in everything if s.state == TunnelStates.CONNECTED)
        problems = sum(1 for s in everything if s.state in (TunnelStates.ERROR, TunnelStates.RETRYING))
        kind = "" if not n else ("ok" if up == n else ("err" if problems else "warn"))
        set_pill(self.p_conn, f"{up} of {n} connected" if n else "No connections", kind)

        need = self._sign_in_state()
        set_pill(self.p_signin, need[0], need[1])
        have_tools = bool(aws_cli.find_aws() and aws_cli.find_plugin())
        set_pill(self.p_tools, "Tools ready" if have_tools else "Tools missing", "ok" if have_tools else "err")
        others = max(0, total_up - up)
        self.summary.set_text(
            f"{total_up} of {len(self.cfg.tunnels)} connection(s) open across all accounts."
            + (f" {others} belong to other accounts and keep running." if others else "")
            if self.cfg.tunnels else "Add your AWS account to get started.")
        return False

    def _sign_in_state(self) -> tuple[str, str]:
        by_name = {p.name: p for p in self._profiles()}
        cur = self.current_key()
        profs = [by_name[t.profile] for t in self._visible_tunnels() if t.profile in by_name]
        if not profs:
            return "Not signed in", "warn"
        if profs[0].kind != aws_profiles.SSO:
            return "Access keys", "ok"
        sessions = {p.sso_session for p in profs if p.sso_session}
        if any(sso.read_access_token(s) for s in sessions):
            return "Signed in", "ok"
        return "Sign-in needed", "err"

    def _tick(self) -> bool:
        self.refresh_status()
        return True

    # ---- actions ------------------------------------------------------------------------
    def _write_enabled(self, on: bool) -> None:
        cfg = config_store.load() or self.cfg
        for t in cfg.tunnels:
            t.enabled = on
        config_store.save(cfg)
        self.cfg = cfg
        self.app.manager.poke()

    def _start(self) -> None:
        if not self.cfg.tunnels:
            self.toast("Nothing to start yet. Use Add / change servers.")
            return
        self._write_enabled(True)
        self.app.ensure_manager()
        self.toast("Opening connections...")

    def _stop(self) -> None:
        self._write_enabled(False)
        self.toast("Connections closed.")

    def _restart(self) -> None:
        self._write_enabled(True)
        self.app.ensure_manager()
        self.app.manager.restart_all()
        self.toast("Restarting connections...")

    def _on_close(self, _w) -> bool:
        self.set_visible(False)       # connections keep running; Quit in the menu ends them
        self.app.note_hidden()
        return True

    # ---- helpers for rows ---------------------------------------------------------------
    def toast(self, text: str) -> None:
        self.toasts.add_toast(Adw.Toast(title=text, timeout=4))

    def copy(self, text: str) -> None:
        Gdk.Display.get_default().get_clipboard().set(text)

    def copy_secret(self, text: str) -> None:
        cb = Gdk.Display.get_default().get_clipboard()
        cb.set(text)
        self._clip_token += 1
        token = self._clip_token

        def clear() -> bool:
            if token == self._clip_token:     # only if nothing newer was copied meanwhile
                cb.set("")
            return False
        GLib.timeout_add_seconds(60, clear)

    def show_banner(self, text: str, button: str | None, cb) -> None:
        self.banner.set_title(text)
        self.banner.set_button_label(button or "")
        self._banner_cb = cb
        self.banner.set_revealed(True)

    def _banner_action(self) -> None:
        self.banner.set_revealed(False)
        if self._banner_cb:
            self._banner_cb()
