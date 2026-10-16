"""Plain data classes shared by the engine and the interface. JSON names follow the Windows edition (PascalCase-free, lower camel)."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field, fields
from typing import Any

from .classifier import is_prod


def _build(cls, data: dict[str, Any]):
    """Create a dataclass from a dict, ignoring unknown keys so newer files still load."""
    names = {f.name for f in fields(cls)}
    return cls(**{k: v for k, v in (data or {}).items() if k in names})


@dataclass
class TunnelConfig:
    """One SSH-over-SSM tunnel: local 127.0.0.1:local_port -> instance:remote_port."""

    name: str = ""
    instance_id: str = ""
    profile: str = ""          # AWS profile (account) this server is reached with
    region: str = ""
    local_port: int = 0
    remote_port: int = 22
    protocol: str = "ssh"      # "ssh" for Linux servers, "rdp" for Windows servers
    user_name: str = "ec2-user"
    environment: str = ""      # Production, Staging ... from the server's AWS tags
    key_file: str | None = None
    enabled: bool = True
    os_name: str = ""

    @property
    def is_rdp(self) -> bool:
        return self.protocol.lower() == "rdp"

    @property
    def is_prod(self) -> bool:
        return is_prod(self.environment)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "TunnelConfig":
        return _build(cls, data)


@dataclass
class AppConfig:
    profile: str = ""   # account and region last chosen in setup (only pre-selects lists)
    region: str = ""
    tunnels: list[TunnelConfig] = field(default_factory=list)
    aliases: dict[str, str] = field(default_factory=dict)   # friendly account names
    selected_account: str = ""                              # primary-account key shown on the dashboard
    autostart_connections: bool = True

    def alias_or(self, key: str, fallback: str) -> str:
        value = self.aliases.get(key, "")
        return value if value.strip() else fallback

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "AppConfig":
        cfg = _build(cls, {k: v for k, v in (data or {}).items() if k != "tunnels"})
        cfg.tunnels = [TunnelConfig.from_dict(t) for t in (data or {}).get("tunnels", [])]
        return cfg


@dataclass
class DiscoveredServer:
    name: str = ""
    profile: str = ""
    environment: str = ""
    instance_id: str = ""
    private_ip: str = ""
    platform: str = "Linux"
    key_name: str = ""
    key_fingerprint: str = ""
    ssm_online: bool = False
    os_name: str = ""

    @property
    def is_windows(self) -> bool:
        return "windows" in self.platform.lower()

    @property
    def is_prod(self) -> bool:
        return is_prod(self.environment)

    @property
    def suggested_user(self) -> str:
        """The login user that distribution's cloud images use by default."""
        if self.is_windows:
            return "Administrator"
        n = (self.os_name or "").lower()
        for needle, user in (("ubuntu", "ubuntu"), ("debian", "admin"), ("centos", "centos"),
                             ("fedora", "fedora"), ("rocky", "rocky")):
            if needle in n:
                return user
        return "ec2-user"   # Amazon Linux, Red Hat, SUSE, AlmaLinux, Oracle Linux and unknown

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "DiscoveredServer":
        return _build(cls, data)


class TunnelStates:
    STARTING = "Starting"
    CONNECTED = "Connected"
    RETRYING = "Retrying"
    ERROR = "Error"
    DISABLED = "Disabled"


@dataclass
class TunnelStatus:
    name: str = ""
    instance_id: str = ""
    local_port: int = 0
    state: str = TunnelStates.STARTING
    message: str = ""
    since: float = 0.0
