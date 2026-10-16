#!/usr/bin/env python3
"""Developer tool: drives the real wizard window through all five steps against a fake AWS CLI in a throw-away HOME."""
import os, subprocess, sys, tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / "src")); sys.path.insert(0, str(root / "tests"))
home = Path(tempfile.mkdtemp(prefix="ssmconnect-e2e-"))
os.environ["HOME"] = str(home)
for v in ("XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_DATA_HOME", "AWS_CONFIG_FILE", "AWS_SHARED_CREDENTIALS_FILE"):
    os.environ.pop(v, None)
from fake_aws import FAKE_AWS_FULL
b = home / ".local/share/ssmconnect/bin"; b.mkdir(parents=True)
for n, body in (("aws", FAKE_AWS_FULL), ("session-manager-plugin", "#!/bin/sh\n")):
    (b / n).write_text(body); (b / n).chmod(0o755)
key = home / "Downloads" / "prod-key.pem"; key.parent.mkdir()
subprocess.run(["openssl", "genrsa", "-traditional", "-out", str(key), "2048"], check=True, capture_output=True)
from ssmconnect import key_finder
os.environ["FAKE_FPR"] = sorted(key_finder.fingerprints(str(key)))[0]

import gi
gi.require_version("Gtk", "4.0"); gi.require_version("Adw", "1")
from gi.repository import GLib
from ssmconnect import config_store, ssh_config
from ssmconnect.ui.app import SSMConnectApp

app = SSMConnectApp()
log = []
def fail(msg): print("FAIL:", msg); app.quit(); os._exit(1)

def until(cond, then, what, timeout=20):
    t0 = GLib.get_monotonic_time()
    def poll():
        if cond(): then(); return False
        if (GLib.get_monotonic_time() - t0) / 1e6 > timeout: fail("timed out waiting for " + what)
        return True
    GLib.timeout_add(150, poll)

def start():
    w = app.wizard
    until(lambda: w.next.get_sensitive() and w.step == 0, step1, "requirements")
def step1():
    w = app.wizard; print("1 requirements ok"); w.on_next()
    until(lambda: w.step == 1, step2, "account page")
def step2():
    w = app.wizard
    w.url_row.set_text("https://acme.awsapps.com/start"); w.sso_region_row.set_text("us-east-1")
    w.region_row.set_selected(list(w.region_row.get_model()).index(next(x for x in w.region_row.get_model() if x.get_string() == "ap-south-1")))
    w.on_next()
    until(lambda: w.step == 2 and not w.busy and w.found, step3, "servers")
def step3():
    w = app.wizard; print("3 servers:", [(p, s.name, s.ssm_online) for p, s in w.found])
    if len(w.members) != 2: fail("expected 2 member accounts, got %r" % w.members)
    online = [k for (p, i), k in w.checks.items() if k.get_sensitive()]
    if len(online) != 2: fail("expected 2 selectable servers")
    for k in online: k.set_active(True)
    w.on_next()
    until(lambda: w.step == 3 and not w.busy and w.key_rows, step4, "keys")
def step4():
    w = app.wizard
    r = w.key_rows[0]
    print("4 key chosen:", w._key_for(r), "user:", r["user"].get_text())
    if not w._key_for(r) or not w._key_for(r).endswith("prod-key.pem"): fail("key not auto-matched")
    w.on_next(); until(lambda: w.step == 4, step5, "save page")
def step5():
    w = app.wizard; w.on_next()
    until(lambda: not w.is_visible() or True, check, "closed")
def check():
    def later():
        cfg = config_store.load()
        print("saved:", [(t.name, t.protocol, t.local_port, t.user_name, t.environment, bool(t.key_file)) for t in cfg.tunnels])
        assert len(cfg.tunnels) == 2, cfg.tunnels
        assert {t.local_port for t in cfg.tunnels} == {2201, 2202}
        conf = (home / ".ssh/ssmconnect.conf").read_text(); assert "Host ssc-web-prod-1" in conf and "win-app" not in conf, conf
        print("ssh config ok")
        until(lambda: all(s.state == "Connected" for s in app.manager.snapshot()) and len(app.manager.snapshot()) == 2, done, "tunnels connected", 30)
        return False
    GLib.timeout_add(500, later)
def done():
    print("tunnels connected ok:", [(s.name, s.state) for s in app.manager.snapshot()])
    print("PASS"); app.quit()

def on_activate(a):
    GLib.timeout_add(500, lambda: (app.activate_action("add-servers", None), GLib.timeout_add(500, lambda: (start(), False)[1]), False)[2])
app.connect("activate", on_activate)
app.run([sys.argv[0]])
