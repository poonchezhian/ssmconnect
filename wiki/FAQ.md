# FAQ

**Do I need to open port 22 or set up a bastion host?**
No. SSMConnect uses AWS Systems Manager, so port 22 can stay closed.

**Does SSMConnect store my AWS password or my SSH keys?**
No. With a company sign-in you sign in through your browser. Access keys, if you use them, go only to your own `~/.aws/credentials`. Keys stay where they are; SSMConnect only points your tools at them.

**Which tools can I connect with?**
PuTTY, WinSCP and MobaXterm for Linux (SSH), and Remote Desktop for Windows. Sessions are saved as `SSC - <server name>`.

**Can I copy files from one server to another?**
Yes. **Servers > File transfer** opens two panes, one server on each side. Select files or folders and press Copy. The data passes through your PC over the SSM tunnels, so port 22 stays closed and the servers do not need to reach each other. See [Copy Files Between Servers](File-Transfer).

**Why is there only one account on screen?**
The dashboard shows one sign-in at a time to keep things clear. Use the switcher in the top right to change. Connections of every sign-in keep running in the background.

**I added a new AWS account in my company. Will it appear?**
Yes. Run **Servers > Scan for new or changed servers**, or wait for the daily scan. It needs your sign-in to be active.

**Why does a server show PROD?**
Because its `Environment` tag (or `Env`, `Stage`, `Tier`) says production. See [Server Types and Tags](Server-Types-and-Tags).

**Can I rename an account?**
Yes: **AWS > Manage accounts**. It changes only the name SSMConnect shows, not your AWS profile.

**What happens when I close the window?**
It hides to the system tray and your connections stay up. **File > Exit** stops everything.

**Do my connections come back after a reboot?**
Yes, the tray icon starts when you sign in to Windows and reopens your connections.

**Is it safe for production servers?**
Tunnels listen on your PC only and never take over a busy port, and nothing runs as administrator. Please test on a non-production server first while the project is in early release.

**Windows says the app is from an unknown publisher.**
The installer is not code-signed yet. You can compare its SHA-256 with the one on the release page.

**Which AWS regions are supported?**
All of them. One region is searched per scan; pick it in the wizard.

**How do I update?**
A banner appears when a new version is out. Click **Update now**; your settings are kept.

**Where do I report a bug or ask for a feature?**
[GitHub Issues](https://github.com/poonchezhian/ssmconnect/issues).
