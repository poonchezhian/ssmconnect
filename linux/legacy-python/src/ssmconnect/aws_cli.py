"""Thin, safe wrapper around the AWS CLI. Every value is validated elsewhere and the CLI is always started with an
argument list, never a command string."""

from __future__ import annotations

import json
import os
import platform
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from . import classifier, paths
from .aws_profiles import COMMON_REGIONS
from .models import DiscoveredServer, TunnelConfig


@dataclass
class CliResult:
    exit_code: int
    output: str = ""
    error: str = ""

    @property
    def ok(self) -> bool:
        return self.exit_code == 0


def machine_dir() -> str:
    """Folder name AWS uses for this CPU in its Linux downloads."""
    m = platform.machine().lower()
    return "arm64" if m in ("aarch64", "arm64") else "64bit"


def _search_path() -> str:
    extra = [str(paths.data_dir() / "bin"), str(Path.home() / ".local" / "bin"),
             "/usr/local/aws-cli/v2/current/bin", "/usr/local/sessionmanagerplugin/bin", "/usr/local/bin"]
    return os.pathsep.join(extra + [os.environ.get("PATH", "")])


def _which(name: str, fixed: list[str]) -> str | None:
    for f in fixed:
        if os.access(f, os.X_OK) and os.path.isfile(f):
            return f
    return shutil.which(name, path=_search_path())


def find_aws() -> str | None:
    return _which("aws", [str(paths.data_dir() / "bin" / "aws")])


def find_plugin() -> str | None:
    return _which("session-manager-plugin", [str(paths.data_dir() / "bin" / "session-manager-plugin")])


def build_env() -> dict[str, str]:
    env = dict(os.environ)
    env["PATH"] = _search_path()      # lets `aws` find the Session Manager plugin
    env["AWS_PAGER"] = ""
    env.pop("AWS_PROFILE", None)      # the profile is always passed explicitly
    return env


def build_args(args: list[str], profile: str = "", region: str = "") -> list[str]:
    aws = find_aws()
    if not aws:
        raise FileNotFoundError("AWS CLI is not installed.")
    cmd = [aws, *args]
    if profile.strip():
        cmd += ["--profile", profile]
    if region.strip():
        cmd += ["--region", region]
    return cmd


def run(args: list[str], profile: str = "", region: str = "", timeout: float = 60) -> CliResult:
    try:
        cmd = build_args(args, profile, region)
    except FileNotFoundError as ex:
        return CliResult(-1, "", str(ex))
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, env=build_env(),
                           stdin=subprocess.DEVNULL, encoding="utf-8", errors="replace")
        return CliResult(p.returncode, p.stdout.strip(), p.stderr.strip())
    except subprocess.TimeoutExpired:
        return CliResult(-1, "", "Timed out waiting for the AWS CLI.")
    except OSError as ex:
        return CliResult(-1, "", str(ex))


def list_profiles() -> list[str]:
    if not find_aws():
        return []
    r = run(["configure", "list-profiles"], timeout=20)
    return [x.strip() for x in r.output.splitlines() if x.strip()] if r.ok else []


def sso_login(profile: str) -> CliResult:
    """Opens the browser sign-in for an SSO profile."""
    return run(["sso", "login"], profile=profile, timeout=180)


def who_am_i(profile: str, region: str = "") -> CliResult:
    """Checks the sign-in. STS needs some region, so a neutral one is used when none is chosen yet."""
    return run(["sts", "get-caller-identity", "--output", "json"], profile=profile, region=region or "us-east-1", timeout=30)


def list_regions(profile: str) -> list[str]:
    """Region names this account can use; falls back to the common list when AWS cannot be asked."""
    r = run(["ec2", "describe-regions", "--query", "Regions[].RegionName", "--output", "json"],
            profile=profile, region="us-east-1", timeout=30)
    if r.ok:
        try:
            names = sorted(x for x in json.loads(r.output) if isinstance(x, str) and x)
            if names:
                return names
        except ValueError:
            pass
    return sorted(COMMON_REGIONS)


def _cell(row: list, i: int) -> str:
    return row[i] if len(row) > i and isinstance(row[i], str) else ""


def discover(profile: str, region: str) -> list[DiscoveredServer]:
    """Lists running EC2 instances and marks which ones SSM can actually reach. Raises RuntimeError with a plain message."""
    ssm = run(["ssm", "describe-instance-information", "--query",
               "InstanceInformationList[].[InstanceId,PingStatus,PlatformType,PlatformName]", "--output", "json"],
              profile, region, 60)
    if not ssm.ok:
        raise RuntimeError(friendly_error(ssm.error))
    ec2 = run(["ec2", "describe-instances", "--filters", "Name=instance-state-name,Values=running", "--query",
               "Reservations[].Instances[].[InstanceId,PrivateIpAddress,Tags[?Key=='Name']|[0].Value,KeyName,Tags]",
               "--output", "json"], profile, region, 60)
    if not ec2.ok:
        raise RuntimeError(friendly_error(ec2.error))

    prints: dict[str, str] = {}   # optional: without ec2:DescribeKeyPairs, key files are matched by name
    kp = run(["ec2", "describe-key-pairs", "--query", "KeyPairs[].[KeyName,KeyFingerprint]", "--output", "json"],
             profile, region, 30)
    if kp.ok:
        try:
            for row in json.loads(kp.output):
                prints[_cell(row, 0)] = _cell(row, 1)
        except ValueError:
            pass

    managed = {_cell(r, 0): (_cell(r, 1) == "Online", _cell(r, 2), _cell(r, 3)) for r in json.loads(ssm.output or "[]")}
    result = []
    for row in json.loads(ec2.output or "[]"):
        iid = _cell(row, 0)
        online, platform_, os_name = managed.get(iid, (False, "", ""))
        name = _cell(row, 2).strip() or iid
        tags = [(t.get("Key", ""), t.get("Value", "") or "") for t in (row[4] if len(row) > 4 and isinstance(row[4], list) else [])
                if isinstance(t, dict) and t.get("Key")]
        result.append(DiscoveredServer(
            name=name, profile=profile, environment=classifier.classify(tags, name), instance_id=iid,
            private_ip=_cell(row, 1), platform=platform_ or "Linux", key_name=_cell(row, 3),
            key_fingerprint=prints.get(_cell(row, 3), ""), ssm_online=online, os_name=os_name))
    result.sort(key=lambda s: (not s.ssm_online, s.name.lower()))
    return result


def friendly_error(raw: str, local_port: int | None = None) -> str:
    """Turns raw AWS CLI / Session Manager errors into a plain-language status a developer can act on."""
    t = raw or ""
    low = t.lower()

    def has(*xs: str) -> bool:
        return any(x.lower() in low for x in xs)

    if has("TargetNotConnected"):
        return ("Server is offline in SSM: the SSM agent is not running or the server has no SSM instance role. "
                "Check it in AWS Systems Manager > Fleet Manager.")
    if has("InvalidInstanceId") or (has("is not valid") and has("instance")):
        return "That server ID was not found in this AWS account and region. Check the region, or run setup again."
    if has("RegisterClient") or (has("InvalidRequestException") and has("sso")):
        return ("AWS did not accept the company sign-in URL or its region. Check the URL (like https://yourcompany.awsapps.com/start) "
                "and the region of the sign-in page with your AWS admin.")
    if has("Token has expired", "ExpiredToken", "sso login", "SSO session", "SSO token",
           "The security token included in the request is expired"):
        return "Your AWS sign-in has expired. Sign in again: AWS > Sign in again in the dashboard."
    if has("Unable to locate credentials", "NoCredentialProviders"):
        return "No AWS credentials found for this account. Use AWS > Sign in again, or add the account with AWS > Add AWS account."
    if has("could not be found") and has("profile"):
        return "That AWS account profile does not exist on this computer. Open AWS > Add AWS account and add it again."
    if has("ssm:StartSession") or (has("not authorized to perform") and has("ssm")):
        return "Your AWS role is not allowed to start SSM sessions (needs ssm:StartSession on this server). Ask your AWS admin."
    if has("UnauthorizedOperation", "AccessDenied"):
        return ("Your AWS role does not have permission for this action. Ask your AWS admin "
                "(needs ec2:DescribeInstances, ssm:DescribeInstanceInformation, ssm:StartSession).")
    if has("SessionManagerPlugin is not found") or (has("session-manager-plugin") and has("not found", "no such file")):
        return "The Session Manager plugin is missing. Click Add / change servers to install it."
    if has("address already in use", "bind: address"):
        where = f"Local port {local_port}" if local_port else "The local port"
        return f"{where} is already used by another program. Run setup again to pick a free port."
    if has("Could not connect to the endpoint", "Connect timeout", "Name or service not known", "getaddrinfo",
           "Temporary failure in name resolution"):
        return "Cannot reach AWS. Check your internet connection, VPN or proxy, and the region name."
    if has("ThrottlingException", "Rate exceeded"):
        return "AWS is rate-limiting requests. SSMConnect will retry automatically."
    first = next((l.strip() for l in t.splitlines() if l.strip()), "")
    return first or "AWS CLI returned an unknown error."


def get_windows_password(t: TunnelConfig, pem_file: str) -> str:
    """Decrypts a Windows server's Administrator password with the key pair's .pem file (AWS stores it encrypted)."""
    r = run(["ec2", "get-password-data", "--instance-id", t.instance_id, "--priv-launch-key", pem_file,
             "--query", "PasswordData", "--output", "text"], t.profile, t.region, 60)
    if not r.ok:
        raise RuntimeError(friendly_error(r.error))
    pw = r.output.strip()
    if not pw or pw == "None":
        raise RuntimeError("AWS has no generated password for this server (it may use a domain login or a password you set). "
                           "Use the password you know.")
    return pw
