"""Which local TCP ports are in use, read from /proc/net (no extra tools, works the same on every distribution)."""

from __future__ import annotations

import ipaddress
import socket
import struct

_LISTEN = "0A"


def _parse(path: str, v6: bool) -> list[tuple[str, int]]:
    out: list[tuple[str, int]] = []
    try:
        with open(path, encoding="ascii") as fh:
            next(fh, None)
            for line in fh:
                parts = line.split()
                if len(parts) < 4 or parts[3] != _LISTEN:
                    continue
                addr_hex, port_hex = parts[1].split(":")
                if v6:
                    raw = bytes.fromhex(addr_hex)
                    addr = socket.inet_ntop(socket.AF_INET6, b"".join(struct.pack("<I", struct.unpack(">I", raw[i:i + 4])[0]) for i in range(0, 16, 4)))
                else:
                    addr = socket.inet_ntoa(struct.pack("<I", int(addr_hex, 16)))
                out.append((addr, int(port_hex, 16)))
    except (OSError, ValueError, StopIteration):
        pass
    return out


def listeners() -> list[tuple[str, int]]:
    return _parse("/proc/net/tcp", False) + _parse("/proc/net/tcp6", True)


def is_listening(port: int) -> bool:
    return any(p == port for _, p in listeners())


def is_loopback(addr: str) -> bool:
    try:
        ip = ipaddress.ip_address(addr)
        return ip.is_loopback or (isinstance(ip, ipaddress.IPv6Address) and ip.ipv4_mapped is not None and ip.ipv4_mapped.is_loopback)
    except ValueError:
        return False


def is_exposed(port: int) -> bool:
    """True when something listens on the port on an address other than this computer's own loopback."""
    return any(p == port and not is_loopback(a) for a, p in listeners())


def free_port(start: int, taken: set[int]) -> int:
    """The first port from `start` that no program is using and no saved connection has."""
    used = {p for _, p in listeners()} | taken
    port = max(start, 1024)
    while port in used and port < 65535:
        port += 1
    return port
