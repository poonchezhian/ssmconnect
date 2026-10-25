# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 2026-10-25 (Linux 2.0.6, compared with Windows 2.0.7).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
Ported from Windows 2.0.x: see "Done recently". What is left needs a person with real servers or hardware:
- [ ] **Try the copy-files window against real servers**: copying a folder between two servers, Stop, replacing existing files, switching to another user with and without a sudo password (the password helper in `/tmp/.ssc-*` must be gone afterwards). The layout was checked on screen (2026-10-25); the data path is covered by tests that use a stand-in `ssh`.
- [ ] **Try the usage labels with a real AWS role**, with and without permission to run `ssm:SendCommand` (dashes and the one-hour back-off), and on a Windows server. The labels and colours were checked on screen with sample readings.
- [ ] **Try the login check against real servers** (wrong user, wrong key, rebuilt server) and look at the "Which key file is this?" prompt on screen.
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.

## Decided not to port (revisit if wanted)
- **Organization sidebar (Windows 2.0.0).** The Linux window already has the account switcher in the band, the organization chips, filters and search, and the three status pills; a sidebar would use width the cards need.
- **One Stop / Start button (2.0.1).** Linux keeps Start, Stop and Restart: Restart is used after changing settings.
- **Stop after three failed tries until the next sign-in.** Linux keeps retrying with a back-off of up to 60 seconds, so a server that comes back is picked up without clicking; the 30-second rule keeps a flapping connection from looking healthy.
- **Whole-card tint and usage bars on the status row.** Linux keeps the coloured left strip and a separate row of usage labels (the look checked on screen on 2026-10-25).

## Release process
- [ ] Add the release-notes entry (wiki) and the README size figure automatically instead of by hand. The first release that exercises the new CI steps (`dist/` swap and wiki sync) should be checked.

## Packaging (not in Windows, found while comparing)
- [ ] The `.rpm` is built on Fedora (`fc44`) and needs glibc 2.39+. Tested 2026-10-20 in containers: installs on AlmaLinux 10 (RHEL 10) and openSUSE Tumbleweed; fails on AlmaLinux 9 (RHEL 9, also needs libadwaita 1.5) and openSUSE Leap 15.6 (glibc). Decide whether RHEL 9 / Leap are worth a separate build (older toolchain, newer libadwaita unavailable there).
- [ ] **Try the Flatpak on a real RHEL 8/9, Ubuntu 22.04 / Mint 21 and Debian 12 machine.** It is built and tested on Fedora 44 (flatpak 1.18): installs from the signed repository, finds the host's ssh, terminal, Remote Desktop client and file manager, and downloads the AWS tools. Older hosts have flatpak 1.12 to 1.14 and were not tried.
- [ ] Only `x86_64`/`amd64`. No arm64 build.

## Done recently
- A way to look at the running app without a desktop: run it on a GTK Broadway display (`gtk4-broadwayd :9`, `GDK_BACKEND=broadway BROADWAY_DISPLAY=:9 GTK_USE_PORTAL=0 ADW_DISABLE_PORTAL=1`) with a throw-away `HOME` that holds a sample config and a stand-in `aws`, and take a screenshot of `http://127.0.0.1:8089/` with Chrome's debugging port. It found the missing-cards bug fixed in 2.0.6.
- Ported from Windows 2.0.x (Linux 2.0.0 to 2.0.6, 2026-10-25): sign-in in the user's own browser with a CLI fallback and link check; local AWS emulator settings ignored; 30-second flap rule; login check before Terminal, Files and sftp; CPU, memory and disk labels with Preferences switches and *Refresh usage*; two-pane *Copy files between servers* with switch user through sudo and no root; OS logos; filters, search and colour key. Released as 2.0.5; 2.0.6 fixes cards missing from the dashboard.
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
