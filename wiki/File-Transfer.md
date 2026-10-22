# Copy Files Between Servers

**Move files from one AWS server to another with two panes and a Copy button, while port 22 stays closed on both.** No S3 bucket, no `scp` commands, no bastion host, and the two servers never have to reach each other.

Open it from **Servers > File transfer: browse and copy files...** in the dashboard (it also opens from a server card's tools when you want to move files). The window is titled **Copy files between servers**.

![Two panes, one server on each side, with Copy buttons in the middle (illustration with sample servers)](https://poonchezhian.github.io/assets/file-transfer-between-servers.png)

## Copy a file in four steps

1. **Pick the organization** at the top (your company sign-in). Both sides list only its servers, because only servers of one sign-in can be linked.
2. **Pick a server on the left and one on the right.** Each pane starts with *Select the server to connect* and connects to nothing until you choose. SSMConnect then signs in as the server's default user with its saved key. There is nothing to type.
3. **Browse and select.** Open folders on either side, then select one or more files or folders. Folders are copied with everything inside them.
4. **Press Copy.** Use **Copy ▶** to send the selection from left to right, or **◀ Copy** for the other direction. The bar shows progress and **Stop** cancels.

## How the data travels

```
Server A  ->  SSM tunnel  ->  your PC  ->  SSM tunnel  ->  Server B
```

- Each server is reached through an AWS Systems Manager tunnel that SSMConnect already keeps open on your PC, so **no inbound port is opened** on either server.
- Files are read from one server and written to the other in small blocks as they pass through your PC. Nothing is saved on your disk.
- The servers do not need a network path to each other. They can be in different VPCs, subnets or AWS accounts, as long as both belong to the same sign-in organization.
- Everything is carried over SSH (SFTP) inside the SSM session.

## Switch to another user (sudo)

Some files belong to another account, for example a web server's user. In either pane, pick or type the user and click **Switch user**. SSMConnect stays signed in as the server's default user and does the file work through `sudo -u <user>`, the way Ubuntu lets its default user become someone else.

- If sudo asks for a password, type the default user's sudo password in the box. Leave it blank otherwise. The password is handed to sudo through a short-lived private helper file that is removed when you close the window.
- **root is not allowed.** The window refuses `root` and any account with root rights (uid 0).

## What it protects you from

- **Nothing connects until you pick a server.** A pane that shows *Select the server to connect* has no open login.
- **No surprise overwrites.** If a name already exists in the destination, you see the list and must confirm before anything is replaced.
- **A clear result.** When a copy finishes you see how many files were copied. Items that failed are listed with the reason, and the full detail goes to the log (**Help > Open log file**).
- **Stop at any time.** Files already copied stay where they are. A partly copied file can be copied again.
- **The login is checked first.** Before a pane signs in, SSMConnect finds the key that belongs to the server, tests which login user it accepts, saves what works, and asks once for the key file if none fits.

## Good to know

- **Linux servers (SSH) only.** Windows servers use Remote Desktop and are not listed.
- **Both servers must show Connected** on the dashboard. Only enabled, connected servers are listed. If a server connected later, click **Refresh**.
- **The server's key must be a `.pem`** for the file window. A key protected by a passphrase cannot be used here.
- **Speed depends on your PC's connection to AWS**, because the data passes through it. For very large or repeating transfers (hundreds of gigabytes, nightly jobs) an S3-based or automated copy is the better tool.
- **There is no resume.** A file that was interrupted is copied again from the start.
- **The same server can be on both sides** when you need to copy between two folders or between two users. Open a different folder on one side.

## If something does not work

See [Troubleshooting](Troubleshooting) (the *File transfer* rows) or the [FAQ](FAQ). The most common cause is a server that is not green yet, or a login user without permission on the folder.

## Related

- [Dashboard Guide](Dashboard-Guide): the cards, buttons and status colours
- [Security](Security): what is protected and how
- [Getting Started](Getting-Started): install and first run
