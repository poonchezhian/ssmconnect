"""Opens the tools a person connects with: a terminal running ssh, the file manager over sftp://, a Remote Desktop client,
plus detection of what is installed so the dashboard only offers what works."""

from __future__ import annotations

import os
import shlex
import shutil
import subprocess

from . import applog
from .models import TunnelConfig

# (executable, arguments placed before the command to run). The first one found is used.
_TERMINALS: list[tuple[str, list[str]]] = [
    ("ptyxis", ["--new-window", "--"]),                # Fedora 41+ default
    ("kgx", ["--"]),                                   # GNOME Console
    ("gnome-terminal", ["--"]),
    ("konsole", ["-e"]),
    ("xfce4-terminal", ["-x"]),
    ("mate-terminal", ["-x"]),
    ("lxterminal", ["-e"]),
    ("tilix", ["-e"]),
    ("kitty", []),
    ("alacritty", ["-e"]),
    ("wezterm", ["start", "--"]),
    ("foot", []),
    ("terminator", ["-x"]),
    ("xterm", ["-e"]),
    ("x-terminal-emulator", ["-e"]),
]
_RDP_CLIENTS = ["xfreerdp3", "xfreerdp", "sdl-freerdp3", "sdl-freerdp", "remmina"]


def find_terminal() -> tuple[str, list[str]] | None:
    pref = os.environ.get("TERMINAL", "").strip()
    if pref and shutil.which(pref.split()[0]):
        known = dict(_TERMINALS)
        base = os.path.basename(pref.split()[0])
        return pref.split()[0], known.get(base, ["-e"])
    for exe, args in _TERMINALS:
        if shutil.which(exe):
            return exe, args
    return None


def have_ssh() -> bool:
    return shutil.which("ssh") is not None


def have_file_manager() -> bool:
    return shutil.which("xdg-open") is not None or shutil.which("gio") is not None


def find_rdp_client() -> str | None:
    return next((c for c in _RDP_CLIENTS if shutil.which(c)), None)


def _spawn(cmd: list[str]) -> None:
    subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)


def open_terminal(alias: str) -> None:
    """Runs `ssh <alias>` in the person's terminal. The terminal stays open if the connection fails, so errors can be read."""
    term = find_terminal()
    if not term:
        raise RuntimeError("No terminal program was found. Install one (for example GNOME Terminal), or use 'Copy ssh command'.")
    exe, args = term
    shell_cmd = f"ssh {shlex.quote(alias)}; status=$?; if [ $status -ne 0 ]; then echo; read -r -p 'ssh ended with an error. Press Enter to close.' _; fi"
    cmd = [exe, *args, "bash", "-c", shell_cmd]
    applog.info("Opening terminal: " + " ".join(shlex.quote(c) for c in cmd))
    _spawn(cmd)


def open_files(user: str, alias: str) -> None:
    """Browse the server in the desktop file manager (sftp://). ssh_config supplies the port and key."""
    url = f"sftp://{user}@{alias}/"
    for cmd in (["gio", "open", url], ["xdg-open", url]):
        if shutil.which(cmd[0]):
            _spawn(cmd)
            return
    raise RuntimeError("No file manager was found to open the server's files.")


def open_rdp(t: TunnelConfig) -> None:
    """Opens Remote Desktop to a Windows server through its tunnel (127.0.0.1:<local port>)."""
    client = find_rdp_client()
    if not client:
        raise RuntimeError("No Remote Desktop client was found. Install Remmina or FreeRDP (xfreerdp), then try again.")
    user = t.user_name.strip() or "Administrator"
    if client == "remmina":
        cmd = [client, "-c", f"rdp://{user}@127.0.0.1:{t.local_port}"]
    else:
        cmd = [client, f"/v:127.0.0.1:{t.local_port}", f"/u:{user}", "/cert:tofu", "/dynamic-resolution", "+clipboard"]
    _spawn(cmd)


def open_url(url: str) -> None:
    if not url.startswith(("https://", "http://")):
        return
    for exe in ("xdg-open", "gio"):
        if shutil.which(exe):
            _spawn([exe, "open", url] if exe == "gio" else [exe, url])
            return


def open_file(path: str) -> None:
    if shutil.which("xdg-open"):
        _spawn(["xdg-open", path])


def tools_summary() -> dict[str, str | None]:
    t = find_terminal()
    return {"ssh": shutil.which("ssh"), "terminal": t[0] if t else None, "rdp": find_rdp_client(),
            "files": shutil.which("xdg-open") or shutil.which("gio")}
