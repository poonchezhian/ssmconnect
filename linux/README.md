# SSMConnect for Linux

SSH and Remote Desktop to AWS servers **with port 22 closed**, through AWS Systems Manager (SSM) Session Manager.
This is the native Linux edition (Rust, GTK 4 + libadwaita). The Windows edition lives on the `main` branch; this branch
only ever adds files under `linux/` and `.github/workflows/linux-*.yml`, so merging or pushing never overwrites it.

> **Status:** early iteration (0.0.x). Try it against a non-production server first.

## How it works

```
ssh ssc-web-1  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

SSMConnect keeps one tunnel per server open as you (never root), and writes `Host ssc-<name>` entries to
`~/.ssh/ssmconnect.conf`. After that every OpenSSH-based tool works: `ssh`, `scp`, `rsync -e ssh`, VS Code Remote-SSH,
and the file manager via `sftp://`.

## Install

Download from the [Releases page](https://github.com/poonchezhian/ssmconnect/releases) (tags named `linux-vX.Y.Z`; check `SHA256SUMS.txt`):

| Distribution | Install |
|---|---|
| Fedora, RHEL, openSUSE | `sudo dnf install ./ssmconnect-X.Y.Z-1.x86_64.rpm` |
| Debian 13+, Ubuntu 24.04+, Mint 22+ | `sudo apt install ./ssmconnect_X.Y.Z_amd64.deb` |
| Anything else with GTK 4.12+ and libadwaita 1.5+ | unpack `ssmconnect-X.Y.Z-linux-x86_64.tar.gz`, run `./install.sh` (to `~/.local`, no root) |
| From source | `cargo build --release --locked`, then `packaging/stage.sh "" ~/.local` |

Then open **SSMConnect** from the application menu. The first run starts the setup wizard.

## Requirements

- A compiled, native program: no Python, Electron or Wine. It links GTK 4 (4.12+) and libadwaita (1.5+), which every current desktop distribution ships
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

## Good to know

- There is no system-tray icon: GNOME removed tray support and not every desktop has one. Instead, closing the window keeps your
  connections open in the background (you get one notification), reopening SSMConnect brings the window back, and
  **Quit** in the menu closes everything. **File > Start when I sign in** brings connections back after login.
- Wayland and X11 are both supported (plain GTK 4).
- `ssh ssc-<name>` works from any terminal while SSMConnect is running. Parallel `sftp`/`scp` transfers over one SSM port forward can
  fail; use a single connection.
- Your own `~/.ssh/config` hosts and AWS profiles are never modified, except one `Include` line (with a `.ssmconnect.bak` copy).
  `ssmconnect cleanup` removes it again.

## Troubleshooting

| Symptom | What to do |
|---|---|
| Dashboard says *Tools missing* | `ssmconnect doctor`, then `ssmconnect install-tools` |
| *Sign-in needed* | AWS > Sign in again |
| A server stays red | Read the plain-language reason under its name; most often the SSM agent is offline or the role lacks `ssm:StartSession` |
| Terminal button dimmed | Install a terminal emulator, or use More > Copy ssh command |
| Remote Desktop dimmed | Install Remmina or FreeRDP |
| Log file | Help > Open log file, or `~/.local/state/ssmconnect/ssmconnect.log` |

## Develop

Needs Rust (stable) plus `gtk4-devel` and `libadwaita-devel` (Debian: `libgtk-4-dev libadwaita-1-dev`).

```
cd linux
cargo test                           # unit + integration tests, with a fake AWS CLI
cargo run                            # the app
cargo run --example e2e_wizard       # drives the real wizard end to end (needs a display)
cargo run --example screenshot -- out.png dark   # renders the window to a PNG
packaging/build-tarball.sh           # also build-deb.sh and build-rpm.sh
```

## Versioning

Semantic Versioning, tags `linux-vX.Y.Z` (never `vX.Y.Z`, which belongs to Windows), see [CHANGELOG.md](CHANGELOG.md).
The 0.0.x releases were a Python prototype; 0.1.0 onward is the native Rust edition.

```
scripts/bump-version.sh patch        # or minor / major; edit CHANGELOG.md; commit
git tag -a linux-v0.1.4 -m "SSMConnect for Linux 0.1.4"
git push origin linux --follow-tags  # the workflow builds and publishes the release
```

Licensed under the Apache License 2.0 (see `../LICENSE`).
