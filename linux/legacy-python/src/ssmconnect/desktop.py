"""Desktop integration: the launcher entry, 'start when I sign in', and the full clean-up used by Help > Uninstall."""

from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

from . import APP_ID, paths, ssh_config


def _exec_line(extra: str = "") -> str:
    exe = shutil.which("ssmconnect") or f"{sys.executable} -m ssmconnect"
    return f"{exe} {extra}".strip()


def _entry(extra: str = "") -> str:
    return (
        "[Desktop Entry]\nType=Application\nName=SSMConnect\n"
        "Comment=SSH and Remote Desktop to AWS servers over Session Manager\n"
        f"Exec={_exec_line(extra)}\nIcon={APP_ID}\nTerminal=false\n"
        "Categories=Network;RemoteAccess;\nStartupNotify=true\nKeywords=aws;ssm;ssh;rdp;tunnel;\n"
    )


def autostart_enabled() -> bool:
    return paths.autostart_file().exists()


def set_autostart(on: bool) -> None:
    f = paths.autostart_file()
    if on:
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text(_entry("--background") + "X-GNOME-Autostart-enabled=true\n", encoding="utf-8")
    else:
        f.unlink(missing_ok=True)


def create_launcher() -> Path:
    """Adds SSMConnect to the application menu for this user (useful when run from a tarball or a source checkout)."""
    f = paths.applications_dir() / f"{APP_ID}.desktop"
    f.parent.mkdir(parents=True, exist_ok=True)
    f.write_text(_entry(), encoding="utf-8")
    return f


def cleanup_user(keep_settings: bool = False) -> list[str]:
    """Removes only the current user's SSMConnect traces: the ssh include, autostart, launcher and (optionally kept) settings.
    The user's own ssh hosts, AWS profiles and servers in AWS are never touched. Returns what was done."""
    done: list[str] = []
    ssh_config.remove()
    done.append("Disconnected SSMConnect from your ssh configuration")
    for f in (paths.autostart_file(), paths.applications_dir() / f"{APP_ID}.desktop"):
        if f.exists():
            f.unlink()
            done.append(f"Removed {f}")
    if not keep_settings:
        for d in (paths.config_dir(), paths.state_dir()):
            shutil.rmtree(d, ignore_errors=True)
        done.append("Removed your SSMConnect settings and log")
    return done
