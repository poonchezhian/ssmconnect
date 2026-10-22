# What's New

The full list with downloads and checksums is on the [Releases page](https://github.com/poonchezhian/ssmconnect/releases).

## SSMConnect for Linux

There is now a native Linux edition with a signed installer. Read the [announcement](Linux-Edition) and the [Linux release notes](Linux-Release-Notes). The Windows versions below are unchanged.

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
