# Linux: Install, Update and Remove

## Which file is for my system?

| Your system (vendor-supported releases, checked 2026-10) | Use |
|---|---|
| Fedora 43 and 44 | the `.rpm` |
| RHEL, AlmaLinux, Rocky Linux 10 | the `.rpm` |
| openSUSE Leap 16.0 and Tumbleweed | the `.rpm` |
| Ubuntu 24.04 and 26.04, Linux Mint 22 | the `.deb` |
| Debian 13 | the `.deb` |
| RHEL, AlmaLinux, Rocky Linux 8 and 9 | the Flatpak |
| Ubuntu 22.04, Linux Mint 21 | the Flatpak |
| Debian 12 | the Flatpak |

The native packages and the `.bin` installer need GTK 4.12+, libadwaita 1.5+ and glibc 2.39+, which the first five rows ship. The older releases in the last three rows cannot run them, so the Flatpak brings its own GTK and libadwaita (the GNOME runtime). Releases a vendor no longer supports (for example Fedora 42, Debian 11, openSUSE Leap 15.6) are not covered.

**Flatpak** (needs the `flatpak` package, which is in the standard repositories of all three families; on Ubuntu 22.04 and Mint 21 it is already there or one `sudo apt install flatpak` away):

```
flatpak install --user https://poonchezhian.github.io/ssmconnect/ssmconnect.flatpakref
flatpak run io.github.poonchezhian.SSMConnect
```

`flatpak update` brings new versions. The Flatpak keeps its own settings (`~/.var/app/io.github.poonchezhian.SSMConnect`), reads your `~/.aws` and `~/.ssh`, and opens the terminal, file manager, Remote Desktop client and ssh on your desktop. A single-file copy, `ssmconnect-linux-x86_64.flatpak` (with `.asc` signature), is on every release page; install it with `flatpak install --user ssmconnect-linux-x86_64.flatpak`.

## Install

1. Download `ssmconnect-linux-x86_64.bin` and its `.asc` signature from the [newest Linux release](https://github.com/poonchezhian/ssmconnect/releases/tag/linux-latest).
2. [Check the signature](#check-the-signature-before-you-run-it).
3. Run it: open a terminal in the download folder and type `sh ./ssmconnect-linux-x86_64.bin` (or right-click the file, make it executable, and double-click it).

The installer window has four steps:

1. **Welcome.** Shows what it is going to do and the licence. If SSMConnect is already installed it says so and offers *Update* or *Remove*.
2. **Requirements.** Fetched from their own sources *before* SSMConnect is installed: OpenSSH (and optionally Remmina for Windows servers) from your distribution's repositories (it asks for your password), and the AWS CLI and Session Manager plugin from Amazon with Amazon's signature checked. Nothing third-party is carried inside the installer.
3. **Where to install.** *Just for me* (`~/.local`, no password) or *For all users* (`/usr/local`, asks for your password).
4. **Done.** Open SSMConnect from your application menu. The first run guides you through adding your AWS account.

No display (for example over ssh)? The same questions are asked in the terminal. Useful options: `--text`, `--yes` (accept the defaults), `--system` (all users), `--prefix DIR`, `--update-only`.

If GTK 4 or libadwaita are missing, the file itself offers to install them from your distribution before the window opens.

The same file names always point at the newest version: `ssmconnect-linux-x86_64.bin`, `ssmconnect-linux-x86_64.rpm` and `ssmconnect-linux-amd64.deb` (each with a `.asc` signature) under [releases/download/linux-latest](https://github.com/poonchezhian/ssmconnect/releases/tag/linux-latest). Older versions stay on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases).

## Check the signature before you run it

Every file is signed with the **SSMConnect Linux Releases** key:

```
Fingerprint  FF9F 45E6 4711 7103 89B3  F1FC 7483 B1F8 DBE4 A803
Key ID       7483B1F8DBE4A803   (ed25519, expires 2028-10-15)
```

```
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin.asc
curl -LO https://raw.githubusercontent.com/poonchezhian/ssmconnect/linux/linux/SIGNING-KEY.asc

gpg --import SIGNING-KEY.asc
gpg --fingerprint 7483B1F8DBE4A803
gpg --verify ssmconnect-linux-x86_64.bin.asc ssmconnect-linux-x86_64.bin
```

- The fingerprint that `gpg --fingerprint` prints must match the one above, character for character.
- You want to see **Good signature from "SSMConnect Linux Releases"**. The warning *"This key is not certified with a trusted signature"* is normal: it only means you have not personally certified the key.
- **BAD signature**, or a different fingerprint: do not run the file, and [report it](https://github.com/poonchezhian/ssmconnect/issues).
- `SHA256SUMS.txt` (with its own `.asc`) lists the checksums of all files: `sha256sum -c SHA256SUMS.txt`.

The installer also verifies what it downloads for you: the AWS CLI and the Session Manager plugin are checked against Amazon's own signing keys, which are built into SSMConnect and pinned by fingerprint. A download that does not match is refused.

## Update

Run the newer `.bin`. It finds the installation, updates it **in place** and keeps your servers, accounts and settings (a copy of your saved servers is saved first, as `config.json.before-update-from-<version>`). If SSMConnect is running it keeps using the old version until you close and open it again.

- `--update-only` does nothing if SSMConnect is not installed.
- Going back to an older version needs an explicit confirmation.
- An install made by a package manager (`dnf`, `apt`) is left to that package manager.
- **File > Preferences** can check for new versions once a day and tell you when one exists.
- **Installed from the `.rpm` or `.deb`?** The package adds the SSMConnect update repository (a signed repository on GitHub Pages) and its key, so new versions arrive with your system's normal updates: `sudo dnf upgrade` (Fedora, RHEL 10, openSUSE Tumbleweed) or `sudo apt update && sudo apt upgrade` (Debian, Ubuntu), or your software centre. To stop getting updates from it, set `enabled=0` in `/etc/yum.repos.d/ssmconnect.repo` or `Enabled: no` in `/etc/apt/sources.list.d/ssmconnect.sources`. To add it before installing, see https://poonchezhian.github.io/ssmconnect/.
- **Help > Check for updates**, then **Update now**, installs the new version for you: SSMConnect downloads the installer, checks its signature and checksum, closes, updates itself (your servers and settings are kept) and opens again. This works for a copy installed by the installer in your own folder; for a package-manager or all-users install it opens the download page instead.

## Remove

Run the installer again and choose **Remove SSMConnect…**, or `ssmconnect uninstall` (add `--prefix /usr/local` if you installed for all users). **Your saved servers, settings and log are kept** unless you tick *Also delete my saved servers, settings and log* (`--remove-settings`). The AWS tools SSMConnect downloaded are kept unless you tick *Also remove the AWS CLI and Session Manager plugin…* (`--remove-tools`). Your `~/.aws` and your own ssh hosts are never touched.

## Where things live

| What | Where |
|---|---|
| Settings and saved servers | `~/.config/ssmconnect/` |
| Log and live status | `~/.local/state/ssmconnect/` |
| AWS tools downloaded by SSMConnect | `~/.local/share/ssmconnect/` |
| The program (just for me) | `~/.local/bin/ssmconnect` |
| ssh names (`ssh ssc-…`) | `~/.ssh/ssmconnect.conf`, loaded by one line in `~/.ssh/config` |

See also [Linux Edition](Linux-Edition) and [Troubleshooting](Troubleshooting).
