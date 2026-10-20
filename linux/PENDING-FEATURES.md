# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 2026-10-20 (Linux 1.0.0).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.

## Release process
- [ ] Add the release-notes entry (wiki) and the README size figure automatically instead of by hand. The first release that exercises the new CI steps (`dist/` swap and wiki sync) should be checked.

## Packaging (not in Windows, found while comparing)
- [ ] The `.rpm` is built on Fedora (`fc44`) and needs glibc 2.39+. Tested 2026-10-20 in containers: installs on AlmaLinux 10 (RHEL 10) and openSUSE Tumbleweed; fails on AlmaLinux 9 (RHEL 9, also needs libadwaita 1.5) and openSUSE Leap 15.6 (glibc). Decide whether RHEL 9 / Leap are worth a separate build (older toolchain, newer libadwaita unavailable there).
- [ ] **Support every vendor-supported OS release** (deferred by the owner to a later day; checked against endoflife.date on 2026-10-20). Supported and probably fine once built/tested natively: Fedora 43/44, RHEL/Alma/Rocky 10, openSUSE Leap 16.0 and Tumbleweed, Ubuntu 24.04/26.04, Debian 13, Mint 22. Supported but libadwaita is older than the 1.5 the UI needs: RHEL/Alma/Rocky 8 and 9, Ubuntu 22.04 and Mint 21, Debian 12. Options were: native packages only for the first group; plus a Flatpak for the second; or port the UI to older libadwaita. Out of support, ignore: Leap 15.6, Fedora 42 and older, Debian 11, Ubuntu 25.10 and older non-LTS.
- [ ] Only `x86_64`/`amd64`. No arm64 build.

## Done recently
- Update repository for dnf and apt, shipped with the rpm and deb (0.6.1).
- `.deb` and `.rpm` now build on this computer (`dpkg` and `rpm-build` installed, 2026-10-20). `/tmp` is a small RAM disk here, so build the rpm with `TMPDIR=$HOME/.cache/ssmconnect-build packaging/build-rpm.sh` (it takes about 10 minutes).
- CI signing (the `LINUX_SIGNING_KEY` secret) works: signed rpm, detached signatures, update repository and stable `linux-latest` download names are all produced by CI (0.6.2 to 0.6.4).
- Signed the 0.4.0 release files (signature files added to the release, 2026-10-20).
- One-click "Update now" and a one-time hint on GNOME when there is no tray (0.6.0).
- Tray icon (0.5.0).
- Select all / Select none in the setup wizard server list (0.5.1).

## Not planned (Windows-only on purpose)
- PuTTY, WinSCP and MobaXterm sessions. On Linux, SSMConnect writes `ssh ssc-<name>` entries instead.
