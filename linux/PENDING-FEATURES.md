# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 20.6.30-20 (Linux 0.6.1).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.

## Release process
_(nothing open here right now)_

## Done recently
- Update repository for dnf and apt, shipped with the rpm and deb (0.6.1).
- `.deb` and `.rpm` now build on this computer (`dpkg` and `rpm-build` installed, 2026-10-20). `/tmp` is a small RAM disk here, so build the rpm with `TMPDIR=$HOME/.cache/ssmconnect-build packaging/build-rpm.sh` (it takes about 10 minutes).
- `LINUX_SIGNING_KEY` added to the repository's GitHub secrets (2026-10-20), so tag builds can sign releases. Not yet exercised: check the signatures on the next release that CI publishes.
- Signed the 0.4.0 release files (signature files added to the release, 2026-10-20).
- One-click "Update now" and a one-time hint on GNOME when there is no tray (0.6.0).
- Tray icon (0.5.0).
- Select all / Select none in the setup wizard server list (0.5.1).

## Not planned (Windows-only on purpose)
- PuTTY, WinSCP and MobaXterm sessions. On Linux, SSMConnect writes `ssh ssc-<name>` entries instead.
