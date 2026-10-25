# Linux Release Notes

Linux releases are tagged `linux-vX.Y.Z` on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases); the Windows app keeps its own versions. The complete list is `linux/CHANGELOG.md` in the source.

## 2.0.2
- **See how busy each server is.** Every server card shows CPU, memory and disk use as small green, amber or red labels (under 60%, 60 to 85%, above), and its size on the second line. SSMConnect asks the server through Session Manager with a short read-only command, so your AWS role needs to be allowed to run commands there; without that the labels stay as dashes. Connected servers refresh about every 5 minutes; production servers only when you choose **Servers > Refresh usage** (or switch it on in Preferences). An account that is refused is left alone for an hour.

## 2.0.1
- **The login is checked before a tool opens.** A green card only meant the secure tunnel was up. Now, before Terminal, Files or sftp opens, SSMConnect finds the key that belongs to the server, tests which login user it accepts, saves what works and refreshes the `ssh ssc-...` entries. No more terminal windows that close on "Permission denied". If no key fits it asks you for one; if the server does not answer it says so and lets you open the tool anyway.

## 2.0.0
The Linux edition joins the 2.x line and will catch up with the Windows app step by step.
- **Sign-in opens in your own browser** as a new tab, so a sign-in you already have there is reused. Only AWS and your company's sign-in pages are opened.
- **Local AWS emulator settings are ignored** during sign-in and connections (`AWS_ENDPOINT_URL*`, loose keys, a leftover `AWS_CONFIG_FILE`), so floci or LocalStack leftovers cannot send you to a local address.

## 1.1.0
- **Flatpak for older systems.** RHEL, AlmaLinux and Rocky Linux 8 and 9, Ubuntu 22.04, Linux Mint 21 and Debian 12 are still supported by their vendors but ship a GTK 4 / libadwaita too old for the `.rpm` and `.deb`. The Flatpak brings its own: `flatpak install --user https://poonchezhian.github.io/ssmconnect/ssmconnect.flatpakref`, and `flatpak update` keeps it current. See [Install and Update](Linux-Install-and-Update#which-file-is-for-my-system) for which file fits which system.

## 1.0.0
- **Looks like the Windows app.** The dashboard, setup wizard, *Manage accounts* and *Server identities* now share the Windows edition's look: the teal header band, menu bar, status pills, server cards with a coloured strip, and the bottom bar with Start, Stop, Restart and *Add / change servers*. Light and dark themes both work.
- Fixed blank icons after removing a per-user copy of SSMConnect (a stale icon cache).
- Download links that never go stale: `ssmconnect-linux-x86_64.bin`, `.rpm` and `ssmconnect-linux-amd64.deb` always point at the newest version.
- From 1.0.0 on, Linux releases are regular releases. They are not marked *Latest*, so the Windows release stays the one behind the main download button.

## 0.6.4
- **Download links that never go stale.** `ssmconnect-linux-x86_64.bin`, `.rpm` and `ssmconnect-linux-amd64.deb` always point at the newest version (see [Install and Update](Linux-Install-and-Update)).

## 0.6.3
- **Fixed blank icons after removing a copy.** Uninstalling a per-user copy could leave a stale icon cache that made every SSMConnect icon blank, also for a package install. Uninstall now cleans it up.

## 0.6.2
- Behind the scenes: releases are now signed and the update repository is published automatically. No change to the app.

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
