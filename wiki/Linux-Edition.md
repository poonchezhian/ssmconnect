# SSMConnect for Linux

**SSMConnect now runs natively on Linux.** Same idea as on Windows: connect to your AWS servers with port 22 closed, no bastion host, no VPN, no infrastructure knowledge needed.

```
ssh ssc-web-1  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your server (port 22)
```

## What you get

- A native desktop app (GTK 4 and libadwaita, written in Rust, light and dark themes). Not Wine, not Mono, not Electron.
- The same dashboard: one card per server, a live status dot, PROD / UAT tags from your AWS tags, Start / Stop / Restart, accounts and organizations.
- **Terminal** opens `ssh` to the server. **Files** opens the server's folders over SFTP in your file manager (or the `sftp` client in a terminal). **Remote Desktop** and **Get password** for Windows servers (Remmina or FreeRDP).
- Every server also gets a short name for the command line: `ssh ssc-<server-name>`, `scp`, `rsync`, VS Code Remote-SSH.
- A **Preferences** window with every setting in plain words, and a **Server identities** window to confirm each server's SSH identity (no editing of `known_hosts` by hand).
- One **installer file** (`.bin`) with a wizard. It fetches what it needs from each tool's own source, never bundles third-party tools, updates an existing install in place and **keeps your servers and settings**. Removing it never deletes your data unless you tick the box.
- A **signed** release: every file has an OpenPGP signature you can check ([how](Linux-Install-and-Update#check-the-signature-before-you-run-it)).

## Get it

1. Download **[ssmconnect-linux-x86_64.bin](https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin)** (and its [signature](https://github.com/poonchezhian/ssmconnect/releases/download/linux-latest/ssmconnect-linux-x86_64.bin.asc)) from the [newest Linux release](https://github.com/poonchezhian/ssmconnect/releases/tag/linux-latest).
2. [Check the signature](Linux-Install-and-Update#check-the-signature-before-you-run-it) (recommended).
3. Run it: `sh ./ssmconnect-linux-x86_64.bin`, or make it executable and double-click it. Follow the installer window.

Details, updating and removing: [Install and Update](Linux-Install-and-Update). What changed so far: [Linux Release Notes](Linux-Release-Notes).

## What it needs

| Needed | Where it comes from |
|---|---|
| A desktop Linux with GTK 4.12+ and libadwaita 1.5+ (Ubuntu 24.04+, Fedora 40+, Debian 13+ and similar) | your distribution (the installer offers to install them) |
| OpenSSH client | your distribution |
| AWS CLI v2 and the Session Manager plugin | Amazon, straight from Amazon's servers, signature checked |
| Remmina or FreeRDP (only for Windows servers) | your distribution (optional) |

On the AWS side nothing changes: each server needs the SSM Agent and an instance role such as `AmazonSSMManagedInstanceCore`, and your role needs `ec2:DescribeInstances`, `ssm:DescribeInstanceInformation` and `ssm:StartSession`. See [For IT Teams](For-IT-Teams).

## Good to know

- Early release (0.x). Try it against a non-production server first.
- There is no system-tray icon (GNOME removed tray support). Closing the window keeps your connections open; **Quit** in the menu closes them.
- The Windows edition is unchanged and keeps its own releases (`vX.Y.Z`). Linux releases are tagged `linux-vX.Y.Z`.

[Source code](https://github.com/poonchezhian/ssmconnect/tree/linux/linux) | [Report a problem](https://github.com/poonchezhian/ssmconnect/issues)
