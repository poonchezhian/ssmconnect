"""Looks for SSH private keys (.pem, id_rsa ...) in the usual places and, on request, in any folder the person picks.
Only the first few hundred bytes of a candidate are read to confirm it is a key. Nothing is copied or uploaded.

Keys are also recognised by content: AWS shows a fingerprint for every key pair, and for an unencrypted RSA key the same
fingerprint can be worked out locally (see `fingerprints`), so a file is matched to its server whatever it is called."""

from __future__ import annotations

import base64
import hashlib
import os
import re
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

_SKIP = {"node_modules", ".git", ".cache", ".cargo", ".rustup", ".npm", ".gradle", ".m2", ".var", "snap", "flatpak",
         ".local", ".mozilla", ".config", "proc", "sys", "dev", "__pycache__", "venv", ".venv", "site-packages"}


@dataclass
class KeyFile:
    path: str
    format: str                       # "pem" or "openssh"
    fingerprints: set[str] = field(default_factory=set)

    @property
    def stem(self) -> str:
        return Path(self.path).stem

    def __str__(self) -> str:
        return f"{Path(self.path).name}   ({Path(self.path).parent})"


def default_folders() -> list[str]:
    home = Path.home()
    names = [".ssh", ".aws", "Downloads", "Desktop", "Documents", "Dropbox", "Google Drive", "keys", ".pem"]
    xdg = {n: os.environ.get(v) for n, v in (("dl", "XDG_DOWNLOAD_DIR"), ("doc", "XDG_DOCUMENTS_DIR"), ("desk", "XDG_DESKTOP_DIR"))}
    out = [home / n for n in names] + [Path(p) for p in xdg.values() if p]
    return [str(p) for p in dict.fromkeys(out) if p.is_dir()]


def scan_standard(limit: float = 15.0, cancelled: Callable[[], bool] | None = None) -> list[KeyFile]:
    found: dict[str, KeyFile] = {}
    stop = time.monotonic() + limit
    for folder in default_folders():
        _walk(folder, 5, found, stop, cancelled)
    return _sorted(found)


def scan_folder(folder: str, limit: float = 30.0, cancelled: Callable[[], bool] | None = None) -> list[KeyFile]:
    found: dict[str, KeyFile] = {}
    _walk(folder, 10, found, time.monotonic() + limit, cancelled)
    return _sorted(found)


def _sorted(d: dict[str, KeyFile]) -> list[KeyFile]:
    return sorted(d.values(), key=lambda k: (k.stem.lower(), k.path.lower()))


def _walk(folder: str, depth: int, found: dict[str, KeyFile], stop: float, cancelled) -> None:
    if depth < 0 or time.monotonic() > stop or (cancelled and cancelled()):
        return
    try:
        with os.scandir(folder) as it:
            entries = sorted(it, key=lambda e: e.name)
    except OSError:
        return
    for e in entries:
        try:
            if e.is_symlink():
                continue
            if e.is_file():
                fmt = classify(e.path)
                if fmt and e.path not in found:
                    found[e.path] = KeyFile(e.path, fmt, fingerprints(e.path) if fmt == "pem" else set())
            elif e.is_dir() and e.name not in _SKIP:
                _walk(e.path, depth - 1, found, stop, cancelled)
        except OSError:
            continue


def classify(file: str) -> str | None:
    """'pem' or 'openssh' when the file really is an unencrypted-or-not private key, otherwise None. PuTTY .ppk files are not
    used on Linux: OpenSSH cannot read them."""
    try:
        name = os.path.basename(file)
        ext = os.path.splitext(name)[1].lower()
        if not (ext in (".pem", ".key", ".cer") or (not ext and name.startswith("id_"))):
            return None
        size = os.path.getsize(file)
        if size < 100 or size > 32 * 1024:
            return None
        with open(file, "r", encoding="utf-8", errors="replace") as fh:
            head = fh.read(256)
        if "BEGIN OPENSSH PRIVATE KEY" in head:
            return "openssh"
        if "PRIVATE KEY-----" in head:
            return "pem"
    except OSError:
        pass
    return None


def find(keys: list[KeyFile], key_name: str, fingerprint: str | None) -> tuple[KeyFile | None, str]:
    """Finds the key file for a server. Most reliable first: the file's fingerprint equals the server's key-pair fingerprint;
    then a file named exactly like the key pair; then a similar name. Also returns how it matched, in plain words."""
    rank = lambda k: 0 if k.format == "pem" else 1   # noqa: E731
    if fingerprint:
        hit = sorted((k for k in keys if fingerprint.lower() in k.fingerprints), key=rank)
        if hit:
            return hit[0], "verified: this key belongs to the server"
    if not key_name.strip():
        return None, ""
    exact = sorted((k for k in keys if k.stem.lower() == key_name.lower()), key=rank)
    if exact:
        return exact[0], "matched by name"

    def norm(s: str) -> str:
        t = re.sub(r"[^a-z0-9]", "", s.lower())
        return t[:-3] if t.endswith("pem") else t

    want = norm(key_name)
    if len(want) >= 4:
        sim = sorted((k for k in keys if (n := norm(k.stem)) == want or
                      (len(n) >= 4 and (n.startswith(want) or want.startswith(n)))), key=rank)
        if sim:
            return sim[0], "similar name - please check"
    return None, ""


# ---- fingerprints -----------------------------------------------------------------------------
# AWS shows the SHA-1 of the PKCS#8 private key for key pairs it created, and the MD5 of the public key for imported ones.
# Both are computed here with a tiny DER reader, so no extra libraries or openssl binary are needed.
_RSA_ALGO = bytes.fromhex("300d06092a864886f70d0101010500")   # SEQUENCE { OID rsaEncryption, NULL }


def _tlv(tag: int, body: bytes) -> bytes:
    n = len(body)
    if n < 0x80:
        head = bytes([tag, n])
    else:
        raw = n.to_bytes((n.bit_length() + 7) // 8, "big")
        head = bytes([tag, 0x80 | len(raw)]) + raw
    return head + body


def _read(buf: bytes, pos: int) -> tuple[int, bytes, int]:
    tag = buf[pos]
    ln = buf[pos + 1]
    pos += 2
    if ln & 0x80:
        k = ln & 0x7F
        ln = int.from_bytes(buf[pos:pos + k], "big")
        pos += k
    return tag, buf[pos:pos + ln], pos + ln


def _children(body: bytes) -> list[tuple[int, bytes]]:
    out, pos = [], 0
    while pos < len(body):
        tag, val, pos = _read(body, pos)
        out.append((tag, val))
    return out


def fingerprints(file: str) -> set[str]:
    """Fingerprints AWS would display for this key pair, or an empty set (passphrase-protected, not RSA, unreadable)."""
    try:
        text = Path(file).read_text(encoding="utf-8", errors="strict")
        if "ENCRYPTED" in text:
            return set()
        m = re.search(r"-----BEGIN ([A-Z ]*)PRIVATE KEY-----(.+?)-----END", text, re.S)
        if not m:
            return set()
        der = base64.b64decode("".join(m.group(2).split()))
        if m.group(1).strip() == "RSA":
            pkcs1 = der
            pkcs8 = _tlv(0x30, _tlv(0x02, b"\x00") + _RSA_ALGO + _tlv(0x04, pkcs1))
        elif m.group(1).strip() == "":
            pkcs8 = der
            _, seq, _ = _read(der, 0)
            kids = _children(seq)
            if len(kids) < 3 or kids[1][1] != _RSA_ALGO[2:]:
                return set()
            pkcs1 = kids[2][1]
        else:
            return set()
        _, seq, _ = _read(pkcs1, 0)
        n, e = _children(seq)[1][1], _children(seq)[2][1]
        pub_pkcs1 = _tlv(0x30, _tlv(0x02, n) + _tlv(0x02, e))
        spki = _tlv(0x30, _RSA_ALGO + _tlv(0x03, b"\x00" + pub_pkcs1))
        colon = lambda h: ":".join(h[i:i + 2] for i in range(0, len(h), 2))   # noqa: E731
        return {colon(hashlib.sha1(pkcs8).hexdigest()), colon(hashlib.md5(spki).hexdigest())}   # noqa: S324 - AWS's format
    except (OSError, ValueError, IndexError, UnicodeDecodeError):
        return set()
