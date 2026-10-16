# SSMConnect for Linux

SSH and Remote Desktop to AWS servers **with port 22 closed**, through AWS Systems Manager (SSM) Session Manager.
This is the native Linux edition (GTK 4 + libadwaita). The Windows edition lives on the `main` branch; this branch
only ever adds files under `linux/` and `.github/workflows/linux-*.yml`, so merging or pushing never overwrites it.

> **Status:** early iteration (0.0.x). Try it against a non-production server first.

## How it works

```
ssh ssc-web-1  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

SSMConnect keeps one tunnel per server open as you (never root), and writes `Host ssc-<name>` entries to
`~/.ssh/ssmconnect.conf`. After that every OpenSSH-based tool works: `ssh`, `scp`, `rsync -e ssh`, VS Code Remote-SSH,
and the file manager via `sftp://`.

## Requirements

- Linux with Python 3.10+, GTK 4, libadwaita 1 and PyGObject (Fedora: `python3-gobject gtk4 libadwaita`;
  Debian/Ubuntu: `python3-gi gir1.2-gtk-4.0 gir1.2-adw-1`)
- OpenSSH client; optionally FreeRDP or Remmina for Windows servers
- AWS CLI v2 and the Session Manager plugin: `ssmconnect install-tools` installs both for your user, no root needed

## Command line

```
ssmconnect                 open the app
ssmconnect doctor          check what is installed
ssmconnect install-tools   install AWS CLI + Session Manager plugin for this user
ssmconnect list | status   saved servers | live state
ssmconnect run             keep connections open in this terminal
ssmconnect scan            look for new or changed servers
ssmconnect cleanup         remove SSMConnect's traces for this user
```

## Where things live (XDG)

| What | Where |
|---|---|
| Settings, server cache | `~/.config/ssmconnect/` |
| Live status, log | `~/.local/state/ssmconnect/` |
| Tools SSMConnect installed | `~/.local/share/ssmconnect/` |
| ssh entries | `~/.ssh/ssmconnect.conf` (+ one `Include` line in `~/.ssh/config`) |

## Develop

```
cd linux
python3 -m unittest discover -s tests
PYTHONPATH=src python3 -m ssmconnect doctor
```

## Versioning

Semantic Versioning, tags `linux-vX.Y.Z`, see [CHANGELOG.md](CHANGELOG.md) and `scripts/bump-version.sh`.

Licensed under the Apache License 2.0 (see `../LICENSE`).
