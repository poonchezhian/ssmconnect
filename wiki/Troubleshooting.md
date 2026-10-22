# Troubleshooting

| Symptom | What to do |
|---|---|
| A server shows **SSM ready: No** | The SSM Agent is not running on it, or its instance role lacks SSM permissions (for example `AmazonSSMManagedInstanceCore`). |
| PuTTY says "No supported authentication methods available" | The server did not accept the key or user. From 2.0.2 SSMConnect tests the login before opening the tool and fixes the user and key by itself. If it asks for a key file, choose the `.pem` (or `.ppk`) this server was created with. A key protected by a passphrase cannot be converted for PuTTY or WinSCP; use MobaXterm, or save it as a `.ppk` in PuTTYgen. |
| "Your AWS sign-in has expired" | Click the red **AWS sign-in** pill (or the **Sign in** button on the server card). Your browser opens; sign in and the pill turns green. **AWS > Sign in again** does the same. |
| The AWS pill is red | The sign-in of the account you are viewing has expired. Click the pill to sign in. Other accounts are checked separately when you switch to them. |
| A card says **Stopped** | The server failed 3 tries in a row, so SSMConnect stopped trying. Read the reason on the card, fix it, then click **Sign in again** (top right) to give every server 3 more tries. |
| Sign-in window does not open my browser | Pick the browser in the sign-in window, or click **Open the window again**. If the browser is not listed, SSMConnect uses the Windows default. |
| The usage bars on a card show -- | No usage reading yet. Click **Refresh usage**. If it stays empty, your AWS role is probably not allowed to run commands through SSM (`ssm:SendCommand`); hover a bar for the reason. Production servers are not read automatically (click **Refresh usage**), and the **Servers** menu can switch automatic reading off or on. Sizes also need `ec2:DescribeInstances`, `ec2:DescribeInstanceTypes` and `ec2:DescribeVolumes`. |
| A connection closed by itself | Connections stay open until you click **Stop connections** or exit, and SSMConnect reopens a session that AWS ends. After 3 failed tries a card shows **Stopped**: read its message, then click **Sign in again**. Your administrators may also set an idle limit in AWS. |
| A card is amber or red | Read the message on the card. It says what to do: sign in, install a tool, start the connections, or free a port that is in use. |
| "Port already in use" | Another program uses that local port. Close it or restart SSMConnect. SSMConnect never takes over a busy port on purpose. |
| Tunnel "stopped for safety" | A port was found reachable from the network. Restart the connections; if it repeats, check for another program using the port. |
| A new AWS account under my sign-in is not listed | Run **Servers > Scan for new or changed servers**. If the message says the sign-in expired, use **AWS > Sign in again** and scan again. |
| Servers show no PROD / UAT tag | The server has no `Environment` tag. See [Server Types and Tags](Server-Types-and-Tags). |
| Tray icon missing | Check the hidden-icons arrow in the taskbar, or open SSMConnect from the Start menu. |
| PuTTY sessions missing | Re-run the setup wizard's last step. PuTTY needs `.ppk` keys; install PuTTYgen so `.pem` files convert automatically. |
| MobaXterm sessions missing | Close MobaXterm, then apply again. If the log says MobaXterm is installed but has not been opened yet, open it once, close it, and apply again. From version 1.0.43 SSMConnect finds MobaXterm wherever it was installed, and from 1.0.44 the dashboard shows a MobaXterm button on each server card. From 1.0.45 that button saves the sessions into MobaXterm for you; if MobaXterm is already open it opens a new tab instead. |
| File transfer cannot sign in (see [Copy Files Between Servers](File-Transfer)) | Both servers must show Connected. The default user signs in with the server's saved key, which must be a `.pem` (a `.ppk` cannot be used). Switching to another user uses `sudo -u <user>` from the default user, so the default user needs sudo rights (the Ubuntu default). If sudo asks for a password, enter it in the password box. |
| File transfer lists no servers | Only servers that are enabled and showing Connected on the dashboard are listed. Sign in, wait until the cards turn green, then click Refresh. The root user is blocked in this tool on purpose. |
| File transfer lists no other servers | Only servers of the same organization (company sign-in) can be linked. Pick the organization at the top of the window. |
| File transfer says permission denied | The signed-in user cannot read the source or write to the destination folder. Switch to a user that can (for example `ubuntu`), or copy into that user's home folder. |
| WinSCP transfers fail with parallel connections | SSM port forwarding can limit simultaneous connections. Set WinSCP to a single connection. |
| Windows server will not open | The server needs the SSM Agent and RDP enabled; **Get password** needs the server's `.pem` key. |

## Still stuck?

1. **Help > Open log file** and look at the last lines.
2. Check [FAQ](FAQ).
3. Open an [issue](https://github.com/poonchezhian/ssmconnect/issues) with the log lines (remove anything private first).
