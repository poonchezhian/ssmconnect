"""Looks for a newer Linux release on the project's GitHub Releases page (once a day). It only tells the person: the app is
installed by the package manager or the tarball, so upgrading is done the same way. Tags are `linux-vMAJOR.MINOR.PATCH`."""

from __future__ import annotations

import json
import re
import time
import urllib.request
from dataclasses import dataclass

from . import __version__, paths

REPO = "poonchezhian/ssmconnect"
TAG_PREFIX = "linux-v"
RELEASES_PAGE = f"https://github.com/{REPO}/releases"
_INTERVAL = 24 * 3600


@dataclass(frozen=True)
class UpdateInfo:
    version: str
    tag: str
    notes: str
    page_url: str


def parse(v: str) -> tuple[int, int, int] | None:
    m = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", v.strip())
    return (int(m[1]), int(m[2]), int(m[3])) if m else None


def _state_path():
    return paths.config_dir() / "update-check.json"


def _load() -> dict:
    try:
        return json.loads(_state_path().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def due() -> bool:
    return time.time() - _load().get("last_check", 0) >= _INTERVAL


def should_notify(version: str) -> bool:
    """True the first time a given version is found, so the notification appears once per version."""
    st = _load()
    if st.get("notified") == version:
        return False
    st["notified"] = version
    try:
        paths.write_private(_state_path(), json.dumps(st))
    except OSError:
        pass
    return True


def newest_from(releases: list[dict]) -> UpdateInfo | None:
    best: tuple[tuple[int, int, int], dict] | None = None
    for r in releases:
        tag = str(r.get("tag_name", ""))
        if not tag.startswith(TAG_PREFIX) or r.get("draft") or r.get("prerelease"):
            continue
        v = parse(tag[len(TAG_PREFIX):])
        if v and (best is None or v > best[0]):
            best = (v, r)
    if best is None:
        return None
    v, r = best
    return UpdateInfo(".".join(map(str, v)), r["tag_name"], str(r.get("body") or "")[:1500], str(r.get("html_url") or RELEASES_PAGE))


def check(force: bool = False) -> UpdateInfo | None:
    """The newer release, or None (up to date, offline, or checked less than a day ago)."""
    if not force and not due():
        return None
    try:
        req = urllib.request.Request(f"https://api.github.com/repos/{REPO}/releases?per_page=30",
                                     headers={"Accept": "application/vnd.github+json", "User-Agent": "SSMConnect-Linux"})
        with urllib.request.urlopen(req, timeout=15) as r:   # noqa: S310 - fixed https URL
            releases = json.loads(r.read().decode("utf-8"))
    except (OSError, ValueError):
        return None
    st = _load()
    st["last_check"] = time.time()
    try:
        paths.write_private(_state_path(), json.dumps(st))
    except OSError:
        pass
    info = newest_from(releases)
    mine, theirs = parse(__version__), info and parse(info.version)
    return info if info and mine and theirs and theirs > mine else None
