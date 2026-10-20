# SSMConnect for Linux

SSH and Remote Desktop to AWS servers **with port 22 closed**, through AWS Systems Manager (SSM) Session Manager.
This is the native Linux edition (Rust, GTK 4 + libadwaita). The Windows edition lives in the same repository (everything outside the `linux/` folder); the two editions
are kept side by side on the `main` branch and are released separately.

> **Status:** early release (0.x). Try it against a non-production server first.

## How it works

```
ssh ssc-web-1  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

SSMConnect keeps one tunnel per server open as you (never root), and writes `Host ssc-<name>` entries to
`~/.ssh/ssmconnect.conf`. After that every OpenSSH-based tool works: `ssh`, `scp`, `rsync -e ssh`, VS Code Remote-SSH,
and the file manager via `sftp://`.

## Install

Download from the [Releases page](https://github.com/poonchezhian/ssmconnect/releases) (tags named `linux-vX.Y.Z`; check `SHA256SUMS.txt`).

**The installer (any distribution): `ssmconnect-X.Y.Z-linux-x86_64.bin`**

```
sh ./ssmconnect-X.Y.Z-linux-x86_64.bin      # or chmod +x it and double-click
```

It opens an installation wizard (or asks questions in the terminal when there is no display: `--text`, `--yes` accepts the
defaults, `--system` installs for all users). The wizard works in this order, so SSMConnect is only installed once everything it needs is there:

1. **Requirements, fetched from their own sources** (nothing third-party is bundled in the installer):
   - GTK 4 and libadwaita from your distribution's repositories, if they are missing (the file itself offers this before the window opens)
   - OpenSSH client (and optionally Remmina for Windows servers) from your distribution's repositories, with its package manager (dnf, apt, zypper or pacman), after asking for your password
   - AWS CLI v2 and the Session Manager plugin straight from Amazon, with Amazon's OpenPGP signature checked
2. **SSMConnect itself**, for you (`~/.local`, no password) or for all users (`/usr/local`).

**Updating:** run the newer `.bin`. It finds the existing installation, updates it in place and keeps your servers, accounts and settings (it saves a copy
of your saved servers first). `--update-only` makes it do nothing when SSMConnect is not installed.

**Removing:** *Remove SSMConnect…* on the installer's first page, or `ssmconnect uninstall` (add `--prefix /usr/local` for an all-users install). Your saved
servers and settings stay unless you ask for them to be deleted (`--remove-settings`); the AWS tools SSMConnect downloaded stay unless you add `--remove-tools`.

**Native packages** (the package manager pulls OpenSSH and GTK for you):

| Distribution | Install |
|---|---|
| Fedora, RHEL, openSUSE | `sudo dnf install ./ssmconnect-X.Y.Z-1.x86_64.rpm` |
| Debian 13+, Ubuntu 24.04+, Mint 22+ | `sudo apt install ./ssmconnect_X.Y.Z_amd64.deb` |
| From source | `cargo build --release --locked`, then `target/release/ssmconnect --installer` |

After a package install, the first run of SSMConnect offers to fetch the AWS CLI and the Session Manager plugin (the same signed downloads).
Then open **SSMConnect** from the application menu; the first run starts the setup wizard.

## Requirements

- A compiled, native program: no Python, Electron or Wine. It links GTK 4 (4.12+) and libadwaita (1.5+), which every current desktop distribution ships
- OpenSSH client; optionally FreeRDP or Remmina for Windows servers
- AWS CLI v2 and the Session Manager plugin, fetched for you from Amazon (`ssmconnect install-tools` does it from a terminal)

## Verifying the download (signed releases)

Every file on a Linux release page (`.bin`, `.deb`, `.rpm`, `SHA256SUMS.txt`) comes with a detached OpenPGP signature (`<file>.asc`) made with the
**SSMConnect Linux Releases** key:

```
Fingerprint  FF9F 45E6 4711 7103 89B3  F1FC 7483 B1F8 DBE4 A803
Key ID       7483B1F8DBE4A803      (ed25519, created 2026-10-16, expires 2028-10-15)
```

The public key is [`SIGNING-KEY.asc`](SIGNING-KEY.asc) in this folder. Check a download before you run it:

```
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-v0.5.1/ssmconnect-0.5.1-linux-x86_64.bin
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-v0.5.1/ssmconnect-0.5.1-linux-x86_64.bin.asc
curl -LO https://raw.githubusercontent.com/poonchezhian/ssmconnect/linux/linux/SIGNING-KEY.asc

gpg --import SIGNING-KEY.asc
gpg --fingerprint 7483B1F8DBE4A803        # must show exactly the fingerprint above
gpg --verify ssmconnect-0.5.1-linux-x86_64.bin.asc ssmconnect-0.5.1-linux-x86_64.bin
```

`gpg` should say **Good signature from "SSMConnect Linux Releases"**. (It also prints a warning that the key is not certified by you; that is expected, the fingerprint check above is what ties the key to this project.) If it says **BAD signature**, do not run the file. The release also lists `SHA256SUMS.txt` and `SHA256SUMS.txt.asc`: `sha256sum -c SHA256SUMS.txt` checks the checksums, and the `.asc` file is their signature.

For maintainers: `scripts/sign-release.sh` signs everything in `dist-linux/` and verifies it in a clean keyring, exactly as above. In GitHub Actions the release job
signs with the key stored in the repository secret `LINUX_SIGNING_KEY` (the exported private key), when that secret exists.

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
