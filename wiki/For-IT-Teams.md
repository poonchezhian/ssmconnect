# For IT Teams

## What each server needs

- The **SSM Agent** running.
- An **instance role** that allows SSM, for example the AWS managed policy `AmazonSSMManagedInstanceCore`.
- Network path from the instance to the SSM endpoints (internet access or VPC endpoints for `ssm`, `ssmmessages` and `ec2messages`).

## What each person needs

Permissions equivalent to:

- `ec2:DescribeInstances`
- `ssm:DescribeInstanceInformation`
- `ssm:StartSession`

Access to Windows passwords (the **Get password** button) also needs `ec2:GetPasswordData` and the server's key file.

## Pre-fill the company sign-in

On a PC that is part of your network, SSMConnect finds the IAM Identity Center address and region by itself, in this order:

1. Settings you push: registry `HKLM\SOFTWARE\Policies\SSMConnect` values `SsoStartUrl` and `SsoRegion` (Group Policy or Intune), **or** `%ProgramData%\SSMConnect\sso.json` containing `{"startUrl":"https://yourcompany.awsapps.com/start","region":"ap-south-1"}`, **or** the environment variables `SSMCONNECT_SSO_URL` and `SSMCONNECT_SSO_REGION`.
2. Existing `~/.aws/config` settings.
3. Sign-ins the AWS CLI already cached.
4. A guess from the company domain, always shown for confirmation.

Without one of the first three, nothing on the network announces the address, so a one-time policy or file is the dependable way.

## Tagging

Ask teams to tag instances with `Environment` (Production, UAT, Development, ...). See [Server Types and Tags](Server-Types-and-Tags).

## Unattended install and uninstall

- The setup file runs with `/quiet`.
- An uninstall run with `/quiet` keeps user settings unless you add `RemoveSettings=1`.
- `SSMConnect.Wizard.exe --cleanup` removes only the saved sessions and settings of the current user.

## Verifying downloads

Every release lists the SHA-256 of each file in `SHA256SUMS.txt`. Compare with `Get-FileHash .\SSMConnect-Setup-x64.exe`.

## Supported Windows

64-bit Windows 10 (1607+), Windows 11, Windows Server 2016, 2019, 2022 and later with the desktop experience. Server Core has no screen for the setup window.
