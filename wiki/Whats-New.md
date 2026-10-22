# What's New

The full list with downloads and checksums is on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases).

## SSMConnect for Linux

There is now a native Linux edition with a signed installer. Read the [announcement](Linux-Edition) and the [Linux release notes](Linux-Release-Notes). The Windows versions below are unchanged.

## 1.0.50
- **No root in file transfer.** The file transfer window never works as root. Typing `root` is refused, and any account that turns out to have root rights (uid 0) is refused too. Use a normal user such as `ubuntu` or `ec2-user`.
- **Only connected servers are listed.** The server lists show only servers that are enabled and showing Connected on the dashboard right now. The organization list shows how many are connected. If servers connect later, click **Refresh**.

## 1.0.49
- **Switch user now uses sudo.** In the file transfer window, pick another user and click **Switch user**. SSMConnect stays signed in as the server's default user with its saved key and runs the file work through `sudo -u <user>`, the way Ubuntu lets its default user become someone else. No password sign-in has to be switched on. If sudo asks for a password, type the default user's sudo password in the box; otherwise leave it blank.

## 1.0.48
- **File transfer window layout fixed.** The server lists on each side were hidden behind the title bar in 1.0.47. Each side now shows its server drop-down, user and password boxes, as intended.
- **Servers listed under the organization.** The organization starts on the one you are viewing on the dashboard. Each server in the drop-down shows the AWS account it lives in, and picking one signs in as its default user with the saved key.
- **Sign-in problems are shown on the pane.** If a server cannot be signed in, the reason is printed under that side (hover for the full text) instead of a bare message.

## 1.0.47
- **File transfer signs in for you.** Pick a server and it signs in as that server's default user with its saved key, with nothing to type. To act as another user, pick or type the user, enter its password and click **Switch user**. A switched user signs in with a password only; the key is used for the default user alone.
- **Same organization only.** The file transfer window links servers of one top-level organization (company sign-in) at a time. Choose the organization at the top; both sides list only its servers. Copying to a server outside it is not possible.
- **This PC is no longer a side** of the file transfer window, so it is strictly server to server.

## 1.0.46
- **A proper file browser: Servers > File transfer.** Two side-by-side panes. Each pane is a server or **This PC**. Pick the login user on each side (type any user and click Connect to switch), open folders, select files or folders, and press Copy in either direction. Progress, Stop, and a warning before anything is replaced.
- **Server to server and to or from your PC** with the same window. The data passes through your PC, so the servers never need to reach each other. No extra tools needed.
- Replaces the 1.0.45 copy window, which needed Windows OpenSSH and a prompt window.

## 1.0.45
- **Copy files between servers.** New menu item **Servers > Copy files between servers...**. Pick a file or folder on one server and a folder on another, and SSMConnect copies it through your SSM connections. A window shows progress. Needs the OpenSSH Client that comes with Windows and a `.pem` key.
- **MobaXterm button now creates the sessions for you.** Click MobaXterm on a server card and SSMConnect saves your servers into MobaXterm first, then opens the right one. If MobaXterm is already open, the server opens in a new tab instead.
- **A clear message when MobaXterm does not start.** The click is logged, and if MobaXterm does not open you are told and the session name is copied.

## 1.0.44
- **MobaXterm button on the dashboard.** Each server card now has a MobaXterm button next to PuTTY and WinSCP, and it opens the saved session straight away.
- **Tools are found as soon as SSMConnect starts.** PuTTY, WinSCP and MobaXterm are all checked at launch, so the right buttons show up immediately. They are checked again when the window opens, and every two minutes after that.
- **Setup wizard and dashboard agree.** The "SSH tools found on this PC" line now uses the same check as everything else.

## 1.0.43
- **MobaXterm is found wherever it is installed.** SSMConnect now also checks the Windows installed-programs list, not only the usual folders, and the setup wizard uses the same check as the rest of the app.
- **A clearer message when MobaXterm has never been opened.** If it is installed but has not created its settings file yet, the log tells you to open it once, close it, and run Apply again.

## 1.0.42
- **One-click sign-in:** click the red AWS sign-in pill, or the new **Sign in** button on an affected server card, and your browser opens. Accounts under one company sign-in need just one visit.
- **Status row follows the account you view:** the Connections pill, the AWS sign-in pill and the summary now describe the selected sign-in. The summary also shows the total across all accounts when you have several.
- **Coloured, embossed buttons:** Start is green, Stop red, Restart amber, the main action teal. Glossy edges, soft shadows, a short hover lift and a press effect, on the toolbar and on every server card.

## 1.0.41
- **Accounts screen:** one collapsed line per sign-in; expand to see its organization accounts. Duplicate entries for the same sign-in or AWS account are merged.
- **Rescan finds new organizations:** a scan now looks for AWS accounts added under your sign-in and scans every organization, not only those with saved servers.
- **New buttons:** rounded, shaded buttons with hover and pressed states and small icons.
- **Hover notes** on Start, Stop, Restart and Add / change servers. A dimmed button explains why on hover.
- **Tighter screens:** full-width status pills, organization chips on the dashboard, local port on each card, and short help cards in the setup wizard.

## 1.0.40
- The build script no longer waits for a key press.
- Optional code-signing step in the build (used once a certificate is available).

## 1.0.39
- **Rename and remove accounts** from **AWS > Manage accounts**.
- A themed account switcher in the header.
- **Server types from AWS tags** (`Environment`, `Env`, `Stage`, `Tier`), with the name used only as a fallback.
- **Tunnel hardening:** local-only listeners, no start on a busy port.
- Organization grouping applies to accounts that were already discovered.

## 1.0.37 and earlier
- Account switcher showing one sign-in at a time.
- Add-account step built into the setup wizard.
- Multiple AWS accounts and organization grouping.
