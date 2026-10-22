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
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// One side of the file browser: pick a server (signed in as its default user with the saved key), optionally switch to
/// another user with sudo, then browse its folders.
/// </summary>
sealed class FilePane : GlassPanel
{
    static readonly string[] CommonUsers = { "ec2-user", "ubuntu", "admin", "centos", "fedora", "rocky", "ssm-user" };

    List<TunnelConfig> _servers;
    readonly Func<int, bool> _isUp;
    readonly Func<TunnelConfig, Task<bool>>? _ensure;
    readonly Func<TunnelConfig, IWin32Window?, Task<bool>>? _login;
    const string Placeholder = "Select the server to connect";
    readonly Label _hint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(112, 128, 132), BackColor = Color.White, Text = Placeholder };
    readonly GlassSelect _server = new() { Width = 300, MutedIndex = 0 };
    bool _loadingServers;
    readonly GlassText _user = new() { Width = 130, Chevron = true };
    readonly GlassText _pass = new() { Width = 190, UseSystemPasswordChar = true, PlaceholderText = "sudo password (if asked)" };
    readonly Button _connect = new() { Text = "Switch user", AutoSize = true, MinimumSize = new Size(110, 30), Tag = "primary" };
    readonly Button _up = new() { Text = "Up", AutoSize = true, MinimumSize = new Size(52, 30) };
    readonly Button _refresh = new() { Text = "Refresh", AutoSize = true, MinimumSize = new Size(76, 30) };
    readonly Button _newFolder = new() { Text = "New folder", AutoSize = true, MinimumSize = new Size(96, 30) };
    readonly GlassText _path = new() { Dock = DockStyle.Fill };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Color.White };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.FromArgb(94, 110, 114), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
    readonly List<FsEntry> _entries = new();
    readonly ToolTip _tip = new();

    public FsBase? Fs { get; private set; }
    public TunnelConfig? Server => _server.SelectedIndex >= 1 && _server.SelectedIndex <= _servers.Count ? _servers[_server.SelectedIndex - 1] : null;
    public string CurrentPath { get; private set; } = "";
    public event Action? Changed;

    readonly Func<TunnelConfig, string> _label;

    public FilePane(Func<int, bool> isUp, Func<TunnelConfig, string> label, Func<TunnelConfig, Task<bool>>? ensure = null, Func<TunnelConfig, IWin32Window?, Task<bool>>? login = null)
    {
        _servers = new List<TunnelConfig>();
        _isUp = isUp;
        _ensure = ensure;
        _login = login;
        _label = label;
        Dock = DockStyle.Fill;
        Padding = new Padding(14, 12, 14, 10);
        Margin = new Padding(0);
        _hint.Font = AppTheme.Semi(11f);

        _user.Choices.AddRange(CommonUsers);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        top.Controls.Add(Wrap(_server));
        top.Controls.Add(Wrap(_user));
        top.Controls.Add(Wrap(_pass));
        top.Controls.Add(_connect);
        _connect.Margin = new Padding(0, 2, 0, 6);

        var nav = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 4, RowCount = 1 };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.Controls.Add(_up, 0, 0);
        nav.Controls.Add(_path, 1, 0);
        nav.Controls.Add(_refresh, 2, 0);
        nav.Controls.Add(_newFolder, 3, 0);
        _path.Margin = new Padding(6, 3, 6, 3);

        _list.Columns.Add("Name", 260);
        _list.Columns.Add("Size", 90, HorizontalAlignment.Right);
        _list.Columns.Add("Modified", 130);
        _list.SmallImageList = MakeIcons();
        GlassList.Apply(_list);
        _list.ListViewItemSorter = null;

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = AppTheme.Line };
        frame.Controls.Add(_list);
        frame.Controls.Add(_hint);
        _hint.BringToFront();

        Controls.Add(frame);
        Controls.Add(_status);
        Controls.Add(nav);
        Controls.Add(top);

        _server.SelectedIndexChanged += async (_, _) => { if (_loadingServers) return; OnServerChosen(); if (Server != null) await ConnectAsync(auto: true); };
        _user.TextChanged += (_, _) => UpdateUserState();
        _user.ChoicePicked += UpdateUserState;
        _connect.Click += async (_, _) => await ConnectAsync(auto: false);
        _up.Click += async (_, _) => { if (Fs != null) await GoAsync(Fs.Parent(CurrentPath)); };
        _refresh.Click += async (_, _) => await GoAsync(CurrentPath);
        _path.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await GoAsync(_path.Text.Trim()); } };
        _list.DoubleClick += async (_, _) =>
        {
            if (_list.SelectedIndices.Count != 1) return;
            var e = _entries[_list.SelectedIndices[0]];
            if (e.IsDir && Fs != null) await GoAsync(e.FullPath);
        };
        _newFolder.Click += async (_, _) => await NewFolderAsync();
        _list.SelectedIndexChanged += (_, _) => Changed?.Invoke();
        SetEnabledState();
    }

    /// <summary>Fills the server list (the servers of one organization) and signs in to <paramref name="pick"/> as its default user.</summary>
    public async Task SetServersAsync(IEnumerable<TunnelConfig> servers, TunnelConfig? pick)
    {
        _loadingServers = true;
        CloseFs();
        _servers = servers.ToList();
        _server.Items.Clear();
        _server.Items.Add(Placeholder);
        foreach (var s in _servers) _server.Items.Add(_label(s));
        _server.SelectedIndex = 0;   // never sign in by default: the person picks the server
        _loadingServers = false;
        OnServerChosen();
        await Task.CompletedTask;
    }

    bool IsDefaultUser => Server is { } t && string.Equals(_user.Text.Trim(), t.UserName, StringComparison.Ordinal);

    void UpdateUserState()
    {
        var def = IsDefaultUser;
        _pass.Enabled = Server != null && !def && _connect.Enabled;
        _connect.Text = def ? "Reconnect" : "Switch user";
        if (def) _pass.Text = "";
    }

    static Control Wrap(Control c) { c.Margin = new Padding(0, 2, 8, 6); return c; }

    static ImageList MakeIcons()
    {
        var il = new ImageList { ImageSize = new Size(22, 22), ColorDepth = ColorDepth.Depth32Bit };
        var folder = new Bitmap(22, 22);
        using (var g = Graphics.FromImage(folder))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Color.FromArgb(240, 190, 70));
            g.FillRectangle(b, 3, 5, 8, 4);
            using var b2 = new SolidBrush(Color.FromArgb(250, 205, 90));
            g.FillRectangle(b2, 3, 8, 16, 10);
        }
        var file = new Bitmap(22, 22);
        using (var g = Graphics.FromImage(file))
        {
            using var b = new SolidBrush(Color.White);
            using var pen = new Pen(Color.FromArgb(120, 140, 145));
            g.FillRectangle(b, 5, 3, 12, 16);
            g.DrawRectangle(pen, 5, 3, 12, 16);
            using var line = new Pen(Color.FromArgb(190, 205, 208));
            for (var y = 8; y < 17; y += 3) g.DrawLine(line, 8, y, 14, y);
        }
        il.Images.Add(folder);
        il.Images.Add(file);
        return il;
    }

    void OnServerChosen()
    {
        CloseFs();
        _pass.Text = "";
        if (Server is { } t)
        {
            if (!_user.Choices.Contains(t.UserName)) _user.Choices.Insert(0, t.UserName);
            _user.Text = t.UserName;
            _status.Text = "Signing in as " + t.UserName + " with the saved key ...";
            _hint.Text = "Signing in ...";
        }
        else { _user.Text = ""; _status.Text = _servers.Count == 0 ? "No servers in this organization." : "Not connected."; _hint.Text = Placeholder; }
        UpdateUserState();
        SetEnabledState();
        Changed?.Invoke();
    }

    void CloseFs()
    {
        var old = Fs;
        Fs = null; CurrentPath = "";
        _entries.Clear(); _list.Items.Clear(); _path.Text = "";
        if (old != null) Task.Run(() => { try { old.Dispose(); } catch { } });
    }

    void SetEnabledState()
    {
        var on = Fs != null;
        _up.Enabled = _refresh.Enabled = _newFolder.Enabled = _path.Enabled = _list.Enabled = on;
        _hint.Visible = !on;
        _list.Visible = on;
        _connect.Enabled = Server != null;
        _user.Enabled = Server != null;
        Changed?.Invoke();
    }

    /// <summary>Locks the pane while a copy is running.</summary>
    public void SetBusy(bool busy)
    {
        _server.Enabled = _connect.Enabled = _user.Enabled = !busy;
        var on = !busy && Fs != null;
        _up.Enabled = _refresh.Enabled = _newFolder.Enabled = _path.Enabled = _list.Enabled = on;
        UpdateUserState();
    }

    async Task ConnectAsync(bool auto)
    {
        CloseFs();
        var t = Server;
        if (t == null) return;
        _connect.Enabled = false;
        try
        {
            var user = _user.Text.Trim();
            if (user.Length == 0) { MessageBox.Show(FindForm(), "Enter the user to sign in as.", "Copy files"); return; }
            if (user.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "The root user is not allowed in this tool.";
                if (!auto) MessageBox.Show(FindForm(), "For safety, this tool never works as root. Use a normal user such as ubuntu or ec2-user.", "Copy files");
                return;
            }
            if (!_isUp(t.LocalPort))
            {
                _status.Text = "This server is not available. Sign in again on the dashboard, then click Reconnect.";
                if (!auto) MessageBox.Show(FindForm(), $"\"{t.Name}\" is not available right now. Use Sign in again on the dashboard, then try again.", "Copy files");
                return;
            }
            if (_ensure != null)
            {
                _status.Text = "Opening the secure connection (a few seconds) ...";
                if (!await _ensure(t))
                {
                    _status.Text = "Could not open the connection to this server.";
                    if (!auto) MessageBox.Show(FindForm(), $"The secure connection to \"{t.Name}\" did not open. Check the card on the dashboard for the reason.", "Copy files");
                    return;
                }
            }
            var wasDefault = string.Equals(user, t.UserName, StringComparison.Ordinal);
            if (_login != null)
            {
                _status.Text = "Checking the login for this server ...";
                if (!await _login(t, FindForm()))
                {
                    _status.Text = "Could not sign in: the key or user for this server is not set up.";
                    _hint.Text = "Could not sign in. Pick the server again to retry.";
                    return;
                }
                if (wasDefault && !string.Equals(_user.Text.Trim(), t.UserName, StringComparison.Ordinal)) { if (!_user.Choices.Contains(t.UserName)) _user.Choices.Insert(0, t.UserName); _user.Text = t.UserName; user = t.UserName; }
            }
            var def = string.Equals(user, t.UserName, StringComparison.Ordinal);
            _status.Text = def ? $"Signing in as {user} with the saved key ..." : $"Switching to {user} with sudo ...";
            var pw = _pass.Text;
            FsBase fs = def ? await Task.Run(() => (FsBase)SftpFs.Open(t)) : await Task.Run(() => (FsBase)SudoFs.Open(t, user, pw));
            Fs = fs;
            _status.Text = $"Signed in as {user}";
            SetEnabledState();
            await GoAsync(fs.Home);
        }
        catch (Exception ex)
        {
            CloseFs();
            _hint.Text = "Could not sign in. Pick the server again to retry.";
            var first = ex.Message.Split('\n')[0];
            _status.Text = "Could not sign in: " + first;
            _tip.SetToolTip(_status, ex.Message);
            var msg = ex.Message;
            var def = string.Equals(_user.Text.Trim(), t.UserName, StringComparison.Ordinal);
            if (ex is Renci.SshNet.Common.SshAuthenticationException)
                msg = "The server did not accept the saved key for the default user. Check the user name and key in the server's settings.\n\n" + ex.Message;
            else if (ex is System.Net.Sockets.SocketException || ex is Renci.SshNet.Common.SshConnectionException)
                msg = "Could not reach the server through its connection. Make sure it shows Connected on the dashboard.\n\n" + ex.Message;
            AppLog.Warn("File browser sign-in failed: " + ex.Message);
            if (!auto) MessageBox.Show(FindForm(), msg, "Copy files");
        }
        finally { _connect.Enabled = true; UpdateUserState(); SetEnabledState(); }
    }

    public async Task GoAsync(string path)
    {
        var fs = Fs;
        if (fs == null) return;
        try
        {
            _status.Text = "Loading ...";
            var list = await Task.Run(() => fs.List(path));
            if (!ReferenceEquals(fs, Fs)) return;
            CurrentPath = path;
            _path.Text = path;
            _entries.Clear(); _entries.AddRange(list);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in list)
            {
                var it = new ListViewItem(e.Name, e.IsDir ? 0 : 1);
                it.SubItems.Add(e.IsDir ? "" : FormatSize(e.Size));
                it.SubItems.Add(e.Modified == DateTime.MinValue ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm"));
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            _status.Text = $"{list.Count} item(s)  -  {fs.Label}";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not open that folder: " + ex.Message;
            if (!string.IsNullOrEmpty(CurrentPath) && CurrentPath != path) _path.Text = CurrentPath;
        }
        Changed?.Invoke();
    }

    async Task NewFolderAsync()
    {
        var fs = Fs;
        if (fs == null) return;
        var name = Prompt.Ask(FindForm(), "New folder", "Name of the new folder:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var target = fs.Combine(CurrentPath, name.Trim());
            await Task.Run(() => fs.MakeDir(target));
            await GoAsync(CurrentPath);
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), "Could not create the folder: " + ex.Message, "Copy files"); }
    }

    public List<FsEntry> Selected() => _list.SelectedIndices.Cast<int>().Where(i => i < _entries.Count).Select(i => _entries[i]).ToList();
    public HashSet<string> NamesHere() => new(_entries.Select(e => e.Name), StringComparer.Ordinal);

    public static string FormatSize(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1024L * 1024) return (b / 1024.0).ToString("0.#") + " KB";
        if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " MB";
        return (b / 1073741824.0).ToString("0.##") + " GB";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { try { Fs?.Dispose(); } catch { } }
        base.Dispose(disposing);
    }
}

/// <summary>A tiny "type a value" dialog.</summary>
static class Prompt
{
    public static string? Ask(IWin32Window? owner, string title, string label, string initial)
    {
        using var f = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(420, 130), Font = AppTheme.Body(),
        };
        var l = new Label { Text = label, AutoSize = true, Left = 16, Top = 16 };
        var tb = new TextBox { Left = 16, Top = 44, Width = 388, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 218, Top = 84, Width = 90, Height = 30, Tag = "primary" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 314, Top = 84, Width = 90, Height = 30 };
        f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        AppTheme.Style(f);
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }
}

/// <summary>
/// Two-pane file browser between servers of ONE organization (the top-level AWS sign-in). Each side signs in as the
/// server's default user with its saved key; a different user can be switched to with sudo. Select files or
/// folders on one side and copy them to the folder open on the other. The data travels through this PC.
/// </summary>
public sealed class TransferDialog : Form
{
    readonly FilePane _left, _right;
    readonly Button _toRight = new() { Text = "Copy  ▶", AutoSize = true, MinimumSize = new Size(110, 40), Tag = "primary" };
    readonly Button _toLeft = new() { Text = "◀  Copy", AutoSize = true, MinimumSize = new Size(110, 40), Tag = "primary" };
    readonly GlassProgress _bar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
    readonly Label _info = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly Button _cancel = new() { Text = "Stop", AutoSize = true, MinimumSize = new Size(90, 34), Enabled = false };
    readonly Button _close = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.Cancel };
    readonly TunnelConfig? _preselect;
    readonly string? _startKey;
    CancellationTokenSource? _cts;
    bool _busy;

    /// <summary>The servers of one top-level organization.</summary>
    public sealed record Org(string Key, string Label, List<TunnelConfig> Servers);

    readonly List<Org> _all;
    List<Org> _orgs = new();
    readonly Func<int, bool> _isUp;
    readonly Button _refreshOrgs = new() { Text = "Refresh", AutoSize = true, MinimumSize = new Size(90, 30) };
    readonly GlassSelect _orgBox = new() { Width = 320 };
    bool _orgLoading;

    public TransferDialog(IEnumerable<Org> orgs, Func<int, bool> isUp, Func<TunnelConfig, string> serverLabel, TunnelConfig? preselect = null, string? startOrgKey = null, Func<TunnelConfig, Task<bool>>? ensureOpen = null, Func<TunnelConfig, IWin32Window?, Task<bool>>? ensureLogin = null)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Copy files between servers";
        Font = AppTheme.Body();
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(980, 580);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = AppTheme.Page;

        _all = orgs.Where(o => o.Servers.Count > 0).ToList();
        _isUp = isUp;
        _left = new FilePane(isUp, serverLabel, ensureOpen, ensureLogin);
        _right = new FilePane(isUp, serverLabel, ensureOpen, ensureLogin);
        _preselect = preselect;
        _startKey = preselect != null ? _all.FirstOrDefault(o => o.Servers.Contains(preselect))?.Key : startOrgKey;

        var hdr = new HeaderBand { Dock = DockStyle.Top, Height = 84 };
        hdr.Set("Copy files between servers", "Pick a server in each pane to sign in. To act as another user, pick it and click Switch user (done with sudo). Only servers of the same organization can be linked.");

        var root = new BackdropPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12) };
        var orgCard = new GlassPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 8, 12, 8) };
        var orgBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 3, 0, 0), BackColor = Color.Transparent };
        orgCard.Controls.Add(orgBar);
        orgBar.Controls.Add(new Label { Text = "Organization:", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        _orgBox.Margin = new Padding(0, 2, 0, 0);
        orgBar.Controls.Add(_orgBox);
        _refreshOrgs.Margin = new Padding(8, 0, 0, 0);
        orgBar.Controls.Add(_refreshOrgs);

        var mid = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 120, 0, 0), BackColor = Color.Transparent };
        _toRight.Margin = new Padding(4, 0, 4, 14); _toLeft.Margin = new Padding(4, 0, 4, 14);
        mid.Controls.Add(_toRight);
        mid.Controls.Add(_toLeft);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 10, 0, 10), BackColor = Color.Transparent };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.Controls.Add(_left, 0, 0);
        body.Controls.Add(mid, 1, 0);
        body.Controls.Add(_right, 2, 0);

        var footerCard = new GlassPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(10, 6, 10, 6) };
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = Color.Transparent };
        footerCard.Controls.Add(footer);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_info, 0, 0);
        footer.Controls.Add(_bar, 1, 0);
        footer.Controls.Add(_cancel, 2, 0);
        footer.Controls.Add(_close, 3, 0);
        _bar.Margin = new Padding(8, 10, 8, 10);
        _info.Text = "Loading the connected servers ...";

        root.Controls.Add(body);
        root.Controls.Add(footerCard);
        root.Controls.Add(orgCard);
        Controls.Add(root);
        Controls.Add(hdr);
        AppTheme.Style(this);

        _orgBox.SelectedIndexChanged += async (_, _) => { if (!_orgLoading) await LoadOrgAsync(_orgBox.SelectedIndex, null); };
        _refreshOrgs.Click += async (_, _) => { if (_busy) return; await ReloadAsync(CurrentKey(), null); };
        Shown += async (_, _) => await ReloadAsync(_startKey, _preselect);

        _left.Changed += UpdateButtons;
        _right.Changed += UpdateButtons;
        _toRight.Click += async (_, _) => await CopyAsync(_left, _right);
        _toLeft.Click += async (_, _) => await CopyAsync(_right, _left);
        _cancel.Click += (_, _) => _cts?.Cancel();
        CancelButton = _close;
        FormClosing += (_, e) =>
        {
            if (_busy && MessageBox.Show(this, "A copy is still running. Stop it and close?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            _cts?.Cancel();
        };
        UpdateButtons();
    }

    string? CurrentKey() => _orgBox.SelectedIndex >= 0 && _orgBox.SelectedIndex < _orgs.Count ? _orgs[_orgBox.SelectedIndex].Key : null;

    /// <summary>Lists only the servers that are enabled and available (Ready or Connected) on the dashboard right now.</summary>
    async Task ReloadAsync(string? keepKey, TunnelConfig? pick)
    {
        _orgs = _all.Select(o => new Org(o.Key, o.Label, o.Servers.Where(s => s.Enabled && _isUp(s.LocalPort)).ToList())).Where(o => o.Servers.Count > 0).ToList();
        _orgLoading = true;
        _orgBox.Items.Clear();
        foreach (var o in _orgs) _orgBox.Items.Add($"{o.Label}   ({o.Servers.Count} available)");
        var idx = Math.Max(0, _orgs.FindIndex(o => o.Key == keepKey));
        if (_orgs.Count > 0) _orgBox.SelectedIndex = idx;
        _orgLoading = false;
        if (_orgs.Count == 0)
        {
            await _left.SetServersAsync(new List<TunnelConfig>(), null);
            await _right.SetServersAsync(new List<TunnelConfig>(), null);
            _info.Text = "No servers are available right now. Sign in on the dashboard so the cards turn green, then click Refresh.";
            return;
        }
        _info.Text = "Tip: Switch user uses sudo, like Ubuntu does. Leave the password blank unless sudo asks for one. root is not allowed.";
        await LoadOrgAsync(idx, pick);
    }

    async Task LoadOrgAsync(int index, TunnelConfig? pick)
    {
        if (index < 0 || index >= _orgs.Count) return;
        var o = _orgs[index];
        await Task.WhenAll(_left.SetServersAsync(o.Servers, null), _right.SetServersAsync(o.Servers, null));
    }

    void UpdateButtons()
    {
        _toRight.Enabled = !_busy && _left.Fs != null && _right.Fs != null && _left.Selected().Count > 0;
        _toLeft.Enabled = !_busy && _left.Fs != null && _right.Fs != null && _right.Selected().Count > 0;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        _left.SetBusy(busy); _right.SetBusy(busy);
        _cancel.Enabled = busy;
        _close.Enabled = !busy;
        UpdateButtons();
    }

    sealed record Job(FsEntry Entry, string Dest);

    async Task CopyAsync(FilePane from, FilePane to)
    {
        if (_busy || from.Fs == null || to.Fs == null) return;
        var items = from.Selected();
        if (items.Count == 0) { MessageBox.Show(this, "Select the files or folders to copy first.", Text); return; }
        if (string.IsNullOrEmpty(to.CurrentPath)) { MessageBox.Show(this, "Open the folder on the other side where the files should go.", Text); return; }
        var fromT = from.Server; var toT = to.Server;
        var org = _orgBox.SelectedIndex >= 0 && _orgBox.SelectedIndex < _orgs.Count ? _orgs[_orgBox.SelectedIndex] : null;
        if (fromT == null || toT == null || org == null || !org.Servers.Contains(fromT) || !org.Servers.Contains(toT))
        { MessageBox.Show(this, "Files can only be copied between servers of the same organization.", Text); return; }
        if (fromT == toT && from.CurrentPath == to.CurrentPath)
        { MessageBox.Show(this, "The source and the destination are the same folder. Open a different folder on one side.", Text); return; }
        if (fromT == toT && items.Any(i => i.IsDir && (to.CurrentPath == i.FullPath || to.CurrentPath.StartsWith(i.FullPath.TrimEnd('/') + "/", StringComparison.Ordinal))))
        { MessageBox.Show(this, "A folder cannot be copied into itself.", Text); return; }

        var src = from.Fs; var dst = to.Fs; var dstDir = to.CurrentPath;
        var clash = to.NamesHere();
        var dupes = items.Where(i => clash.Contains(i.Name)).Select(i => i.Name).ToList();
        if (dupes.Count > 0)
        {
            var shown = string.Join("\n", dupes.Take(8)) + (dupes.Count > 8 ? $"\n... and {dupes.Count - 8} more" : "");
            if (MessageBox.Show(this, $"These already exist in the destination and will be replaced:\n\n{shown}\n\nContinue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        _bar.Value = 0;
        var failures = new List<string>();
        long total = 0, done = 0;
        int copied = 0;
        var lastUi = Environment.TickCount64;
        void Ui(string text, bool force = false)
        {
            var now = Environment.TickCount64;
            if (!force && now - lastUi < 120) return;
            lastUi = now;
            var frac = total <= 0 ? 0 : (int)Math.Min(1000, done * 1000 / total);
            try { BeginInvoke(new Action(() => { _bar.Value = frac; _info.Text = text; })); } catch { }
        }

        try
        {
            await Task.Run(() =>
            {
                var dirs = new List<string>();
                var files = new List<Job>();
                void Walk(FsEntry e, string dest, int depth)
                {
                    ct.ThrowIfCancellationRequested();
                    if (e.IsDir)
                    {
                        if (depth > 40) { failures.Add($"{e.Name}: folders nested too deeply, skipped"); return; }
                        dirs.Add(dest);
                        List<FsEntry> kids;
                        try { kids = src.List(e.FullPath); }
                        catch (Exception ex) { failures.Add($"{e.Name}: {ex.Message}"); return; }
                        foreach (var k in kids) Walk(k, dst.Combine(dest, k.Name), depth + 1);
                    }
                    else { files.Add(new Job(e, dest)); total += e.Size; }
                }
                Ui("Preparing the list of files ...", force: true);
                foreach (var it in items) Walk(it, dst.Combine(dstDir, it.Name), 0);

                foreach (var d in dirs)
                {
                    ct.ThrowIfCancellationRequested();
                    try { if (!dst.Exists(d)) dst.MakeDir(d); }
                    catch (Exception ex) { failures.Add($"{d}: {ex.Message}"); }
                }

                var buf = new byte[128 * 1024];
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var r = src.OpenRead(f.Entry.FullPath);
                        using var w = dst.OpenWrite(f.Dest);
                        int n;
                        while ((n = r.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            w.Write(buf, 0, n);
                            done += n;
                            Ui($"Copying {f.Entry.Name}   ({FilePane.FormatSize(done)} of {FilePane.FormatSize(total)})");
                        }
                        copied++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { failures.Add($"{f.Entry.Name}: {ex.Message}"); done += f.Entry.Size; }
                }
            });
            _bar.Value = 1000;
            _info.Text = failures.Count == 0 ? $"Done. {copied} file(s) copied." : $"Finished with problems: {copied} copied, {failures.Count} failed.";
        }
        catch (OperationCanceledException) { _info.Text = "Stopped. Files already copied stay where they are."; }
        catch (Exception ex)
        {
            AppLog.Error("File copy failed: " + ex);
            _info.Text = "The copy failed.";
            MessageBox.Show(this, "The copy failed: " + ex.Message, Text);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose(); _cts = null;
        }

        if (failures.Count > 0)
        {
            AppLog.Warn("File copy problems: " + string.Join(" | ", failures.Take(20)));
            var shown = string.Join("\n", failures.Take(8)) + (failures.Count > 8 ? $"\n... and {failures.Count - 8} more (see the log file)" : "");
            MessageBox.Show(this, "Some items could not be copied:\n\n" + shown + "\n\nA \"permission denied\" usually means the signed-in user cannot read the source or write to the destination folder. Switch the user on that side (it uses sudo) and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        await to.GoAsync(to.CurrentPath);
    }
}
