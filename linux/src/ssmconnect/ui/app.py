"""The Adw.Application: single instance, keeps connections running while the window is hidden, daily background scan and update check."""

from __future__ import annotations

import sys
from pathlib import Path

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
gi.require_version("Gdk", "4.0")
from gi.repository import Adw, Gdk, Gio, GLib, Gtk  # noqa: E402

from .. import APP_ID, __version__, applog, aws_cli, aws_profiles, config_store, desktop, discovery, launchers, paths, sso, update_checker
from ..tunnel_manager import TunnelManager
from .common import confirm, in_background, message
from .window import Dashboard

_HERE = Path(__file__).resolve().parent


def _icon_dirs() -> list[Path]:
    return [d for d in (_HERE.parents[2] / "data" / "icons", Path("/usr/share/icons"), Path("/usr/local/share/icons")) if d.is_dir()]


class SSMConnectApp(Adw.Application):
    def __init__(self, background: bool = False):
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.DEFAULT_FLAGS)
        self.background = background
        self.manager = TunnelManager()
        self.window: Dashboard | None = None
        self._told_hidden = False
        self.set_resource_base_path(None)

    # ---- lifecycle ------------------------------------------------------------------------
    def do_startup(self) -> None:
        Adw.Application.do_startup(self)
        Gtk.Window.set_default_icon_name(APP_ID)
        theme = Gtk.IconTheme.get_for_display(Gdk.Display.get_default())
        for d in _icon_dirs():
            theme.add_search_path(str(d))
        css = Gtk.CssProvider()
        css.load_from_path(str(_HERE / "style.css"))
        Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        self._actions()
        cfg = config_store.load()
        if cfg and cfg.tunnels and cfg.autostart_connections:
            self.manager.start()
        self.hold()                       # the app lives until Quit, even with the window hidden
        GLib.timeout_add_seconds(8, self._background_jobs)

    def do_activate(self) -> None:
        if self.background and not self.window and not getattr(self, "_shown_once", False):
            self._shown_once = True      # started by 'start when I sign in': stay hidden until asked
            return
        self.show_window()

    def show_window(self) -> Dashboard:
        if not self.window:
            self.window = Dashboard(self)
            cfg = config_store.load()
            if cfg is not None and not cfg.tunnels:
                GLib.idle_add(lambda: (self.activate_action("add-servers", None), False)[1])
        self.window.set_visible(True)
        self.window.present()
        return self.window

    def ensure_manager(self) -> None:
        self.manager.start()
        self.manager.poke()

    def note_hidden(self) -> None:
        if not self._told_hidden:
            self._told_hidden = True
            n = Gio.Notification.new("SSMConnect is still running")
            n.set_body("Your connections stay open. Open SSMConnect again from the application menu, or choose Quit to close them.")
            self.send_notification("hidden", n)

    def do_shutdown(self) -> None:
        self.manager.stop()
        Adw.Application.do_shutdown(self)

    # ---- actions --------------------------------------------------------------------------
    def _add(self, name: str, fn, state=None) -> None:
        a = Gio.SimpleAction.new(name, None) if state is None else Gio.SimpleAction.new_stateful(name, None, GLib.Variant.new_boolean(state))
        a.connect("activate", lambda act, p: fn(act))
        self.add_action(a)

    def _actions(self) -> None:
        self._add("quit", lambda a: self.quit())
        self._add("add-servers", lambda a: self.open_wizard(False))
        self._add("add-account", lambda a: self.open_wizard(True))
        self._add("manage-accounts", lambda a: self.open_accounts())
        self._add("sign-in-again", lambda a: self.sign_in_again())
        self._add("scan", lambda a: self.scan_now())
        self._add("changes", lambda a: self.show_changes())
        self._add("check-updates", lambda a: self.check_updates(True))
        self._add("open-log", lambda a: launchers.open_file(str(paths.log_path())) if paths.log_path().exists()
                  else self._toast("There is no log file yet."))
        self._add("cleanup", lambda a: self.cleanup())
        self._add("about", lambda a: self.about())
        self._add("create-launcher", lambda a: self._toast(f"Added: {desktop.create_launcher()}"))

        def toggle(act):
            on = not act.get_state().get_boolean()
            desktop.set_autostart(on)
            act.set_state(GLib.Variant.new_boolean(on))
            self._toast("SSMConnect will start when you sign in." if on else "SSMConnect will not start when you sign in.")
        self._add("autostart", toggle, desktop.autostart_enabled())
        self.set_accels_for_action("app.quit", ["<Primary>q"])

    def _toast(self, text: str) -> None:
        if self.window:
            self.window.toast(text)

    def open_wizard(self, add_account: bool) -> None:
        win = self.show_window()
        message(win, "Setup arrives in the next release",
                "The guided setup is not part of version 0.0.2 yet. Meanwhile you can add accounts with `aws configure sso` and servers by "
                "editing ~/.config/ssmconnect/config.json. See the README.")

    def open_accounts(self) -> None:
        message(self.show_window(), "Coming soon", "Account management arrives in the next release.")

    def sign_in_again(self) -> None:
        win = self.show_window()
        cfg = config_store.load() or win.cfg
        profs = {p.name: p for p in aws_profiles.list_profiles()}
        todo = [profs[t.profile] for t in win._visible_tunnels() if t.profile in profs and profs[t.profile].kind == aws_profiles.SSO]
        if not todo:
            self._toast("This account does not use a company sign-in.")
            return
        p = todo[0]
        self._toast("Your browser opens to sign in...")

        def done(r, err):
            if err or not r.ok:
                self._toast(aws_cli.friendly_error(r.error if r else str(err)))
            else:
                self._toast("Signed in.")
                self.manager.poke()
        in_background(lambda: aws_cli.sso_login(p.name), done)

    def scan_now(self) -> None:
        cfg = config_store.load() or AppConfigFallback()
        self._toast("Scanning your accounts...")

        def done(changes, err):
            if err:
                self._toast(f"Scan failed: {err}")
            elif changes:
                self._toast(f"{len(changes)} change(s) found. See Servers > What changed.")
            else:
                self._toast("Scan finished. Nothing changed.")
        in_background(lambda: discovery.Monitor.run(cfg, force=True), done)

    def show_changes(self) -> None:
        changes = discovery.take_pending()
        message(self.show_window(), "What changed since the last scan",
                "\n".join("• " + c for c in changes) if changes else "Nothing has changed since you last looked.")

    def check_updates(self, manual: bool) -> None:
        def done(info, err):
            if info:
                if manual or update_checker.should_notify(info.version):
                    if manual:
                        confirm(self.show_window(), f"SSMConnect {info.version} is available",
                                f"You have {__version__}.\n\n{info.notes[:600]}\n\nOpen the download page?", "Open page",
                                lambda: launchers.open_url(info.page_url))
                    else:
                        n = Gio.Notification.new(f"SSMConnect {info.version} is available")
                        n.set_body("Open the Releases page to update.")
                        self.send_notification("update", n)
            elif manual:
                self._toast(f"You have the latest version ({__version__}).")
        in_background(lambda: update_checker.check(force=manual), done)

    def cleanup(self) -> None:
        win = self.show_window()
        confirm(win, "Remove SSMConnect's settings for this user?",
                "This disconnects SSMConnect from ssh, removes 'start when I sign in', the menu entry, your saved servers and the log. "
                "Your own ssh hosts, AWS profiles and everything in AWS stay untouched. SSMConnect then closes.\n\n"
                "To remove the program itself, use your package manager (for example: sudo dnf remove ssmconnect).",
                "Remove and quit", lambda: (desktop.cleanup_user(), self.quit()), destructive=True)

    def about(self) -> None:
        d = Adw.AboutDialog(application_name="SSMConnect", application_icon=APP_ID, version=__version__,
                            developer_name="Poonchezhian Periasamy", license_type=Gtk.License.APACHE_2_0,
                            website="https://github.com/poonchezhian/ssmconnect",
                            issue_url="https://github.com/poonchezhian/ssmconnect/issues",
                            comments="SSH and Remote Desktop to AWS servers over Session Manager, with port 22 closed.")
        d.present(self.show_window())

    # ---- background work ---------------------------------------------------------------------
    def _background_jobs(self) -> bool:
        cfg = config_store.load()
        if cfg and cfg.tunnels:
            def done(changes, err):
                if changes:
                    n = Gio.Notification.new("SSMConnect found changes in your servers")
                    n.set_body(f"{len(changes)} change(s). Open Servers > What changed since the last scan.")
                    self.send_notification("scan", n)
            in_background(lambda: discovery.Monitor.run(cfg, force=False), done)
        self.check_updates(False)
        return False


def AppConfigFallback():
    from ..models import AppConfig
    return AppConfig()


def run_gui(background: bool = False) -> int:
    app = SSMConnectApp(background=background)
    return app.run([sys.argv[0]])
