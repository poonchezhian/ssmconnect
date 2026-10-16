"""Installs the AWS CLI v2 and the Session Manager plugin for the current user only, with no administrator rights.

Both come straight from Amazon over HTTPS (awscli.amazonaws.com and the session-manager-downloads bucket) and are placed in
~/.local/share/ssmconnect, with links in ~/.local/share/ssmconnect/bin. If your distribution packages them, prefer your
package manager; SSMConnect uses whichever it finds first.

KNOWN LIMIT: downloads are not yet checked against Amazon's detached signature (tracked in CHANGELOG.md)."""

from __future__ import annotations

import io
import os
import platform
import shutil
import subprocess
import tarfile
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from typing import Callable

from . import applog, aws_cli, paths

Progress = Callable[[str], None]

_ALLOWED_HOSTS = ("https://awscli.amazonaws.com/", "https://s3.amazonaws.com/session-manager-downloads/")


def _arch() -> str:
    m = platform.machine().lower()
    if m in ("x86_64", "amd64"):
        return "x86_64"
    if m in ("aarch64", "arm64"):
        return "aarch64"
    raise RuntimeError(f"This computer's processor ({m}) is not supported by Amazon's Linux downloads.")


def aws_cli_url() -> str:
    return f"https://awscli.amazonaws.com/awscli-exe-linux-{_arch()}.zip"


def plugin_url() -> str:
    folder = "ubuntu_64bit" if _arch() == "x86_64" else "ubuntu_arm64"
    return f"https://s3.amazonaws.com/session-manager-downloads/plugin/latest/{folder}/session-manager-plugin.deb"


def _download(url: str, progress: Progress | None, what: str) -> bytes:
    if not url.startswith(_ALLOWED_HOSTS):
        raise RuntimeError("Refusing to download from an unexpected address.")
    if progress:
        progress(f"Downloading {what}...")
    req = urllib.request.Request(url, headers={"User-Agent": "SSMConnect-Linux"})
    with urllib.request.urlopen(req, timeout=60) as r:   # noqa: S310 - host checked above
        total = int(r.headers.get("Content-Length") or 0)
        buf, got = io.BytesIO(), 0
        while chunk := r.read(256 * 1024):
            buf.write(chunk)
            got += len(chunk)
            if progress and total:
                progress(f"Downloading {what}... {got * 100 // total}%")
    return buf.getvalue()


def _extract_zip(data: bytes, dest: Path) -> None:
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        root = dest.resolve()
        for info in z.infolist():
            target = (dest / info.filename).resolve()
            if root not in target.parents and target != root:
                raise RuntimeError("The download contains an unsafe path.")
            z.extract(info, dest)
            mode = info.external_attr >> 16
            if mode and not info.is_dir():
                os.chmod(target, mode & 0o755 | 0o600)


def install_aws_cli(progress: Progress | None = None) -> None:
    data_dir = paths.data_dir()
    bin_dir = data_dir / "bin"
    bin_dir.mkdir(parents=True, exist_ok=True)
    data = _download(aws_cli_url(), progress, "the AWS CLI")
    if progress:
        progress("Installing the AWS CLI...")
    with tempfile.TemporaryDirectory(prefix="ssmconnect-awscli-") as tmp:
        _extract_zip(data, Path(tmp))
        script = Path(tmp) / "aws" / "install"
        r = subprocess.run([str(script), "-i", str(data_dir / "aws-cli"), "-b", str(bin_dir), "--update"],
                           capture_output=True, text=True, timeout=300)
        if r.returncode != 0:
            raise RuntimeError("The AWS CLI installer failed: " + (r.stderr.strip() or r.stdout.strip())[:300])
    applog.info("Installed the AWS CLI to " + str(data_dir / "aws-cli"))


def _deb_data(deb: bytes) -> tarfile.TarFile:
    """Reads the data.tar.* member of a .deb (an `ar` archive) without dpkg."""
    if not deb.startswith(b"!<arch>\n"):
        raise RuntimeError("The plugin download is not a valid package.")
    pos = 8
    while pos + 60 <= len(deb):
        name = deb[pos:pos + 16].decode().strip().rstrip("/")
        size = int(deb[pos + 48:pos + 58].decode().strip())
        body = deb[pos + 60:pos + 60 + size]
        if name.startswith("data.tar"):
            return tarfile.open(fileobj=io.BytesIO(body), mode="r:*")
        pos += 60 + size + (size % 2)
    raise RuntimeError("The plugin package has no program files in it.")


def install_plugin(progress: Progress | None = None) -> None:
    data_dir = paths.data_dir()
    deb = _download(plugin_url(), progress, "the Session Manager plugin")
    if progress:
        progress("Installing the Session Manager plugin...")
    with _deb_data(deb) as tar:
        member = next((m for m in tar.getmembers() if m.isfile() and m.name.endswith("/bin/session-manager-plugin")), None)
        if member is None:
            raise RuntimeError("The plugin package did not contain the plugin program.")
        src = tar.extractfile(member)
        assert src is not None
        target = data_dir / "plugin" / "bin" / "session-manager-plugin"
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_name(target.name + ".tmp")
        with open(tmp, "wb") as fh:
            shutil.copyfileobj(src, fh)
        os.chmod(tmp, 0o755)
        os.replace(tmp, target)
    link = data_dir / "bin" / "session-manager-plugin"
    link.parent.mkdir(parents=True, exist_ok=True)
    if link.is_symlink() or link.exists():
        link.unlink()
    link.symlink_to(target)
    applog.info("Installed the Session Manager plugin to " + str(target))


def install_missing(progress: Progress | None = None) -> list[str]:
    """Installs whatever is missing. Returns what was installed; raises RuntimeError with a plain message on failure."""
    done: list[str] = []
    try:
        if not aws_cli.find_aws():
            install_aws_cli(progress)
            done.append("AWS CLI")
        if not aws_cli.find_plugin():
            install_plugin(progress)
            done.append("Session Manager plugin")
    except (OSError, urllib.error.URLError, zipfile.BadZipFile, tarfile.TarError, subprocess.SubprocessError) as ex:
        raise RuntimeError(f"Could not install the missing components ({ex}). Check your internet connection, "
                           "or install them with your package manager.") from ex
    return done
