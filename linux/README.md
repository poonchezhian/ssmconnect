# SSMConnect for Linux

SSH and Remote Desktop to AWS servers **with port 22 closed**, through AWS Systems Manager (SSM) Session Manager.
This is the native Linux edition (Rust, GTK 4 + libadwaita). The Windows edition lives in the same repository (everything outside the `linux/` folder); the two editions
are kept side by side on the `main` branch and are released separately.

> **Status:** 1.0, the first stable release of the native Linux edition. As with any tool that opens connections to your servers, try it against a non-production server first.

## How it works

```
ssh ssc-web-1  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

SSMConnect keeps one tunnel per server open as you (never root), and writes `Host ssc-<name>` entries to
`~/.ssh/ssmconnect.conf`. After that every OpenSSH-based tool works: `ssh`, `scp`, `rsync -e ssh`, VS Code Remote-SSH,
and the file manager via `sftp://`.

## Install

Download from the [Releases page](https://github.com/poonchezhian/ssmconnect/releases) (tags named `linux-vX.Y.Z`). Full steps, updating and removing: [Install and update](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update).

- **Any distribution:** `sh ./ssmconnect-X.Y.Z-linux-x86_64.bin` (or double-click it). The installer fetches what is missing (GTK 4/libadwaita, OpenSSH from your distribution; the AWS CLI and Session Manager plugin from Amazon, signature checked), then installs SSMConnect for you (`~/.local`) or for all users (`--system`). Running a newer `.bin` updates in place and keeps your settings; `ssmconnect uninstall` removes it.
- **Fedora 40+, RHEL 10 and clones, openSUSE Tumbleweed:** `sudo dnf install ./ssmconnect-X.Y.Z-1.x86_64.rpm` (`sudo zypper install` on openSUSE). Older releases (RHEL 9, Leap 15.6) are too old for the rpm: use the `.bin` installer there if their GTK 4 / libadwaita allow, or a newer release.
- **Debian 13+, Ubuntu 24.04+, Mint 22+:** `sudo apt install ./ssmconnect_X.Y.Z_amd64.deb`
- **From source:** `cargo build --release --locked`, then `target/release/ssmconnect --installer`

Package installs also add the signed SSMConnect update repository (`https://poonchezhian.github.io/ssmconnect/`), so new releases arrive with `dnf upgrade` / `apt upgrade` / your software centre.

## Requirements

- A compiled, native program: no Python, Electron or Wine. It links GTK 4 (4.12+) and libadwaita (1.5+), which every current desktop distribution ships
- OpenSSH client; optionally FreeRDP or Remmina for Windows servers
- AWS CLI v2 and the Session Manager plugin, fetched for you from Amazon (`ssmconnect install-tools` does it from a terminal)

## Verifying the download (signed releases)

Every release file (`.bin`, `.deb`, `.rpm`, `SHA256SUMS.txt`) has a detached OpenPGP signature (`<file>.asc`) from the **SSMConnect Linux Releases** key:

```
Fingerprint  FF9F 45E6 4711 7103 89B3  F1FC 7483 B1F8 DBE4 A803
Key ID       7483B1F8DBE4A803      (ed25519, created 2026-10-16, expires 2028-10-15)
```

The public key is [`SIGNING-KEY.asc`](SIGNING-KEY.asc). Step by step: [check the signature](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update#check-the-signature-before-you-run-it).
For maintainers: `scripts/sign-release.sh` signs by hand; in GitHub Actions the release job signs with the repository secret `LINUX_SIGNING_KEY` and publishes the update repository.

## Settings and server identities, all in windows

**File > Preferences** holds every setting in plain language (start-up, terminal and Remote Desktop programs, what *Files* opens, ports, ssh names, updates, the AWS tools,
your data). **Servers > Server identities** lets you confirm a server's identity the first time (and shows a clear warning if it ever changes), so nobody has to edit
`~/.ssh/known_hosts` by hand.

## Security of the tools it installs

`ssmconnect install-tools` (and the setup wizard) download the AWS CLI and the Session Manager plugin straight from Amazon over HTTPS and
verify Amazon's OpenPGP signature on each file against public keys built into SSMConnect, pinned by fingerprint, before anything is
unpacked. A file that is damaged, tampered with or unsigned is refused. See `src/signature.rs`.

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

- A tray icon shows how many connections are open and has a quick menu. GNOME needs the AppIndicator extension for it; without a tray, closing the window keeps your
  connections open in the background (you get one notification), reopening SSMConnect brings the window back, and **Quit** in the menu closes everything.
  **File > Start when I sign in** brings connections back after login.
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
packaging/build-bin.sh               # the .bin installer; also build-deb.sh and build-rpm.sh
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
