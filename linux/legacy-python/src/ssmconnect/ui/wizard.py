"""The five-step setup: Requirements, AWS account, Servers, SSH keys, Save."""

from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk  # noqa: E402

from .. import (aws_cli, aws_profiles as ap, config_store, discovery, key_finder, launchers, netcheck, prereq_installer,
                sso, sso_discovery, ssh_config)
from ..validator import is_valid_login, is_valid_region
from ..models import AppConfig, DiscoveredServer, TunnelConfig
from .common import in_background, message, plain_error

STEPS = ["Requirements", "AWS account", "Servers", "SSH keys", "Save"]
FIRST_PORT = 2201


class Wizard(Adw.Window):
    def __init__(self, app, parent: Gtk.Window | None, add_account: bool = False):
        super().__init__(title="Set up SSMConnect", default_width=760, default_height=700, modal=True, transient_for=parent)
        self.app, self.add_account = app, add_account
        self.cfg: AppConfig = config_store.load() or AppConfig()
        self.step = 0
        self.primary_key = ""
        self.members: list[ap.ProfileInfo] = []
        self.region = ""
        self.found: list[tuple[str, DiscoveredServer]] = []     # (profile, server)
        self.checks: dict[tuple[str, str], Gtk.CheckButton] = {}
        self.skipped: list[str] = []
        self.keys: list[key_finder.KeyFile] = []
        self.key_rows: list[dict] = []
        self.busy = False

        view = Adw.ToolbarView()
        header = Adw.HeaderBar()
        self.step_label = Gtk.Label()
        header.set_title_widget(self.step_label)
        view.add_top_bar(header)
        self.toasts = Adw.ToastOverlay()
        self.stack = Gtk.Stack(transition_type=Gtk.StackTransitionType.SLIDE_LEFT_RIGHT, vexpand=True)
        self.toasts.set_child(self.stack)
        view.set_content(self.toasts)
        bar = Gtk.Box(spacing=8, margin_top=8, margin_bottom=8, margin_start=16, margin_end=16)
        self.back = Gtk.Button(label="Back")
        self.back.connect("clicked", lambda _b: self.go(self.step - 1))
        self.status = Gtk.Label(xalign=0, hexpand=True, wrap=True, ellipsize=3)
        self.status.add_css_class("summary")
        self.next = Gtk.Button(label="Next")
        self.next.add_css_class("suggested-action")
        self.next.connect("clicked", lambda _b: self.on_next())
        for w in (self.back, self.status, self.next):
            bar.append(w)
        view.add_bottom_bar(bar)
        self.set_content(view)

        self.pages = [self._page_requirements(), self._page_account(), self._page_servers(), self._page_keys(), self._page_save()]
        for i, p in enumerate(self.pages):
            self.stack.add_named(p, str(i))
        self.go(0)

    # ---- plumbing ------------------------------------------------------------------------
    def _scroller(self, child: Gtk.Widget) -> Gtk.Widget:
        sc = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER, vexpand=True)
        clamp = Adw.Clamp(maximum_size=640, margin_top=18, margin_bottom=18, margin_start=16, margin_end=16)
        clamp.set_child(child)
        sc.set_child(clamp)
        return sc

    def _box(self) -> Gtk.Box:
        return Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)

    def _heading(self, box: Gtk.Box, title: str, text: str) -> None:
        h = Gtk.Label(label=title, xalign=0)
        h.add_css_class("title-2")
        box.append(h)
        d = Gtk.Label(label=text, xalign=0, wrap=True)
        d.add_css_class("dim-label")
        box.append(d)

    @staticmethod
    def _fresh(group: Adw.PreferencesGroup) -> Adw.PreferencesGroup:
        """An empty replacement for a preferences group, in the same place."""
        parent = group.get_parent()
        new = Adw.PreferencesGroup()
        parent.insert_child_after(new, group)
        parent.remove(group)
        return new

    def say(self, text: str) -> None:
        self.status.set_text(text)

    def go(self, n: int) -> None:
        n = max(0, min(n, len(STEPS) - 1))
        if n == 3 and not any(not s.is_windows for _, s in self._selected()):
            n = 4 if self.step < 3 else 2            # no Linux server ticked: nothing to match a key to
        self.step = n
        self.stack.set_visible_child_name(str(n))
        self.step_label.set_text(f"Step {n + 1} of {len(STEPS)}: {STEPS[n]}")
        self.back.set_visible(n > 0)
        self.next.set_label("Save and connect" if n == 4 else "Next")
        self.say("")
        self.next.set_sensitive(True)
        {0: self._enter_requirements, 1: self._enter_account, 2: self._enter_servers,
         3: self._enter_keys, 4: self._enter_save}[n]()

    def on_next(self) -> None:
        if self.busy:
            return
        {0: lambda: self.go(1), 1: self._leave_account, 2: lambda: self.go(3), 3: lambda: self.go(4), 4: self._save}[self.step]()

    def set_busy(self, on: bool, text: str = "") -> None:
        self.busy = on
        self.next.set_sensitive(not on)
        self.back.set_sensitive(not on)
        self.say(text)

    # ---- 1. requirements -------------------------------------------------------------------
    def _page_requirements(self) -> Gtk.Widget:
        box = self._box()
        self._heading(box, "What SSMConnect needs", "Two small programs from Amazon open the secure tunnels. SSMConnect can fetch them for you.")
        self.req_group = Adw.PreferencesGroup()
        box.append(self.req_group)
        self.install_btn = Gtk.Button(label="Install what is missing", halign=Gtk.Align.START)
        self.install_btn.add_css_class("suggested-action")
        self.install_btn.connect("clicked", lambda _b: self._install())
        box.append(self.install_btn)
        note = Gtk.Label(xalign=0, wrap=True, label="Installed for you only, in ~/.local/share/ssmconnect. No administrator rights needed. "
                         "If your distribution packages them, you can install those instead and click Check again.")
        note.add_css_class("dim-label")
        box.append(note)
        again = Gtk.Button(label="Check again", halign=Gtk.Align.START)
        again.connect("clicked", lambda _b: self._enter_requirements())
        box.append(again)
        return self._scroller(box)

    def _enter_requirements(self) -> None:
        self.req_group = self._fresh(self.req_group)
        items = [("AWS CLI", aws_cli.find_aws(), True), ("Session Manager plugin", aws_cli.find_plugin(), True),
                 ("ssh (OpenSSH client)", launchers.tools_summary()["ssh"], False),
                 ("Terminal", launchers.tools_summary()["terminal"], False),
                 ("Remote Desktop client (for Windows servers)", launchers.tools_summary()["rdp"], False)]
        missing_required = False
        for name, found, required in items:
            row = Adw.ActionRow(title=name, subtitle=found or ("Missing" if required else "Not found (optional)"))
            row.add_prefix(Gtk.Image(icon_name="object-select-symbolic" if found else ("dialog-error-symbolic" if required else "dialog-warning-symbolic")))
            self.req_group.add(row)
            missing_required |= required and not found
        self.install_btn.set_visible(missing_required)
        self.next.set_sensitive(not missing_required and not self.busy)
        if missing_required:
            self.say("Install the missing programs to continue.")

    def _install(self) -> None:
        self.set_busy(True, "Starting...")
        self.install_btn.set_sensitive(False)

        def work():
            return prereq_installer.install_missing(lambda m: GLib.idle_add(self.say, m))

        def done(res, err):
            self.set_busy(False)
            self.install_btn.set_sensitive(True)
            if err:
                message(self, "Could not install", plain_error(err))
            self._enter_requirements()
        in_background(work, done)

    # ---- 2. account ------------------------------------------------------------------------
    def _page_account(self) -> Gtk.Widget:
        box = self._box()
        self._heading(box, "Your AWS account", "Sign in once with your company account. Every AWS account in your organization comes along.")
        self.acct_choice = Adw.ComboRow(title="Sign-in")
        g = Adw.PreferencesGroup()
        g.add(self.acct_choice)
        box.append(g)

        self.new_box = Adw.PreferencesGroup()
        self.url_row = Adw.EntryRow(title="Company sign-in address (like https://yourcompany.awsapps.com/start)")
        self.sso_region_row = Adw.EntryRow(title="Region of the sign-in page (like us-east-1)")
        self.sso_region_row.set_text("us-east-1")
        self.new_box.add(self.url_row)
        self.new_box.add(self.sso_region_row)
        self.hint = Gtk.Label(xalign=0, wrap=True)
        self.hint.add_css_class("dim-label")
        box.append(self.new_box)
        box.append(self.hint)
        self.keys_link = Gtk.Button(label="I use access keys instead", halign=Gtk.Align.START)
        self.keys_link.add_css_class("flat")
        self.keys_link.connect("clicked", lambda _b: self._access_keys_dialog())
        box.append(self.keys_link)

        g2 = Adw.PreferencesGroup()
        self.region_row = Adw.ComboRow(title="Region of your servers", subtitle="Where your EC2 servers live, for example ap-south-1")
        g2.add(self.region_row)
        box.append(g2)
        self.acct_choice.connect("notify::selected", lambda *_: self._account_changed())
        return self._scroller(box)

    def _primaries(self) -> list[tuple[str, str]]:
        everyone = ap.list_profiles()
        seen: dict[str, str] = {}
        for p in everyone:
            if p.kind != ap.OTHER:
                seen.setdefault(ap.primary_key(p), ap.primary_label(self.cfg, p, everyone))
        return list(seen.items())

    def _enter_account(self) -> None:
        self.cfg = config_store.load() or self.cfg
        self._prim = self._primaries()
        labels = [l for _, l in self._prim] + ["Add a new company sign-in…"]
        self.acct_choice.set_model(Gtk.StringList.new(labels))
        pick = len(labels) - 1 if (self.add_account or not self._prim) else 0
        if not self.add_account and self.primary_key:
            pick = next((i for i, (k, _) in enumerate(self._prim) if k == self.primary_key), pick)
        elif not self.add_account and self.cfg.selected_account:
            pick = next((i for i, (k, _) in enumerate(self._prim) if k == self.cfg.selected_account), pick)
        self.acct_choice.set_selected(pick)
        regions = list(ap.COMMON_REGIONS)
        want = self.cfg.region or "us-east-1"
        if want not in regions:
            regions.insert(0, want)
        self.region_row.set_model(Gtk.StringList.new(sorted(regions)))
        self.region_row.set_selected(sorted(regions).index(want))
        cand = sso_discovery.find()
        if cand and not self.url_row.get_text():
            self.url_row.set_text(cand[0].start_url)
            if cand[0].region:
                self.sso_region_row.set_text(cand[0].region)
            self.hint.set_text(f"Filled in for you. {cand[0].source}.")
        elif not cand and not self.url_row.get_text():
            self.hint.set_text("Ask your AWS admin for the sign-in address if you do not know it.")
            in_background(sso_discovery.guess_from_domain, self._guessed)
        self._account_changed()

    def _guessed(self, res, err) -> None:
        if res and not self.url_row.get_text():
            self.url_row.set_text(res[0].start_url)
            self.hint.set_text(f"{res[0].source}.")

    def _account_changed(self) -> None:
        new = self.acct_choice.get_selected() >= len(getattr(self, "_prim", []))
        self.new_box.set_visible(new)
        self.next.set_label("Sign in" if new else "Next")

    def _leave_account(self) -> None:
        sel = self.acct_choice.get_selected()
        self.region = self.region_row.get_selected_item().get_string()
        if not is_valid_region(self.region):
            message(self, "Choose a region", "Pick the region where your servers live.")
            return
        if sel < len(self._prim):
            self.primary_key = self._prim[sel][0]
            self._existing_sign_in()
        else:
            self._new_sign_in()

    def _members_of(self, key: str) -> list[ap.ProfileInfo]:
        everyone = ap.list_profiles()
        group = [p for p in everyone if ap.primary_key(p) == key]
        return ap.one_each(group, {t.profile for t in self.cfg.tunnels})

    def _existing_sign_in(self) -> None:
        group = [p for p in ap.list_profiles() if ap.primary_key(p) == self.primary_key]
        if not group:
            return
        if group[0].kind != ap.SSO:
            self.members = self._members_of(self.primary_key)
            self.go(2)
            return
        sessions = list(dict.fromkeys(p.sso_session for p in group if p.sso_session))
        self.set_busy(True, "Checking your sign-in...")

        def work():
            session = next((s for s in sessions if sso.read_access_token(s)), None)
            if session is None:
                GLib.idle_add(self.say, "Your browser opens: finish signing in there...")
                session = sessions[0]
                r = sso.login(session)
                if not r.ok:
                    raise RuntimeError(aws_cli.friendly_error(r.error))
            token = sso.read_access_token(session)
            region = ap.session_info(session)[1]
            if token and region:     # pick up AWS accounts added to the organization since last time
                known = {p.account_id for p in group if p.account_id}
                fresh = [a for a in sso.list_accounts(token, region) if a.id not in known]
                if fresh:
                    sso.add_all_accounts(session, token, region, next((p.role_name for p in group if p.role_name), ""), fresh,
                                         lambda m: GLib.idle_add(self.say, m))
            return True

        def done(_r, err):
            self.set_busy(False)
            if err:
                message(self, "Sign-in did not finish", plain_error(err))
                return
            self.members = self._members_of(self.primary_key)
            self.go(2)
        in_background(work, done)

    def _new_sign_in(self) -> None:
        url, sso_region = self.url_row.get_text().strip(), self.sso_region_row.get_text().strip()
        if not sso_discovery.is_valid_url(url):
            message(self, "Check the sign-in address", "It should look like https://yourcompany.awsapps.com/start")
            return
        if not is_valid_region(sso_region):
            message(self, "Check the sign-in region", "Use the AWS region of the sign-in page, for example us-east-1.")
            return
        url = url.split("#", 1)[0].rstrip("/")
        session = sso_discovery.session_name_for(url)
        have = ap.session_info(session)[0]
        if have and ap.normalise_url(have) != ap.normalise_url(url):
            session += "-2"
        ap.save_sso_session(session, url, sso_region)
        self.set_busy(True, "Your browser opens: finish signing in there...")

        def work():
            r = sso.login(session)
            if not r.ok:
                raise RuntimeError(aws_cli.friendly_error(r.error or r.output))
            token = sso.read_access_token(session)
            if not token:
                raise RuntimeError("The sign-in finished but AWS did not hand over a token. Please try again.")
            accounts = sso.list_accounts(token, sso_region)
            if not accounts:
                raise RuntimeError("You are signed in, but no AWS accounts are assigned to you. Ask your AWS admin.")
            made = sso.add_all_accounts(session, token, sso_region, "", accounts, lambda m: GLib.idle_add(self.say, m))
            if not made:
                raise RuntimeError("You have no role in any of the AWS accounts. Ask your AWS admin to assign one.")
            return made

        def done(made, err):
            self.set_busy(False)
            if err:
                message(self, "Sign-in did not finish", plain_error(err))
                return
            p = ap.find(made[0][0])
            self.primary_key = ap.primary_key(p) if p else ""
            self.members = self._members_of(self.primary_key)
            self.say(f"Signed in. {len(self.members)} AWS account(s) found.")
            self.go(2)
        in_background(work, done)

    def _access_keys_dialog(self) -> None:
        d = Adw.AlertDialog(heading="Use access keys", body="Keys are saved only in your own ~/.aws/credentials file.")
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        name = Gtk.Entry(placeholder_text="Name for this account, e.g. my-company")
        kid = Gtk.Entry(placeholder_text="Access key ID (AKIA…)")
        secret = Gtk.PasswordEntry(placeholder_text="Secret access key", show_peek_icon=True)
        for w in (name, kid, secret):
            box.append(w)
        d.set_extra_child(box)
        d.add_response("cancel", "Cancel")
        d.add_response("save", "Save")
        d.set_response_appearance("save", Adw.ResponseAppearance.SUGGESTED)

        def resp(_d, r):
            if r != "save":
                return
            try:
                if not ap.is_valid_name(name.get_text().strip()):
                    raise ValueError("Use letters, digits, dashes or dots for the name.")
                ap.save_access_keys(name.get_text().strip(), kid.get_text().strip(), secret.get_text().strip())
            except ValueError as ex:
                message(self, "Could not save", str(ex))
                return
            self._enter_account()
            p = ap.find(name.get_text().strip())
            if p:
                keys = [k for k, _ in self._prim]
                self.acct_choice.set_selected(keys.index(ap.primary_key(p)))
        d.connect("response", resp)
        d.present(self)

    # ---- 3. servers ------------------------------------------------------------------------
    def _page_servers(self) -> Gtk.Widget:
        box = self._box()
        self._heading(box, "Pick your servers", "Tick the servers you want to connect to. Servers marked 'not reachable' have no working SSM agent yet.")
        self.srv_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=16)
        box.append(self.srv_box)
        return self._scroller(box)

    def _enter_servers(self) -> None:
        while (c := self.srv_box.get_first_child()):
            self.srv_box.remove(c)
        self.found, self.checks, self.skipped = [], {}, []
        spin = Gtk.Spinner(spinning=True, halign=Gtk.Align.CENTER, width_request=32, height_request=32)
        self.srv_box.append(spin)
        self.set_busy(True, f"Searching {len(self.members)} AWS account(s) for servers...")
        done_count = [0]
        results: list[tuple[str, list[DiscoveredServer] | None, str]] = []

        def one(p: ap.ProfileInfo):
            try:
                res = (p.name, aws_cli.discover(p.name, self.region), "")
            except RuntimeError as ex:
                res = (p.name, None, str(ex))
            done_count[0] += 1
            GLib.idle_add(self.say, f"Searched {done_count[0]} of {len(self.members)} AWS account(s)...")
            return res

        def work():
            with ThreadPoolExecutor(max_workers=4) as pool:
                return list(pool.map(one, self.members))

        in_background(work, lambda res, err: self._servers_done(res, err))

    def _servers_done(self, results, err) -> None:
        self.set_busy(False)
        while (c := self.srv_box.get_first_child()):
            self.srv_box.remove(c)
        if err:
            message(self, "Could not search for servers", plain_error(err))
            return
        have = {(t.profile, t.instance_id) for t in self.cfg.tunnels}
        configured = {t.instance_id for t in self.cfg.tunnels}
        any_server = False
        for profile, servers, why in results:
            if servers is None:
                self.skipped.append(f"{self.cfg.alias_or(profile, profile)}: {why}")
                continue
            discovery.update(profile, self.region, servers, configured, record_changes=False)
            if not servers:
                continue
            g = Adw.PreferencesGroup(title=GLib.markup_escape_text(self.cfg.alias_or(profile, profile)))
            for s in servers:
                any_server = True
                self.found.append((profile, s))
                row = Adw.ActionRow(title=GLib.markup_escape_text(s.name),
                                    subtitle=GLib.markup_escape_text(
                                        f"{'Windows' if s.is_windows else (s.os_name or 'Linux')} · {s.private_ip}"
                                        + ("" if s.ssm_online else " · not reachable through SSM")))
                chk = Gtk.CheckButton(valign=Gtk.Align.CENTER, active=(profile, s.instance_id) in have and s.ssm_online)
                chk.set_sensitive(s.ssm_online)
                row.add_prefix(chk)
                row.set_activatable_widget(chk)
                if s.environment:
                    tag = Gtk.Label(label="PROD" if s.is_prod else s.environment, valign=Gtk.Align.CENTER)
                    tag.add_css_class("tag")
                    if s.is_prod:
                        tag.add_css_class("prod")
                    row.add_suffix(tag)
                self.checks[(profile, s.instance_id)] = chk
                g.add(row)
            self.srv_box.append(g)
        if self.skipped:
            lbl = Gtk.Label(xalign=0, wrap=True, label="Some AWS accounts could not be searched:\n• " + "\n• ".join(self.skipped))
            lbl.add_css_class("dim-label")
            self.srv_box.append(lbl)
        if not any_server:
            self.srv_box.append(Adw.StatusPage(title="No servers found", icon_name="network-server-symbolic",
                                               description="No running servers in this region. Try another region (Back), or check your permissions."))

    def _selected(self) -> list[tuple[str, DiscoveredServer]]:
        return [(p, s) for p, s in self.found if self.checks.get((p, s.instance_id)) and self.checks[(p, s.instance_id)].get_active()]

    # ---- 4. keys ---------------------------------------------------------------------------
    def _page_keys(self) -> Gtk.Widget:
        box = self._box()
        self._heading(box, "SSH keys", "Each Linux server needs the key file it was created with. SSMConnect looks for it and checks it against the server. "
                      "Nothing is copied or uploaded.")
        self.key_group = Adw.PreferencesGroup()
        box.append(self.key_group)
        row = Gtk.Box(spacing=8)
        b = Gtk.Button(label="Search a folder…")
        b.connect("clicked", lambda _b: self._pick_folder())
        row.append(b)
        self.key_note = Gtk.Label(xalign=0, wrap=True)
        self.key_note.add_css_class("dim-label")
        row.append(self.key_note)
        box.append(row)
        return self._scroller(box)

    def _enter_keys(self) -> None:
        self.set_busy(True, "Looking for key files on this computer...")
        in_background(lambda: key_finder.scan_standard(15), lambda res, err: self._keys_found(res or []))

    def _keys_found(self, keys: list[key_finder.KeyFile]) -> None:
        self.set_busy(False)
        known = {k.path for k in self.keys}
        self.keys += [k for k in keys if k.path not in known]
        self._build_key_rows()

    def _pick_folder(self) -> None:
        dlg = Gtk.FileDialog(title="Choose a folder to search for key files")

        def done(d, res):
            try:
                f = d.select_folder_finish(res)
            except GLib.Error:
                return
            path = f.get_path()
            self.set_busy(True, "Searching that folder...")
            in_background(lambda: key_finder.scan_folder(path, 30), lambda r, e: self._keys_found(r or []))
        dlg.select_folder(self, None, done)

    def _build_key_rows(self) -> None:
        old = {r["id"]: r for r in self.key_rows}
        self.key_group = self._fresh(self.key_group)
        self.key_rows = []
        existing = {t.instance_id: t for t in self.cfg.tunnels}
        names = ["(no key file: use ssh-agent or ask later)"] + [str(k) for k in self.keys]
        missing = 0
        for profile, s in self._selected():
            exp = Adw.ExpanderRow(title=GLib.markup_escape_text(s.name))
            user = Adw.EntryRow(title="Login user")
            prev = old.get(s.instance_id + profile)
            user.set_text(prev["user"].get_text() if prev else (existing[s.instance_id].user_name if s.instance_id in existing else s.suggested_user))
            exp.add_row(user)
            dd = Adw.ComboRow(title="Key file" if not s.is_windows else "Key file (only needed to get the password)")
            dd.set_model(Gtk.StringList.new(names))
            chosen = None
            how = ""
            if prev and prev["combo"].get_selected() > 0:
                chosen, how = self.keys[prev["combo"].get_selected() - 1], "kept"
            elif s.instance_id in existing and existing[s.instance_id].key_file:
                chosen = next((k for k in self.keys if k.path == existing[s.instance_id].key_file), None)
                how = "already saved"
            if chosen is None:
                chosen, how = key_finder.find(self.keys, s.key_name, s.key_fingerprint)
            if chosen:
                dd.set_selected(self.keys.index(chosen) + 1)
                exp.set_subtitle(GLib.markup_escape_text(f"{chosen.stem} - {how}"))
            else:
                missing += 1
                exp.set_subtitle(GLib.markup_escape_text(f"No key found for '{s.key_name or 'unknown key'}'"))
            dd.connect("notify::selected", lambda c, _p, e=exp: e.set_subtitle(""))
            exp.add_row(dd)
            exp.set_expanded(chosen is None)
            self.key_group.add(exp)
            self.key_rows.append({"id": s.instance_id + profile, "profile": profile, "server": s, "user": user, "combo": dd})
        self.key_note.set_text(f"{missing} server(s) have no key yet. Choose one, or search a folder." if missing else "All keys found.")

    def _key_for(self, row: dict) -> str | None:
        i = row["combo"].get_selected()
        return self.keys[i - 1].path if i > 0 else None

    # ---- 5. save ---------------------------------------------------------------------------
    def _page_save(self) -> Gtk.Widget:
        box = self._box()
        self._heading(box, "Ready", "This is what will be saved. Connections open right away, and ssh gets a short name for each server.")
        self.sum_group = Adw.PreferencesGroup()
        box.append(self.sum_group)
        return self._scroller(box)

    def _enter_save(self) -> None:
        self.sum_group = self._fresh(self.sum_group)
        sel = self._selected()
        if not sel:
            self.sum_group.add(Adw.ActionRow(title="No servers ticked", subtitle="Saving will remove this account's saved connections."))
        rows = {r["id"]: r for r in self.key_rows}
        for profile, s in sel:
            r = rows.get(s.instance_id + profile)
            key = self._key_for(r) if r else None
            if s.is_windows:
                sub = "Remote Desktop · password via key file" if key else "Remote Desktop"
            else:
                sub = f"ssh {ssh_config.host_alias(s.name, s.instance_id)} · user {r['user'].get_text() if r else s.suggested_user}" + ("" if key else " · no key file")
            self.sum_group.add(Adw.ActionRow(title=GLib.markup_escape_text(s.name), subtitle=GLib.markup_escape_text(sub)))

    def _save(self) -> None:
        cfg = config_store.load() or self.cfg
        mine = {m.name for m in self.members}
        keep = [t for t in cfg.tunnels if t.profile not in mine]
        prev = {(t.profile, t.instance_id): t for t in cfg.tunnels}
        taken = {t.local_port for t in keep}
        rows = {r["id"]: r for r in self.key_rows}
        new: list[TunnelConfig] = []
        for profile, s in self._selected():
            old = prev.get((profile, s.instance_id))
            r = rows.get(s.instance_id + profile)
            port = old.local_port if old and old.local_port not in taken \
                else netcheck.free_port(FIRST_PORT, taken | {t.local_port for t in new})
            user = (r["user"].get_text().strip() if r else "") or s.suggested_user
            if not is_valid_login(user):
                message(self, "Check the login user", f"'{user}' is not a valid user name for {s.name}.")
                return
            new.append(TunnelConfig(name=s.name, instance_id=s.instance_id, profile=profile, region=self.region, local_port=port,
                                    remote_port=3389 if s.is_windows else 22, protocol="rdp" if s.is_windows else "ssh",
                                    user_name=user, environment=s.environment, key_file=self._key_for(r) if r else None,
                                    enabled=True, os_name=s.os_name))
            taken.add(port)
        cfg.tunnels = keep + new
        cfg.profile = self.members[0].name if self.members else cfg.profile
        cfg.region = self.region
        cfg.selected_account = self.primary_key
        try:
            config_store.save(cfg)
            for m in self.members:
                if not m.region:
                    ap.save_region(m.name, self.region)
            ssh_config.apply(cfg.tunnels)
        except OSError as ex:
            message(self, "Could not save", f"{ex}")
            return
        self.app.ensure_manager()
        if self.app.window:
            self.app.window.reload()
            self.app.window.toast(f"Saved {len(new)} server(s). Opening connections...")
        self.close()
