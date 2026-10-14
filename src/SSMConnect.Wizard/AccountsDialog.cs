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


using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>Lists the AWS accounts SSMConnect knows, lets the person give them friendly names and remove the ones they no longer use.</summary>
public sealed class AccountsDialog : Form
{
    readonly TreeView _tree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowLines = false, ShowRootLines = false, FullRowSelect = true, HideSelection = false, ItemHeight = 34, BackColor = Color.White, Font = AppTheme.Body(10f) };
    readonly Button _rename = new() { Text = "Rename...", AutoSize = true, MinimumSize = new Size(120, 36), Padding = new Padding(8, 2, 8, 2) };
    readonly Button _remove = new() { Text = "Remove...", AutoSize = true, MinimumSize = new Size(120, 36), Padding = new Padding(8, 2, 8, 2) };
    readonly Button _close = new() { Text = "Done", AutoSize = true, MinimumSize = new Size(110, 36), Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.OK, Tag = "primary" };
    readonly Label _hint = new() { Dock = DockStyle.Bottom, Height = 46, ForeColor = Color.FromArgb(94, 110, 114), Padding = new Padding(22, 6, 22, 0) };
    AppConfig _cfg;
    List<ProfileInfo> _profiles = new();

    /// <summary>True when something was renamed or removed, so the caller refreshes.</summary>
    public bool Changed { get; private set; }

    sealed record Node(bool IsPrimary, string Key, List<ProfileInfo> Members);

    public AccountsDialog()
    {
        _cfg = ConfigStore.Load() ?? new AppConfig();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Your AWS accounts";
        Font = AppTheme.Body();
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = AppTheme.Page;

        var hdr = new HeaderBand { Dock = DockStyle.Top, Height = 84 };
        hdr.Set("Your AWS accounts", "Give accounts a friendly name, or remove the ones you no longer use.");
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 12, 20, 12), BackColor = Color.White };
        footer.Controls.Add(_close);
        _remove.Margin = new Padding(0, 0, 10, 0); _rename.Margin = new Padding(10, 0, 0, 0);
        footer.Controls.Add(_remove);
        footer.Controls.Add(_rename);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 4), BackColor = AppTheme.Page };
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(6) };
        frame.Paint += (_, e) => { using var p = new Pen(AppTheme.Line); e.Graphics.DrawRectangle(p, 0, 0, frame.Width - 1, frame.Height - 1); };
        frame.Controls.Add(_tree);
        body.Controls.Add(frame);
        Controls.Add(body);
        Controls.Add(_hint);
        Controls.Add(footer);
        Controls.Add(hdr);
        body.BringToFront();
        AcceptButton = _close;

        _hint.Text = "Removing an account deletes its saved connections from SSMConnect and its login from this PC. Your PuTTY / WinSCP sessions and the servers in AWS are not touched.";
        _tree.AfterSelect += (_, _) => UpdateButtons();
        _tree.NodeMouseDoubleClick += (_, _) => RenameSelected();
        _rename.Click += (_, _) => RenameSelected();
        _remove.Click += (_, _) => RemoveSelected();
        AppTheme.Style(this);
        Shown += (_, _) => Reload(null);
    }

    string PrimaryLabel(string key, ProfileInfo rep) => _cfg.AliasOr(key, AwsProfiles.PrimaryName(rep));

    void Reload(string? select)
    {
        _cfg = ConfigStore.Load() ?? new AppConfig();
        _profiles = AwsProfiles.List();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (var g in _profiles.GroupBy(AwsProfiles.PrimaryKey).OrderBy(g => PrimaryLabel(g.Key, g.First()), StringComparer.OrdinalIgnoreCase))
        {
            var members = g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var n = new TreeNode($"{PrimaryLabel(g.Key, members[0])}    ({members.Count} account{(members.Count == 1 ? "" : "s")})") { Tag = new Node(true, g.Key, members), NodeFont = AppTheme.Semi(10f) };
            foreach (var m in members)
            {
                var servers = _cfg.Tunnels.Count(t => t.Profile == m.Name);
                var shown = _cfg.AliasOr(m.Name, m.Name);
                var c = new TreeNode($"{shown}{(m.AccountId != null ? "    " + m.AccountId : "")}{(servers > 0 ? $"    {servers} server{(servers == 1 ? "" : "s")}" : "")}") { Tag = new Node(false, m.Name, new List<ProfileInfo> { m }) };
                n.Nodes.Add(c);
            }
            _tree.Nodes.Add(n);
            n.Expand();
        }
        _tree.EndUpdate();
        if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
        UpdateButtons();
    }

    Node? Current => _tree.SelectedNode?.Tag as Node;

    void UpdateButtons() { _rename.Enabled = _remove.Enabled = Current != null; }

    void RenameSelected()
    {
        if (Current is not { } n) return;
        var fallback = n.IsPrimary ? AwsProfiles.PrimaryName(n.Members[0]) : n.Key;
        var now = _cfg.AliasOr(n.Key, fallback);
        var typed = Ask(this, "Rename account", n.IsPrimary ? "Friendly name for this AWS account (shown in the account menu):" : "Friendly name for this organization account:", now);
        if (typed == null) return;
        typed = typed.Trim();
        if (typed.Length > 60) typed = typed.Substring(0, 60);
        if (typed.Length == 0 || typed == fallback) _cfg.Aliases.Remove(n.Key); else _cfg.Aliases[n.Key] = typed;
        ConfigStore.Save(_cfg);
        Changed = true;
        Reload(n.Key);
    }

    void RemoveSelected()
    {
        if (Current is not { } n) return;
        var names = n.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var servers = _cfg.Tunnels.Count(t => names.Contains(t.Profile));
        var what = n.IsPrimary ? $"the AWS account \"{PrimaryLabel(n.Key, n.Members[0])}\" and its {n.Members.Count} organization account(s)" : $"the account \"{_cfg.AliasOr(n.Key, n.Key)}\"";
        var msg = $"Remove {what}?\n\n" +
                  (servers > 0 ? $"{servers} saved server connection(s) will be removed from SSMConnect.\n" : "") +
                  "The login is deleted from this PC. You can add it again at any time.\nYour PuTTY / WinSCP sessions and the servers in AWS are not changed.";
        if (MessageBox.Show(this, msg, "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        _cfg.Tunnels.RemoveAll(t => names.Contains(t.Profile));
        foreach (var m in n.Members) { _cfg.Aliases.Remove(m.Name); DiscoveryCache.Forget(m.Name); }
        if (n.IsPrimary) _cfg.Aliases.Remove(n.Key);
        if (names.Contains(_cfg.Profile)) _cfg.Profile = _cfg.Tunnels.FirstOrDefault()?.Profile ?? "";
        ConfigStore.Save(_cfg);

        var failed = new List<string>();
        foreach (var m in n.Members)
        {
            try { AwsProfiles.RemoveProfile(m.Name); } catch (Exception ex) { failed.Add(m.Name + ": " + ex.Message); }
        }
        // the sign-in itself goes when no remaining profile uses it
        var sessions = n.Members.Select(m => m.SsoSession).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var left = AwsProfiles.List();
        foreach (var s in sessions)
            if (!left.Any(p => p.SsoSession == s)) { try { AwsProfiles.RemoveSsoSession(s!); } catch { } }
        if (failed.Count > 0) MessageBox.Show(this, "Some settings could not be removed from your AWS files:\n\n" + string.Join("\n", failed), "Remove account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Changed = true;
        Reload(null);
    }

    /// <summary>A small themed text prompt. Returns null when cancelled.</summary>
    public static string? Ask(IWin32Window owner, string title, string label, string initial)
    {
        using var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(480, 170), Font = AppTheme.Body(), BackColor = AppTheme.Page,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
        };
        var l = new Label { Text = label, AutoSize = false, Left = 22, Top = 18, Width = 436, Height = 40 };
        var t = new TextBox { Left = 22, Top = 62, Width = 436, Text = initial, MaxLength = 60 };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 258, Top = 112, Width = 96, Height = 36, Tag = "primary" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 362, Top = 112, Width = 96, Height = 36 };
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        AppTheme.Style(f);
        f.Shown += (_, _) => { t.Focus(); t.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
    }
}
