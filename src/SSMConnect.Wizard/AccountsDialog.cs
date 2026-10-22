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

using System.Runtime.InteropServices;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// Lists the AWS accounts SSMConnect knows, one line per sign-in (collapsed). Expanding a line shows the organization accounts under it.
/// Accounts can be given a friendly name or removed.
/// </summary>
public sealed class AccountsDialog : Form
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowLines = false, ShowRootLines = true, ShowPlusMinus = true,
        FullRowSelect = true, HideSelection = false, ItemHeight = 38, Indent = 26, BackColor = Color.White, Font = AppTheme.Body(10f),
    };
    readonly Button _rename = new() { Text = "Rename...", AutoSize = false, Size = new Size(120, 38) };
    readonly Button _remove = new() { Text = "Remove...", AutoSize = false, Size = new Size(120, 38) };
    readonly Button _close = new() { Text = "Done", AutoSize = false, Size = new Size(110, 38), DialogResult = DialogResult.OK, Tag = "primary" };
    readonly Label _hint = new() { Dock = DockStyle.Bottom, Height = 38, ForeColor = Color.FromArgb(94, 110, 114), Padding = new Padding(24, 4, 24, 0), Text = "Click the arrow to see the organization accounts under a sign-in." };
    AppConfig _cfg;
    List<ProfileInfo> _profiles = new();
    bool _sized;

    /// <summary>True when something was renamed or removed, so the caller refreshes.</summary>
    public bool Changed { get; private set; }

    /// <summary>Key: where the friendly name is stored. All: every saved profile behind the line (used when removing). Shown: what the person sees under a sign-in.</summary>
    sealed record Node(bool IsPrimary, string Key, List<ProfileInfo> All);

    public AccountsDialog()
    {
        _cfg = ConfigStore.Load() ?? new AppConfig();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Your AWS accounts";
        Font = AppTheme.Body();
        ClientSize = new Size(600, 400);
        MinimumSize = new Size(500, 340);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = AppTheme.Page;

        var hdr = new HeaderBand { Dock = DockStyle.Top, Height = 76 };
        hdr.Set("Your AWS accounts", "Rename a sign-in or remove the ones you no longer use.");
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 12, 20, 10), BackColor = AppTheme.Page };
        footer.Paint += (_, e) => { using var p = new Pen(AppTheme.Line); e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        _close.Margin = new Padding(0); _remove.Margin = new Padding(0, 0, 10, 0); _rename.Margin = new Padding(0, 0, 10, 0);
        footer.Controls.Add(_close);
        footer.Controls.Add(_remove);
        footer.Controls.Add(_rename);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 6), BackColor = AppTheme.Page };
        var frame = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 12) };
        frame.Controls.Add(_tree);
        body.Controls.Add(frame);
        Controls.Add(body);
        Controls.Add(_hint);
        Controls.Add(footer);
        Controls.Add(hdr);
        body.BringToFront();
        AcceptButton = _close;

        _tree.HandleCreated += (_, _) => { try { SetWindowTheme(_tree.Handle, "explorer", null); } catch { } };
        _tree.AfterSelect += (_, _) => UpdateButtons();
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node != null) { _tree.SelectedNode = e.Node; RenameSelected(); } };
        _rename.Click += (_, _) => RenameSelected();
        _remove.Click += (_, _) => RemoveSelected();
        AppTheme.Style(this);
        Shown += (_, _) => Reload(null);
    }

    string PLabel(Node n) => AwsProfiles.PrimaryLabel(_cfg, n.All[0], n.All);

    void Reload(string? select)
    {
        _cfg = ConfigStore.Load() ?? new AppConfig();
        _profiles = AwsProfiles.List();
        var withServers = _cfg.Tunnels.Select(t => t.Profile).ToHashSet(StringComparer.Ordinal);
        int ServersOf(IEnumerable<ProfileInfo> shown)
        {
            var names = shown.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            var ids = shown.Where(x => x.AccountId != null).Select(x => x.AccountId!).ToHashSet(StringComparer.Ordinal);
            // servers saved under any profile of the same AWS account count for that account
            return _cfg.Tunnels.Count(t => names.Contains(t.Profile) || (_profiles.FirstOrDefault(p => p.Name == t.Profile)?.AccountId is { } id && ids.Contains(id)));
        }

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        TreeNode? pick = null;
        var groups = _profiles.GroupBy(AwsProfiles.PrimaryKey)
            .Select(g => new Node(true, g.Key, g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(n => PLabel(n), StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var g in groups)
        {
            var shown = AwsProfiles.OneEach(g.All, withServers);
            var servers = ServersOf(shown);
            var text = shown.Count > 1
                ? $"{PLabel(g)}      {shown.Count} accounts{(servers > 0 ? $"  -  {servers} server{(servers == 1 ? "" : "s")}" : "")}"
                : $"{PLabel(g)}{(servers > 0 ? $"      {servers} server{(servers == 1 ? "" : "s")}" : "")}";
            var node = new TreeNode(text) { Tag = g, NodeFont = AppTheme.Semi(10.5f) };
            if (shown.Count > 1 || g.All[0].Kind == ProfileKind.Sso)
            {
                foreach (var m in shown)
                {
                    var one = new[] { m };
                    var sv = ServersOf(one);
                    var child = new TreeNode($"{_cfg.AliasOr(m.Name, m.Name)}{(m.AccountId != null ? "      " + m.AccountId : "")}{(sv > 0 ? $"      {sv} server{(sv == 1 ? "" : "s")}" : "")}")
                    {
                        Tag = new Node(false, m.Name, new List<ProfileInfo> { m }),
                        ForeColor = Color.FromArgb(70, 88, 92),
                    };
                    node.Nodes.Add(child);
                }
            }
            _tree.Nodes.Add(node);
            if (select != null && g.Key == select) pick = node;   // stays collapsed
        }
        _tree.EndUpdate();
        _tree.CollapseAll();
        _tree.SelectedNode = pick ?? (_tree.Nodes.Count > 0 ? _tree.Nodes[0] : null);
        UpdateButtons();

        if (!_sized)
        {
            // fit the window to what is shown instead of leaving a large empty list
            _sized = true;
            var f = DeviceDpi / 96f;
            var rows = Math.Max(2, Math.Min(8, _tree.Nodes.Count));
            var h = (int)((76 + 62 + 38 + 40) * f) + rows * _tree.ItemHeight + (int)(30 * f);
            ClientSize = new Size(ClientSize.Width, Math.Clamp(h, (int)(340 * f), (int)(560 * f)));
        }
    }

    Node? Current => _tree.SelectedNode?.Tag as Node;

    void UpdateButtons() { _rename.Enabled = _remove.Enabled = Current != null; }

    void RenameSelected()
    {
        if (Current is not { } n) return;
        var fallback = n.IsPrimary ? AwsProfiles.PrimaryName(n.All[0]) : n.Key;
        var now = n.IsPrimary ? PLabel(n) : _cfg.AliasOr(n.Key, n.Key);
        var typed = Ask(this, "Rename account", n.IsPrimary ? "Friendly name for this AWS sign-in (shown in the account menu):" : "Friendly name for this organization account:", now);
        if (typed == null) return;
        typed = typed.Trim();
        if (typed.Length > 60) typed = typed.Substring(0, 60);
        if (n.IsPrimary) foreach (var m in n.All) foreach (var k in AwsProfiles.LegacyKeys(m)) _cfg.Aliases.Remove(k);
        if (typed.Length == 0 || typed == fallback) _cfg.Aliases.Remove(n.Key); else _cfg.Aliases[n.Key] = typed;
        ConfigStore.Save(_cfg);
        Changed = true;
        Reload(n.IsPrimary ? n.Key : null);
    }

    void RemoveSelected()
    {
        if (Current is not { } n) return;
        var names = n.All.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var servers = _cfg.Tunnels.Count(t => names.Contains(t.Profile));
        var shownCount = n.IsPrimary ? AwsProfiles.OneEach(n.All).Count : 1;
        var what = n.IsPrimary
            ? $"the AWS sign-in \"{PLabel(n)}\"" + (shownCount > 1 ? $" and its {shownCount} organization accounts" : "")
            : $"the account \"{_cfg.AliasOr(n.Key, n.Key)}\"";
        var msg = $"Remove {what}?\n\n" +
                  (servers > 0 ? $"{servers} saved server connection(s) will be removed from SSMConnect.\n" : "") +
                  "The login is deleted from this PC. You can add it again at any time.\nYour PuTTY / WinSCP sessions and the servers in AWS are not changed.";
        if (MessageBox.Show(this, msg, "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        _cfg.Tunnels.RemoveAll(t => names.Contains(t.Profile));
        foreach (var m in n.All) { _cfg.Aliases.Remove(m.Name); DiscoveryCache.Forget(m.Name); }
        if (n.IsPrimary) { _cfg.Aliases.Remove(n.Key); foreach (var m in n.All) foreach (var k in AwsProfiles.LegacyKeys(m)) _cfg.Aliases.Remove(k); }
        if (names.Contains(_cfg.Profile)) _cfg.Profile = _cfg.Tunnels.FirstOrDefault()?.Profile ?? "";
        ConfigStore.Save(_cfg);

        var failed = new List<string>();
        foreach (var m in n.All)
        {
            try { AwsProfiles.RemoveProfile(m.Name); } catch (Exception ex) { failed.Add(m.Name + ": " + ex.Message); }
        }
        // the sign-in itself goes when no remaining profile uses it
        var sessions = n.All.Select(m => m.SsoSession).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var left = AwsProfiles.List();
        foreach (var s in sessions)
            if (!left.Any(p => p.SsoSession == s)) { try { AwsProfiles.RemoveSsoSession(s!); } catch { } }
        if (failed.Count > 0) MessageBox.Show(this, "Some settings could not be removed from your AWS files:\n\n" + string.Join("\n", failed), "Remove account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Changed = true;
        _sized = true;
        Reload(null);
    }

    /// <summary>A small themed text prompt. Returns null when cancelled.</summary>
    public static string? Ask(IWin32Window owner, string title, string label, string initial)
    {
        using var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(480, 160), Font = AppTheme.Body(), BackColor = AppTheme.Page,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
        };
        var l = new Label { Text = label, AutoSize = false, Left = 22, Top = 18, Width = 436, Height = 36 };
        var t = new TextBox { Left = 22, Top = 58, Width = 436, Text = initial, MaxLength = 60 };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 250, Top = 104, Width = 100, Height = 38, Tag = "primary" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 358, Top = 104, Width = 100, Height = 38 };
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        AppTheme.Style(f);
        f.Shown += (_, _) => { t.Focus(); t.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
    }
}
