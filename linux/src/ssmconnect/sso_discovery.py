"""Finds the company sign-in URL and region on its own: settings pushed by IT, existing AWS CLI settings, cached sign-in
tokens, and finally (clearly labelled) a guess from the company's own domain."""

from __future__ import annotations

import json
import os
import re
import socket
import urllib.error
import urllib.request
from dataclasses import dataclass, replace
from pathlib import Path

from . import paths

_URL = re.compile(r"^https://[A-Za-z0-9.\-]+\.(awsapps\.com|amazonaws\.com\.cn)(/start)?/?(#/?)?$|^https://[A-Za-z0-9.\-/]+$")


@dataclass(frozen=True)
class SsoCandidate:
    start_url: str
    region: str
    source: str
    guess: bool = False


def _clean(raw: str | None) -> str:
    u = (raw or "").strip()
    return u.split("#", 1)[0].rstrip("/")


def _admin_files() -> list[Path]:
    """Files an IT team can push with its configuration tooling (system-wide, then per user)."""
    return [Path("/etc/ssmconnect/sso.json"), paths.config_dir() / "sso.json"]


def find() -> list[SsoCandidate]:
    found: list[SsoCandidate] = []

    def add(url: str | None, region: str | None, source: str, guess: bool = False) -> None:
        url = _clean(url)
        if not url.lower().startswith("https://") or not _URL.match(url):
            return
        region = (region or "").strip()
        for i, c in enumerate(found):
            if c.start_url.lower() == url.lower():
                if not c.region and region:      # keep the first source, but learn a region
                    found[i] = replace(c, region=region)
                return
        found.append(SsoCandidate(url, region, source, guess))

    # 1) pushed by the company's IT team
    for f in _admin_files():
        try:
            d = json.loads(f.read_text(encoding="utf-8"))
            add(d.get("startUrl"), d.get("region"), f"Set by your IT team ({f})")
        except (OSError, ValueError, AttributeError):
            pass
    add(os.environ.get("SSMCONNECT_SSO_URL"), os.environ.get("SSMCONNECT_SSO_REGION"), "Environment setting")
    add(os.environ.get("AWS_SSO_START_URL"), os.environ.get("AWS_SSO_REGION"), "Environment setting")

    # 2) AWS CLI configuration already on this computer
    for cfg in dict.fromkeys([paths.aws_dir() / "config", paths.aws_config_path()]):
        try:
            section, urls, regs = "", {}, {}
            for raw in cfg.read_text(encoding="utf-8", errors="replace").splitlines():
                line = raw.strip()
                if line.startswith("["):
                    section = line.strip("[]")
                elif "=" in line:
                    k, _, v = line.partition("=")
                    if k.strip() == "sso_start_url":
                        urls[section] = v.strip()
                    elif k.strip() == "sso_region":
                        regs[section] = v.strip()
            for sec, url in urls.items():
                add(url, regs.get(sec, ""), "AWS settings already on this computer")
        except OSError:
            pass

    # 3) sign-in tokens cached by the AWS CLI
    try:
        for f in (paths.aws_dir() / "sso" / "cache").glob("*.json"):
            try:
                d = json.loads(f.read_text(encoding="utf-8"))
                add(d.get("startUrl"), d.get("region"), "Earlier AWS sign-in on this computer")
            except (OSError, ValueError, AttributeError):
                pass
    except OSError:
        pass
    return found


def _domain_names() -> list[str]:
    names: list[str] = []
    fqdn = socket.getfqdn()
    host = socket.gethostname()
    for d in (os.environ.get("DOMAIN"), fqdn if fqdn != host else None):
        parts = [p for p in (d or "").lower().split(".") if p]
        if len(parts) >= 2:
            names += [parts[-2], "-".join(parts)]
            if len(parts) >= 3:
                names.append(parts[-3] + "-" + parts[-2])
    mail = os.environ.get("EMAIL", "")
    if "@" in mail:       # a work address often names the company
        parts = mail.split("@", 1)[1].lower().split(".")
        if len(parts) >= 2:
            names.append(parts[-2])
    return [n for n in dict.fromkeys(names) if re.fullmatch(r"[a-z0-9][a-z0-9-]{1,62}", n)]


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):   # noqa: D401, ANN001
        return None


def guess_from_domain() -> list[SsoCandidate]:
    """Tries https://<company>.awsapps.com/start for names taken from this computer's domain and reports the ones that
    answer. These are guesses and are labelled so; the person confirms by signing in."""
    opener = urllib.request.build_opener(_NoRedirect)
    out: list[SsoCandidate] = []
    for n in _domain_names():
        url = f"https://{n}.awsapps.com/start"
        try:
            with opener.open(urllib.request.Request(url, method="GET"), timeout=6) as r:
                ok = r.status == 200
        except urllib.error.HTTPError as e:
            loc = e.headers.get("Location", "") if e.headers else ""
            ok = e.code in (301, 302, 303, 307) and any(x in loc for x in ("awsapps.com", "amazonaws.com", "signin.aws"))
        except (urllib.error.URLError, OSError):
            ok = False
        if ok:
            out.append(SsoCandidate(url, "", "Guessed from your company domain (please confirm)", True))
    return out
