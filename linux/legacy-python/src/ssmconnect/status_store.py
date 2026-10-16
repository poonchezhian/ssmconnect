"""status.json: the live tunnel state, written by the tunnel manager so other tools (and `ssmconnect status`) can read it."""

from __future__ import annotations

import json
import time
from dataclasses import asdict

from . import paths
from .models import TunnelStatus


def save(tunnels: list[TunnelStatus]) -> None:
    try:
        paths.write_private(paths.status_path(), json.dumps(
            {"updated": time.time(), "tunnels": [asdict(t) for t in tunnels]}, indent=2))
    except OSError:
        pass


def load() -> tuple[float, list[TunnelStatus]]:
    try:
        data = json.loads(paths.status_path().read_text(encoding="utf-8"))
        return float(data.get("updated", 0)), [TunnelStatus(**t) for t in data.get("tunnels", [])]
    except (OSError, ValueError, TypeError):
        return 0.0, []
