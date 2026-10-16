# Changelog (Linux edition)

All notable changes to the Linux edition of SSMConnect. Versions follow [Semantic Versioning](https://semver.org/);
Git tags and GitHub Releases are named `linux-vX.Y.Z` so they never clash with the Windows edition's `vX.Y.Z` tags.
While the major version is 0, the minor and patch numbers may change behaviour between releases.

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
- Downloads of the AWS CLI and plugin are fetched over HTTPS from Amazon but their signatures are not yet verified.
