"""Per-user locations, following the XDG Base Directory specification. Nothing is shared between users."""

from __future__ import annotations

import os
from pathlib import Path


def _xdg(var: str, fallback: str) -> Path:
    value = os.environ.get(var, "")
    return Path(value) if value and os.path.isabs(value) else Path.home() / fallback


def config_dir() -> Path:
    """config.json, discovery.json, update-check.json."""
    return _xdg("XDG_CONFIG_HOME", ".config") / "ssmconnect"


def state_dir() -> Path:
    """status.json and the log file."""
    return _xdg("XDG_STATE_HOME", ".local/state") / "ssmconnect"


def data_dir() -> Path:
    """Tools SSMConnect installed for the user (AWS CLI, Session Manager plugin)."""
    return _xdg("XDG_DATA_HOME", ".local/share") / "ssmconnect"


def autostart_file() -> Path:
    return _xdg("XDG_CONFIG_HOME", ".config") / "autostart" / "io.github.poonchezhian.SSMConnect.desktop"


def applications_dir() -> Path:
    return _xdg("XDG_DATA_HOME", ".local/share") / "applications"


def config_path() -> Path:
    return config_dir() / "config.json"


def status_path() -> Path:
    return state_dir() / "status.json"


def discovery_path() -> Path:
    return config_dir() / "discovery.json"


def log_path() -> Path:
    return state_dir() / "ssmconnect.log"


def aws_dir() -> Path:
    return Path.home() / ".aws"


def aws_config_path() -> Path:
    value = os.environ.get("AWS_CONFIG_FILE", "")
    return Path(value) if value else aws_dir() / "config"


def aws_credentials_path() -> Path:
    value = os.environ.get("AWS_SHARED_CREDENTIALS_FILE", "")
    return Path(value) if value else aws_dir() / "credentials"


def ssh_dir() -> Path:
    return Path.home() / ".ssh"


def write_private(path: Path, text: str) -> None:
    """Atomic write with owner-only permissions (these files can name servers and keys)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as fh:
        fh.write(text)
    os.replace(tmp, path)
