"""Makes the servers usable from plain OpenSSH, the way the Windows edition configured PuTTY and WinSCP.

SSMConnect writes its own file, ~/.ssh/ssmconnect.conf, with one `Host ssc-<server>` block per SSH server and adds one
`Include` line to ~/.ssh/config (a backup is made first). The user's own hosts are never modified. After setup, any tool
that reads ssh_config works: `ssh ssc-web-1`, `scp`, `rsync -e ssh`, VS Code Remote-SSH, the file manager's sftp:// ..."""

from __future__ import annotations

import os
import re
import shlex
import shutil
from pathlib import Path

from . import paths
from .models import TunnelConfig

INCLUDE_LINE = "Include ~/.ssh/ssmconnect.conf"
MARKER = "# SSMConnect (managed): the next line loads the servers SSMConnect set up. Remove it to disconnect SSMConnect from ssh."
HEADER = "# Written by SSMConnect. Do not edit: this file is rewritten whenever you save servers.\n"


def conf_path() -> Path:
    return paths.ssh_dir() / "ssmconnect.conf"


def user_config_path() -> Path:
    return paths.ssh_dir() / "config"


def host_alias(name: str, instance_id: str = "") -> str:
    """A short, shell-friendly name: 'Web Server 1' -> 'ssc-web-server-1'."""
    slug = re.sub(r"[^a-z0-9]+", "-", (name or "").lower()).strip("-")[:40].strip("-")
    if not slug:
        slug = (instance_id or "server").lower()
    return "ssc-" + slug


def _unique_aliases(tunnels: list[TunnelConfig]) -> dict[int, str]:
    seen: dict[str, int] = {}
    result: dict[int, str] = {}
    for i, t in enumerate(tunnels):
        a = host_alias(t.name, t.instance_id)
        if a in seen:
            a = f"{a}-{t.instance_id[-6:] or t.local_port}"
        seen[a] = i
        result[i] = a
    return result


def aliases_for(tunnels: list[TunnelConfig]) -> dict[str, str]:
    """instance_id -> unique alias (what the dashboard shows and `ssh` accepts)."""
    ssh = [t for t in tunnels if not t.is_rdp]
    return {t.instance_id: a for t, a in zip(ssh, _unique_aliases(ssh).values())}


def render(tunnels: list[TunnelConfig]) -> str:
    ssh = [t for t in tunnels if not t.is_rdp and t.enabled]
    out = [HEADER]
    for t, alias in zip(ssh, _unique_aliases(ssh).values()):
        out.append(f"Host {alias}")
        out.append(f"    # {t.name} ({t.instance_id}){' [' + t.environment + ']' if t.environment else ''}")
        out.append("    HostName 127.0.0.1")
        out.append(f"    Port {t.local_port}")
        out.append(f"    User {t.user_name}")
        if t.key_file and _safe_path(t.key_file):
            out.append(f"    IdentityFile {_quote(t.key_file)}")
            out.append("    IdentitiesOnly yes")
        # every tunnel is 127.0.0.1:<port>, so each server needs its own identity in known_hosts
        out.append(f"    HostKeyAlias {alias}-{t.instance_id}")
        out.append("    StrictHostKeyChecking accept-new")
        out.append("    ServerAliveInterval 30")
        out.append("")
    return "\n".join(out)


def _safe_path(p: str) -> bool:
    return "\n" not in p and "\r" not in p


def _quote(p: str) -> str:
    return '"' + p.replace('"', r'\"') + '"' if re.search(r"\s", p) else p


def apply(tunnels: list[TunnelConfig]) -> None:
    """Rewrites ssmconnect.conf and makes sure ~/.ssh/config includes it (a .ssmconnect.bak copy is kept)."""
    d = paths.ssh_dir()
    d.mkdir(mode=0o700, exist_ok=True)
    paths.write_private(conf_path(), render(tunnels))
    cfg = user_config_path()
    text = cfg.read_text(encoding="utf-8", errors="replace") if cfg.exists() else ""
    if INCLUDE_LINE in text:
        return
    if cfg.exists():
        shutil.copy2(cfg, cfg.with_name(cfg.name + ".ssmconnect.bak"))
    # Include must come before any Host/Match block to apply to every host
    paths.write_private(cfg, f"{MARKER}\n{INCLUDE_LINE}\n\n{text}")


def remove() -> None:
    """Disconnects SSMConnect from ssh: deletes ssmconnect.conf and the Include line. The user's own hosts stay."""
    cfg = user_config_path()
    if cfg.exists():
        text = cfg.read_text(encoding="utf-8", errors="replace")
        if INCLUDE_LINE in text:
            shutil.copy2(cfg, cfg.with_name(cfg.name + ".ssmconnect.bak"))
            lines = [l for l in text.splitlines() if l.strip() not in (INCLUDE_LINE, MARKER)]
            body = "\n".join(lines).lstrip("\n")
            paths.write_private(cfg, body + ("\n" if body else ""))
    try:
        conf_path().unlink()
    except FileNotFoundError:
        pass


def alias_of(t: TunnelConfig, everyone: list[TunnelConfig]) -> str:
    """The Host alias written for this server (unique even when two servers share a name)."""
    return aliases_for(everyone).get(t.instance_id) or host_alias(t.name, t.instance_id)


def ssh_command(alias: str) -> list[str]:
    return ["ssh", alias]


def ssh_command_text(alias: str) -> str:
    return " ".join(shlex.quote(x) for x in ssh_command(alias))


def sftp_url(user: str, alias: str) -> str:
    """The alias is used as the host so ssh_config supplies the port and key to the file manager."""
    return f"sftp://{user}@{alias}/"


def expand(path: str) -> str:
    return os.path.expanduser(path)
