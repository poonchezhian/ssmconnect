# AWS Accounts and Organizations

## Two words to know

- **Sign-in (primary account):** one company sign-in (AWS IAM Identity Center) or one set of access keys.
- **Organization account:** one AWS account inside a company sign-in. A company sign-in usually has several. SSMConnect finds all of them when you sign in, so you never add them one by one.

## The account switcher

The pill in the top right of the dashboard shows the sign-in you are looking at. Click it to switch, add an account, or manage accounts.

- It lists **sign-ins only**, never the organizations under them.
- The window shows **one sign-in at a time**. Under it, servers are grouped by organization account.
- Switching is just a change of view. Connections of the other sign-ins keep running, and the summary and tray icon still cover everything.
- A row of chips under the status pills lists each organization account with its server count. Accounts with no servers yet are greyed.

## Add an AWS account

**AWS > Add AWS account** (or **Add AWS account...** in the switcher) opens the setup wizard on one page:

1. Paste your company sign-in address, for example `https://yourcompany.awsapps.com/start`. It is usually filled in for you.
2. Click **Sign in with AWS**, then sign in and approve in the browser (up to 3 minutes).
3. SSMConnect finds your accounts and roles and carries on to region and servers.

Prefer access keys? Use the small **Use access keys instead** link. The keys are written only to your own `~/.aws/credentials`, the same way the AWS CLI does it.

## Manage accounts: rename or remove

**AWS > Manage accounts...** shows one collapsed line per sign-in. Click the arrow to see the organization accounts under it.

- **Rename** gives a sign-in or an organization a friendly name. It appears in the switcher, the headings and the wizard. The AWS profile name itself does not change. Leave the box empty to go back to the original name.
- **Remove** deletes the account's saved connections from SSMConnect and its login from this PC. Your PuTTY and WinSCP sessions and the servers in AWS are not touched. You can add the account again at any time.

## New organization accounts show up on rescan

If your company adds an AWS account under your sign-in later, **Servers > Scan for new or changed servers** (and the daily background scan) finds it, saves a profile for it and scans it. This only works while that sign-in is still active. If it has expired, SSMConnect tells you; use **AWS > Sign in again** and scan once more.

## Sign in again

When a sign-in expires, the AWS pill turns red and names the account. **AWS > Sign in again** opens the browser for it. Access-key accounts do not expire this way.

## Good to know

- One account saved twice under two profile names (or two nicknames for the same sign-in address) is shown once.
- Only organization accounts are listed. The organizational-unit folders above them in AWS are not shown.
- One region is searched per scan.
