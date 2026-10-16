"""Remembers what discovery found (discovery.json) so the server list is not lost between runs, works out what changed
since the previous scan, and runs the once-a-day passive rescan."""

from __future__ import annotations

import json
import threading
import time
from dataclasses import asdict, dataclass, field
from datetime import date, datetime

from . import applog, aws_cli, aws_profiles, paths, sso
from .models import AppConfig, DiscoveredServer

_gate = threading.RLock()


@dataclass
class ScanRecord:
    profile: str = ""
    region: str = ""
    scanned: float = 0.0       # last successful listing (epoch seconds)
    attempted: float = 0.0     # last try; a failed try (expired sign-in) is repeated after an hour, not every minute
    last_error: str = ""
    servers: list[DiscoveredServer] = field(default_factory=list)


@dataclass
class DiscoveryState:
    scans: list[ScanRecord] = field(default_factory=list)
    pending_changes: list[str] = field(default_factory=list)
    changes_found: float = 0.0


def load() -> DiscoveryState:
    with _gate:
        try:
            raw = json.loads(paths.discovery_path().read_text(encoding="utf-8"))
            st = DiscoveryState(pending_changes=list(raw.get("pending_changes", [])), changes_found=raw.get("changes_found", 0.0))
            for s in raw.get("scans", []):
                st.scans.append(ScanRecord(s.get("profile", ""), s.get("region", ""), s.get("scanned", 0.0),
                                           s.get("attempted", 0.0), s.get("last_error", ""),
                                           [DiscoveredServer.from_dict(x) for x in s.get("servers", [])]))
            return st
        except (OSError, ValueError, TypeError, AttributeError):
            return DiscoveryState()


def _save(state: DiscoveryState) -> None:
    try:
        paths.write_private(paths.discovery_path(), json.dumps(asdict(state), indent=2))
    except OSError as ex:
        applog.warn(f"Could not save the server cache: {ex}")


def _same(r: ScanRecord, profile: str, region: str) -> bool:
    return r.profile.lower() == profile.lower() and r.region.lower() == region.lower()


def get(profile: str, region: str) -> ScanRecord | None:
    return next((r for r in load().scans if _same(r, profile, region)), None)


def update(profile: str, region: str, servers: list[DiscoveredServer], configured_ids: set[str] | None = None,
           record_changes: bool = True) -> list[str]:
    """Stores a successful scan. Returns the differences from the previous scan (none for an account's first scan)."""
    with _gate:
        state = load()
        old = next((r for r in state.scans if _same(r, profile, region)), None)
        changes = [] if old is None or not old.scanned else diff(old.servers, servers, profile, region, configured_ids)
        if old is None:
            old = ScanRecord(profile=profile, region=region)
            state.scans.append(old)
        old.servers, old.scanned, old.attempted, old.last_error = servers, time.time(), time.time(), ""
        if record_changes and changes:
            state.pending_changes += changes
            state.changes_found = time.time()
        _save(state)
        return changes


def forget(profile: str) -> None:
    """Drops everything remembered about an account (used when the account is removed)."""
    with _gate:
        state = load()
        before = len(state.scans)
        state.scans = [r for r in state.scans if r.profile.lower() != profile.lower()]
        if len(state.scans) != before:
            _save(state)


def record_failure(profile: str, region: str, error: str) -> None:
    with _gate:
        state = load()
        r = next((x for x in state.scans if _same(x, profile, region)), None)
        if r is None:
            r = ScanRecord(profile=profile, region=region)
            state.scans.append(r)
        r.attempted, r.last_error = time.time(), error
        _save(state)


def peek_pending() -> list[str]:
    return load().pending_changes


def take_pending() -> list[str]:
    with _gate:
        state = load()
        out = list(state.pending_changes)
        state.pending_changes, state.changes_found = [], 0.0
        _save(state)
        return out


def diff(before: list[DiscoveredServer], after: list[DiscoveredServer], profile: str, region: str,
         configured_ids: set[str] | None) -> list[str]:
    acct = f"[{profile} / {region}] "
    tag = lambda s: " (you have a connection for it)" if configured_ids and s.instance_id in configured_ids else ""   # noqa: E731
    was, now = {s.instance_id: s for s in before}, {s.instance_id: s for s in after}
    changes: list[str] = []
    for s in after:
        if s.instance_id not in was:
            changes.append(f"{acct}New server: {s.name} ({s.instance_id}, {s.platform})"
                           f"{'' if s.ssm_online else ', not reachable through SSM yet'}")
    for s in before:
        if s.instance_id not in now:
            changes.append(f"{acct}No longer running: {s.name} ({s.instance_id}){tag(s)}")
    for n in after:
        o = was.get(n.instance_id)
        if o is None:
            continue
        if o.ssm_online != n.ssm_online:
            changes.append(f"{acct}{n.name}: {'is now reachable through SSM' if n.ssm_online else 'can no longer be reached through SSM'}{tag(n)}")
        if o.key_name != n.key_name:
            changes.append(f"{acct}{n.name}: key pair changed from '{o.key_name}' to '{n.key_name}'. The saved key file may no longer work{tag(n)}")
        if o.name != n.name:
            changes.append(f"{acct}Server renamed: '{o.name}' is now '{n.name}'{tag(n)}")
        if o.private_ip != n.private_ip:
            changes.append(f"{acct}{n.name}: private IP changed from {o.private_ip} to {n.private_ip}")
        if o.os_name and n.os_name and o.os_name.lower() != n.os_name.lower():
            changes.append(f"{acct}{n.name}: operating system changed from {o.os_name} to {n.os_name}. The login user may differ{tag(n)}")
    return changes


# ---- the once-a-day passive rescan ------------------------------------------------------------
def is_due(r: ScanRecord | None, now: datetime | None = None) -> bool:
    """True when this account has not been scanned today, and no failed try happened within the last hour."""
    now = now or datetime.now()
    if r is None:
        return True
    if r.scanned and datetime.fromtimestamp(r.scanned).date() == now.date():
        return False
    if r.last_error and time.time() - r.attempted < 3600:
        return False
    return True


def watched_accounts(cfg: AppConfig) -> list[tuple[str, str]]:
    """The accounts to watch: those with saved connections, the one last used in setup, and every other AWS account under
    the same company sign-in."""
    base = [(t.profile, t.region) for t in cfg.tunnels if t.profile.strip() and t.region.strip()]
    if cfg.profile.strip() and cfg.region.strip():
        base.append((cfg.profile, cfg.region))
    out = list(base)
    try:
        everyone = aws_profiles.list_profiles()
        preferred = {t.profile for t in cfg.tunnels}
        for profile, region in base:
            p = next((x for x in everyone if x.name == profile), None)
            if p and p.kind == aws_profiles.SSO:
                out += [(m.name, region) for m in aws_profiles.same_sign_in(p, everyone, preferred)]
    except Exception as ex:   # noqa: BLE001
        applog.warn(f"Could not list the organization accounts to watch: {ex}")
    return list(dict.fromkeys(out))


class Monitor:
    last_sync: date | None = None
    need_sign_in: list[str] = []

    @classmethod
    def run(cls, cfg: AppConfig, force: bool = False, cancelled=lambda: False) -> list[str]:
        """Scans every watched account that is due (all when `force`). Never prompts and never opens a browser: an expired
        sign-in is skipped and tried again later. Returns the new differences."""
        found: list[str] = []
        if force or cls.last_sync != date.today():
            try:
                sync = sso.sync_members()
                cls.need_sign_in = sync.need_sign_in
                if not sync.need_sign_in:
                    cls.last_sync = date.today()
                found += [f"New AWS account found under your sign-in: {a}" for a in sync.added]
            except Exception as ex:   # noqa: BLE001
                applog.warn(f"Looking for new AWS accounts failed: {ex}")
        configured = {t.instance_id for t in cfg.tunnels}
        for profile, region in watched_accounts(cfg):
            if cancelled():
                break
            if not force and not is_due(get(profile, region)):
                continue
            try:
                servers = aws_cli.discover(profile, region)
                found += update(profile, region, servers, configured)
                applog.info(f"Server scan {profile}/{region}: {len(servers)} servers.")
            except Exception as ex:   # noqa: BLE001
                record_failure(profile, region, str(ex))
                applog.warn(f"Server scan {profile}/{region} skipped: {ex}")
        return found
