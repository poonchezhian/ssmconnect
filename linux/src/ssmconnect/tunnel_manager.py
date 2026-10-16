"""Keeps one `aws ssm start-session` port-forward alive per configured server, as the signed-in user.

The manager runs on a background thread inside the app (or `ssmconnect run` on a terminal). It reacts to config changes,
renews sessions that AWS closed for being idle, backs off when something is really wrong, and publishes plain-language
state through `snapshot()`, the `on_change` callback and status.json."""

from __future__ import annotations

import ctypes
import os
import signal
import subprocess
import threading
import time
from dataclasses import dataclass, field
from typing import Callable

from . import applog, aws_cli, config_store, netcheck, status_store, validator
from .models import AppConfig, TunnelConfig, TunnelStates, TunnelStatus

_PR_SET_PDEATHSIG = 1


def _die_with_parent() -> None:   # runs in the child just before exec
    try:
        ctypes.CDLL("libc.so.6", use_errno=True).prctl(_PR_SET_PDEATHSIG, signal.SIGTERM)
    except OSError:
        pass


@dataclass
class _Slot:
    tunnel: TunnelConfig
    proc: subprocess.Popen | None = None
    next_start: float = 0.0
    started_at: float = 0.0
    backoff: int = 5
    state: str = TunnelStates.STARTING
    message: str = "Starting..."
    since: float = field(default_factory=time.time)
    last_error: str = ""
    failures: int = 0       # consecutive attempts that ended without ever connecting
    refreshing: bool = False   # a healthy session ended (idle timeout) and a new one opens quietly


def _key(t: TunnelConfig) -> str:
    return f"{t.instance_id}|{t.remote_port}|{t.local_port}|{t.profile}|{t.region}"


class TunnelManager:
    def __init__(self, load_config: Callable[[], AppConfig | None] = config_store.load, interval: float = 3.0):
        self._load = load_config
        self._interval = interval
        self._lock = threading.RLock()
        self._wake = threading.Event()
        self._slots: dict[str, _Slot] = {}
        self._invalid: dict[int, str] = {}
        self._cfg: AppConfig | None = None
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self.on_change: Callable[[], None] | None = None

    # ---- control ------------------------------------------------------------------------
    @property
    def running(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def start(self) -> None:
        if self.running:
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="tunnel-manager", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._wake.set()
        t = self._thread
        if t:
            t.join(timeout=10)
        self._thread = None

    def poke(self) -> None:
        """Wakes the loop now, for example after the config changed or the person signed in."""
        self._wake.set()

    def snapshot(self) -> list[TunnelStatus]:
        with self._lock:
            return self._statuses()

    # ---- loop -----------------------------------------------------------------------------
    def _run(self) -> None:
        applog.info("Tunnel manager started.")
        try:
            while not self._stop.is_set():
                try:
                    with self._lock:
                        self._cfg = self._load() or self._cfg   # keep the last good config if the file is mid-write
                        if self._cfg is not None:
                            self._reconcile(self._cfg)
                        self._publish()
                except Exception as ex:   # noqa: BLE001
                    applog.error(f"Reconcile failed: {ex!r}")
                self._wake.wait(self._interval)
                self._wake.clear()
        finally:
            with self._lock:
                for s in self._slots.values():
                    self._stop_slot(s)
                self._slots.clear()
                self._cfg = None
                status_store.save([])
            self._notify()
            applog.info("Tunnel manager stopped.")

    def _notify(self) -> None:
        cb = self.on_change
        if cb:
            try:
                cb()
            except Exception as ex:   # noqa: BLE001
                applog.warn(f"on_change failed: {ex!r}")

    def _reconcile(self, cfg: AppConfig) -> None:
        self._invalid.clear()
        global_problem = validator.validate_global(cfg)
        desired: dict[str, TunnelConfig] = {}
        for i, t in enumerate(cfg.tunnels):
            if not t.enabled:
                continue
            problem = global_problem or validator.validate_tunnel(t, cfg.tunnels)
            if problem:
                self._invalid[id(t)] = problem
                continue
            desired[_key(t)] = t

        for key in [k for k in self._slots if k not in desired]:
            applog.info(f"Stopping tunnel {self._slots[key].tunnel.name}")
            self._stop_slot(self._slots[key])
            del self._slots[key]

        for key, tunnel in desired.items():
            slot = self._slots.get(key)
            if slot is None:
                slot = self._slots[key] = _Slot(tunnel=tunnel)
            slot.tunnel = tunnel   # picks up renames and environment changes without a restart
            self._step(slot, cfg)

    def _step(self, slot: _Slot, cfg: AppConfig) -> None:
        t, now = slot.tunnel, time.time()
        if slot.proc is not None and slot.proc.poll() is None:
            # Safety first: a tunnel must only ever listen on this computer. If anything else shows up, close it.
            if netcheck.is_exposed(t.local_port):
                applog.error(f"Tunnel {t.name}: port {t.local_port} is open to the network. Closing the session.")
                self._stop_slot(slot)
                slot.refreshing, slot.backoff, slot.next_start = False, 60, now + 60
                self._set(slot, TunnelStates.ERROR,
                          f"Stopped for safety: port {t.local_port} was reachable from the network, not only from this computer.")
                return
            # Fallback detection: process alive and the local port is listening => the tunnel is up.
            if (slot.state == TunnelStates.STARTING or slot.refreshing) and netcheck.is_listening(t.local_port):
                slot.refreshing, slot.failures = False, 0
                self._set(slot, TunnelStates.CONNECTED, f"Connected. Local port {t.local_port} is ready.")
            elif slot.refreshing and now - slot.started_at > 25:
                slot.refreshing = False    # the quiet renewal is not coming up: now it is worth telling the person
                self._set(slot, TunnelStates.STARTING, "Reconnecting...")
            elif slot.state == TunnelStates.STARTING and now - slot.started_at > 45:
                self._set(slot, TunnelStates.STARTING, slot.last_error or
                          "Still connecting... check the server is online in SSM and you are signed in to AWS.")
            return

        if slot.proc is not None:
            # The session ended. Usually that is normal: AWS closes an idle port-forward session, and a session also ends
            # when the client using it disconnects. Those are renewed quietly.
            code = slot.proc.returncode
            ran_for = now - slot.started_at
            was_up = slot.state == TunnelStates.CONNECTED
            for stream in (slot.proc.stdout, slot.proc.stderr):
                try:
                    stream and stream.close()
                except (OSError, ValueError):
                    pass
            slot.proc = None
            if slot.last_error:
                slot.refreshing = False
                slot.backoff = 5 if ran_for > 60 else min(slot.backoff * 2, 60)
                slot.next_start = now + slot.backoff
                self._set(slot, TunnelStates.ERROR, f"{slot.last_error} Retrying in {slot.backoff}s.")
                applog.warn(f"Tunnel {t.name} exited with code {code}: {slot.last_error}")
            elif was_up and ran_for > 20:
                slot.failures, slot.backoff, slot.next_start, slot.refreshing = 0, 5, now, True
                applog.info(f"Tunnel {t.name}: session ended after {int(ran_for / 60)} min (code {code}); renewing it quietly.")
            else:
                slot.failures += 1       # ended without ever being healthy: retry a couple of times without alarm
                slot.backoff = min(2 * slot.failures, 60)
                slot.next_start = now + slot.backoff
                applog.warn(f"Tunnel {t.name} ended after {int(ran_for)}s with code {code} (attempt {slot.failures}).")
                if slot.failures >= 3:
                    slot.refreshing = False
                    slot.backoff = min(slot.backoff * 2, 60)
                    slot.next_start = now + slot.backoff
                    self._set(slot, TunnelStates.RETRYING,
                              f"Cannot keep a connection open (code {code}). Check the server is online in SSM and you are "
                              f"signed in to AWS. Trying again in {slot.backoff}s.")
                elif not slot.refreshing:
                    self._set(slot, TunnelStates.STARTING, "Reconnecting...")

        if time.time() >= slot.next_start:
            self._start_slot(slot)

    def _start_slot(self, slot: _Slot) -> None:
        t = slot.tunnel
        slot.last_error = ""
        if not aws_cli.find_aws():
            self._set(slot, TunnelStates.ERROR, "The AWS CLI is not installed. Click Add / change servers in the dashboard to install it.")
            slot.next_start = time.time() + 10
            return
        if not aws_cli.find_plugin():
            self._set(slot, TunnelStates.ERROR, "The Session Manager plugin is not installed. Click Add / change servers in the dashboard to install it.")
            slot.next_start = time.time() + 10
            return

        # Never start on a port another program already owns: it could answer in place of the server and the dashboard would
        # wrongly show "connected" (and SSH would send the login to it).
        if netcheck.is_listening(t.local_port):
            if slot.refreshing and slot.failures < 3:   # the old session is still letting go of the port
                slot.failures += 1
                slot.next_start = time.time() + 2
                return
            slot.backoff = min(slot.backoff * 2, 60)
            slot.next_start = time.time() + slot.backoff
            self._set(slot, TunnelStates.ERROR,
                      aws_cli.friendly_error("address already in use", t.local_port) + f" Trying again in {slot.backoff}s.")
            return

        try:
            cmd = aws_cli.build_args(["ssm", "start-session", "--target", t.instance_id,
                                      "--document-name", "AWS-StartPortForwardingSession",
                                      "--parameters", f"portNumber={t.remote_port},localPortNumber={t.local_port}"],
                                     t.profile, t.region)
            p = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                                 encoding="utf-8", errors="replace", env=aws_cli.build_env(), start_new_session=True,
                                 preexec_fn=_die_with_parent)
        except Exception as ex:   # noqa: BLE001
            slot.backoff = min(slot.backoff * 2, 60)
            slot.next_start = time.time() + slot.backoff
            self._set(slot, TunnelStates.ERROR, aws_cli.friendly_error(str(ex), t.local_port))
            applog.error(f"Could not start tunnel {t.name}: {ex}")
            return
        slot.proc, slot.started_at = p, time.time()
        for stream, is_err in ((p.stdout, False), (p.stderr, True)):
            threading.Thread(target=self._read, args=(slot, p, stream, is_err), daemon=True).start()
        if not slot.refreshing:
            self._set(slot, TunnelStates.STARTING, "Opening secure session to the server...")
        applog.info(f"Started tunnel {t.name}: 127.0.0.1:{t.local_port} -> {t.instance_id}:{t.remote_port}")

    def _read(self, slot: _Slot, proc: subprocess.Popen, stream, is_err: bool) -> None:
        try:
            for line in stream:
                if proc is slot.proc:
                    self._on_line(slot, line.strip(), is_err)
        except (OSError, ValueError):
            pass
        finally:
            self._wake.set()    # a session that ended is noticed straight away

    def _on_line(self, slot: _Slot, line: str, is_err: bool) -> None:
        if not line:
            return
        low = line.lower()
        with self._lock:
            port = slot.tunnel.local_port
            if "waiting for connections" in low or "opened for sessionid" in low:
                slot.last_error, slot.refreshing, slot.failures = "", False, 0
                self._set(slot, TunnelStates.CONNECTED, f"Connected. Local port {port} is ready.")
                applog.info(f"[{slot.tunnel.name}] {line}")
            elif is_err or "an error occurred" in low:
                slot.last_error = aws_cli.friendly_error(line, port)
                applog.warn(f"[{slot.tunnel.name}] {line}")
            else:
                applog.info(f"[{slot.tunnel.name}] {line}")

    def _set(self, slot: _Slot, state: str, message: str) -> None:
        if slot.state == state and slot.message == message:
            return
        if slot.state != state:
            slot.since = time.time()
        slot.state, slot.message = state, message
        self._publish()

    def _statuses(self) -> list[TunnelStatus]:
        out: list[TunnelStatus] = []
        for t in (self._cfg.tunnels if self._cfg else []):
            s = self._slots.get(_key(t))
            if s:
                out.append(TunnelStatus(t.name, t.instance_id, t.local_port, s.state, s.message, s.since))
            elif (why := self._invalid.get(id(t))):
                out.append(TunnelStatus(t.name, t.instance_id, t.local_port, TunnelStates.ERROR, why, time.time()))
            else:
                out.append(TunnelStatus(t.name, t.instance_id, t.local_port, TunnelStates.DISABLED, "Turned off.", time.time()))
        return out

    def _publish(self) -> None:
        status_store.save(self._statuses())
        self._notify()

    @staticmethod
    def _stop_slot(slot: _Slot) -> None:
        p, slot.proc = slot.proc, None
        if p and p.poll() is None:
            try:
                os.killpg(p.pid, signal.SIGTERM)   # the CLI and the plugin run in their own process group
                try:
                    p.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(p.pid, signal.SIGKILL)
            except (ProcessLookupError, PermissionError, OSError):
                pass
        if p:
            for stream in (p.stdout, p.stderr):
                try:
                    stream and stream.close()
                except (OSError, ValueError):
                    pass
