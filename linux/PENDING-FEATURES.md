# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 2026-10-20 (Linux 0.6.0).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.

## Release process
- [ ] **Sign releases in CI.** The `LINUX_SIGNING_KEY` secret is not set in GitHub, so the automatic release build would publish unsigned files. Releases are currently signed by hand with the local release key.
- [ ] **0.4.0 has no signature files** although the README says every release is signed (0.5.0 and later are signed).
- [ ] **Build `.deb` and `.rpm` on this machine** (needs `dpkg-deb` and `rpmbuild`) so a release does not depend on the GitHub build artifacts.

## Done recently
- One-click "Update now" and a one-time hint on GNOME when there is no tray (0.6.0).
- Tray icon (0.5.0).
- Select all / Select none in the setup wizard server list (0.5.1).

## Not planned (Windows-only on purpose)
- PuTTY, WinSCP and MobaXterm sessions. On Linux, SSMConnect writes `ssh ssc-<name>` entries instead.
