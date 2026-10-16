"""Per-user settings in ~/.config/ssmconnect. Nothing is shared between users and nothing runs with elevated rights."""

from __future__ import annotations

import json
import shutil
import time

from . import applog, paths
from .models import AppConfig


def load() -> AppConfig | None:
    """The saved config, an empty one if none exists yet, or None if the file is unreadable."""
    path = paths.config_path()
    try:
        if not path.exists():
            return AppConfig()
        cfg = AppConfig.from_dict(json.loads(path.read_text(encoding="utf-8")))
    except (OSError, ValueError, TypeError):
        return None
    for t in cfg.tunnels:   # settings from before per-server accounts: copy the default onto each tunnel
        t.profile = t.profile or cfg.profile
        t.region = t.region or cfg.region
    return cfg


def load_or_recover() -> tuple[AppConfig, str | None]:
    """Like load(), but a damaged file is copied aside first so nothing is silently lost. Returns (config, warning)."""
    cfg = load()
    if cfg is not None:
        return cfg, None
    path = paths.config_path()
    warning = "Your saved settings file could not be read, so SSMConnect is starting with an empty list."
    try:
        copy = path.with_name(f"config.corrupt-{time.strftime('%Y%m%d-%H%M%S')}.json")
        shutil.copy2(path, copy)
        warning += f" The damaged file was kept as {copy}."
    except OSError:
        pass
    applog.warn(warning)
    return AppConfig(), warning


def save(cfg: AppConfig) -> None:
    path = paths.config_path()
    if path.exists():
        try:
            shutil.copy2(path, path.with_name(path.name + ".bak"))
        except OSError:
            pass
    paths.write_private(path, json.dumps(cfg.to_dict(), indent=2))
