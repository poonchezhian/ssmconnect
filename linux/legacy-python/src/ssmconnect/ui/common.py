"""Small helpers shared by the windows."""

from __future__ import annotations

import threading
from typing import Callable

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk  # noqa: E402


def in_background(work: Callable[[], object], done: Callable[[object, BaseException | None], None] | None = None) -> None:
    """Runs `work` on a thread; `done(result, error)` is called on the GTK main loop."""
    def target() -> None:
        try:
            result, err = work(), None
        except BaseException as ex:   # noqa: BLE001 - reported to the caller, never lost
            result, err = None, ex
        if done:
            GLib.idle_add(lambda: (done(result, err), False)[1])
    threading.Thread(target=target, daemon=True).start()


def plain_error(err: BaseException) -> str:
    return str(err) if isinstance(err, RuntimeError) else f"Something unexpected went wrong: {err}"


def pill(text: str = "", css: str = "") -> Gtk.Label:
    lbl = Gtk.Label(label=text)
    lbl.add_css_class("pill")
    if css:
        lbl.add_css_class(css)
    return lbl


def set_pill(lbl: Gtk.Label, text: str, kind: str) -> None:
    lbl.set_text(text)
    for k in ("ok", "warn", "err"):
        lbl.remove_css_class(k)
    if kind:
        lbl.add_css_class(kind)


def confirm(parent: Gtk.Window, heading: str, body: str, action: str, on_yes: Callable[[], None], destructive: bool = False) -> None:
    d = Adw.AlertDialog(heading=heading, body=body)
    d.add_response("cancel", "Cancel")
    d.add_response("yes", action)
    d.set_response_appearance("yes", Adw.ResponseAppearance.DESTRUCTIVE if destructive else Adw.ResponseAppearance.SUGGESTED)
    d.set_default_response("cancel")
    d.connect("response", lambda _d, r: on_yes() if r == "yes" else None)
    d.present(parent)


def message(parent: Gtk.Window, heading: str, body: str) -> None:
    d = Adw.AlertDialog(heading=heading, body=body)
    d.add_response("ok", "OK")
    d.present(parent)
