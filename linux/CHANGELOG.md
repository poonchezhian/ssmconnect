# Changelog (Linux edition)

All notable changes to the Linux edition of SSMConnect. Versions follow [Semantic Versioning](https://semver.org/);
Git tags and GitHub Releases are named `linux-vX.Y.Z` so they never clash with the Windows edition's `vX.Y.Z` tags.
While the major version is 0, the minor and patch numbers may change behaviour between releases.

## [0.3.2] - 2026-10-16
Same icon as the Windows edition.
### Changed
- The application icon is now the Windows edition's artwork (taken from `src/SSMConnect.Wizard/Assets/ssmconnect.ico`), installed as PNGs in the sizes the
  desktop expects (16, 24, 32, 48, 64, 128, 256) in place of the earlier placeholder. It shows in the application menu, the dock and window switcher, the
  About box, and on the installer's first page. Updating from an older version removes the old icon file and installs the new ones.

## [0.3.1] - 2026-10-16
No more leftover connections.
### Fixed
- **Stopping SSMConnect with a signal left its Session Manager sessions running.** Logging out, shutting down or `kill` (SIGTERM/SIGHUP/SIGINT) ended the app
  without closing its connections, and the next start then reported every local port as "already used by another program". The app now closes everything
  cleanly on those signals, the same as Quit.
- **Leftover sessions are cleared away.** If a previous run was stopped abruptly (crash, power loss) and its Session Manager plugin is still holding a port,
  the new run recognises it (same plugin, same port, not one of its own sessions), ends it and connects. Anything else holding a port is still left alone and
  reported, as before.
### Tests
- A leftover plugin holding the port is ended and replaced; another program's port is never touched; the running app exits on SIGTERM with all of its ports freed.

## [0.3.0] - 2026-10-16
Updates that keep your data, and every setting in a window.
### Installer
- **An existing installation is updated in place.** The installer recognises it (also what was installed by an older installer) and says so:
  "SSMConnect 0.2.1 is installed in ~/.local. This installer will update it to 0.3.0. Your servers, accounts and settings are kept."
  It reuses the same location, removes only files the new version no longer ships, and first copies your saved servers to
  `config.json.before-update-from-<old version>`.
- **Your data is never removed unless you ask.** Settings, saved servers, the log, the AWS tools SSMConnect downloaded, `~/.aws` and your ssh hosts all
  stay when updating and when removing. *Remove SSMConnect…* (on the installer's first page, or `ssmconnect uninstall`) has two separate, unticked-by-default
  choices: delete my saved servers/settings/log, and remove the AWS tools SSMConnect downloaded (`--remove-settings`, `--remove-tools` on the command line).
- `--update-only`: do nothing (exit code 3) unless SSMConnect is already installed.
- Going back to an older version needs an explicit tick (window) or answer (terminal); `--yes` never downgrades.
- Installs made by a package manager (dnf, apt) are left to that package manager, with the exact command to use.
- Tells you when SSMConnect is running (it keeps the old version until restarted).
### Preferences (new window, File > Preferences)
- Open my connections automatically; start SSMConnect when I sign in; make me confirm each server's identity first; let `ssh ssc-…` work (adds/removes
  the one line in `~/.ssh/config`); terminal program; Remote Desktop program; what the Files button opens; first local port for new servers;
  look for new versions daily (+ check now); install or repair the AWS tools; open the settings folder / log; delete all my SSMConnect settings.
  Each has a plain sentence saying what it does. No setting needs a file or a command line any more.
### Server identities (new, Servers > Server identities…, and More > Server identity… on each server)
- See, confirm and forget each server's SSH identity (host key) without touching `known_hosts`. *Check and confirm…* asks the server through its open tunnel,
  shows the fingerprints, optionally checks one your administrator gave you, and saves it under the server's own name. A changed identity is flagged with an
  explanation and needs a deliberate *Review…*. A copy of `known_hosts` is kept as `known_hosts.ssmconnect.bak`; your own entries are never changed.
- A banner offers to review servers not yet confirmed. With "make me confirm each server's identity first" on, the Terminal and Files buttons wait for it,
  and ssh itself uses `StrictHostKeyChecking yes`.
### Also
- **More > Change login user or key file…** on each server (no need to run the wizard again).
- The example programs (`screenshot`, `e2e_wizard`) no longer hand over to a running SSMConnect.
### Tests
- Identities against a real local sshd (scan, fingerprint equals ssh-keygen's, trust, repeated trust adds nothing, change detection, forget, user entries kept),
  update keeps settings and drops obsolete files, removal keeps data unless asked, version order.

## [0.2.1] - 2026-10-16
Files button fixed.
### Fixed
- **Files did nothing** for Linux servers. The button ran `gio open sftp://...`, which only prints "location is not mounted" (and the error was
  thrown away). It now logs in first (`gio mount`), then opens the mounted folders in the file manager; where GVFS is not available it uses the file
  manager's own SFTP (Dolphin on KDE, Thunar, Nemo, Caja, Nautilus, PCManFM), and as a last resort the plain `sftp` client in a terminal.
- A failure is now shown instead of being swallowed: *the server refused the login (check the user and key file)*, *the connection is not open yet*,
  or *the server's host key changed*.
### Added
- **More > Open sftp in a terminal** on every Linux server, and `ssmconnect files NAME` / `ssmconnect sftp NAME` on the command line
  (NAME is the server's name, its `ssc-` alias or its instance id).
- Opening the files runs in the background (it logs in first, which takes a moment) and the dashboard says so.
### Verified
- Against a real local SSH/SFTP server through the same `~/.ssh/config` alias mechanism: mounts and opens Nautilus (about 2 s), a wrong user
  gives the login message, a stopped server gives the connection message.

## [0.2.0] - 2026-10-16
One installer file, and nothing third-party inside it.
### Added
- `ssmconnect-X.Y.Z-linux-ARCH.bin`: a single self-extracting file. Running it opens an installation wizard (Welcome, Requirements,
  Where to install, Done), or asks the same questions in the terminal without a display (`--text`; `--yes` accepts the defaults;
  `--system` installs for all users; `--prefix DIR`; `--extract DIR` only unpacks).
- Requirements are fetched from their own sources before SSMConnect is installed: GTK 4 / libadwaita (offered by the file itself if missing),
  OpenSSH and Remmina from the distribution's repositories through dnf, apt, zypper or pacman (asks for the password, via pkexec or sudo),
  and the AWS CLI and Session Manager plugin from Amazon with their signatures checked. The installer never bundles any of them.
- The file checks its own integrity (SHA-256) before running anything, and refuses a damaged or truncated download.
- `ssmconnect uninstall [--prefix DIR]` removes what the installer installed, using the list it wrote. Settings and the user's tools stay.
- The desktop entry written by the installer points at the installed program by absolute path, so it works even when `~/.local/bin` is not on PATH.
### Changed
- The program carries its own desktop entry, icon, AppStream data and documents, so the `.bin` is the only file to download. The `.deb` and `.rpm`
  are staged by the program itself (`--install-files --destdir`), which replaces `stage.sh`; the old tarball is replaced by the `.bin`.
- `LICENSE` and `NOTICE` are also kept in `linux/` so the source builds on its own.
### Tests
- Installer file layout, uninstall from the list (including a hostile list), package staging, package-manager detection and commands,
  and a CI smoke test that runs the `.bin` end to end with real signed downloads.

## [0.1.4] - 2026-10-16
Signed downloads.
### Added
- `install-tools` and the wizard's *Install what is missing* now check every download against the OpenPGP signature Amazon
  publishes next to it (`<file>.sig`) before anything is unpacked or run. A missing or non-matching signature stops the
  install with a plain message.
- Amazon's two public keys are built in (AWS CLI Team key `FB5D B77F D5C1 18B8 0511 ADA8 A631 0ACC 4672 475C`; Session Manager plugin
  signer key `7959 6371 24CE 093A D501 D47A 2C4D 4AFF 6F67 57EE`), copied from AWS's documentation and pinned by full fingerprint,
  which is checked each time a key is loaded. Verification is pure Rust (no `gpg` needed) and handles both RSA and ECDSA keys.
- Tests: pinned fingerprints, wrong-key and garbage-signature refusal, and (with `SSMCONNECT_NET_TESTS=1`) real Amazon packages
  verify, a flipped byte or the wrong key is refused. A non-blocking CI job runs these against the live downloads, so a key
  rotation by AWS is noticed.
### Fixed
- `ssmconnect install-tools` printed every percent repeatedly; it now prints each message once.
- Piping output (`ssmconnect list | head`) no longer ends with a panic message.
### Known limits
- If AWS rotates a signing key, installs are refused until a new SSMConnect release ships the new key (the AWS CLI key is valid until 2027-07-01).

## [0.1.3] - 2026-10-16
Rust rewrite, part 4: native packaging; the Python edition is removed.
### Changed
- Packages now ship the compiled program: tarball (`ssmconnect-X.Y.Z-linux-ARCH.tar.gz` with `install.sh`), `.deb` and `.rpm`
  (built from source in a Fedora container). They depend on GTK 4 (>= 4.12) and libadwaita (>= 1.5) only; no Python.
- The release workflow builds with Cargo (`--locked`), runs the tests, validates the desktop and AppStream files, smoke-tests the
  installed deb and rpm, and publishes `SHA256SUMS.txt`.
- `Cargo.lock` is committed so builds are reproducible.
### Removed
- `legacy-python/` (the 0.0.x prototype). It stays available at tag `linux-v0.0.5`.
### Known limits
- The `.deb` and `.rpm` builders run in CI; `dpkg-deb` and `rpmbuild` are not installed on the development machine, so only
  the tarball install was run locally.

## [0.1.2] - 2026-10-16
Rust rewrite, part 3: setup wizard and account management. The Rust edition now does everything the Python edition did.
### Added
- Five-step setup wizard in Rust (requirements with one-click user-space install, AWS account / company sign-in / access keys,
  parallel server search, key matching, save), and the Manage accounts dialog (rename, remove).
- `cargo run --example e2e_wizard` drives the real wizard end to end against a fake AWS CLI (needs a display).

## [0.1.1] - 2026-10-16
Rust rewrite, part 2: the dashboard.
### Added
- Native GTK 4 + libadwaita dashboard in Rust, same layout as 0.0.x: account switcher, status pills, summary, organization chips,
  Start / Stop / Restart, server cards with Terminal / Files / Remote Desktop / Get password / Copy actions, hide-to-background,
  notifications, daily scan, update check, About, clean-up.
- `cargo run --example screenshot` renders the real window to a PNG against a fake AWS CLI.
### Not yet
- Setup wizard and accounts dialog.

## [0.1.0] - 2026-10-16
Rust rewrite, part 1: the engine and the command line. The Python edition (0.0.x) is kept under `legacy-python/` until the
Rust edition has everything it had.
### Changed
- The whole engine is now compiled Rust (no Python at run time): tunnel manager, AWS profiles, SSO, discovery, ssh integration,
  key matching by fingerprint (pure Rust DER reader), launchers, user-space tool installer, update check.
- Settings written by 0.0.x are read as they are (same file names and field names).
### Added
- 20 automated tests (unit and integration, with a fake AWS CLI); every Python test was ported.
### Not yet in the Rust edition
- The window, setup wizard and accounts dialog (next releases). Packaging still ships the Python edition until then.

## [0.0.5] - 2026-10-16
Documentation and polish.
### Added
- README: install table per distribution, behaviour without a tray icon, troubleshooting, release procedure.
### Fixed
- Wizard step indicator icon for installed tools.

## [0.0.4] - 2026-10-16
Native packaging and releases.
### Added
- Installable builds from one staging script, so every format ships identical files: a tarball with `install.sh`
  (per user by default, no root; `--uninstall` removes it), a `.deb` (Debian, Ubuntu, Mint) and an `.rpm` (Fedora, RHEL, openSUSE).
- Desktop entry, AppStream metadata (validated) and scalable icon, so the app appears in the application menu and software centres.
- GitHub Actions workflow for the Linux edition only (`linux` branch and `linux-v*` tags): tests, metadata checks,
  package builds, install smoke tests on Ubuntu and Fedora, `SHA256SUMS.txt`, and a GitHub Release (0.x as pre-release) with notes taken from this file.
- `scripts/bump-version.sh` keeps `_version.py`, `VERSION` and the AppStream release in step.
### Changed
- The update check also considers pre-releases, since all 0.x builds are published as such.
### Known limits
- The `.deb` and `.rpm` builders run in CI; they could not be run on the development machine (no `dpkg-deb`/`rpmbuild`).

## [0.0.3] - 2026-10-16
The setup wizard and account management.
### Added
- Five-step setup: Requirements (one click installs the AWS CLI and plugin for the user), AWS account (company sign-in address
  pre-filled from IT policy files, `~/.aws` or cached sign-ins; browser sign-in; every organization account is added), Servers
  (searched in parallel, SSM-reachable ones tickable, skipped accounts explained), SSH keys (matched by fingerprint, then name),
  Save (ports from 2201, ssh entries written, tunnels opened).
- Re-opening the wizard for an existing sign-in picks up AWS accounts added to the organization since last time.
- Access keys as a secondary path; keys stay in `~/.aws/credentials` (mode 0600).
- Manage accounts: one collapsed line per sign-in, rename (friendly names) and remove (deletes only SSMConnect's data and the local login).
- Developer tools: `scripts/e2e_wizard.py` drives the real wizard against a fake AWS CLI; `scripts/screenshot.py`.
### Changed
- Same-case tidy-ups: `uat2`/`prod01` style tag values are normalised (UAT, Production).

## [0.0.2] - 2026-10-16
The window.
### Added
- Native GTK 4 + libadwaita dashboard: account switcher, three status pills, summary line, organization chips,
  Start / Stop / Restart, and one card per server grouped by AWS account (red PROD tag, live status dot, plain-language problems).
- Per-server actions: Terminal (ssh), Files (sftp:// in the file manager), Remote Desktop, Get password
  (clipboard cleared after 60 s), Copy ssh command. Unavailable buttons are dimmed and explain why when clicked.
- Closing the window hides it and keeps connections open; Quit closes them. Desktop notifications for scan results and updates.
- Menus: start when I sign in, add to application menu, sign in again, scan, what changed, check for updates, open log, clean up, about.
- Single instance through the desktop's own application activation; app icon.
### Known limits
- The setup wizard and the accounts dialog are not in this release (they show a note). Add accounts with `aws configure sso`.

## [0.0.1] - 2026-10-16
First iteration: the engine, without a window yet.
### Added
- Tunnel manager: one `aws ssm start-session` port-forward per server on `127.0.0.1`, quiet renewal of idle sessions,
  back-off, safety close when a port is reachable from the network, refusal to start on a port another program owns.
- AWS profile reader/writer for `~/.aws/config` and `credentials` (SSO sessions, organization accounts, access keys),
  with `.ssmconnect.bak` backups and owner-only file permissions.
- Server discovery, change detection and the daily passive rescan.
- Environment type from AWS tags (PROD, UAT, ...), SSH key matching by fingerprint, name or similar name.
- OpenSSH integration: `Host ssc-<server>` entries in `~/.ssh/ssmconnect.conf`, included from `~/.ssh/config`.
- Launchers for a terminal, the file manager (`sftp://`) and Remote Desktop (FreeRDP / Remmina).
- User-space installer for the AWS CLI and Session Manager plugin (no root needed).
- Command line: `doctor`, `install-tools`, `list`, `status`, `run`, `scan`, `cleanup`.
### Known limits
