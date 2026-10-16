import os
import socket
import stat
import time
import unittest

from base import IsolatedHome

from ssmconnect import config_store, netcheck, status_store
from ssmconnect.models import AppConfig, TunnelConfig, TunnelStates
from ssmconnect.tunnel_manager import TunnelManager

FAKE_AWS = """#!/usr/bin/env python3
import os, socket, sys, time
a = sys.argv
if "start-session" in a:
    if "i-0fa1100000000000" in a:
        sys.stderr.write("An error occurred (TargetNotConnected) when calling the StartSession operation\\n"); sys.exit(254)
    port = int(a[a.index("--parameters") + 1].split("localPortNumber=")[1])
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("127.0.0.1", port)); s.listen(1)
    print("Starting session with SessionId: x"); print("Port %d opened for sessionId x." % port); print("Waiting for connections...", flush=True)
    time.sleep(600)
"""


def wait_for(cond, timeout=15):
    end = time.time() + timeout
    while time.time() < end:
        if cond():
            return True
        time.sleep(0.1)
    return False


class TunnelTests(IsolatedHome):
    def setUp(self):
        super().setUp()
        b = self.home / ".local/share/ssmconnect/bin"
        b.mkdir(parents=True)
        for n, body in (("aws", FAKE_AWS), ("session-manager-plugin", "#!/bin/sh\n")):
            f = b / n
            f.write_text(body)
            f.chmod(f.stat().st_mode | stat.S_IXUSR)
        s = socket.socket(); s.bind(("127.0.0.1", 0)); self.port = s.getsockname()[1]; s.close()
        self.mgr = None

    def tearDown(self):
        if self.mgr:
            self.mgr.stop()
        super().tearDown()

    def tunnel(self, iid="i-0123456789abcdef0", port=None):
        return TunnelConfig(name="web", instance_id=iid, profile="p", region="ap-south-1", local_port=port or self.port)

    def start(self, *tunnels):
        config_store.save(AppConfig(tunnels=list(tunnels)))
        self.mgr = TunnelManager(interval=0.2)
        self.mgr.start()

    def state(self, i=0):
        s = self.mgr.snapshot()
        return s[i].state if len(s) > i else None

    def test_connects_and_stops_cleanly(self):
        self.start(self.tunnel())
        self.assertTrue(wait_for(lambda: self.state() == TunnelStates.CONNECTED), self.mgr.snapshot())
        self.assertTrue(netcheck.is_listening(self.port))
        self.assertFalse(netcheck.is_exposed(self.port))
        self.assertEqual(status_store.load()[1][0].state, TunnelStates.CONNECTED)
        self.mgr.stop()
        self.assertTrue(wait_for(lambda: not netcheck.is_listening(self.port)))

    def test_friendly_error_for_offline_server(self):
        self.start(self.tunnel("i-0fa1100000000000"))
        self.assertTrue(wait_for(lambda: self.state() == TunnelStates.ERROR))
        self.assertIn("offline in SSM", self.mgr.snapshot()[0].message)

    def test_refuses_port_used_by_another_program(self):
        s = socket.socket(); s.bind(("127.0.0.1", self.port)); s.listen(1)
        try:
            self.start(self.tunnel())
            self.assertTrue(wait_for(lambda: self.state() == TunnelStates.ERROR))
            self.assertIn("already used", self.mgr.snapshot()[0].message)
        finally:
            s.close()

    def test_config_change_reconciles(self):
        self.start(self.tunnel())
        self.assertTrue(wait_for(lambda: self.state() == TunnelStates.CONNECTED))
        t = self.tunnel(); t.enabled = False
        config_store.save(AppConfig(tunnels=[t]))
        self.mgr.poke()
        self.assertTrue(wait_for(lambda: self.state() == TunnelStates.DISABLED))
        self.assertTrue(wait_for(lambda: not netcheck.is_listening(self.port)))

    def test_invalid_config_reported_not_started(self):
        bad = self.tunnel(); bad.instance_id = "oops"
        self.start(bad)
        self.assertTrue(wait_for(lambda: self.state() == TunnelStates.ERROR))
        self.assertIn("not a valid EC2 instance", self.mgr.snapshot()[0].message)


if __name__ == "__main__":
    unittest.main()
