# Getting Started

## What you need

| Needed | Why |
|---|---|
| Windows 10 (1607+), Windows 11 or Windows Server 2016+ with a desktop, 64-bit | Runs the dashboard, tray icon and tunnels |
| An AWS login: your company sign-in (IAM Identity Center) or access keys | To find and reach your servers |
| PuTTY, WinSCP and/or MobaXterm (or Remote Desktop for Windows servers) | The tool you actually connect with |

The **AWS CLI v2** and the **Session Manager plugin** are also needed. The installer includes the plugin, and the app offers to install the AWS CLI for you on first start (checked against Amazon's digital signature).

## Install

1. [Download SSMConnect-Setup-x64.exe](https://github.com/poonchezhian/ssmconnect/releases/latest/download/SSMConnect-Setup-x64.exe).
2. Run it, accept the licence, and install. Windows asks for permission once.
3. Leave **Open the SSMConnect dashboard now** ticked on the last page.

Tip: to pin it to the taskbar, right-click the open SSMConnect window's taskbar button and choose **Pin to taskbar**. **File > Create desktop shortcut** adds a desktop icon.

## First run: the setup wizard

The wizard has five short steps. The page on the right of each step explains why it is there.

1. **Requirements.** Checks the AWS CLI and the Session Manager plugin. If one is missing, click **Install missing components automatically**.
2. **AWS account.** Paste your company sign-in address (it is often pre-filled), sign in once in the browser, and choose a region. Every AWS account under the sign-in comes along automatically. Using access keys instead? There is a small link on the same page.
3. **Servers.** SSMConnect lists your running servers and marks which ones SSM can reach. Tick the ones you want.
4. **SSH keys.** Each Linux server is matched to its key file. Use **Find key files on this PC**, **Search a folder...** or **Browse for a key...** to help.
5. **Save.** SSMConnect writes the sessions into PuTTY, WinSCP and MobaXterm and starts the tunnels.

## Connect

- Open PuTTY, WinSCP or MobaXterm and pick the session named **SSC - your server name**, or
- click **PuTTY** / **WinSCP** (Linux) or **Remote Desktop** (Windows) on the server's card in the dashboard.

A card turns **green** only when the tunnel is really up. If it is not, the card says in plain words what is wrong.

## Keeping it up to date

SSMConnect checks GitHub for a new version a few seconds after it starts and then once a day. When one exists, a banner appears; nothing installs by itself. **Update now** downloads the new setup, verifies its SHA-256 checksum, and upgrades in place. Your servers, keys, accounts and sessions are kept. **Help > Check for updates** checks immediately.

## Uninstall

Use **Help > Uninstall SSMConnect**, the Start-menu shortcut, or **Settings > Apps > SSMConnect**. A **Keep my settings** box (ticked by default) lets a later install pick up where you left off. The AWS CLI and Session Manager plugin stay installed, and your own PuTTY/WinSCP sessions and `~/.aws` files are never touched.
