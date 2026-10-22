# Dashboard Guide

## The layout

- **Left: organizations and filters.** Each sign-in (organization) has a status dot and a server count. Under **Show** you can filter the cards: *All servers*, *Connected*, *Needs attention* or *PROD only*. Three status pills sit at the bottom: *Connections* (for example "Connections: on"), *AWS sign-in* (OK, or needs sign-in) and *AWS tools* (CLI and plugin installed). Hover a pill for details.
- **Top: the organization you are viewing.** It shows the sign-in state and time left, how many servers are connected, when usage was last read, a search box, **Refresh usage** and **Sign in again**.
- **Sign in with one click.** When the *AWS sign-in* pill turns red, click it. Your browser opens, you sign in, and the pill turns green by itself. A card with an expired sign-in also gets a **Sign in** button. Accounts that share one company sign-in need only one visit to the browser.
- **Middle: the server cards.** Servers of several AWS accounts are grouped under a quiet heading per account. If there are more cards than fit, the list scrolls.
- **Bottom:** the colour key (including the usage colours), **Stop connections** (or **Start connections**) on the left and **Add / change servers...** on the right.
- **Account switcher:** see [AWS Accounts and Organizations](AWS-Accounts-and-Organizations).

## Server cards

Each card shows the server name, its type (a red **PROD** tag, or a grey one such as UAT or DEVELOPMENT), the operating system logo, one line with the OS, vCPU, memory and disk size, and a status. On the same row as the status, right-aligned, three small bars show how busy the server is: **CPU**, **memory** and **disk**, each with an icon and a percentage.

| Card colour | Meaning |
|---|---|
| Green **Connected** | The tunnel is up and the local port is really open |
| Amber | Signing in, connecting or reconnecting |
| Red **Stopped** | The server failed 3 tries in a row; the card says why. Click **Sign in again** to retry |
| Grey | The connection is switched off |

| Usage bar colour | Meaning |
|---|---|
| Green | under 60% in use |
| Amber | 60 to 85% |
| Red | over 85% |
| Grey `--` | no reading yet |

Usage is read with a short read-only command through SSM (it needs `ssm:SendCommand`). SSMConnect reads connected non-production servers by itself about every 5 minutes; production servers are read only when you click **Refresh usage**. The **Servers** menu has two switches to turn automatic reading off, or to include production. If your role may not run commands, the bars stay `--` and their tooltip says so.

Buttons on a card: **PuTTY**, **WinSCP** and **MobaXterm** for Linux servers; **Remote Desktop** and **Get password** for Windows servers. All buttons are always visible and dimmed when they cannot be used. Before a tool opens, SSMConnect checks that the server really accepts your login (key and user) and fixes it if it can. Hover a card for the instance ID and the full message.

## Stop and start connections

Every connection opens together when SSMConnect starts (or after you sign in) and stays open until you stop it or exit.

| Control | What it does |
|---|---|
| **Stop connections** (bottom left) | Closes every connection. Open sessions drop. Your servers in AWS are not touched. The button then reads **Start connections** |
| **Start connections** | Opens them all again |
| **Connections > Restart connections** | Closes and reopens everything. Use it after your PC wakes from sleep |
| **Add / change servers...** | Opens the setup wizard to find servers and choose which ones to connect |

If AWS ends a session, SSMConnect quietly opens the next one. A server that fails 3 tries in a row shows **Stopped** until you click **Sign in again**.

## Copy files between servers

**Servers > File transfer: browse and copy files...** opens two panes, one server on each side, with Copy buttons in the middle. See [Copy Files Between Servers](File-Transfer).

## Menus

| Menu | Items |
|---|---|
| **File** | Create desktop shortcut, Hide to tray, Exit |
| **AWS** | Add AWS account, Manage accounts (rename, remove), Sign in again |
| **Connections** | Start, Stop and Restart connections |
| **Servers** | Scan for new or changed servers, What changed since the last scan, File transfer (copy files between servers), and the two switches for reading CPU, memory and disk use |
| **Help** | About, Check for updates, Open log file, Uninstall |

## The tray icon

SSMConnect starts with Windows and lives in the system tray. The icon colour matches the overall status. Closing the window only hides it to the tray; **File > Exit** stops all connections.

## Daily server scan

The first time SSMConnect runs each day (and again if your PC stays on past midnight) it quietly re-lists the servers of every account you use, including every organization account. Nothing opens or changes. New, removed, renamed, unreachable or key-changed servers appear as a tray notification and under **Servers > What changed since the last scan**. A sign-in that has expired is skipped and retried an hour later. **Servers > Scan for new or changed servers** does it right now and shows what it found.

## Where things are stored

Everything is under `%LOCALAPPDATA%\SSMConnect`:

| File | What |
|---|---|
| `config.json` | Your saved connections and friendly names |
| `status.json` | Live tunnel status |
| `discovery.json` | The servers found by the last scans |
| `keys\` | Converted `.ppk` keys for PuTTY (readable only by your Windows user) |
| the log | **Help > Open log file** |
