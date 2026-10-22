# Troubleshooting

| Symptom | What to do |
|---|---|
| A server shows **SSM ready: No** | The SSM Agent is not running on it, or its instance role lacks SSM permissions (for example `AmazonSSMManagedInstanceCore`). |
| "Your AWS sign-in has expired" | Click the red **AWS sign-in** pill (or the **Sign in** button on the server card). Your browser opens; sign in and the pill turns green. **AWS > Sign in again** does the same. |
| The AWS pill is red | The sign-in of the account you are viewing has expired. Click the pill to sign in. Other accounts are checked separately when you switch to them. |
| A card is amber or red | Read the message on the card. It says what to do: sign in, install a tool, start the connections, or free a port that is in use. |
| "Port already in use" | Another program uses that local port. Close it or restart SSMConnect. SSMConnect never takes over a busy port on purpose. |
| Tunnel "stopped for safety" | A port was found reachable from the network. Restart the connections; if it repeats, check for another program using the port. |
| A new AWS account under my sign-in is not listed | Run **Servers > Scan for new or changed servers**. If the message says the sign-in expired, use **AWS > Sign in again** and scan again. |
| Servers show no PROD / UAT tag | The server has no `Environment` tag. See [Server Types and Tags](Server-Types-and-Tags). |
| Tray icon missing | Check the hidden-icons arrow in the taskbar, or open SSMConnect from the Start menu. |
| PuTTY sessions missing | Re-run the setup wizard's last step. PuTTY needs `.ppk` keys; install PuTTYgen so `.pem` files convert automatically. |
| MobaXterm sessions missing | Close MobaXterm, then apply again. If the log says MobaXterm is installed but has not been opened yet, open it once, close it, and apply again. From version 1.0.43 SSMConnect finds MobaXterm wherever it was installed, and from 1.0.44 the dashboard shows a MobaXterm button on each server card. From 1.0.45 that button saves the sessions into MobaXterm for you; if MobaXterm is already open it opens a new tab instead. |
| File transfer cannot sign in | Both servers must show Connected. The default user signs in with the server's saved key, which must be a `.pem` (a `.ppk` cannot be used). Switching to another user uses `sudo -u <user>` from the default user, so the default user needs sudo rights (the Ubuntu default). If sudo asks for a password, enter it in the password box. |
| File transfer lists no other servers | Only servers of the same organization (company sign-in) can be linked. Pick the organization at the top of the window. |
| File transfer says permission denied | The signed-in user cannot read the source or write to the destination folder. Switch to a user that can (for example `ubuntu`), or copy into that user's home folder. |
| WinSCP transfers fail with parallel connections | SSM port forwarding can limit simultaneous connections. Set WinSCP to a single connection. |
| Windows server will not open | The server needs the SSM Agent and RDP enabled; **Get password** needs the server's `.pem` key. |

## Still stuck?

1. **Help > Open log file** and look at the last lines.
2. Check [FAQ](FAQ).
3. Open an [issue](https://github.com/poonchezhian/ssmconnect/issues) with the log lines (remove anything private first).
