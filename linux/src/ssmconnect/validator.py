"""Checks everything read from config.json before it is used to start a process or written into another tool's config."""

from __future__ import annotations

import re
from typing import Iterable

from .models import AppConfig, TunnelConfig

_INSTANCE = re.compile(r"^m?i-[0-9a-f]{8,17}$")
_PROFILE = re.compile(r"^[A-Za-z0-9_.@:+=,\- ]{1,128}$")
_REGION = re.compile(r"^[a-z]{2}(-[a-z]+)+-\d$")
_LOGIN = re.compile(r"^[A-Za-z0-9_.\-]{1,64}$")


def is_valid_region(region: str) -> bool:
    return bool(_REGION.match(region or ""))


def is_valid_login(user: str) -> bool:
    return bool(_LOGIN.match(user or ""))


def validate_global(cfg: AppConfig) -> str | None:
    if cfg.profile.strip() and not _PROFILE.match(cfg.profile):
        return "The AWS profile name contains characters that are not allowed."
    if cfg.region.strip() and not _REGION.match(cfg.region):
        return f"'{cfg.region}' does not look like an AWS region (for example ap-south-1)."
    return None


def validate_tunnel(t: TunnelConfig, everyone: Iterable[TunnelConfig]) -> str | None:
    """None when the tunnel is fine, otherwise a plain-language reason."""
    if not t.profile.strip():
        return "No AWS account is chosen for this server. Use Add / change servers."
    if not _PROFILE.match(t.profile):
        return "The AWS profile name contains characters that are not allowed."
    if not _REGION.match(t.region or ""):
        return f"'{t.region}' does not look like an AWS region (for example ap-south-1). Use Add / change servers."
    if not _INSTANCE.match(t.instance_id or ""):
        return f"'{t.instance_id}' is not a valid EC2 instance ID (it looks like i-0123456789abcdef0)."
    if not 1024 <= t.local_port <= 65535:
        return f"Local port {t.local_port} is not allowed. Use a port from 1024 to 65535."
    if not 1 <= t.remote_port <= 65535:
        return f"Remote port {t.remote_port} is not valid."
    if sum(1 for o in everyone if o.enabled and o.local_port == t.local_port) > 1:
        return f"Local port {t.local_port} is used by more than one connection."
    if not _LOGIN.match(t.user_name or ""):
        return "The login user name contains characters that are not allowed."
    return None
