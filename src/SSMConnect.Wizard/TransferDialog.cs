// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Diagnostics;
using System.Text;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// Copies a file or folder from one server to another through the SSM tunnels, using the scp that ships with Windows.
/// The data flows server A -> this PC -> server B (scp -3), so the two servers never need to reach each other.
/// </summary>
public sealed class TransferDialog : Form
{
    sealed record Choice(TunnelConfig Tunnel, bool Up)
    {
        public override string ToString() => $"{Tunnel.Name}  ({Tunnel.UserName}, port {Tunnel.LocalPort}){(Up ? "" : "  - not connected")}";
    }

    readonly ComboBox _from = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly ComboBox _to = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly TextBox _fromPath = new() { Dock = DockStyle.Fill, Text = "~/" };
    readonly TextBox _toPath = new() { Dock = DockStyle.Fill, Text = "~/" };
    readonly CheckBox _recursive = new() { Text = "It is a folder (copy everything inside it)", AutoSize = true };
    readonly Button _swap = new() { Text = "Swap", AutoSize = true, MinimumSize = new Size(90, 34) };
    readonly Button _go = new() { Text = "Start copying", AutoSize = true, MinimumSize = new Size(150, 36), Padding = new Padding(8, 2, 8, 2), Tag = "primary" };
    readonly Button _close = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(110, 36), Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };

    public TransferDialog(IEnumerable<TunnelConfig> servers, Func<int, bool> isUp, TunnelConfig? preselect = null)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Copy files between servers";
        Font = AppTheme.Body();
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(640, 470);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = AppTheme.Page;

        var choices = servers.Where(s => !s.IsRdp && s.Enabled).Select(s => new Choice(s, isUp(s.LocalPort))).ToList();
        foreach (var c in choices) { _from.Items.Add(c); _to.Items.Add(c); }
        if (choices.Count > 0)
        {
            var first = preselect != null ? choices.FindIndex(c => c.Tunnel == preselect) : 0;
            _from.SelectedIndex = Math.Max(0, first);
            _to.SelectedIndex = choices.Count > 1 ? (_from.SelectedIndex == 0 ? 1 : 0) : 0;
        }

        var hdr = new HeaderBand { Dock = DockStyle.Top, Height = 84 };
        hdr.Set("Copy files between servers", "Pick a file or folder on one server and where it should land on another.");

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 12, 20, 12), BackColor = Color.White };
        footer.Controls.Add(_close);
        _go.Margin = new Padding(10, 0, 0, 0);
        footer.Controls.Add(_go);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(22, 14, 22, 4), AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 8) });
            c.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(c);
        }
        Row("Copy from server", _from);
        Row("File or folder", _fromPath);
        Row("Copy to server", _to);
        Row("Put it in folder", _toPath);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label());
        var opts = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 6) };
        opts.Controls.Add(_recursive);
        opts.Controls.Add(_swap);
        _swap.Margin = new Padding(24, 0, 0, 0);
        grid.Controls.Add(opts);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label());
        grid.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Color.FromArgb(94, 110, 114), Margin = new Padding(0, 10, 0, 0),
            Text = "Examples:  /var/www/app/backup.zip  or  ~/reports\n\n" +
                   "Both servers must show Connected on the dashboard. The files travel through this PC, so a large copy depends on your internet speed. " +
                   "A window opens to show progress and to ask for a password if the server uses one. Servers that log in with a key need the .pem key file (a .ppk key cannot be used here)."
        });

        Controls.Add(grid);
        Controls.Add(footer);
        Controls.Add(hdr);
        AppTheme.Style(this);

        _swap.Click += (_, _) =>
        {
            (_from.SelectedIndex, _to.SelectedIndex) = (_to.SelectedIndex, _from.SelectedIndex);
            (_fromPath.Text, _toPath.Text) = (_toPath.Text, _fromPath.Text);
        };
        _go.Click += (_, _) => Start();
        CancelButton = _close;
    }

    static string? FindScp()
    {
        var p = Path.Combine(Environment.SystemDirectory, "OpenSSH", "scp.exe");
        return File.Exists(p) ? p : ToolDetector.FindExe("scp.exe");
    }

    static string Q(string s) => s.Replace("%", "%%");

    void Start()
    {
        if (_from.SelectedItem is not Choice a || _to.SelectedItem is not Choice b) { MessageBox.Show(this, "Pick both servers first.", Text); return; }
        if (a.Tunnel == b.Tunnel) { MessageBox.Show(this, "Pick two different servers.", Text); return; }
        if (!a.Up || !b.Up)
        {
            MessageBox.Show(this, "Both servers must be connected first. Click \"Start connections\" on the dashboard and wait until the servers turn green.", Text);
            return;
        }
        var src = _fromPath.Text.Trim();
        var dst = _toPath.Text.Trim();
        if (src.Length == 0 || dst.Length == 0) { MessageBox.Show(this, "Enter the file or folder to copy and the folder to put it in.", Text); return; }
        var scp = FindScp();
        if (scp == null)
        {
            MessageBox.Show(this, "Windows' built-in OpenSSH Client is needed for copying. Turn it on in Settings > Apps > Optional features > Add a feature > OpenSSH Client, then try again.", Text);
            return;
        }
        foreach (var t in new[] { a.Tunnel, b.Tunnel })
        {
            if (!string.IsNullOrWhiteSpace(t.KeyFile) && t.KeyFile!.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"\"{t.Name}\" uses a .ppk key, which scp cannot read. Edit the server and choose the .pem version of the key (the one AWS gave you), then try again.", Text);
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(ConfigStore.DataDir);
            var cfg = Path.Combine(ConfigStore.DataDir, "transfer-ssh.config");
            var sb = new StringBuilder();
            void Host(string alias, TunnelConfig t)
            {
                sb.Append("Host ").AppendLine(alias);
                sb.AppendLine("  HostName 127.0.0.1");
                sb.AppendLine($"  Port {t.LocalPort}");
                sb.AppendLine($"  User {t.UserName}");
                if (!string.IsNullOrWhiteSpace(t.KeyFile))
                {
                    sb.AppendLine($"  IdentityFile \"{t.KeyFile!.Replace('\\', '/')}\"");
                    sb.AppendLine("  IdentitiesOnly yes");
                }
                sb.AppendLine("  StrictHostKeyChecking no");
                sb.AppendLine("  UserKnownHostsFile NUL");
                sb.AppendLine("  LogLevel ERROR");
                sb.AppendLine("  ServerAliveInterval 30");
                sb.AppendLine();
            }
            Host("ssc-from", a.Tunnel);
            Host("ssc-to", b.Tunnel);
            File.WriteAllText(cfg, sb.ToString(), new UTF8Encoding(false));

            var script = Path.Combine(ConfigStore.DataDir, "transfer.cmd");
            var lines = new List<string>
            {
                "@echo off",
                "title SSMConnect - copy files between servers",
                $"echo Copying from {Q(a.Tunnel.Name)} to {Q(b.Tunnel.Name)} ...",
                "echo.",
                $"\"{scp}\" -3 {(_recursive.Checked ? "-r " : "")}-F \"{cfg}\" \"ssc-from:{Q(src)}\" \"ssc-to:{Q(dst)}\"",
                "echo.",
                "if errorlevel 1 (echo FAILED - read the message above. Check the paths, the user name and that both servers are connected.) else (echo DONE - the copy finished.)",
                "echo.",
                "pause",
            };
            File.WriteAllLines(script, lines, new UTF8Encoding(false));
            AppLog.Info($"Transfer: {a.Tunnel.Name}:{src} -> {b.Tunnel.Name}:{dst}");
            Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = ConfigStore.DataDir });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not start the copy: " + ex.Message, Text);
        }
    }
}
