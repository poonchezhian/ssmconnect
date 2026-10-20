# Linux edition: pending features

What the Linux edition still lacks compared with the Windows app, plus open items. Last updated 2026-10-20 (Linux 0.5.1).
Done items move to [CHANGELOG.md](CHANGELOG.md); delete them from here.

## Features
- [ ] **One-click "Update now".** Windows downloads the new installer, checks its SHA-256 and upgrades in place. Linux only shows a banner and opens the download page; the person runs the new `.bin` themselves.
- [ ] **Verify the tray icon on a real tray.** Added in 0.5.0 but only tested on a desktop without a tray (stock GNOME). Check it on KDE or GNOME with the AppIndicator extension: icon, tooltip, menu entries, "needs attention" state.
- [ ] **Tray on stock GNOME.** GNOME shows no tray without the "AppIndicator and KStatusNotifierItem Support" extension. Decide whether to detect this and tell the person once.

## Release process
- [ ] **Sign releases in CI.** The `LINUX_SIGNING_KEY` secret is not set in GitHub, so the automatic release build would publish unsigned files. Releases are currently signed by hand with the local release key.
- [ ] **0.4.0 has no signature files** although the README says every release is signed (0.5.0 and later are signed).
- [ ] **Build `.deb` and `.rpm` on this machine** (needs `dpkg-deb` and `rpmbuild`) so a release does not depend on the GitHub build artifacts.

## Done recently
- Tray icon (0.5.0).
- Select all / Select none in the setup wizard server list (0.5.1).

## Not planned (Windows-only on purpose)
- PuTTY, WinSCP and MobaXterm sessions. On Linux, SSMConnect writes `ssh ssc-<name>` entries instead.
