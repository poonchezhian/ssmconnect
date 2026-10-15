# Dashboard Guide

## The top

- **Three status pills:** *Connections* (for example "3 of 8 up"), *AWS sign-in* (OK, or "needs sign-in") and *AWS tools* (CLI and plugin installed). The first two always describe the sign-in you are looking at and change when you switch accounts. Hover a pill for details.
- **Sign in with one click:** when the *AWS sign-in* pill turns red, click it. Your browser opens, you sign in, and the pill turns green by itself. A server card with an expired sign-in also gets a **Sign in** button. Accounts that share one company sign-in need only one visit to the browser.
- **The summary line** says how many connections of the current sign-in are up, and when you have several sign-ins it adds the total across all of them. Red cards show the reason. The tray icon always covers everything.
- **Organization chips** appear when your sign-in has more than one AWS account.
- **Account switcher** in the header: see [AWS Accounts and Organizations](AWS-Accounts-and-Organizations).

## Server cards

Each card shows the server name, its type (a red **PROD** tag, or a grey one such as UAT or DEVELOPMENT), the operating system, the local port, and a status.

| Colour | Meaning |
|---|---|
| Green **Connected** | The tunnel is up and the local port is really open |
| Amber | Connecting, reconnecting or checking |
| Red **Problem** | Something is wrong; the card says what |
| Grey **Off** | The connection is switched off |

Buttons on a card: **PuTTY** and **WinSCP** for Linux servers, **Remote Desktop** and **Get password** for Windows servers. If no SSH tool is installed, **Copy session name** copies the saved session name for you. Hover a card for the instance ID and the full message.

## Start, Stop, Restart

| Button | What it does |
|---|---|
| **Start** | Opens all your secure connections. Each server gets a local port that PuTTY, WinSCP or Remote Desktop connects through. |
| **Stop** | Closes every connection. Open sessions drop. Your servers in AWS are not touched. |
| **Restart** | Closes and reopens every connection. Use it after your PC wakes from sleep or when a card shows a problem. |

Start is green, Stop is red, Restart is amber and the main action (**Add / change servers...**) is teal. Buttons are raised with a gloss and a soft shadow, lift slightly when you hover and press down when you click. A button with nothing to do (for example Start while already running) is faded but still shows a hover note that says why.

**Add / change servers...** opens the setup wizard to find servers and choose which ones to connect.

## Menus

| Menu | Items |
|---|---|
| **File** | Create desktop shortcut, Hide to tray, Exit |
| **AWS** | Add AWS account, Manage accounts (rename, remove), Sign in again |
| **Servers** | Scan for new or changed servers, What changed since the last scan |
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
| `keys\` | Converted `.ppk` keys for PuTTY |
| the log | **Help > Open log file** |
