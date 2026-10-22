# Security

SSMConnect is built so that connecting is easy and nothing extra is exposed.

## What it does for you

- **No open ports on your servers.** Everything goes through AWS Systems Manager; port 22 can stay closed.
- **Local only.** Tunnels listen on `127.0.0.1`. If a tunnel port is ever found open to the network, SSMConnect closes that session and reports it.
- **No impostors.** A tunnel never starts on a port another program already uses, so nothing else can answer in place of your server and the dashboard cannot show a false green.
- **Validated input.** Every value read from the configuration (instance ID, account, region, ports, login) is checked before it reaches the AWS CLI, which is always started with an argument list, never a command-line string.
- **Tied to the app.** Sessions are closed when SSMConnect exits.
- **No secrets kept.** SSMConnect stores no password and no key of its own. A copied Windows password leaves the clipboard after 60 seconds. Access keys you type go only to your own `~/.aws/credentials`, like the AWS CLI does.
- **Safe edits.** Before changing your AWS files or your MobaXterm settings, a `.ssmconnect.bak` copy is kept beside them. Your own sessions in PuTTY and WinSCP are never modified.
- **Runs as you.** Nothing runs as administrator or SYSTEM. Each Windows user has their own settings, tunnels and `~/.aws` sign-in.
- **File transfer stays on your PC.** Files pass through your PC's memory in small blocks and are not saved to disk. The file window never works as root, and a sudo password you type is handed to sudo through a short-lived private helper file on the server that is removed when the window closes.
- **Converted keys are locked down.** The `.ppk` copies SSMConnect makes for PuTTY are readable only by your Windows user.
- **Only AWS sign-in pages open.** The sign-in window opens links on AWS's own domains only.
- **Checked downloads.** Updates are verified against the SHA-256 checksum published with the release. The AWS installers are checked against Amazon's digital signature and pinned by hash.

## What to know

- Another user logged in to the same PC at the same time can reach the local tunnel ports (`127.0.0.1:2201` and up). They still need valid SSH credentials for the server, so the SSH login is what protects it.
- The installer and executables are not code-signed yet, so Windows SmartScreen may warn on first run. Compare the file's SHA-256 with `SHA256SUMS.txt` on the release page if you want to verify it.
- MobaXterm configuration is best effort.

## Reporting a security issue

Please report security problems privately, not as a public issue. Use **Report a vulnerability** on the repository's Security tab, or follow the instructions in the [security policy](https://github.com/poonchezhian/ssmconnect/security/policy). Do not include secrets, access keys or company names in the report.
