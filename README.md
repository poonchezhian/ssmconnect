# SSMConnect

SSH-style access to AWS servers **with port 22 closed**, using AWS Systems Manager (SSM) Session Manager.

SSMConnect is a small Windows app for developers who just want to connect. A short wizard finds your servers (Linux over SSH, Windows over Remote Desktop), opens secure tunnels through SSM, and saves ready-to-use sessions in **PuTTY**, **WinSCP** and **MobaXterm**. No SSH keys on open ports, no bastion host, no infrastructure knowledge needed.

> **Status:** early release. Please test against a non-production server first.

## Download

**[Download SSMConnect for Windows (SSMConnect-Setup-x64.exe)](https://github.com/poonchezhian/ssmconnect/releases/latest/download/SSMConnect-Setup-x64.exe)**: one file, 64-bit Windows 10 or later. It installs the AWS CLI and the Session Manager plugin too if they are missing.

All versions, release notes and checksums are on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases/latest). `SHA256SUMS.txt` on each release lists the SHA-256 of every file; compare it with `Get-FileHash .\SSMConnect-Setup-x64.exe` if you like.

Once installed, SSMConnect keeps itself up to date (see [Updates](#updates)).

## How it works

```
PuTTY / WinSCP / MobaXterm  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your EC2 instance (port 22)
```

- The **dashboard** (`SSMConnect.Wizard.exe`) opens automatically when the install finishes. It shows every server with a live status that turns **green** once the tunnel is really up, and says in plain words what is wrong when it is not. A **tray icon** shows the same status at a glance and lets you start, stop or restart your connections.
- The **setup wizard** (the dashboard's **Add / change servers** button) lists running EC2 instances, shows which ones SSM can reach, and assigns each a local port starting at `2201`.
- The **tunnel manager** runs inside the same app (dashboard and tray icon), **as you, not as an administrator**. It keeps one `aws ssm start-session` port-forward alive per server, reacts to config changes immediately, writes its live status to `%LOCALAPPDATA%\SSMConnect\status.json`, and restarts tunnels that drop (idle timeout, network blip). The tray icon starts at every sign-in, so your connections come back by themselves. If you exit the tray icon, the connections stop.
- The servers found by setup are remembered (`%LOCALAPPDATA%\SSMConnect\discovery.json`) and shown again next time. Once a day, the first time SSMConnect runs that day, it quietly re-lists the servers of every account you use. Nothing opens: new, removed, renamed, unreachable or key-changed servers appear as a tray notification and under **Servers > What changed since the last scan**. Your saved PuTTY / WinSCP sessions stay as they are until you update them. **Servers > Scan for new or changed servers** checks immediately; an expired AWS sign-in is skipped and retried an hour later.
- Saved sessions are named `SSC - <server name>` (older `SSMConnect - ` sessions are replaced automatically), carry the server's login user and key file, and point at `127.0.0.1:<local port>`. Your own sessions are never touched.

## AWS accounts and organizations

- A **primary account** is one company sign-in (AWS IAM Identity Center) or one set of access keys. Under a company sign-in there are usually several **organization accounts** (member accounts); SSMConnect finds all of them when you sign in, so you never pick them one by one.
- The **account switcher** (top right of the dashboard) lists your primary accounts only, with the current one on top. The window shows **one primary account at a time**; its servers are grouped under a quiet heading per organization account. Switching is only a view change: connections of other accounts keep running.
- **Add AWS account** (in the switcher, or **AWS > Add AWS account**) opens the setup wizard on one page: paste your company's sign-in address (it is usually pre-filled), sign in once in the browser, and continue to region and servers. Access keys are a small link on the same page.
- **AWS > Manage accounts** lets you give any account or organization a **friendly name** (shown in the switcher, the headings and the wizard; the AWS profile name is not changed) and **remove** an account. Removing deletes its saved connections from SSMConnect and its login from `~/.aws`; your PuTTY / WinSCP sessions and the servers in AWS are not touched.
- When you open the server step, SSMConnect checks that every organization account under the sign-in has been searched, even if the account was found before, so the grouping is always complete.

## Server types come from tags

A server's type (Production, Staging, UAT, Test, Development, ...) is read from its AWS tags, not its name: `Environment` first, then `Env`, `Stage`, `Tier` and similar, in any letter case. Values such as `prod`, `PRD` or `production` all show as **PROD** (red); `non-prod` and `pre-prod` are never treated as production. The server name is used only as a last resort when a server has no such tag. Tag your instances with `Environment` for the most reliable result.

## Requirements

| Needed | Why |
|---|---|
| Windows 10 / Server 2016 or later, 64-bit | Dashboard, tray icon and tunnel manager |
| [AWS CLI v2](https://awscli.amazonaws.com/AWSCLIV2.msi) | Talks to AWS |
| [Session Manager plugin](https://s3.amazonaws.com/session-manager-downloads/plugin/latest/windows/SessionManagerPluginSetup.exe) | Opens the SSM tunnel |
| An AWS account login (company sign-in or access keys) | Sign-in. The wizard creates it for you; add as many accounts as you need |
| PuTTY, WinSCP and/or MobaXterm | The tool you connect with |

On the AWS side, each server needs the SSM Agent running and an instance role that allows SSM (for example `AmazonSSMManagedInstanceCore`). Your own role needs `ec2:DescribeInstances`, `ssm:DescribeInstanceInformation` and `ssm:StartSession`. The wizard tells you plainly when something is missing.

The `SSMConnect-Setup` installer includes the Session Manager plugin and installs it only if it is missing. The AWS CLI is not bundled (it would push the installer past GitHub's 25 MB web-upload limit): the first time you open SSMConnect, the setup wizard detects what is missing and offers an **Install missing components automatically** button. It downloads the official installers from Amazon, checks their digital signature, and installs them (one Windows permission prompt each). PuTTY, WinSCP and MobaXterm are never bundled.

## Install

1. [Download SSMConnect-Setup-x64.exe](https://github.com/poonchezhian/ssmconnect/releases/latest/download/SSMConnect-Setup-x64.exe) (or pick a version on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases)). It is the only file you need. It installs the Session Manager plugin if missing, and the app fetches the AWS CLI from Amazon on first start if that is missing.
2. Run it, read and accept the Apache 2.0 licence, install.
3. Leave **Open the SSMConnect dashboard now** ticked on the last page. The dashboard opens and, on first run, starts the four-step setup. It also offers a desktop shortcut (File > Create desktop shortcut).
   To pin it to the taskbar, right-click the open SSMConnect window's taskbar button and choose **Pin to taskbar** (Windows does not let apps do this for you).
4. In the wizard, **add your AWS account** first (company sign-in URL or access keys; the wizard looks up your accounts and roles for you), then choose a region once it connects. Add more accounts later from the account switcher; servers from different accounts can run side by side.
5. Open PuTTY / WinSCP / MobaXterm and pick the session called `SSC - <your server>`.

## Updates

SSMConnect looks for a newer release on GitHub a few seconds after it starts and then once a day, quietly. When one exists, a banner appears at the top of the dashboard and the tray icon shows one message. Nothing installs by itself.

- **Update now** downloads the new setup file, checks its SHA-256 against the checksum published with the release, and runs it. Setup closes SSMConnect, upgrades it **in place** (Windows asks for permission once), and opens the new version. Your servers, keys, accounts and saved sessions are kept.
- **Later** hides the banner until the next daily check. **What's new** opens the release notes.
- **Help > Check for updates now** checks immediately.

If your PC cannot reach GitHub the check is skipped silently and tried again later.

## Uninstall

Use **Help > Uninstall SSMConnect** in the dashboard, the **Uninstall SSMConnect** Start-menu shortcut, or **Settings > Apps > SSMConnect > Uninstall**. The AWS CLI and Session Manager plugin are left installed because other tools may use them. This closes the tray icon, deletes the program files and `%LOCALAPPDATA%\SSMConnect` (saved connections, status, log), and removes the `SSC - ...` sessions from PuTTY, WinSCP and MobaXterm for the account that runs the uninstall. Your `~/.aws` files and your own sessions are left alone.

To remove only the saved sessions and settings, run `SSMConnect.Wizard.exe --cleanup`.

Running the setup EXE on a PC that already has SSMConnect asks what to do, with two radio buttons: **Upgrade** (replaces the program in place and keeps your servers, keys and saved sessions) or **Uninstall**. Uninstall has a **Keep my settings** tick box (on by default); when kept, installing SSMConnect again picks the servers up where you left off. For unattended use, an uninstall run with `/quiet` also keeps the settings unless you add `RemoveSettings=1`. **Help > About** shows the version and build number.

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

## Troubleshooting

| Symptom | Fix |
|---|---|
| Server shows **SSM ready: No** | SSM Agent not running, or the instance role lacks SSM permissions |
| "Your AWS sign-in has expired" | Click **AWS > Sign in again** in the dashboard (or **Sign in again** in the wizard) |
| Row is amber / red in the dashboard | The **Details** column says what to do (sign in, install a tool, start the connections, port already in use) |
| Tray icon missing | Check the hidden-icons arrow in the taskbar, or open SSMConnect from the Start menu |
| MobaXterm sessions missing | Close MobaXterm, then click Apply again |
| WinSCP transfers fail with parallel connections | SSM port forwarding may limit simultaneous connections; set WinSCP to a single connection |

## Known limitations

- Windows only. Linux servers over SSH and Windows servers over Remote Desktop.
- Accounts are grouped by organization account only; the organizational-unit folders above them are not shown. One region is searched per scan.
- Nothing runs as an administrator or SYSTEM. Each Windows user has their own settings, tunnels and tray icon, and uses their own `~/.aws` sign-in. Tunnels run only while that user is signed in to Windows.
- Anyone logged in on the same PC at the same time can reach the local tunnel ports (`127.0.0.1:2201` and up). They still need valid SSH credentials for the server, so the SSH login is what protects it.

## Tunnel security

- Tunnels listen on this PC only (`127.0.0.1`). If a tunnel port is ever found open to the network, SSMConnect closes that session and reports it.
- A tunnel is never started on a port another program already uses, so nothing else can answer in place of your server and the dashboard cannot show a false green.
- Everything read from `config.json` (instance ID, account, region, ports, login) is validated before it is passed to the AWS CLI, which is always started with an argument list, never a command line string.
- Sessions are closed when SSMConnect exits (they are tied to the app), and no password or key is stored by SSMConnect. A copied Windows password leaves the clipboard after 60 seconds.
- Access keys you type are written only to your own `~/.aws/credentials`, exactly as the AWS CLI does; a `.ssmconnect.bak` copy of each AWS file is kept beside it before any change.
- The installer and executables are not code-signed yet, so Windows SmartScreen may warn on first run.
- MobaXterm configuration is best effort (its `.ini` is backed up to `MobaXterm.ini.ssmconnect.bak` first).

## Licence

Copyright 2026 Poonchezhian Periasamy (poonchezhian@gmail.com) - [Blog](https://medium.com/@poonchezhian) - [Source](https://github.com/poonchezhian/ssmconnect).
Licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE).

SSMConnect is an independent project and is not affiliated with or endorsed by Amazon Web Services. "AWS" is a trademark of Amazon.com, Inc. or its affiliates.


## Key files, Windows servers and company sign-in

- **Key files.** In setup, step 3, click **Find key files on this PC** (searches the usual folders and local drives), **Search a folder...** (any folder you pick, sub-folders included) or **Browse for a key...**. Each Linux server is matched to the key with the same name as its AWS key pair (for example key pair `prod-key` -> `prod-key.pem`); the **Key file** column shows what was matched. PuTTY needs `.ppk`: when PuTTYgen is installed, `.pem` files are converted automatically into `%LOCALAPPDATA%\SSMConnect\keys`. WinSCP and MobaXterm use the key as it is.
- **Windows servers.** They are listed with the rest, tunnelled to port 3389, and opened with **Open Remote Desktop** in the dashboard (or double-click the row). **Copy Windows password** decrypts the generated Administrator password with the server's `.pem` key and puts it on the clipboard for 60 seconds.
- **Select all / Select none** in the server list ticks every server that is SSM-ready.
- **Supported Windows:** 64-bit Windows 10 (1607+), Windows 11, Windows Server 2016, 2019, 2022 and later with the desktop experience (Server Core has no screen for the setup window).

### For IT teams: pre-fill the company sign-in

On a PC that is part of the company network, SSMConnect finds the IAM Identity Center address and region by itself, in this order: (1) settings you push: registry `HKLM\SOFTWARE\Policies\SSMConnect` values `SsoStartUrl` and `SsoRegion` (Group Policy / Intune), or `%ProgramData%\SSMConnect\sso.json` containing `{"startUrl":"https://yourcompany.awsapps.com/start","region":"ap-south-1"}`, or the environment variables `SSMCONNECT_SSO_URL` / `SSMCONNECT_SSO_REGION`; (2) existing `~/.aws/config` settings; (3) sign-ins the AWS CLI already cached; (4) as a last resort, a guess from the company domain, which is always shown for confirmation. Without one of the first three, nothing on the network announces the address, so a one-time policy or file is the dependable way.
