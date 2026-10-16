"""Reads and writes AWS profiles directly in ~/.aws/config and ~/.aws/credentials, so profiles can be listed and created
without the AWS CLI and without any interactive `aws configure`. Only the sections SSMConnect touches are rewritten;
everything else is preserved and a .ssmconnect.bak copy is kept."""

from __future__ import annotations

import os
import re
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlparse

from . import paths
from .models import AppConfig

_NAME_RULE = re.compile(r"^[A-Za-z0-9_.@\-]{1,64}$")

COMMON_REGIONS = [
    "us-east-1", "us-east-2", "us-west-1", "us-west-2", "ap-south-1", "ap-south-2", "ap-southeast-1", "ap-southeast-2",
    "ap-southeast-3", "ap-northeast-1", "ap-northeast-2", "ap-northeast-3", "ap-east-1", "ca-central-1", "eu-central-1",
    "eu-central-2", "eu-west-1", "eu-west-2", "eu-west-3", "eu-north-1", "eu-south-1", "me-south-1", "me-central-1",
    "sa-east-1", "af-south-1",
]

SSO, KEYS, OTHER = "sso", "keys", "other"


@dataclass(frozen=True)
class ProfileInfo:
    name: str
    kind: str = OTHER
    region: str | None = None
    account_id: str | None = None
    role_name: str | None = None
    sso_session: str | None = None
    start_url: str | None = None

    @property
    def display(self) -> str:
        if self.kind == SSO:
            return f"{self.name}   (company sign-in{', account ' + self.account_id if self.account_id else ''})"
        if self.kind == KEYS:
            return f"{self.name}   (access keys)"
        return self.name


def is_valid_name(name: str | None) -> bool:
    return bool(name and _NAME_RULE.match(name))


# ---- reading ---------------------------------------------------------------------------------
def read_ini(path: Path) -> dict[str, dict[str, str]]:
    result: dict[str, dict[str, str]] = {}
    try:
        cur: dict[str, str] | None = None
        for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
            line = raw.strip()
            if not line or line[0] in "#;":
                continue
            if line[0] == "[" and line[-1] == "]":
                cur = result.setdefault(line[1:-1].strip(), {})
                continue
            if cur is not None and "=" in line:
                k, _, v = line.partition("=")
                cur[k.strip().lower()] = v.strip()
    except OSError:
        pass   # unreadable file: treat as empty
    return result


def list_profiles() -> list[ProfileInfo]:
    cfg, cred = read_ini(paths.aws_config_path()), read_ini(paths.aws_credentials_path())
    names: list[str] = []
    for s in cfg:
        if s == "default":
            names.append("default")
        elif s.startswith("profile "):
            names.append(s[8:].strip())
    names += [s for s in cred if s not in names]

    out = []
    for n in sorted(dict.fromkeys(names), key=str.lower):
        c = cfg.get("default" if n == "default" else "profile " + n, {})
        k = cred.get(n, {})
        get = lambda d, key: d.get(key) or None   # noqa: E731
        session, start = get(c, "sso_session"), get(c, "sso_start_url")
        if session and ("sso-session " + session) in cfg and not start:
            start = get(cfg["sso-session " + session], "sso_start_url")
        if session or start:
            kind = SSO
        elif get(k, "aws_access_key_id") or get(c, "aws_access_key_id"):
            kind = KEYS
        else:
            kind = OTHER
        out.append(ProfileInfo(n, kind, get(c, "region"), get(c, "sso_account_id"), get(c, "sso_role_name"), session, start))
    return out


def find(name: str | None) -> ProfileInfo | None:
    return next((p for p in list_profiles() if p.name == name), None) if name else None


def normalise_url(url: str | None) -> str:
    """The sign-in address reduced to its host, so nicknames differing in letter case or a trailing slash count as one."""
    if not url or not url.strip():
        return ""
    u = url.strip()
    host = urlparse(u).hostname
    return host.lower() if host else u.rstrip("/#").lower()


def primary_key(p: ProfileInfo) -> str:
    """Identifies the primary account (one company sign-in, or one access-key login) a profile belongs to."""
    if p.kind != SSO:
        return "key:" + p.name
    host = normalise_url(p.start_url)
    return "sso:" + (host or (p.sso_session or p.name).lower())


def legacy_keys(p: ProfileInfo) -> list[str]:
    if p.kind != SSO:
        return []
    keys = []
    if p.sso_session:
        keys.append("sso:" + p.sso_session)
    if p.start_url:
        keys.append("sso:" + p.start_url)
    keys.append("sso:" + p.name)
    return keys


def primary_name(p: ProfileInfo) -> str:
    """A short friendly name for the primary account: the sign-in's nickname, or the company part of its address."""
    if p.kind != SSO:
        return p.name
    if p.sso_session and p.sso_session.strip():
        return p.sso_session
    host = urlparse(p.start_url or "").hostname
    return host.split(".")[0] if host else p.name


def primary_label(cfg: AppConfig, p: ProfileInfo, everyone: list[ProfileInfo]) -> str:
    key = primary_key(p)
    if (a := cfg.aliases.get(key, "")).strip():
        return a
    for m in [x for x in everyone if primary_key(x) == key] + [p]:
        for k in legacy_keys(m):
            if (b := cfg.aliases.get(k, "")).strip():
                return b
    return primary_name(p)


def one_each(group: list[ProfileInfo], preferred: set[str] | None = None, favourite: str | None = None) -> list[ProfileInfo]:
    """One profile per AWS account: of several pointing at the same account keep the one with saved connections,
    then the chosen one, then the shortest name."""
    sets: dict[str, list[ProfileInfo]] = {}
    for x in group:
        sets.setdefault("n:" + x.name if not x.account_id else "a:" + x.account_id, []).append(x)
    best = [min(s, key=lambda x: (not (preferred and x.name in preferred), not (favourite and x.name == favourite),
                                  len(x.name), x.name.lower())) for s in sets.values()]
    return sorted(best, key=lambda x: x.name.lower())


def same_sign_in(p: ProfileInfo, everyone: list[ProfileInfo], preferred: set[str] | None = None) -> list[ProfileInfo]:
    """The saved profiles sharing this profile's company sign-in (the other AWS accounts reachable with it)."""
    if p.kind != SSO:
        return [p]
    key = primary_key(p)
    group = [x for x in everyone if primary_key(x) == key]
    if not any(x.name == p.name for x in group):
        group.append(p)
    return one_each(group, preferred, p.name)


def session_info(session: str) -> tuple[str | None, str | None]:
    """The address and region a named company sign-in (sso-session) is set up with."""
    d = read_ini(paths.aws_config_path()).get("sso-session " + session)
    return (d.get("sso_start_url") or None, d.get("sso_region") or None) if d else (None, None)


def profile_name_for(account_name: str, account_id: str, taken: set[str]) -> str:
    slug = re.sub(r"[^A-Za-z0-9_.@\-]+", "-", account_name or "").strip("-")
    if not slug:
        slug = "account-" + account_id
    if len(slug) > 56:
        slug = slug[:56].strip("-")
    lowered = {t.lower() for t in taken}
    return slug + "-" + account_id[-4:] if slug.lower() in lowered else slug


# ---- writing ---------------------------------------------------------------------------------
def save_sso_session(session: str, start_url: str, sso_region: str, with_scopes: bool = True) -> None:
    values = {"sso_start_url": start_url, "sso_region": sso_region}
    if with_scopes:
        values["sso_registration_scopes"] = "sso:account:access"
    _upsert(paths.aws_config_path(), "sso-session " + session, values, None if with_scopes else ["sso_registration_scopes"])


def save_sso_profile(profile: str, session: str, account_id: str, role_name: str) -> None:
    _upsert(paths.aws_config_path(), "profile " + profile, {
        "sso_session": session, "sso_account_id": account_id, "sso_role_name": role_name, "output": "json"})


def save_access_keys(profile: str, key_id: str, secret: str) -> None:
    if not re.fullmatch(r"[A-Z0-9]{16,128}", key_id):
        raise ValueError("That does not look like an AWS access key ID (20 letters and digits, starting with AKIA or ASIA).")
    if not re.fullmatch(r"[A-Za-z0-9/+=]{30,128}", secret):
        raise ValueError("That does not look like an AWS secret access key (about 40 characters).")
    _upsert(paths.aws_credentials_path(), profile, {"aws_access_key_id": key_id, "aws_secret_access_key": secret})
    _upsert(paths.aws_config_path(), "profile " + profile, {"output": "json"})


def save_region(profile: str, region: str) -> None:
    _upsert(paths.aws_config_path(), "default" if profile == "default" else "profile " + profile, {"region": region})


def remove_profile(profile: str) -> None:
    """Removes one account profile (and, for access keys, its stored keys). A copy of each file is kept as .ssmconnect.bak."""
    _delete_section(paths.aws_config_path(), "default" if profile == "default" else "profile " + profile)
    _delete_section(paths.aws_credentials_path(), profile)


def remove_sso_session(session: str) -> None:
    """Call only when no remaining profile uses it."""
    _delete_section(paths.aws_config_path(), "sso-session " + session)


def _backup(path: Path) -> None:
    try:
        data = path.read_bytes()
        bak = path.with_name(path.name + ".ssmconnect.bak")
        bak.write_bytes(data)
        os.chmod(bak, 0o600)
    except OSError:
        pass


def _write(path: Path, lines: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)   # credentials must stay private
    with os.fdopen(fd, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    os.replace(tmp, path)


def _read_lines(path: Path) -> list[str]:
    try:
        return path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []


def _section_end(lines: list[str], start: int) -> int:
    end = start + 1
    while end < len(lines) and not lines[end].lstrip().startswith("["):
        end += 1
    return end


def _delete_section(path: Path, section: str) -> None:
    if not path.exists():
        return
    lines = _read_lines(path)
    header = f"[{section}]"
    start = next((i for i, l in enumerate(lines) if l.strip() == header), -1)
    if start < 0:
        return
    _backup(path)
    del lines[start:_section_end(lines, start)]
    while 0 < start < len(lines) and not lines[start - 1].strip() and not lines[start].strip():
        del lines[start]
    _write(path, lines)


def _upsert(path: Path, section: str, values: dict[str, str], remove: list[str] | None = None) -> None:
    for v in values.values():
        if "\n" in v or "\r" in v:
            raise ValueError("Line breaks are not allowed in AWS settings.")
    lines = _read_lines(path)
    if path.exists():
        _backup(path)
    header = f"[{section}]"
    start = next((i for i, l in enumerate(lines) if l.strip() == header), -1)
    if start < 0:
        if lines and lines[-1].strip():
            lines.append("")
        lines.append(header)
        lines += [f"{k} = {v}" for k, v in values.items()]
    else:
        end = _section_end(lines, start)
        if remove:
            for i in range(end - 1, start, -1):
                if "=" in lines[i] and lines[i].split("=", 1)[0].strip().lower() in {r.lower() for r in remove}:
                    del lines[i]
                    end -= 1
        for k, v in values.items():
            idx = next((i for i in range(start + 1, end)
                        if "=" in lines[i] and lines[i].split("=", 1)[0].strip().lower() == k.lower()), -1)
            text = f"{k} = {v}"
            if idx >= 0:
                lines[idx] = text
            else:
                lines.insert(end, text)
                end += 1
    _write(path, lines)
