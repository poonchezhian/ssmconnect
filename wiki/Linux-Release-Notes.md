# Linux Release Notes

Linux releases are tagged `linux-vX.Y.Z` on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases); the Windows app keeps its own versions. The complete list is `linux/CHANGELOG.md` in the source.

## 0.6.1
- **Updates through your package manager.** The `.rpm` and `.deb` now add the SSMConnect update repository and its signing key, so new releases arrive with your system's normal updates (`sudo dnf upgrade`, `sudo apt upgrade`, GNOME Software, Discover). The repository and the rpm are signed, and your system checks the signatures.

## 0.6.0
- **Update now.** *Help > Check for updates* offers to install the new version itself: it downloads the installer, checks its signature and checksum, closes SSMConnect, updates it in place (servers and settings are kept) and opens the new version. Package-manager installs still update through `dnf` or `apt`.
- **A hint on GNOME** when no tray icon can be shown (GNOME needs the AppIndicator extension).

## 0.5.1
- **Select all / Select none** on the server list in the setup wizard. Select all ticks every server that is ready; servers that are not reachable stay unticked.

## 0.5.0
- **Tray icon.** A notification-area icon shows how many connections are open and has a menu: Open SSMConnect, Start / Stop / Restart connections, Preferences, Quit. Clicking it opens the window, and it flags a connection with a problem. On GNOME it needs the AppIndicator extension; where there is no tray, SSMConnect works as before.

## 0.4.0
- First release presented as a regular (not pre-release) version, published next to the Windows installer. No change in behaviour; the release helper no longer exposes the GitHub token on a command line.

## 0.3.2 - first public Linux release
- **Same icon as the Windows edition**, in every size the desktop needs.
- **Signed releases.** Every file has an OpenPGP signature; the key fingerprint is published in the README and on [Linux: Install and Update](Linux-Install-and-Update#check-the-signature-before-you-run-it).

## 0.3.1
- Logging out, shutting down or stopping SSMConnect now closes all its connections cleanly. If an earlier run left a connection behind, the next start clears it away instead of reporting "port already in use".

## 0.3.0
- **Updates keep your data.** An existing install is updated in place; settings and saved servers are kept (a copy is made first). Removing keeps your data unless you tick the box.
- **Preferences** window: every setting in plain words (start-up, terminal and Remote Desktop programs, what *Files* opens, ports, `ssh ssc-…`, updates, AWS tools, your data).
- **Server identities** window: see, confirm and forget each server's SSH identity without editing `known_hosts`.
- Change a server's login user or key file without running the wizard again.

## 0.2.x
- One installer file (`.bin`) with a wizard; requirements fetched from their own sources, nothing third-party bundled.
- **Files** now works: it logs in over SFTP and opens the server in your file manager (with fallbacks and clear messages). *Open sftp in a terminal* added.

## 0.1.x
- Rewritten in Rust as a native GTK 4 / libadwaita app. AWS CLI and Session Manager plugin downloads are checked against Amazon's signatures.

## 0.0.x
- Prototype (Python). Superseded by 0.1.0.
