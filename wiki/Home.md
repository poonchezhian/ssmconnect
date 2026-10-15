# SSMConnect

**Connect to your AWS servers with port 22 closed. No bastion host, no VPN, no infrastructure knowledge needed.**

SSMConnect is a small Windows app. A short wizard finds your servers, opens secure tunnels through AWS Systems Manager (SSM), and saves ready-to-use sessions in **PuTTY**, **WinSCP** and **MobaXterm**. Windows servers open in **Remote Desktop**.

```
PuTTY / WinSCP / MobaXterm  ->  127.0.0.1:2201  ->  aws ssm start-session  ->  your server
```

## Start here

| I want to... | Go to |
|---|---|
| Install it and connect to my first server | [Getting Started](Getting-Started) |
| Add another AWS account, rename or remove one | [AWS Accounts and Organizations](AWS-Accounts-and-Organizations) |
| Understand the dashboard, cards and buttons | [Dashboard Guide](Dashboard-Guide) |
| Know why a server shows PROD or UAT | [Server Types and Tags](Server-Types-and-Tags) |
| Check what is protected and how | [Security](Security) |
| Fix something that is not working | [Troubleshooting](Troubleshooting) |
| Roll it out to a team | [For IT Teams](For-IT-Teams) |
| Quick answers | [FAQ](FAQ) |
| See what changed | [What's New](Whats-New) |

## In one minute

1. [Download the installer](https://github.com/poonchezhian/ssmconnect/releases/latest/download/SSMConnect-Setup-x64.exe) and run it.
2. Add your AWS account in the wizard (paste your company sign-in address, sign in once in the browser).
3. Tick your servers. Done: open PuTTY, WinSCP or Remote Desktop straight from a server card on the dashboard.

SSMConnect is free and open source (Apache 2.0). It is an independent project and is not affiliated with Amazon Web Services.

[Source code](https://github.com/poonchezhian/ssmconnect) | [Releases](https://github.com/poonchezhian/ssmconnect/releases) | [Report a problem](https://github.com/poonchezhian/ssmconnect/issues)
