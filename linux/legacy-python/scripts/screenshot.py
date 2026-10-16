#!/usr/bin/env python3
"""Developer tool: opens the dashboard against a throw-away HOME with a fake AWS CLI and saves a PNG of the window.
Usage: scripts/screenshot.py out.png [dark|light] [dashboard|wizard]"""
import os, stat, sys, tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "src")); sys.path.insert(0, str(root / "tests"))
out = sys.argv[1]; scheme = sys.argv[2] if len(sys.argv) > 2 else "light"; what = sys.argv[3] if len(sys.argv) > 3 else "dashboard"

home = Path(tempfile.mkdtemp(prefix="ssmconnect-shot-"))
os.environ["HOME"] = str(home)
for v in ("XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"):
    os.environ.pop(v, None)
from test_tunnels import FAKE_AWS
b = home / ".local/share/ssmconnect/bin"; b.mkdir(parents=True)
for n, body in (("aws", FAKE_AWS), ("session-manager-plugin", "#!/bin/sh\n")):
    (b / n).write_text(body); (b / n).chmod(0o755)

from ssmconnect import aws_profiles as ap, config_store
from ssmconnect.models import AppConfig, TunnelConfig
ap.save_sso_session("acme", "https://acme.awsapps.com/start", "us-east-1")
ap.save_sso_profile("acme-prod", "acme", "111111111111", "Admin")
ap.save_sso_profile("acme-dev", "acme", "222222222222", "Admin")
ap.save_sso_profile("acme-empty", "acme", "333333333333", "Admin")
T = lambda n, i, p, env, prof, **k: TunnelConfig(name=n, instance_id=i, profile=prof, region="ap-south-1", local_port=p, environment=env, **{"user_name": "ubuntu", **k})
config_store.save(AppConfig(aliases={"sso:acme.awsapps.com": "Acme Corp", "acme-prod": "Production account", "acme-dev": "Development account"}, tunnels=[
    T("web-prod-1", "i-0123456789abcdef0", 24101, "Production", "acme-prod", os_name="Ubuntu"),
    T("db-prod", "i-0123456789abcdef1", 24102, "Production", "acme-prod", os_name="Amazon Linux"),
    T("api-dev", "i-0123456789abcdef2", 24103, "Development", "acme-dev", os_name="Debian GNU/Linux"),
    T("win-app", "i-0fa1100000000000", 24104, "Test", "acme-dev", protocol="rdp", user_name="Administrator"),
]))
import gi
gi.require_version("Gtk", "4.0"); gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk, Graphene, Gdk
from ssmconnect.ui.app import SSMConnectApp

app = SSMConnectApp()
def shot():
    w = app.window if what == "dashboard" else app.wizard
    p = Gtk.WidgetPaintable.new(w)
    snap = Gtk.Snapshot(); p.snapshot(snap, w.get_width(), w.get_height())
    node = snap.to_node()
    tex = w.get_native().get_renderer().render_texture(node, Graphene.Rect().init(0, 0, w.get_width(), w.get_height()))
    tex.save_to_png(out); print("saved", out)
    app.quit(); return False
def go():
    Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_DARK if scheme == "dark" else Adw.ColorScheme.FORCE_LIGHT)
    if what == "wizard":
        app.open_wizard(False)
    GLib.timeout_add(int(os.environ.get("SHOT_DELAY", "5000")), shot); return False
app.connect("activate", lambda a: GLib.timeout_add(500, go))
app.run([sys.argv[0]])
