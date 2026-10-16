"""Guided company sign-in (IAM Identity Center): sign in, then list the accounts and roles the person may use."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Callable

from . import applog, aws_cli, aws_profiles, paths


@dataclass(frozen=True)
class SsoAccount:
    id: str
    name: str = ""

    def __str__(self) -> str:
        return f"{self.name}  ({self.id})" if self.name.strip() else self.id


def login(session: str) -> aws_cli.CliResult:
    """Opens the browser sign-in for a named sso-session."""
    return aws_cli.run(["sso", "login", "--sso-session", session], timeout=180)


def read_access_token(session: str) -> str | None:
    """The access token the AWS CLI cached after sign-in (file name = SHA-1 of the session name)."""
    try:
        f = paths.aws_dir() / "sso" / "cache" / (hashlib.sha1(session.encode()).hexdigest() + ".json")
        data = json.loads(f.read_text(encoding="utf-8"))
        exp = data.get("expiresAt")
        if exp:
            when = datetime.fromisoformat(exp.replace("Z", "+00:00"))
            if when.tzinfo is None:
                when = when.replace(tzinfo=timezone.utc)
            if when < datetime.now(timezone.utc):
                return None
        return data.get("accessToken") or None
    except (OSError, ValueError):
        return None


def list_accounts(token: str, sso_region: str) -> list[SsoAccount]:
    r = aws_cli.run(["sso", "list-accounts", "--access-token", token, "--region", sso_region,
                     "--query", "accountList[].[accountId,accountName]", "--output", "json"], timeout=60)
    if not r.ok:
        raise RuntimeError(aws_cli.friendly_error(r.error))
    accounts = [SsoAccount(row[0] or "", row[1] if len(row) > 1 and row[1] else "") for row in json.loads(r.output or "[]")]
    return sorted(accounts, key=lambda a: a.name.lower())


def list_roles(token: str, sso_region: str, account_id: str) -> list[str]:
    r = aws_cli.run(["sso", "list-account-roles", "--access-token", token, "--region", sso_region, "--account-id", account_id,
                     "--query", "roleList[].roleName", "--output", "json"], timeout=60)
    if not r.ok:
        raise RuntimeError(aws_cli.friendly_error(r.error))
    return sorted(x for x in json.loads(r.output or "[]") if x)


def add_all_accounts(session: str, token: str, sso_region: str, preferred_role: str, accounts: list[SsoAccount],
                     progress: Callable[[str], None] | None = None) -> list[tuple[str, SsoAccount]]:
    """Saves one profile for every account the person can use under this sign-in, so all can be searched for servers.
    Accounts that already have a profile are reused; the same role is used wherever it exists, else the account's first role."""
    everyone = aws_profiles.list_profiles()
    start_url, _ = aws_profiles.session_info(session)
    key = "sso:" + aws_profiles.normalise_url(start_url)
    existing = [p for p in everyone if p.kind == aws_profiles.SSO and p.account_id and
                (p.sso_session == session or (start_url and aws_profiles.primary_key(p) == key))]
    taken = {p.name for p in everyone}
    result: list[tuple[str, SsoAccount]] = []
    for i, a in enumerate(accounts, 1):
        if progress:
            progress(f"Setting up account {i} of {len(accounts)}: {a.name or a.id}...")
        have = next((p for p in existing if p.account_id == a.id), None)
        if have:
            result.append((have.name, a))
            continue
        try:
            roles = list_roles(token, sso_region, a.id)
        except RuntimeError:
            continue   # no role in this account for this person: skip it
        if not roles:
            continue
        role = preferred_role if preferred_role in roles else roles[0]
        name = aws_profiles.profile_name_for(a.name, a.id, taken)
        aws_profiles.save_sso_profile(name, session, a.id, role)
        taken.add(name)
        result.append((name, a))
    return result


@dataclass
class SyncResult:
    added: list[str] = field(default_factory=list)
    need_sign_in: list[str] = field(default_factory=list)


def sync_members() -> SyncResult:
    """Looks, for every company sign-in that is still signed in, for AWS accounts gained since last time and saves a profile
    for each. Never opens a browser: an expired sign-in is reported in need_sign_in instead."""
    res = SyncResult()
    everyone = aws_profiles.list_profiles()
    groups: dict[str, list[aws_profiles.ProfileInfo]] = {}
    for p in everyone:
        if p.kind == aws_profiles.SSO and p.sso_session:
            groups.setdefault(aws_profiles.primary_key(p), []).append(p)
    for group in groups.values():
        session = token = None
        for s in dict.fromkeys(p.sso_session for p in group):
            token = read_access_token(s)
            if token:
                session = s
                break
        label = aws_profiles.primary_name(group[0])
        if not token or not session:
            res.need_sign_in.append(label)
            continue
        try:
            region = aws_profiles.session_info(session)[1]
            if not region:
                continue
            known = {p.account_id for p in group if p.account_id}
            fresh = [a for a in list_accounts(token, region) if a.id not in known]
            if not fresh:
                continue
            role = next((p.role_name for p in group if p.role_name), "")
            made = add_all_accounts(session, token, region, role, fresh)
            res.added += [f"{a.name} ({a.id})" for _, a in made]
        except Exception as ex:   # noqa: BLE001 - one broken sign-in must not stop the others
            applog.warn(f"Could not look for new AWS accounts under {label}: {ex}")
    return res
