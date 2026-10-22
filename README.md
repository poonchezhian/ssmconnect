# SSMConnect

SSH-style access to AWS servers **with port 22 closed**, using AWS Systems Manager (SSM) Session Manager.

**Copy files from one server to another with two panes and a Copy button, while port 22 stays closed on both.** No S3 bucket, no `scp` commands, no bastion host. The servers never need to reach each other, because the data passes through your PC over the SSM tunnels.

![Copy files between servers: pick a server on each side, select files, press Copy (illustration with sample servers)](https://poonchezhian.github.io/assets/file-transfer-between-servers.png)

SSMConnect is a small app for developers who just want to connect. A short wizard finds your servers (Linux over SSH, Windows over Remote Desktop), opens secure tunnels through SSM, and saves ready-to-use sessions in **PuTTY**, **WinSCP** and **MobaXterm** (Windows) or makes each server one short command away (Linux). No SSH keys on open ports, no bastion host, no infrastructure knowledge needed.

> **Status:** early release. Please test against a non-production server first.

## Copy files between servers

1. Open **Servers > File transfer** and pick your organization.
2. Pick a server on the left and one on the right. SSMConnect signs in with the server's saved key; there is nothing to type.
3. Select files or folders, then press **Copy ▶** or **◀ Copy**. Watch the progress bar, or press **Stop**.

```
Server A  ->  SSM tunnel  ->  your PC  ->  SSM tunnel  ->  Server B
```

- Works across VPCs, subnets and AWS accounts of one sign-in organization. No network path between the servers is needed.
- Copies whole folders, warns before it replaces anything, and never works as root. Need another user? **Switch user** uses `sudo`.
- Files stream through your PC's memory and are never saved to its disk.
- Linux (SSH) servers only. For very large or scheduled transfers, use S3. Full guide: [Copy Files Between Servers](https://github.com/poonchezhian/ssmconnect/wiki/File-Transfer) or the [website guide](https://poonchezhian.github.io/file-transfer/).

## Download

**[SSMConnect for Windows (SSMConnect-Setup-x64.exe)](https://github.com/poonchezhian/ssmconnect/releases/latest/download/SSMConnect-Setup-x64.exe)**: one file, 64-bit Windows 10 or later. It installs the AWS CLI and the Session Manager plugin too if they are missing.

**[SSMConnect for Linux (ssmconnect-linux-x86_64.bin)](https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin)** (about 11 MB): one signed installer for 64-bit Linux desktops, always the newest version. Also as [`.rpm`](https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.rpm) and [`.deb`](https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-amd64.deb). See [Linux edition](#linux-edition) below.

All versions, release notes and checksums are on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases). Windows versions are tagged `vX.Y.Z` (the release marked **Latest**); Linux versions are tagged `linux-vX.Y.Z` and titled **SSMConnect for Linux**. Installed copies check for updates by themselves ([Windows](https://github.com/poonchezhian/ssmconnect/wiki/Getting-Started#keeping-it-up-to-date), [Linux](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update#update)).

## How it works

```
PuTTY / WinSCP / MobaXterm / ssh  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

The dashboard shows every server with a live status that turns **green** once its tunnel is really up. One tunnel per server runs **as you, not as an administrator**, and restarts by itself if it drops. Sessions are named `SSC - <server name>` (Windows) or `ssh ssc-<server name>` (Linux); your own sessions and `~/.ssh/config` entries are never touched.

More in the wiki: [Copy Files Between Servers](https://github.com/poonchezhian/ssmconnect/wiki/File-Transfer), [Getting Started](https://github.com/poonchezhian/ssmconnect/wiki/Getting-Started), [Dashboard Guide](https://github.com/poonchezhian/ssmconnect/wiki/Dashboard-Guide), [AWS accounts and organizations](https://github.com/poonchezhian/ssmconnect/wiki/AWS-Accounts-and-Organizations), [Server types and tags](https://github.com/poonchezhian/ssmconnect/wiki/Server-Types-and-Tags).

## Requirements

- Windows 10 / Server 2016 or later (64-bit), or a Linux desktop that its vendor still supports (Fedora, RHEL family, openSUSE, Ubuntu, Debian, Mint; older releases use the Flatpak)
- [AWS CLI v2](https://awscli.amazonaws.com/AWSCLIV2.msi) and the Session Manager plugin: both installers fetch them from Amazon (signature checked) if they are missing
- An AWS sign-in (company sign-in or access keys; the wizard sets it up) and PuTTY, WinSCP and/or MobaXterm on Windows
- On the AWS side, each server needs the SSM Agent and an instance role such as `AmazonSSMManagedInstanceCore`; your own role needs `ec2:DescribeInstances`, `ssm:DescribeInstanceInformation` and `ssm:StartSession`. See [For IT Teams](https://github.com/poonchezhian/ssmconnect/wiki/For-IT-Teams).

## Install (Windows)

Run `SSMConnect-Setup-x64.exe`, accept the Apache 2.0 licence, and leave **Open the SSMConnect dashboard now** ticked: the setup wizard starts on first run. Details, updating and uninstalling: [Getting Started](https://github.com/poonchezhian/ssmconnect/wiki/Getting-Started). Unattended installs: [For IT Teams](https://github.com/poonchezhian/ssmconnect/wiki/For-IT-Teams#unattended-install-and-uninstall).

## Linux edition

A native Linux app (GTK 4 + libadwaita, written in Rust; no Wine, Mono or Electron) that does the same job, with **Files** over SFTP and **Remote Desktop** for Windows servers. Install the `.rpm` (Fedora, RHEL family 10, openSUSE) or `.deb` (Ubuntu 24.04+, Debian 13, Mint 22), or run the `.bin`; RHEL family 8 and 9, Ubuntu 22.04, Debian 12 and Mint 21 use the Flatpak (`flatpak install --user https://poonchezhian.github.io/ssmconnect/ssmconnect.flatpakref`). The packages add a signed update repository, so new releases arrive with `dnf upgrade` / `apt upgrade` / `flatpak update`. [Which file for which system](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update#which-file-is-for-my-system).

Every Linux release file has a detached OpenPGP signature (`.asc`) from the *SSMConnect Linux Releases* key, fingerprint

```
FF9F 45E6 4711 7103 89B3  F1FC 7483 B1F8 DBE4 A803      (key ID 7483B1F8DBE4A803, ed25519)
```

The public key is [`linux/SIGNING-KEY.asc`](linux/SIGNING-KEY.asc); [how to check a download](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update#check-the-signature-before-you-run-it). More: [Linux edition](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Edition), [Install and update](https://github.com/poonchezhian/ssmconnect/wiki/Linux-Install-and-Update), [`linux/README.md`](linux/README.md), [`linux/CHANGELOG.md`](linux/CHANGELOG.md). The Linux code lives under `linux/`; everything else in this repository is the Windows app.

## Security and limitations

Tunnels listen on `127.0.0.1` only, never take over a busy port, and nothing runs as administrator; no passwords or keys are stored. Windows executables are not code-signed yet, so SmartScreen may warn on first run. Details and the full list of limitations: [Security](https://github.com/poonchezhian/ssmconnect/wiki/Security), [FAQ](https://github.com/poonchezhian/ssmconnect/wiki/FAQ), [Troubleshooting](https://github.com/poonchezhian/ssmconnect/wiki/Troubleshooting).

## Build from source

Run from an elevated PowerShell. `build.ps1` installs the build tools it needs (.NET 8 SDK and [WiX Toolset v3.14](https://github.com/wixtoolset/wix3/releases)) via `install-build-tools.ps1`, downloads and signature-checks the AWS installers, then builds everything.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output in `installer\output\`: a single file, `SSMConnect-Setup-<version>-x64.exe`. The build stops if that file reaches 24 MB, because GitHub's web upload accepts files only up to 25 MB. The latest build is also kept in the repository at `dist\SSMConnect-Setup-x64.exe`.

```
.github\workflows       release.yml: builds on every push; a pushed tag like v1.1.0 publishes the GitHub Release
src\SSMConnect.Core      shared library: AWS CLI wrapper, tunnel manager, config, PuTTY/WinSCP/MobaXterm setup, update checker
src\SSMConnect.Wizard    WinForms dashboard, tray icon and setup wizard (also handles --cleanup)
installer\               WiX v3 (Product.wxs = embedded MSI, Bundle.wxs = the single setup EXE), build script
VERSION                  current version (updated by build.ps1, matched by release tags)
build.ps1                publishes the app framework-dependent (.NET 8 Desktop Runtime, downloaded by the setup file when missing), then builds the single setup EXE
install-build-tools.ps1  installs .NET 8 SDK and WiX v3 for building
```

## Publishing a release

1. Set the version in the `VERSION` file (for example `1.1.0`), commit it, then tag that commit: `git tag v1.1.0 && git push origin main v1.1.0`. The tag must match `VERSION`; the workflow stops if they differ.
2. The `build-and-release` workflow builds the installer with that version and attaches `SSMConnect-Setup-1.1.0-x64.exe`, `SSMConnect-Setup-x64.exe` (the stable name used by the Download link) and `SHA256SUMS.txt` to a new GitHub Release.
3. Installed copies find it within a day and offer **Update now**.

The update check reads `https://api.github.com/repos/poonchezhian/ssmconnect/releases/latest`. If the repository is renamed or moved, change `UpdateChecker.Repo` in `src/SSMConnect.Core/UpdateChecker.cs`, the links in this file and the two `ARPHELPLINK` properties in `installer/Product.wxs`. Local builds made with `build.ps1` raise the patch number in `VERSION` by one each time (commit the file) and are not published.

## Licence

Copyright 2026 Poonchezhian Periasamy (poonchezhian@gmail.com) - [Website](https://poonchezhian.github.io/) - [Blog](https://poonchezhian.github.io/blog/) - [Medium](https://medium.com/@poonchezhian) - [Source](https://github.com/poonchezhian/ssmconnect).
Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE).

SSMConnect is an independent project and is not affiliated with or endorsed by Amazon Web Services. "AWS" is a trademark of Amazon.com, Inc. or its affiliates.


