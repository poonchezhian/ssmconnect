# SSMConnect Project Knowledge

## 1. Purpose and users
- Goal: a very simple, friendly tool that lets a developer with little infrastructure knowledge discover, configure and connect to servers over AWS Systems Manager (SSM) Session Manager.
- Foundational constraint: the target environment has port 22 closed. Everything is designed around SSM tunnels. SCP, rsync-over-SSH and similar tools do not work directly.
- Design stance: think like a developer who knows little about infrastructure. Plain-language messages, one obvious next step, no AWS jargon where avoidable.
- History: a first version was a cross-platform Python terminal app (`aws_connect.py`) plus a Word reference guide. Both are superseded by SSMConnect.

## 2. What SSMConnect is
- A Windows-only desktop app (WinForms, .NET 8) plus an installer (WiX Toolset v3.14 Burn bundle with a managed bootstrapper UI on net462).
- It opens SSH and RDP tunnels over `aws ssm start-session` and configures PuTTY, WinSCP and MobaXterm sessions (named `SSC - <server name>`). Windows servers open in Remote Desktop.
- Tunnel model: tools connect to `127.0.0.1:<port>` (ports from 2201 up), and the app carries the traffic through SSM to the instance.
- Runs per user. There is no Windows Service (dropped on purpose to avoid SYSTEM privilege escalation). A tray icon starts with Windows and keeps tunnels in-process.
- Single instance (mutex `Local\SSMConnect.Dashboard`).
- Single redistributable: one setup `.exe` that bundles AWS CLI v2 and the Session Manager plugin.

## 3. Solution layout
- `src/SSMConnect.Core`: logic with no UI. Key files: `AwsProfiles.cs` (profile model, primary keys, aliases), `AwsSso.cs` (SSO sign-in and silent organization sync), `DiscoveryCache.cs` (discovery.json), tunnel manager, server type detection.
- `src/SSMConnect.Wizard`: the WinForms app. `DashboardForm.cs`, `WizardForm.cs`, `AccountsDialog.cs`, `ServerCard.cs`, `AccountChip.cs`, `AppTheme.cs`, `ProfileDialog.cs` (stub), `Program.cs`.
- `installer/`: WiX bundle, managed bootstrapper app (`MainForm.cs`), `build-installer.ps1`, `signing.ps1`.
- Root scripts: `build.ps1`, `Build-SSMConnect.cmd`, `install-build-tools.ps1`, `VERSION`, `Directory.Build.props`.
- Local data per user: `%LOCALAPPDATA%\SSMConnect` with `config.json` (connections, friendly names), `status.json` (live tunnel status), `discovery.json` (servers from the last scans), `keys\` (converted `.ppk` files) and a log file.

## 4. Setup wizard flow (five steps)
1. Requirements: check AWS CLI and Session Manager plugin; one click installs what is missing (AWS installers verified by Amazon signature and pinned hash).
2. AWS account: paste the company sign-in address (IAM Identity Center start URL), sign in once in the browser, choose a region. All organization accounts come along. Access keys are a small secondary link.
3. Servers: list running instances, mark which are SSM-reachable, tick the ones to connect.
4. SSH keys: match each Linux server to a key file (find on this PC, search a folder, browse).
5. Save: write sessions into PuTTY, WinSCP and MobaXterm, start tunnels.
- Sign-in address pre-fill order: pushed policy (registry `HKLM\SOFTWARE\Policies\SSMConnect` values `SsoStartUrl` and `SsoRegion`, or `%ProgramData%\SSMConnect\sso.json`, or env vars `SSMCONNECT_SSO_URL` and `SSMCONNECT_SSO_REGION`), then existing `~/.aws/config`, then cached CLI sign-ins, then a domain-based guess shown for confirmation.

## 5. Dashboard behaviour
- Header: account switcher pill, three status pills (Connections, AWS sign-in, AWS tools), one summary line, organization chips with server counts (greyed when empty).
- Buttons: Start (open all connections), Stop (close all), Restart (close and reopen), Add / change servers (opens the wizard). Buttons are painted by a custom `ButtonLook` (rounded, gradient, hover and pressed states, icons). Unavailable buttons are dimmed through `ActionButton.Available` instead of `Enabled=false`, so hover notes still show (disabled WinForms controls receive no mouse messages).
- Server cards: name, type tag (red PROD, grey UAT or others), OS, local port, status (green Connected only when the local port is truly open; amber working; red problem with a plain-language reason; grey off). Actions: PuTTY and WinSCP for Linux; Remote Desktop and Get password for Windows; Copy session name when no SSH tool is installed.
- Menus: File (create desktop shortcut, hide to tray, exit), AWS (add account, manage accounts, sign in again), Servers (scan for new or changed servers, what changed), Help (about, check for updates, open log file, uninstall).
- Closing the window hides to tray; File > Exit stops every connection.
- Daily background scan on first run each day (retried if the sign-in has expired), reported through a tray notification and Servers > What changed.

## 6. Accounts model (important)
- Primary account = one sign-in. `AwsProfiles.PrimaryKey` is `"sso:" + lowercase host of the start URL` (fallback lowercased session name) or `"key:" + name` for access keys. Organization (member) accounts are one saved AWS profile per account.
- `OneEach` removes profiles pointing at the same AccountId, preferring the one that has saved tunnels.
- Friendly names: `AppConfig.Aliases`, resolved by `AwsProfiles.PrimaryLabel` (checks the new key, then legacy keys: sso-session name, full start URL, profile name).
- The dashboard shows one sign-in at a time; other sign-ins keep running. Summary and tray cover everything.
- Accounts dialog: one collapsed line per sign-in, expandable to organization accounts; Rename and Remove. Remove deletes SSMConnect's saved connections and the local login, never the user's own PuTTY/WinSCP sessions or anything in AWS.
- Rescan finds new organization accounts: `AwsSso.SyncMembersAsync` uses the cached SSO token only (never opens a browser) and reports expired sign-ins in `NeedSignIn`. `DiscoveryMonitor.Accounts` includes all sibling organization profiles.

## 7. Server types from AWS tags
- Read tags in order: `Environment`, `Env`, `Stage`, `Tier` (any case, also keys ending `:environment` or `-env`). Normalise values: prod/prd/production/live to PROD; pre-prod; uat; stage/staging; test/qa; dev/development; dr; sandbox; internal.
- Only when no tag exists, fall back to guessing from the server name word by word. `non-prod` and `pre-prod` are never treated as production.
- The type is stored when saved and refreshed from scans.

## 8. Security design decisions
- Tunnels listen on `127.0.0.1` only; if a port is ever found reachable from the network the session is closed and reported.
- A tunnel never starts on a port another program already uses (prevents an impostor answering and a false green).
- All values reaching the AWS CLI are validated, and the CLI is started with an argument list, never a command string.
- Sessions end when the app exits. No password or key is stored by the app; a copied Windows password leaves the clipboard after 60 seconds. Access keys go only to the user's own `~/.aws/credentials`.
- Backups (`.ssmconnect.bak`) before editing AWS files or MobaXterm settings. User's own PuTTY and WinSCP sessions are never modified.
- Runs as the user, never admin or SYSTEM. Known limit: another user on the same PC can reach local tunnel ports but still needs SSH credentials. MobaXterm configuration is best effort: since 1.0.43 it is found through the Windows installed-programs list as well as the usual folders, and if it has never been opened the log asks the user to open it once and run Apply again.
- Updates: checked a few seconds after start and daily; download verified against a published SHA-256 before an in-place upgrade; settings preserved.

## 9. Installer and build
- Build entry: double-click `Build-SSMConnect.cmd` (self-elevates, runs `build.ps1`, logs to `build\build.log` which is UTF-16, ends with `RESULT: SUCCESS` or failure). No key press is awaited.
- `build.ps1` auto-increments the patch in `VERSION` on every build (a failed build also consumes a number).
- Build must finish with `RESULT: SUCCESS` and zero warnings. Standing rule: build after every code change.
- Uninstall: Help > Uninstall SSMConnect, Start-menu shortcut or Settings > Apps. A "Keep my settings" box is ticked by default. Quiet uninstall keeps settings unless `RemoveSettings=1`. `SSMConnect.Wizard.exe --cleanup` removes only the current user's sessions and settings.
- Supported: 64-bit Windows 10 (1607+), Windows 11, Windows Server 2016 and later with a desktop.
- Code signing is optional and driven by environment variables: `SSMC_SIGN_THUMBPRINT` or `SSMC_SIGN_PFX` plus `SSMC_SIGN_PFX_PASSWORD`, and `SSMC_SIGN_TIMESTAMP`. It uses `Set-AuthenticodeSignature`; the bundle is signed with WiX `insignia -ib/-ab` around signing the engine. No certificate exists yet, so builds are unsigned and Windows SmartScreen may warn. Azure Artifact Signing is not available in India; an open-source signing service is the intended route.

## 10. IT rollout facts
- Each server needs the SSM Agent running, an instance role such as `AmazonSSMManagedInstanceCore`, and a network path to the `ssm`, `ssmmessages` and `ec2messages` endpoints.
- Each user needs roughly `ec2:DescribeInstances`, `ssm:DescribeInstanceInformation`, `ssm:StartSession`; Get password also needs `ec2:GetPasswordData` and the server's key.
- Tag instances with `Environment` for exact PROD marking.
- Known limit: parallel WinSCP connections over SSM port forwarding can fail; use a single connection.

## 11. Working lessons and environment quirks
- Structured requirements gathering before code prevented rework on the earlier Python tool.
- Documentation rules: for Word documents use Calibri or Aptos, a separate cover page and a separate index page, and Courier New for commands, variables, paths and terminal output. Do not create documentation unless asked.
- The Linux sandbox has no `dotnet` or PowerShell, so builds run on the Windows machine.
- The file-transfer bridge sometimes writes a stale earlier version of a file: always compare sizes after writing and re-write if they differ.
- Computer-use access is granted per app; File Explorer and the command prompt are click-only. Click positions in Explorer vary with file ordering, so take a screenshot first. A `wait` is capped at about 50 seconds per call.
- To live-test a new build, close the installed app first (single instance). That drops live tunnels for about a minute.
- Never read private key file contents; only list file names. Never type credentials or passwords on the user's behalf.

## 12. Known gaps and backlog
- Not yet runtime-tested after the latest UI work: dashboard organization strip, painted buttons, accounts tree, wizard hint cards, silent organization sync (they compiled cleanly only). The installer screens were not changed.
- Observed once: the dashboard briefly showed "17 of 22 connection(s)" before recovering.
- Obtain a code-signing certificate and add signing to the build.
- README statement "File > Uninstall" is wrong; Uninstall is under Help.
- Optional: take real screenshots with a demo profile for documentation.

## 13. Earlier deliverables (reference only)
- Word reference guide (generated with the Node.js `docx` library): SSM Session Manager setup, three connection methods, port forwarding, private Git access (deploy keys, CodeCommit with IAM, PATs in Parameter Store), file transfer (S3 intermediary as default, rsync daemon over SSM port forward on port 873, SSM Run Command), cheat sheet, troubleshooting table, glossary.
- Python terminal tool `aws_connect.py`: parallel multi-region EC2 discovery, key and SSO profiles, favourites in `~/.aws_connect/favourites.json`, profile wizard, production confirmation, VS Code and terminal launch, dependency checker, session logging, launchers `aws_connect_windows.bat` and `aws_connect_linux.sh`.
- Key learning: in an SSM-only environment S3 is the default for file transfer; rsync over an SSM port forward is the advanced option.

## 14. Reference documents that belonged to the project (re-upload to the new project)
- `session-manager-working-with-sessions-start.md`
- `connect-with-systems-manager-session-manager.md`
- `session-manager.md`
- `systems-manager-tools.md`
These are copies of the AWS Systems Manager Session Manager documentation pages.

## 15. Suggested project instructions for the new account
Think from the point of view of a developer with little knowledge of infrastructure. The goal is to make working with AWS SSM very simple. Improve the output so connecting through SSM is easy. Always assume port 22 is closed. Push back on weak ideas before agreeing. Build after every change and confirm success with zero warnings. Use plain, positive, human language.
