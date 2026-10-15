# Security Policy

SSMConnect opens secure tunnels to servers and handles AWS sign-ins on your PC, so security reports are taken seriously and handled privately.

## Supported versions

Only the latest release receives security fixes. Please update first ("Help > Check for updates"), then check whether the problem still occurs.

| Version | Supported |
| ------- | --------- |
| Latest release | Yes |
| Older releases | No |

## Reporting a vulnerability

**Please do not open a public issue, discussion or pull request for a security problem.**

Report it privately in one of these ways:

1. **GitHub private report (preferred):** open the repository's **Security** tab, choose **Report a vulnerability** and fill in the form.
2. **Email:** poonchezhian@gmail.com with the subject line `SSMConnect security report`.

Please include:

- what you found and why it matters (what an attacker could do);
- the SSMConnect version (**Help > About**) and your Windows version;
- clear steps to reproduce, or a proof of concept;
- any logs that help, **with secrets, access keys, tokens and company names removed**.

## What to expect

| Step | Target |
| ---- | ------ |
| Acknowledgement of your report | within 3 working days |
| First assessment (accepted, need more detail, or not a vulnerability) | within 10 working days |
| Fix or mitigation for confirmed high-impact issues | as soon as practical, usually within 30 days |
| Public disclosure | after a fix is released, coordinated with you |

These are good-faith targets for a project maintained by one person, not guarantees. You will be kept informed of progress, and credited in the release notes if you wish.

## Scope

In scope:

- the SSMConnect desktop app, setup program and updater;
- how SSMConnect starts and supervises tunnels (local listeners, ports, process arguments);
- handling of AWS sign-ins, profile files, key files and the clipboard;
- changes SSMConnect makes to PuTTY, WinSCP and MobaXterm settings;
- update download and checksum verification.

Out of scope:

- weaknesses in AWS services, the AWS CLI, the Session Manager plugin, PuTTY, WinSCP or MobaXterm themselves (please report those to their owners);
- findings that need an attacker who already controls the user's Windows account or has administrator rights;
- missing or weak configuration on the servers you connect to (for example SSH settings or instance roles);
- reports produced only by automated scanners with no demonstrated impact;
- social engineering, physical attacks and denial of service against third parties.

Known and documented limits (see the wiki Security page), such as other users on the same PC being able to reach local tunnel ports and the installer not yet being code-signed, are not treated as new vulnerabilities, although suggestions to improve them are welcome.

## Safe harbor

If you act in good faith, test only against your own PC and your own AWS accounts, avoid privacy violations and data loss, and give a reasonable time to fix the issue before disclosing it, I will not pursue legal action against you for your research.

## Security design in brief

- Tunnels listen on `127.0.0.1` only and never start on a port that is already in use.
- Every value passed to the AWS CLI is validated, and the CLI is started with an argument list, never a command string.
- No password or key is stored by SSMConnect; copied Windows passwords leave the clipboard after 60 seconds.
- Nothing runs as administrator or SYSTEM.
- Updates are verified against a published SHA-256 checksum before they are applied.

More detail is on the [Security page of the wiki](https://github.com/poonchezhian/ssmconnect/wiki/Security).
