"""Works out what kind of server something is (Production, Staging ...) from its AWS tags first; the name is only a guess."""

from __future__ import annotations

import re
from typing import Iterable

_KEY_PRIORITY = ("environment", "env", "stage", "tier", "appenv", "app-env", "deployment", "lifecycle", "landscape", "purpose")


def _tokens(s: str) -> list[str]:
    return [t for t in re.split(r"[^a-z0-9]+", s.lower()) if t]


def from_tags(tags: Iterable[tuple[str, str]]) -> str | None:
    """Picks the environment tag (any letter case, also names like 'app:environment'). None when there is none."""
    usable = [(k, v) for k, v in tags if v and v.strip() and not k.lower().startswith("aws:")]
    for want in _KEY_PRIORITY:
        for k, v in usable:
            if k.strip().lower() == want:
                return normalise(v)
    for k, v in usable:
        kl = k.strip().lower()
        if kl.endswith((":environment", "-environment", "_environment", ":env", "-env", "_env")):
            return normalise(v)
    return None


def classify(tags: Iterable[tuple[str, str]], name: str) -> str:
    """Tag first, then a careful guess from the name, otherwise empty."""
    return from_tags(tags) or _guess_from_name(name) or ""


def is_prod(label: str | None) -> bool:
    """True only for real production. 'non-prod', 'pre-prod' and names that merely contain 'prod' are not."""
    return normalise(label or "") == "Production"


def normalise(value: str) -> str:
    """Maps free text such as 'prod', 'PRD', 'non-production' or 'uat2' to a short standard label."""
    v = value.strip()
    if not v:
        return ""
    t = [re.sub(r"\d+$", "", k) or k for k in _tokens(v)]   # "uat2" and "prod01" mean "uat" and "prod"
    joined = "".join(t)

    def has(*words: str) -> bool:
        return any(k in words for k in t) or joined in words

    if has("nonprod", "nonproduction", "preprod", "preproduction") or ("non" in t and has("prod", "production")) \
            or ("pre" in t and has("prod", "production")):
        return "Pre-production"
    if has("prod", "production", "prd", "live"):
        return "Production"
    if has("uat"):
        return "UAT"
    if has("stg", "stage", "staging"):
        return "Staging"
    if has("qa", "test", "testing", "tst", "sit"):
        return "Test"
    if has("dev", "development"):
        return "Development"
    if has("dr", "disasterrecovery"):
        return "DR"
    if has("sandbox", "sbx", "demo", "poc"):
        return "Sandbox"
    if has("internal"):
        return "Internal"
    return v[:24] if len(v) > 24 else v[0].upper() + v[1:]


def _guess_from_name(name: str) -> str | None:
    t = set(_tokens(name))
    if not t:
        return None
    if t & {"nonprod", "preprod"}:
        return "Pre-production"
    if t & {"prod", "production", "prd"}:
        return "Production"
    if "uat" in t:
        return "UAT"
    if t & {"staging", "stg"}:
        return "Staging"
    if t & {"qa", "test"}:
        return "Test"
    if t & {"dev", "development"}:
        return "Development"
    return None
