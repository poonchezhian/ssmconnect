"""Command-line interface. With no command the graphical app opens; the commands below work in a terminal or over ssh."""

from __future__ import annotations

import argparse
import sys
import time

from . import __version__, aws_cli, aws_profiles, config_store, desktop, discovery, launchers, netcheck, status_store
from .tunnel_manager import TunnelManager


def _doctor() -> int:
    ok = True
    for label, found, hint in (
        ("AWS CLI", aws_cli.find_aws(), "run: ssmconnect install-tools"),
        ("Session Manager plugin", aws_cli.find_plugin(), "run: ssmconnect install-tools"),
        ("ssh", launchers.tools_summary()["ssh"], "install OpenSSH client"),
        ("Terminal", launchers.tools_summary()["terminal"], "optional: needed for the Terminal button"),
        ("Remote Desktop client", launchers.tools_summary()["rdp"], "optional: Remmina or FreeRDP, for Windows servers"),
        ("File manager (sftp://)", launchers.tools_summary()["files"], "optional"),
    ):
        print(f"{'ok     ' if found else 'MISSING'}  {label:<24} {found or hint}")
        ok &= bool(found) or "optional" in hint
    profiles = aws_profiles.list_profiles()
    print(f"        AWS profiles             {len(profiles)} found in ~/.aws")
    return 0 if ok else 1


def _list() -> int:
    cfg, warn = config_store.load_or_recover()
    if warn:
        print(warn, file=sys.stderr)
    if not cfg.tunnels:
        print("No servers saved yet. Open SSMConnect and use Add / change servers.")
        return 0
    _, status = status_store.load()
    by_id = {s.instance_id: s for s in status}
    for t in cfg.tunnels:
        s = by_id.get(t.instance_id)
        print(f"{t.name:<32} {t.environment or '-':<15} {'rdp' if t.is_rdp else 'ssh'}  127.0.0.1:{t.local_port:<5} "
              f"{(s.state if s else 'Not running'):<10} {t.instance_id}")
    return 0


def _run() -> int:
    """Keeps the connections open in this terminal until Ctrl+C (no window needed)."""
    mgr = TunnelManager()
    last: list = []

    def show() -> None:
        nonlocal last
        now = [(s.name, s.state, s.message) for s in mgr.snapshot()]
        for row in now:
            if row not in last:
                print(f"{time.strftime('%H:%M:%S')}  {row[0]}: {row[1]} - {row[2]}", flush=True)
        last = now

    mgr.on_change = show
    mgr.start()
    print("SSMConnect: connections are opening. Press Ctrl+C to close them.")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        pass
    mgr.stop()
    return 0


def _scan() -> int:
    cfg, _ = config_store.load_or_recover()
    changes = discovery.Monitor.run(cfg, force=True)
    print("\n".join(changes) if changes else "Scan finished. Nothing changed.")
    return 0


def _install_tools() -> int:
    from . import prereq_installer
    try:
        done = prereq_installer.install_missing(lambda m: print(m, flush=True))
    except RuntimeError as ex:
        print(ex, file=sys.stderr)
        return 1
    print("Installed: " + ", ".join(done) if done else "Everything is already installed.")
    return 0


def _status() -> int:
    when, rows = status_store.load()
    if not rows:
        print("No connections are running (start SSMConnect or `ssmconnect run`).")
        return 0
    print(f"As of {time.strftime('%H:%M:%S', time.localtime(when))}")
    for s in rows:
        up = netcheck.is_listening(s.local_port)
        print(f"{s.name:<32} :{s.local_port:<5} {s.state:<10} {'(port open)' if up else ''} {s.message}")
    return 0


def _cleanup(keep: bool) -> int:
    print("\n".join(desktop.cleanup_user(keep_settings=keep)))
    return 0


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(prog="ssmconnect", description="SSH and Remote Desktop to AWS servers over Session Manager (port 22 closed).")
    p.add_argument("--version", action="version", version=f"SSMConnect for Linux {__version__}")
    p.add_argument("--background", action="store_true", help="start hidden and keep connections open (used by 'start when I sign in')")
    sub = p.add_subparsers(dest="command")
    sub.add_parser("doctor", help="check the tools SSMConnect needs")
    sub.add_parser("install-tools", help="install the AWS CLI and Session Manager plugin for this user")
    sub.add_parser("list", help="list saved servers")
    sub.add_parser("status", help="show live connection status")
    sub.add_parser("run", help="open all connections in this terminal until Ctrl+C")
    sub.add_parser("scan", help="scan every account for new or changed servers")
    c = sub.add_parser("cleanup", help="remove SSMConnect's traces for this user (ssh include, autostart, settings)")
    c.add_argument("--keep-settings", action="store_true")
    args = p.parse_args(argv)

    cmds = {"doctor": _doctor, "install-tools": _install_tools, "list": _list, "status": _status, "run": _run, "scan": _scan}
    if args.command in cmds:
        return cmds[args.command]()
    if args.command == "cleanup":
        return _cleanup(args.keep_settings)
    from .ui.app import run_gui
    return run_gui(background=args.background)
