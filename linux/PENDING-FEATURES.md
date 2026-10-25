# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 2026-10-25 (Linux 2.0.4 unreleased, compared with Windows 2.0.7).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
Gaps against the Windows app (2.0.7). Ported so far: see "Done recently".
- [ ] **Organization sidebar (Windows 2.0.0).** A left sidebar listing the organizations with a status dot and count, and the three status pills at its bottom. Linux has the account switcher in the band plus the filter row and search; there is no sidebar yet.
- [ ] **Stop / Start connections as one toggle (2.0.1).** Linux keeps separate Start, Stop and Restart buttons. Decide whether to merge them.
- [ ] **Stop after three failed tries until sign-in (2.0.0 to 2.0.4).** Windows shows *Stopped* after three failed tries in a row and stays down until a successful sign-in resets everything. Linux keeps retrying with a back-off of up to 60 seconds (and only counts the 30-second flap rule). Decide if the Windows behaviour is wanted on Linux.
- [ ] **Look of the cards.** Windows paints a status tint and back-light on the whole card and puts the usage bars on the status row; Linux keeps the coloured left strip and a separate row of usage labels.
- [ ] **Visual check of the new windows on a real desktop.** The copy-files window, the usage labels, filters, logos and the "Which key file is this?" prompt were built and smoke-tested (the app starts without GTK warnings, the engines have tests) but not looked at on a real screen yet.
- [ ] **Try the copy-files window against real servers**: copying a folder between two servers, Stop, replacing existing files, switching to another user with and without a sudo password (the password helper in `/tmp/.ssc-*` must be gone afterwards).
- [ ] **Try the usage labels with a real AWS role**, with and without permission to run `ssm:SendCommand` (dashes and the one-hour back-off), and on a Windows server.
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.

## Release process
- [ ] Add the release-notes entry (wiki) and the README size figure automatically instead of by hand. The first release that exercises the new CI steps (`dist/` swap and wiki sync) should be checked.

## Packaging (not in Windows, found while comparing)
- [ ] The `.rpm` is built on Fedora (`fc44`) and needs glibc 2.39+. Tested 2026-10-20 in containers: installs on AlmaLinux 10 (RHEL 10) and openSUSE Tumbleweed; fails on AlmaLinux 9 (RHEL 9, also needs libadwaita 1.5) and openSUSE Leap 15.6 (glibc). Decide whether RHEL 9 / Leap are worth a separate build (older toolchain, newer libadwaita unavailable there).
- [ ] **Try the Flatpak on a real RHEL 8/9, Ubuntu 22.04 / Mint 21 and Debian 12 machine.** It is built and tested on Fedora 44 (flatpak 1.18): installs from the signed repository, finds the host's ssh, terminal, Remote Desktop client and file manager, and downloads the AWS tools. Older hosts have flatpak 1.12 to 1.14 and were not tried.
- [ ] Only `x86_64`/`amd64`. No arm64 build.

## Done recently
- Ported from Windows 2.0.x (Linux 2.0.0 to 2.0.4, 2026-10-25): sign-in in the user's own browser with a CLI fallback and link check; local AWS emulator settings ignored; 30-second flap rule; login check before Terminal, Files and sftp; CPU, memory and disk labels with Preferences switches and *Refresh usage*; two-pane *Copy files between servers* with switch user through sudo and no root; OS logos; filters, search and colour key. Not released yet.
- Every vendor-supported OS release is covered: native packages tested in containers on Fedora 43/44, RHEL 10, Leap 16, Tumbleweed, Ubuntu 24.04/26.04, Debian 13; a Flatpak for RHEL 8/9, Ubuntu 22.04, Mint 21, Debian 12 (1.1.0).
- Update repository for dnf and apt, shipped with the rpm and deb (0.6.1).
- `.deb` and `.rpm` now build on this computer (`dpkg` and `rpm-build` installed, 2026-10-20). `/tmp` is a small RAM disk here, so build the rpm with `TMPDIR=$HOME/.cache/ssmconnect-build packaging/build-rpm.sh` (it takes about 10 minutes).
- CI signing (the `LINUX_SIGNING_KEY` secret) works: signed rpm, detached signatures, update repository and stable `linux-latest` download names are all produced by CI (0.6.2 to 0.6.4).
- Signed the 0.4.0 release files (signature files added to the release, 2026-10-20).
- One-click "Update now" and a one-time hint on GNOME when there is no tray (0.6.0).
- Tray icon (0.5.0).
- Select all / Select none in the setup wizard server list (0.5.1).

## Not planned (Windows-only on purpose)
- PuTTY, WinSCP and MobaXterm sessions. On Linux, SSMConnect writes `ssh ssc-<name>` entries instead.
