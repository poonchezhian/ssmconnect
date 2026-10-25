# Changelog (Linux edition)

All notable changes to the Linux edition of SSMConnect. Versions follow [Semantic Versioning](https://semver.org/);
Git tags and GitHub Releases are named `linux-vX.Y.Z` so they never clash with the Windows edition's `vX.Y.Z` tags.
While the major version is 0, the minor and patch numbers may change behaviour between releases.

## [Unreleased]

## [2.0.3] - 2026-10-25
### Added
- **Copy files between servers** (Windows 1.0.46 to 2.0.5): *Servers > Copy files between servers…* opens two panes, each one a server. Pick a server on each side (nothing connects until you pick), open folders, select files or folders and copy in either direction, with a progress bar, **Stop**, and a warning listing what would be replaced. The data goes as a `tar` stream from one server through this computer to the other in 128 KB blocks over the open connections (`ssh` with the saved key; nothing extra on the servers beyond `tar`, `find` and `du`), so the servers never need to reach each other. The login check runs first. One organization (company sign-in) is linked at a time, chosen at the top; only connected Linux servers are listed.
- **Switch user**: act as another user on a server through `sudo -u`, staying signed in as the default user with the saved key, so no password sign-in has to be enabled. A sudo password, if needed, reaches sudo through a private helper in a mode-700 folder in `/tmp` on the server that is removed when you switch again or close the window.
- **Root is never used**: a user named root is refused, and so is any login that turns out to have user id 0.
- Engine in `src/transfer.rs` with tests that run the real copy through a stand-in `ssh`.

## [2.0.2] - 2026-10-25
### Added
- **Processor, memory and disk use on every server card** (from Windows 2.0.0 to 2.0.5), as three small coloured labels: green under 60%, amber 60 to 85%, red above. The card's second line also shows the size (vCPUs, memory, disk). The numbers come from a short read-only command run on the server through Session Manager (`AWS-RunShellScript`, `AWS-RunPowerShellScript` on Windows); it needs permission to run SSM commands, and without it the labels stay as dashes (hover for the reason). Connected servers are read about every 5 minutes, production servers only when you choose *Servers > Refresh usage* (or switch it on in Preferences); an account whose role is refused is left alone for an hour. Readings are kept in `~/.local/state/ssmconnect/usage.json`.
- Preferences: *Show how busy my servers are* and *...also on production servers*.

## [2.0.1] - 2026-10-25
### Added
- **Login check** (from Windows 2.0.2 and 2.0.4). A green card only meant the secure tunnel was up; the login (user and key) was never checked, so a wrong one ended in a terminal window that closed with "Permission denied". Before Terminal, Files and *Open sftp in a terminal* open, SSMConnect now finds the key that belongs to the server (the saved file, else the key on this computer that matches the server's AWS key pair by fingerprint or name), tries the login users that fit (only the server's own operating-system user when it is known; otherwise `ec2-user`, `ubuntu`, `admin`, `centos`, `fedora`, `rocky`, `opc`), never asks for a password, and saves the user and key that work (the `ssh ssc-...` entries are rewritten). A combination the server refused is not tried twice. If no key fits you are asked to pick one; if the server does not answer, or its identity changed, you are told why and can open the tool anyway. A "Checking the login..." message shows meanwhile.

## [2.0.0] - 2026-10-25
The Linux edition moves to the 2.x line, catching up with the Windows app (2.0.7) one feature at a time.
### Added
- **Sign-in opens in your own browser as a new tab** (`aws sso login --no-browser`; the address is checked to be an AWS or your company's sign-in page first). If the AWS CLI gives no address within 10 seconds it opens the browser itself, as before. Sign-in steps are written to the log.
### Changed
- **Local AWS emulator settings can no longer hijack a sign-in.** `AWS_ENDPOINT_URL*`, endpoint addresses in the AWS files (`AWS_IGNORE_CONFIGURED_ENDPOINT_URLS`), loose access keys and profile names in the environment, and an `AWS_CONFIG_FILE` / `AWS_SHARED_CREDENTIALS_FILE` that names a missing file or does not hold the account are ignored for the AWS CLI calls SSMConnect makes (floci, LocalStack...). The account's own files in `~/.aws` win.
- A session that ends after 30 seconds or more is renewed quietly; shorter counts as a failed try (was 20 seconds), like the Windows app.
### Fixed
- Publishing the Flatpak repository from a CI artifact: the empty folders an OSTree repository needs are restored, and the repository index and app-store data are signed when they are made (so `flatpak update` does not complain about missing signatures).

## [1.1.0] - 2026-10-21
Every vendor-supported release of the main distributions is covered.
### Added
- **A Flatpak** for the supported releases whose GTK 4 / libadwaita are too old for the native packages: RHEL, AlmaLinux and Rocky Linux 8 and 9, Ubuntu 22.04, Linux Mint 21 and Debian 12. It carries its own GTK and libadwaita (the GNOME 50 runtime), reads `~/.aws` and `~/.ssh`, and runs the terminal, file manager, Remote Desktop client and ssh on the desktop through `flatpak-spawn --host` (`src/host.rs`). Install with `flatpak install --user https://poonchezhian.github.io/ssmconnect/ssmconnect.flatpakref`; `flatpak update` brings new versions. The Flatpak repository is signed with the release key and published on the same GitHub Pages site; a single-file `ssmconnect-X.Y.Z-x86_64.flatpak` (signed) is on each release, and as `ssmconnect-linux-x86_64.flatpak` on the stable `linux-latest` release.
- `packaging/build-flatpak.sh`, a CI job that builds it, and signing and publishing of the Flatpak repository in `scripts/publish-repo.sh`.
### Changed
- The documentation says which file is for which release (a table in the wiki's *Install and update* page), and lists only vendor-supported releases. Tested by installing in clean containers: Fedora 43 and 44, AlmaLinux 10, openSUSE Leap 16.0 and Tumbleweed (`.rpm`), Ubuntu 24.04 and 26.04, Debian 13 (`.deb`).

## [1.0.0] - 2026-10-20
The first stable release of the native Linux edition, now looking like the Windows app.
### Changed
- **The whole interface now follows the Windows edition**: a deep-teal header band with the logo (and the account switcher, or the setup wizard's step dots), a File / AWS / Servers / Help menu bar, three equal status pills, organization chips, server cards with a coloured status strip, an operating-system badge, an environment tag, a status pill and the action buttons, and a bottom bar with Start / Stop / Restart and *Add / change servers*. The Manage accounts and Server identities dialogs have the same band and bottom bar. Light and dark themes both work.
- Releases from 1.0.0 on are regular releases (not pre-releases). They are never marked *Latest*, so the Windows release keeps that place and the Windows download link keeps working.
### Fixed
- Removing a `~/.local` copy no longer leaves a stale icon cache behind. The leftover `icon-theme.cache` still listed the app icon, so after switching to the package install every icon of SSMConnect was drawn blank; uninstall now rebuilds or deletes that cache.
### Added
- **Stable download names**, like the Windows app's `SSMConnect-Setup-x64.exe`: the rolling `linux-latest` release always holds the newest `ssmconnect-linux-x86_64.bin`, `.rpm` and `ssmconnect-linux-amd64.deb` (with signatures and checksums).
- The release workflow also replaces the installers kept in `dist/` and syncs the GitHub wiki after publishing, so the last manual steps of a release are gone.

## [0.6.4] - 2026-10-20
### Added
- **Stable download names**, like the Windows app's `SSMConnect-Setup-x64.exe`: the rolling `linux-latest` release always holds the newest `ssmconnect-linux-x86_64.bin`, `.rpm` and `ssmconnect-linux-amd64.deb` (with signatures and checksums), so a download link never goes stale. The release workflow refreshes it (`scripts/publish-stable.sh`).

## [0.6.3] - 2026-10-20
### Fixed
- Removing a `~/.local` copy no longer leaves a stale icon cache behind. The leftover `icon-theme.cache` still listed the app icon, so after switching to the package install every icon of SSMConnect was drawn blank; uninstall now rebuilds or deletes that cache.

## [0.6.2] - 2026-10-20
Releases sign and publish themselves.
### Changed
- The release workflow now signs the `.rpm` itself (not only the detached `.asc`) and publishes the update repository on GitHub Pages, both with the release key kept as a repository secret. No change to the app.

## [0.6.1] - 2026-10-20
Updates through your system's package manager.
### Added
- **Update repository.** The `.rpm` and `.deb` now install a repository file (`/etc/yum.repos.d/ssmconnect.repo`, or `/etc/apt/sources.list.d/ssmconnect.sources`) and the SSMConnect
  release key, so new releases arrive with the system's normal updates (`dnf upgrade`, `apt upgrade`, GNOME Software, Discover). The repository is on GitHub Pages
  (`https://poonchezhian.github.io/ssmconnect/`); its metadata and the rpm are signed with the release key, and dnf and apt check the signatures.
  Set `enabled=0` (rpm) or `Enabled: no` (deb) in that file to stop getting updates from it. The `.bin` installer is unchanged.
- `scripts/publish-repo.sh` builds, signs and publishes the repository from the newest `.rpm` and `.deb`.

## [0.6.0] - 2026-10-20
One-click updates.
### Added
- **Update now.** *Help > Check for updates* (and the notification) now offers to install the new version itself, as the Windows edition does: it downloads the installer from the
  GitHub release, accepts it only if its OpenPGP signature is valid for the built-in SSMConnect release key **and** its SHA-256 equals the one published in `SHA256SUMS.txt`,
  then closes SSMConnect, runs the installer in update mode (your servers and settings are kept) and opens the new version. Copies installed by a package manager or for all
  users still get the download page (their package manager or the installer updates them).
- **A hint on GNOME.** When GNOME shows no tray (it needs the AppIndicator extension), SSMConnect says so once in a notification instead of silently having no icon.
### Tests
- The real 0.5.1 release is downloaded and verified against the built-in key and the published checksum, and a damaged copy is refused (`SSMCONNECT_NET_TESTS=1`); checksum-list parsing; only project release addresses are downloaded.

## [0.5.1] - 2026-10-20
Select all in the wizard.
### Added
- **Select all / Select none** on the server list in the setup wizard (as in the Windows edition). Select all ticks every server that is ready; servers that are
  not reachable stay unticked.

## [0.5.0] - 2026-10-20
Tray icon.
### Added
- **Tray icon.** A notification-area icon (StatusNotifierItem) shows how many connections are open and has a menu: Open SSMConnect, Start / Stop / Restart connections,
  Preferences, Quit. Clicking the icon opens the window. It turns to "needs attention" when a connection has a problem. Desktops without a tray (stock GNOME needs
  the AppIndicator extension) simply carry on without the icon, as before.

## [0.4.0] - 2026-10-20
First release presented as a regular (not pre-release) version, published next to the Windows installer.
### Changed
- No change to how the program behaves: this is 0.3.2 promoted to a regular release, with a new version number.
- The release helper script `scripts/publish-release.sh` no longer places the GitHub token on a command line (it was readable from the process list). It now
  reaches git through a temporary `GIT_ASKPASS` helper and curl through standard input.

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
