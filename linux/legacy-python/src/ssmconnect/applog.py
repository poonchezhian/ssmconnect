"""A small rolling log in the user's state directory (Help > Open log file)."""

from __future__ import annotations

import threading
from datetime import datetime

from . import paths

_lock = threading.Lock()
_MAX_BYTES = 1_000_000


def _write(level: str, message: str) -> None:
    try:
        with _lock:
            path = paths.log_path()
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.exists() and path.stat().st_size > _MAX_BYTES:
                path.replace(path.with_suffix(".log.1"))
            with path.open("a", encoding="utf-8") as fh:
                fh.write(f"{datetime.now():%Y-%m-%d %H:%M:%S} {level:<5} {message}\n")
    except OSError:
        pass  # logging must never break the app


def info(message: str) -> None:
    _write("INFO", message)


def warn(message: str) -> None:
    _write("WARN", message)


def error(message: str) -> None:
    _write("ERROR", message)
