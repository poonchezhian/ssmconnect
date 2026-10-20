# Linux: Install, Update and Remove

## Install

1. Download `ssmconnect-0.5.0-linux-x86_64.bin` and its `.asc` signature from the [Linux 0.5.0 release](https://github.com/poonchezhian/ssmconnect/releases/tag/linux-v0.5.0).
2. [Check the signature](#check-the-signature-before-you-run-it).
3. Run it: open a terminal in the download folder and type `sh ./ssmconnect-0.5.0-linux-x86_64.bin` (or right-click the file, make it executable, and double-click it).

The installer window has four steps:

1. **Welcome.** Shows what it is going to do and the licence. If SSMConnect is already installed it says so and offers *Update* or *Remove*.
2. **Requirements.** Fetched from their own sources *before* SSMConnect is installed: OpenSSH (and optionally Remmina for Windows servers) from your distribution's repositories (it asks for your password), and the AWS CLI and Session Manager plugin from Amazon with Amazon's signature checked. Nothing third-party is carried inside the installer.
3. **Where to install.** *Just for me* (`~/.local`, no password) or *For all users* (`/usr/local`, asks for your password).
4. **Done.** Open SSMConnect from your application menu. The first run guides you through adding your AWS account.

No display (for example over ssh)? The same questions are asked in the terminal. Useful options: `--text`, `--yes` (accept the defaults), `--system` (all users), `--prefix DIR`, `--update-only`.

If GTK 4 or libadwaita are missing, the file itself offers to install them from your distribution before the window opens.

## Check the signature before you run it

Every file is signed with the **SSMConnect Linux Releases** key:

```
Fingerprint  FF9F 45E6 4711 7103 89B3  F1FC 7483 B1F8 DBE4 A803
Key ID       7483B1F8DBE4A803   (ed25519, expires 2028-10-15)
```

```
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-v0.5.0/ssmconnect-0.5.0-linux-x86_64.bin
curl -LO https://github.com/poonchezhian/ssmconnect/releases/download/linux-v0.5.0/ssmconnect-0.5.0-linux-x86_64.bin.asc
curl -LO https://raw.githubusercontent.com/poonchezhian/ssmconnect/linux/linux/SIGNING-KEY.asc

gpg --import SIGNING-KEY.asc
gpg --fingerprint 7483B1F8DBE4A803
gpg --verify ssmconnect-0.5.0-linux-x86_64.bin.asc ssmconnect-0.5.0-linux-x86_64.bin
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
- **File > Preferences** can check for new versions once a day (it only tells you, it never installs by itself).

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
