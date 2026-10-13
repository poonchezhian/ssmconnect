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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// Five-step setup wizard: requirements (with one-click install), AWS sign-in, pick servers, match SSH keys, apply.
/// Layout uses docked / flow panels and DPI-aware scaling so text is never clipped, at any Windows display scale.
/// </summary>
public sealed class WizardForm : Form
{
    const int ContentWidth = 760;
    static readonly Color Good = Color.FromArgb(0, 110, 40);
    static readonly Color Bad = Color.FromArgb(175, 0, 20);
    static readonly Color Warn = Color.FromArgb(150, 85, 0);
    static readonly Color Muted = Color.FromArgb(70, 70, 70);

    readonly AppConfig _cfg;
    readonly string? _loadWarning;
    readonly List<Panel> _steps = new();
    readonly Label _title = new() { Font = new Font("Segoe UI Semibold", 15f), AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _back = new() { Text = "< Back", AutoSize = true, MinimumSize = new Size(100, 36), Padding = new Padding(10, 2, 10, 2) };
    readonly Button _next = new() { Text = "Next >", AutoSize = true, MinimumSize = new Size(120, 36), Padding = new Padding(10, 2, 10, 2) };
    readonly HeaderBand _hdr = new() { Dock = DockStyle.Top, Height = 84 };
    int _idx;
    List<DiscoveredServer> _servers = new();

    // step 0
    readonly Label _awsStatus = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f) };
    readonly Label _awsDetail = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(ContentWidth, 0) };
    readonly Label _pluginStatus = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f) };
    readonly Label _pluginDetail = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(ContentWidth, 0) };
    readonly Label _toolsStatus = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };
    readonly Button _install = new() { Text = "Install missing components automatically", AutoSize = true, MinimumSize = new Size(320, 40), Padding = new Padding(12, 4, 12, 4) };
    readonly ProgressBar _progress = new() { Width = ContentWidth, Height = 18, Visible = false };
    readonly Label _installStatus = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };
    // step 1
    readonly ComboBox _profile = new() { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _region = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    readonly Label _profileInfo = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 6) };
    readonly Label _regionHint = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(ContentWidth, 0) };
    readonly Button _ssoBtn = new() { Text = "Sign in (SSO)", AutoSize = true, MinimumSize = new Size(170, 38), Padding = new Padding(10, 2, 10, 2), Visible = false };
    List<ProfileInfo> _profiles = new();
    bool _connected, _profilesLoaded, _loadingProfiles;
    readonly Label _authResult = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };
    // step 2
    readonly ListView _list = new() { CheckBoxes = true, FullRowSelect = true, View = View.Details, Width = ContentWidth, Height = 250, HideSelection = false };
    readonly Label _keyStatus = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), ForeColor = Muted };
    readonly Label _keySummary = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 8) };
    readonly DataGridView _keyGrid = new() { Width = ContentWidth, Height = 300 };
    List<DiscoveredServer> _keyServers = new();
    List<KeyFile> _keys = new();
    bool _keysScanned;
    readonly Label _findResult = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };
    // step 3
    readonly CheckBox _putty = new() { Text = "PuTTY", Checked = true, AutoSize = true };
    readonly CheckBox _winscp = new() { Text = "WinSCP", Checked = true, AutoSize = true };
    readonly CheckBox _moba = new() { Text = "MobaXterm (close it first)", Checked = true, AutoSize = true };
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = ContentWidth, Height = 270, Font = new Font("Consolas", 10f), BackColor = Color.White };

    public WizardForm()
    {
        _cfg = ConfigStore.LoadOrRecover(out _loadWarning);
        // DPI-aware scaling: all sizes below are 96-dpi units and scale with the Windows display setting.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "SSMConnect Setup";
        Font = AppTheme.Body();
        ClientSize = new Size(860, 660);
        MinimumSize = new Size(700, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 12, 28, 8) };
        BackColor = AppTheme.Page;
        host.BackColor = AppTheme.Page;
        _hdr.Steps = new[] { "Requirements", "AWS account", "Servers", "SSH keys", "Save" };
        var header = _hdr;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 12, 20, 12), BackColor = Color.White };
        footer.Controls.Add(_next);
        footer.Controls.Add(_back);
        Controls.Add(host);
        Controls.Add(header);
        Controls.Add(footer);
        host.BringToFront();

        _back.Click += (_, _) => Go(_idx - 1);
        _next.Click += (_, _) => OnNext();

        BuildPrereqStep(host);
        BuildAwsStep(host);
        BuildServersStep(host);
        BuildKeysStep(host);
        BuildApplyStep(host);

        _back.Tag = null; _next.Tag = "primary";
        AppTheme.RoomyRows(_list);
        AppTheme.Style(this);
        Shown += (_, _) => SizeColumns();
        Go(0);
    }

    // ---- layout helpers ----------------------------------------------------------------------
    FlowLayoutPanel NewStep(Panel host)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Visible = false };
        _steps.Add(p);
        host.Controls.Add(p);
        return p;
    }

    static Label Para(string text, int bottom = 12) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, bottom) };

    static FlowLayoutPanel Row(params Control[] controls)
    {
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        foreach (var c in controls)
        {
            if (c is Label or CheckBox) c.Margin = new Padding(0, 7, 12, 0); else c.Margin = new Padding(0, 3, 12, 0);
            r.Controls.Add(c);
        }
        return r;
    }

    void SizeColumns()
    {
        var f = DeviceDpi / 96f;
        int[] w = { 200, 100, 150, 105, 75, 75, 170 };
        for (var i = 0; i < _list.Columns.Count && i < w.Length; i++) _list.Columns[i].Width = (int)(w[i] * f);
    }

    // ---- Step 0: requirements ----------------------------------------------------------------
    void BuildPrereqStep(Panel host)
    {
        var p = NewStep(host);
        p.Controls.Add(Para("SSMConnect opens PuTTY, WinSCP or MobaXterm on servers that have no public SSH (port 22 closed) by tunnelling through AWS Systems Manager. It needs two free tools from Amazon."));
        _awsStatus.Margin = new Padding(0, 4, 0, 0);
        _pluginStatus.Margin = new Padding(0, 14, 0, 0);
        _toolsStatus.Margin = new Padding(0, 14, 0, 14);
        _install.Margin = new Padding(0, 0, 0, 8);
        _progress.Margin = new Padding(0, 0, 0, 6);
        p.Controls.AddRange(new Control[] { _awsStatus, _awsDetail, _pluginStatus, _pluginDetail, _toolsStatus, _install, _progress, _installStatus });

        _install.Click += async (_, _) => await InstallMissingAsync();
    }

    bool PrereqsOk() => AwsCli.FindAws() != null && AwsCli.FindPlugin() != null;

    void RefreshPrereqs()
    {
        var aws = AwsCli.FindAws();
        var plugin = AwsCli.FindPlugin();
        _awsStatus.Text = aws != null ? "AWS CLI v2:  Installed" : "AWS CLI v2:  NOT INSTALLED";
        _awsStatus.ForeColor = aws != null ? Good : Bad;
        _awsDetail.Text = aws ?? "Needed to talk to AWS.";
        _pluginStatus.Text = plugin != null ? "Session Manager plugin:  Installed" : "Session Manager plugin:  NOT INSTALLED";
        _pluginStatus.ForeColor = plugin != null ? Good : Bad;
        _pluginDetail.Text = plugin ?? "Needed to open the secure tunnel.";

        var tools = new List<string>();
        if (File.Exists(@"C:\Program Files\PuTTY\putty.exe") || File.Exists(@"C:\Program Files (x86)\PuTTY\putty.exe")) tools.Add("PuTTY");
        if (File.Exists(@"C:\Program Files (x86)\WinSCP\WinSCP.exe") || File.Exists(@"C:\Program Files\WinSCP\WinSCP.exe")) tools.Add("WinSCP");
        if (Directory.Exists(Path.GetDirectoryName(ToolConfigurator.MobaIniPath)!)) tools.Add("MobaXterm");
        _toolsStatus.Text = "SSH tools found on this PC: " + (tools.Count == 0 ? "none yet (sessions are still saved and appear once you install one)" : string.Join(", ", tools));

        var ok = aws != null && plugin != null;
        _install.Visible = !ok;
        if (ok && !_progress.Visible) { _installStatus.ForeColor = Good; _installStatus.Text = "Everything needed is installed. Click Next."; }
        if (_idx == 0) _next.Enabled = ok;
    }

    async Task InstallMissingAsync()
    {
        _install.Enabled = false;
        _next.Enabled = false;
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Continuous;
        _installStatus.ForeColor = SystemColors.ControlText;
        var status = new Progress<string>(s => _installStatus.Text = s);
        var percent = new Progress<int>(p =>
        {
            if (p < 0) { _progress.Style = ProgressBarStyle.Marquee; return; }
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Clamp(p, 0, 100);
        });
        try
        {
            var ok = await PrereqInstaller.InstallMissingAsync(status, percent);
            _progress.Visible = false;
            RefreshPrereqs();
            if (!ok)
            {
                _installStatus.ForeColor = Bad;
                _installStatus.Text = "Installation finished but a component was still not found. Close this window, reopen SSMConnect Setup and click Check again, or restart Windows.";
            }
            else
            {
                _installStatus.ForeColor = Good;
                _installStatus.Text = "Installed successfully. Click Next.";
            }
        }
        catch (Exception ex)
        {
            _progress.Visible = false;
            _installStatus.ForeColor = Bad;
            _installStatus.Text = "Could not install automatically: " + ex.Message +
                "\nYou can also install them manually: AWS CLI v2 (awscli.amazonaws.com/AWSCLIV2.msi) and the Session Manager plugin (docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html).";
        }
        finally
        {
            _install.Enabled = true;
            if (_idx == 0) _next.Enabled = PrereqsOk();
        }
    }

    // ---- Step 1: AWS account -----------------------------------------------------------------
    ProfileInfo? SelectedProfile => _profile.SelectedItem as ProfileInfo;

    void BuildAwsStep(Panel host)
    {
        var p = NewStep(host);
        p.Controls.Add(Para("Choose the AWS account to connect with. You can add several accounts (for example one per company or project) and come back here to switch between them. The region is chosen after the account is connected."));
        var add = new Button { Text = "Add AWS account...", AutoSize = true, MinimumSize = new Size(190, 34), Padding = new Padding(10, 2, 10, 2) };
        add.Click += (_, _) => OpenAddAccount();
        p.Controls.Add(Row(new Label { Text = "AWS account", AutoSize = true, MinimumSize = new Size(100, 0) }, _profile, add));
        p.Controls.Add(_profileInfo);

        var test = new Button { Text = "Connect and test", AutoSize = true, MinimumSize = new Size(170, 38), Padding = new Padding(10, 2, 10, 2) };
        _ssoBtn.Click += async (_, _) =>
        {
            if (SelectedProfile is not { } pr) return;
            _authResult.ForeColor = SystemColors.ControlText;
            _authResult.Text = "A browser window will open. Finish signing in there (up to 3 minutes)...";
            var r = await AwsCli.SsoLoginAsync(new AppConfig { Profile = pr.Name });
            if (!r.Ok) { _authResult.ForeColor = Bad; _authResult.Text = AwsCli.FriendlyError(r.Error); return; }
            await TestAsync();
        };
        test.Click += async (_, _) => await TestAsync();
        var buttons = Row(_ssoBtn, test);
        buttons.Margin = new Padding(0, 4, 0, 10);
        p.Controls.Add(buttons);

        p.Controls.Add(Row(new Label { Text = "Region", AutoSize = true, MinimumSize = new Size(100, 0) }, _region));
        p.Controls.Add(_regionHint);
        p.Controls.Add(_authResult);

        _profile.SelectedIndexChanged += (_, _) => OnProfileChanged();
        _region.SelectedIndexChanged += (_, _) => ClearServers();
    }

    void OpenAddAccount()
    {
        using var dlg = new ProfileDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK) ReloadProfiles(dlg.CreatedProfile);
    }

    void ReloadProfiles(string? select)
    {
        _loadingProfiles = true;
        try
        {
            _profiles = AwsProfiles.List();
            _profile.Items.Clear();
            _profile.Items.AddRange(_profiles.Cast<object>().ToArray());
            var pick = _profiles.FindIndex(x => x.Name == select);
            if (pick < 0) pick = _profiles.FindIndex(x => x.Name == _cfg.Profile);
            if (pick < 0 && _profiles.Count == 1) pick = 0;
            _profile.SelectedIndex = pick; // -1 leaves it empty so nothing is assumed
        }
        finally { _loadingProfiles = false; }
        OnProfileChanged();
    }

    void OnProfileChanged()
    {
        if (_loadingProfiles) return;
        _connected = false;
        _region.Items.Clear();
        _region.Enabled = false;
        ClearServers();
        var pr = SelectedProfile;
        _ssoBtn.Visible = pr?.Kind == ProfileKind.Sso;
        if (_profiles.Count == 0)
        {
            _profileInfo.Text = "";
            _regionHint.Text = "";
            _authResult.ForeColor = Bad;
            _authResult.Text = "No AWS accounts are set up on this PC yet. Click \"Add AWS account...\" to create one.";
            return;
        }
        if (pr == null)
        {
            _profileInfo.Text = "";
            _regionHint.Text = "";
            _authResult.ForeColor = SystemColors.ControlText;
            _authResult.Text = "Choose an account from the list, or add a new one.";
            return;
        }
        _profileInfo.Text = pr.Kind switch
        {
            ProfileKind.Sso => $"Company sign-in. {(pr.StartUrl != null ? pr.StartUrl : "")}{(pr.AccountId != null ? "  Account " + pr.AccountId : "")}{(pr.RoleName != null ? "  Role " + pr.RoleName : "")}",
            ProfileKind.AccessKeys => "Access keys saved on this PC.",
            _ => "Custom AWS profile.",
        };
        _regionHint.Text = "Click \"Connect and test\" first. The region list unlocks once the account is connected.";
        _authResult.ForeColor = SystemColors.ControlText;
        _authResult.Text = pr.Kind == ProfileKind.Sso ? "Click \"Sign in (SSO)\" to sign in, then the account is tested for you." : "Click \"Connect and test\".";
    }

    void ClearServers()
    {
        _servers = new();
        _prevServers = new();
        _autoFoundFor = "";
        _list.Items.Clear();
        _findResult.Text = "";
    }

    async Task TestAsync()
    {
        if (SelectedProfile is not { } pr) { _authResult.ForeColor = Bad; _authResult.Text = "Choose an AWS account first, or add one."; return; }
        _authResult.ForeColor = SystemColors.ControlText;
        _authResult.Text = "Testing...";
        _connected = false;
        var r = await AwsCli.WhoAmIAsync(new AppConfig { Profile = pr.Name, Region = pr.Region ?? "" });
        if (!r.Ok)
        {
            _authResult.ForeColor = Bad;
            _authResult.Text = AwsCli.FriendlyError(r.Error);
            return;
        }
        _connected = true;
        _authResult.ForeColor = Good;
        _authResult.Text = "Connected to AWS.\n" + Summarise(r.Output);

        // Only now, with the account connected, can the region be chosen.
        _regionHint.Text = "Loading regions...";
        var regions = await AwsCli.ListRegionsAsync(pr.Name);
        if (!string.IsNullOrEmpty(pr.Region) && !regions.Contains(pr.Region)) regions.Add(pr.Region);
        var keep = _cfg.Profile == pr.Name ? _cfg.Region : "";
        _region.Items.Clear();
        _region.Items.AddRange(regions.OrderBy(x => x).Cast<object>().ToArray());
        var want = !string.IsNullOrEmpty(pr.Region) ? pr.Region : keep;
        _region.SelectedItem = regions.Contains(want) ? want : null;
        _region.Enabled = true;
        _regionHint.Text = _region.SelectedItem != null
            ? "Region taken from this account's settings. Change it if your servers are elsewhere."
            : "Choose the region where your servers run.";
    }

    static string Summarise(string json)
    {
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(json);
            return "Account " + d.RootElement.GetProperty("Account").GetString() + " as " + d.RootElement.GetProperty("Arn").GetString();
        }
        catch { return json; }
    }

    void ApplyAwsFields()
    {
        _cfg.Profile = SelectedProfile?.Name ?? "";
        _cfg.Region = _region.SelectedItem as string ?? "";
    }

    // ---- Step 2: servers ---------------------------------------------------------------------
    void BuildServersStep(Panel host)
    {
        var p = NewStep(host);
        p.Controls.Add(Para("Find your servers, then tick the ones you want to connect to. Linux servers open in PuTTY, WinSCP or MobaXterm; Windows servers open in Remote Desktop (RDP). Servers marked \"No\" cannot be reached through SSM (the SSM agent or instance role is missing). The next step finds the SSH key for each Linux server."));
        var find = new Button { Text = "Find my servers", AutoSize = true, MinimumSize = new Size(190, 38), Padding = new Padding(10, 2, 10, 2) };
        p.Controls.Add(Row(find, _findResult));
        var all = new Button { Text = "Select all", AutoSize = true, MinimumSize = new Size(110, 30) };
        var none = new Button { Text = "Select none", AutoSize = true, MinimumSize = new Size(110, 30) };
        all.Click += (_, _) => SetAllChecked(true);
        none.Click += (_, _) => SetAllChecked(false);
        p.Controls.Add(Row(all, none, new Label { Text = "(Select all ticks every server that is ready; servers marked \"No\" stay unticked.)", AutoSize = true, ForeColor = Muted }));

        _list.Font = new Font("Segoe UI", 10f);
        _list.Columns.Add("Name", 220);
        _list.Columns.Add("Environment", 110);
        _list.Columns.Add("Instance ID", 150);
        _list.Columns.Add("Private IP", 110);
        _list.Columns.Add("Ready", 70);
        _list.Columns.Add("OS", 80);
        _list.Margin = new Padding(0, 0, 0, 10);
        _list.ItemCheck += (_, e) =>
        {
            if (e.Index >= 0 && e.Index < _servers.Count && !_servers[e.Index].SsmOnline)
                e.NewValue = CheckState.Unchecked;
        };
        p.Controls.Add(_list);

        _findBtn = find;
        find.Click += async (_, _) => await FindServersAsync();
    }

    Button? _findBtn;
    string _autoFoundFor = "";

    /// <summary>Lists the servers of the chosen account and region. Starts by itself when the page opens.</summary>
    async Task FindServersAsync()
    {
        ApplyAwsFields();
        if (string.IsNullOrWhiteSpace(_cfg.Profile) || string.IsNullOrWhiteSpace(_cfg.Region)) return;
        _findResult.ForeColor = SystemColors.ControlText;
        _findResult.Text = "Searching for your servers...";
        if (_findBtn != null) _findBtn.Enabled = false;
        try
        {
            _servers = await AwsCli.DiscoverAsync(_cfg);
            // remember the result; a manual scan also counts as "looked at", so pending daily-scan changes are cleared
            DiscoveryCache.Update(_cfg.Profile, _cfg.Region, _servers, _cfg.Tunnels.Select(t => t.InstanceId).ToHashSet(), recordChanges: false);
            DiscoveryCache.TakePending();
            FillServerList();
            var ready = _servers.Count(s => s.SsmOnline);
            var win = _servers.Count(s => s.SsmOnline && s.IsWindows);
            _findResult.Text = $"Account {_cfg.Profile}, {_cfg.Region}: {_servers.Count} running, {ready} ready ({ready - win} Linux over SSH, {win} Windows over RDP).";
        }
        catch (Exception ex)
        {
            _findResult.ForeColor = Bad;
            _findResult.Text = ex.Message;
        }
        finally { if (_findBtn != null) _findBtn.Enabled = true; }
    }

    /// <summary>Shows the servers from the last scan of this account straight away, without calling AWS.</summary>
    void LoadCachedServers()
    {
        if (_servers.Count > 0 || string.IsNullOrWhiteSpace(_cfg.Profile) || string.IsNullOrWhiteSpace(_cfg.Region)) return;
        var rec = DiscoveryCache.Get(_cfg.Profile, _cfg.Region);
        if (rec == null || rec.Servers.Count == 0) return;
        _servers = rec.Servers;
        FillServerList();
        var ready = _servers.Count(s => s.SsmOnline);
        _findResult.ForeColor = Muted;
        _findResult.Text = $"Showing the {_servers.Count} server(s) found on {rec.ScannedUtc.ToLocalTime():g}. Click \"Find my servers\" to refresh now ({ready} were ready).";
    }

    bool _deepScanned;
    int _missingKeys;
    readonly Dictionary<string, string> _manualKeys = new(StringComparer.OrdinalIgnoreCase);
    static string ManualId(DiscoveredServer s) => string.IsNullOrWhiteSpace(s.KeyName) ? s.InstanceId : "k:" + s.KeyName;

    void FillServerList()
    {
        var ticked = _list.CheckedItems.Cast<ListViewItem>().Where(i => i.Index < _prevServers.Count).Select(i => _prevServers[i.Index].InstanceId).ToHashSet();
        _prevServers = _servers.ToList();
        _list.Items.Clear();
        foreach (var s in _servers)
        {
            var item = new ListViewItem(s.Name + (s.IsProd ? "  [PROD]" : "")) { UseItemStyleForSubItems = false };
            item.SubItems.AddRange(new[] { s.Environment, s.InstanceId, s.PrivateIp, s.SsmOnline ? "Yes" : "No", s.Platform });
            var rowColor = !s.SsmOnline ? Color.FromArgb(110, 110, 110) : s.IsProd ? Bad : SystemColors.ControlText;
            foreach (ListViewItem.ListViewSubItem sub in item.SubItems) sub.ForeColor = rowColor;
            var osCell = item.SubItems[5];
            osCell.Text = s.IsWindows ? "Windows" : "Linux";
            if (s.SsmOnline) { osCell.BackColor = s.IsWindows ? AppTheme.WinBg : AppTheme.LinuxBg; osCell.ForeColor = s.IsWindows ? AppTheme.WinFg : AppTheme.LinuxFg; }
            _list.Items.Add(item);
        }
        foreach (ListViewItem item in _list.Items)
            item.Checked = ticked.Contains(_servers[item.Index].InstanceId) || _cfg.Tunnels.Any(t => t.InstanceId == _servers[item.Index].InstanceId && t.Profile == _cfg.Profile && t.Region == _cfg.Region);
    }

    List<DiscoveredServer> _prevServers = new();

    void SetAllChecked(bool on)
    {
        for (var i = 0; i < _list.Items.Count && i < _servers.Count; i++)
            _list.Items[i].Checked = on && _servers[i].SsmOnline;
    }

    // ---- Step 4: match SSH keys --------------------------------------------------------------
    void BuildKeysStep(Panel host)
    {
        var p = NewStep(host);
        p.Controls.Add(Para("Each Linux server needs its SSH key. We look for them for you; if one is missing, click Browse on that row.", 6));
        _keySummary.Font = AppTheme.Semi(10f);
        _keySummary.Margin = new Padding(0, 0, 0, 10);
        p.Controls.Add(_keySummary);

        AppTheme.StyleGrid(_keyGrid);
        _keyGrid.Height = 290;
        _keyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Server", ReadOnly = true, Width = 230 });
        _keyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Login user", Width = 100 });
        _keyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _keyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Match", ReadOnly = true, Width = 110 });
        _keyGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "Browse...", UseColumnTextForButtonValue = true, Width = 96, FlatStyle = FlatStyle.Flat });
        _keyGrid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 4) BrowseKeyForRow(e.RowIndex); };
        _keyGrid.Margin = new Padding(0, 0, 0, 10);
        p.Controls.Add(_keyGrid);

        var scan = new Button { Text = "Search this PC", AutoSize = true, MinimumSize = new Size(130, 30), Padding = new Padding(8, 2, 8, 2) };
        scan.Click += async (_, _) => await ScanKeysAsync(scan, allDrives: true);
        var folder = new Button { Text = "Choose key folder...", AutoSize = true, MinimumSize = new Size(150, 30), Padding = new Padding(8, 2, 8, 2) };
        folder.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Choose the folder that holds your server key files (.pem or .ppk). Sub-folders are searched too.", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            await ScanKeysAsync(folder, allDrives: false, extraFolder: dlg.SelectedPath);
        };
        _keyStatus.Font = AppTheme.Body(8.5f);
        p.Controls.Add(Row(scan, folder, _keyStatus));
    }

    /// <summary>Opening this step: list the ticked Linux servers and find their keys on its own.</summary>
    void EnterKeysStep()
    {
        _keyServers = _list.CheckedItems.Cast<ListViewItem>().Select(i => _servers[i.Index]).Where(x => !x.IsWindows).ToList();
        _keyGrid.Rows.Clear();
        foreach (var s in _keyServers)
        {
            var existing = _cfg.Tunnels.FirstOrDefault(t => t.InstanceId == s.InstanceId && t.Profile == _cfg.Profile && t.Region == _cfg.Region);
            var user = existing != null && !string.IsNullOrWhiteSpace(existing.UserName) && existing.UserName != "ec2-user" ? existing.UserName : s.SuggestedUser;
            _keyGrid.Rows.Add(s.Name, user, "", "");
        }
        var windows = _list.CheckedItems.Count - _keyServers.Count;
        if (_keyServers.Count == 0)
        {
            _keyStatus.ForeColor = Muted;
            _keyStatus.Text = windows > 0 ? "The servers you ticked are Windows servers. They use Remote Desktop with a password, so no key is needed. Click Next." : "No servers are ticked. Go back and tick the servers you want.";
            _keySummary.Text = "";
            return;
        }
        _keySummary.Text = "";
        _ = EnsureKeysAsync();
    }

    /// <summary>Usual folders first; if some servers are still without a key, one wider search of this PC, so the person rarely has to do anything.</summary>
    async Task EnsureKeysAsync()
    {
        if (!_keysScanned) await ScanKeysAsync(null, allDrives: false); else AssignKeys();
        if (!_deepScanned && _missingKeys > 0)
        {
            _deepScanned = true;
            await ScanKeysAsync(null, allDrives: true);
        }
    }

    async Task ScanKeysAsync(Button? button, bool allDrives, string? extraFolder = null)
    {
        if (button != null) button.Enabled = false;
        _keyStatus.ForeColor = Muted;
        _keyStatus.Text = "Looking for keys...";
        try
        {
            var found = await Task.Run(() => extraFolder != null
                ? KeyFinder.ScanFolder(extraFolder, TimeSpan.FromSeconds(60))
                : KeyFinder.ScanStandard(allDrives, TimeSpan.FromSeconds(allDrives ? 30 : 8)));
            _keysScanned = true;
            _keys = _keys.Concat(found).GroupBy(k => k.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            AssignKeys();
            _keyStatus.Text = _keys.Count == 0
                ? "No keys found yet. Choose the folder where you keep them."
                : $"{_keys.Count} key file(s) found.";
        }
        catch (Exception ex) { _keyStatus.ForeColor = Bad; _keyStatus.Text = "Key search failed: " + ex.Message; }
        finally { if (button != null) button.Enabled = true; }
    }

    void BrowseKeyForRow(int i)
    {
        if (i < 0 || i >= _keyServers.Count) return;
        var s = _keyServers[i];
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the key file for " + s.Name + (string.IsNullOrWhiteSpace(s.KeyName) ? "" : "  (AWS key pair: " + s.KeyName + ")"),
            Filter = "Key files (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var prints = KeyFingerprint.Compute(dlg.FileName);
        if (prints.Count > 0 && !string.IsNullOrWhiteSpace(s.KeyFingerprint) && !prints.Contains(s.KeyFingerprint) &&
            MessageBox.Show(this, "This key does not look like the one this server was created with" + (string.IsNullOrWhiteSpace(s.KeyName) ? "" : " (AWS key pair \"" + s.KeyName + "\")") + ", so signing in will probably fail.\n\nUse it anyway?",
                            "Different key?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _manualKeys[ManualId(s)] = dlg.FileName;
        AssignKeys();
    }

    /// <summary>Fills "Key File" and "Match" for every server in the grid and says in one line how many are ready.</summary>
    void AssignKeys()
    {
        var ok = 0;
        for (var i = 0; i < _keyGrid.Rows.Count && i < _keyServers.Count; i++)
        {
            var s = _keyServers[i];
            var row = _keyGrid.Rows[i];
            string? path = null; string how = "";
            if (_manualKeys.TryGetValue(ManualId(s), out var mk) && File.Exists(mk))
            {
                path = mk; how = "Chosen";
                var pr = KeyFingerprint.Compute(mk);
                if (pr.Count > 0 && !string.IsNullOrWhiteSpace(s.KeyFingerprint) && !pr.Contains(s.KeyFingerprint)) how = "Different?";
            }
            else
            {
                var (m, h) = KeyFinder.Find(_keys, s.KeyName, s.KeyFingerprint);
                if (m != null) { path = m.Path; how = h.StartsWith("verified") ? "Verified" : h.StartsWith("similar") ? "Check name" : "By name"; }
            }
            var existing = _cfg.Tunnels.FirstOrDefault(t => t.InstanceId == s.InstanceId && t.Profile == _cfg.Profile && t.Region == _cfg.Region)?.KeyFile;
            if (path == null && !string.IsNullOrWhiteSpace(existing) && File.Exists(existing)) { path = existing; how = "Saved"; }

            var good = path != null && how is "Verified" or "By name" or "Chosen" or "Saved";
            row.Cells[2].Value = path == null ? "Not found" : Path.GetFileName(path);
            row.Cells[2].Tag = path;                       // full path is kept here; the cell shows just the file name
            row.Cells[2].ToolTipText = path ?? "";
            row.Cells[3].Value = path == null ? "Missing" : how == "Verified" ? "✓ Verified" : how;
            var colour = path == null ? Bad : good ? Good : Warn;
            row.Cells[2].Style.ForeColor = colour;
            row.Cells[3].Style.ForeColor = colour;
            if (path != null) ok++;
        }
        _missingKeys = _keyServers.Count - ok;
        if (_keyServers.Count == 0) return;
        if (_missingKeys == 0)
        {
            _keySummary.ForeColor = Good;
            _keySummary.Text = _keyServers.Count == 1 ? "All set. Your server has its key." : $"All set. All {_keyServers.Count} servers have their key.";
        }
        else
        {
            _keySummary.ForeColor = Warn;
            _keySummary.Text = $"{_missingKeys} of {_keyServers.Count} servers still need a key. Click Browse on those rows.";
        }
    }

    string? KeyPathFor(DiscoveredServer s)
    {
        var i = _keyServers.IndexOf(s);
        var v = i >= 0 && i < _keyGrid.Rows.Count ? _keyGrid.Rows[i].Cells[2].Tag as string : null;
        return !string.IsNullOrWhiteSpace(v) && File.Exists(v) ? v : null;
    }

    string? UserFor(DiscoveredServer s)
    {
        var i = _keyServers.IndexOf(s);
        var v = i >= 0 && i < _keyGrid.Rows.Count ? _keyGrid.Rows[i].Cells[1].Value as string : null;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    // ---- Step 5: apply -----------------------------------------------------------------------
    readonly Label _applyStatus = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11.5f), MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(0, 0, 0, 6) };
    readonly ProgressBar _applyBar = new() { Width = ContentWidth, Height = 10, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 0, 0, 10) };
    readonly Button _applyBtn = new() { Text = "Start now", AutoSize = true, MinimumSize = new Size(150, 38), Padding = new Padding(10, 2, 10, 2) };
    readonly Button _cancelBtn = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(120, 38), Padding = new Padding(10, 2, 10, 2) };
    CancellationTokenSource? _applyCts;
    bool _applying;

    void BuildApplyStep(Panel host)
    {
        var p = NewStep(host);
        p.Controls.Add(Para("Your connections are saved automatically for the servers you ticked. Your own sessions are never touched; ours start with \"SSC - \". Press Cancel if you want to stop."));
        p.Controls.Add(Row(_putty, _winscp, _moba));
        p.Controls.Add(_toolsNote);
        p.Controls.Add(_applyStatus);
        p.Controls.Add(_applyBar);
        p.Controls.Add(Row(_applyBtn, _cancelBtn));
        _cancelBtn.Visible = false;
        _applyBtn.Click += async (_, _) => { if (_applying) _skipCountdown = true; else await RunApplyFlowAsync(countdown: false); };
        _cancelBtn.Click += (_, _) => _applyCts?.Cancel();
        _log.Margin = new Padding(0, 4, 0, 0);
        _log.Height = 230;
        p.Controls.Add(_log);
    }

    bool _skipCountdown;

    /// <summary>Starts by itself a few seconds after the page opens; Cancel stops it before anything is changed.</summary>
    async Task RunApplyFlowAsync(bool countdown)
    {
        if (_applying) return;
        _applying = true;
        _applyCts = new CancellationTokenSource();
        var ct = _applyCts.Token;
        _skipCountdown = false;
        _cancelBtn.Visible = true;
        _applyBtn.Text = "Start now";
        _applyBtn.Enabled = countdown;
        _next.Enabled = false; _back.Enabled = false;
        _applyBar.Value = 0;
        try
        {
            if (countdown)
            {
                for (var n = 5; n > 0 && !_skipCountdown; n--)
                {
                    _applyStatus.ForeColor = AppTheme.Teal700;
                    _applyStatus.Text = $"Saving your connections in {n}...";
                    _applyBar.Value = (5 - n) * 20;
                    for (var i = 0; i < 10 && !_skipCountdown; i++)
                    {
                        if (ct.IsCancellationRequested || IsDisposed) break;
                        await Task.Delay(100);
                    }
                    if (ct.IsCancellationRequested || IsDisposed) break;
                }
            }
            if (ct.IsCancellationRequested)
            {
                _applyStatus.ForeColor = Warn;
                _applyStatus.Text = "Cancelled. Nothing was changed.";
                Log("Cancelled before anything was saved. Press \"Run again\" when you are ready.");
                return;
            }
            _applyBtn.Enabled = false;
            _applyBar.Value = 100;
            _applyStatus.ForeColor = AppTheme.Teal700;
            _applyStatus.Text = "Saving your connections...";
            var ok = await ApplyAsync(ct);
            if (ct.IsCancellationRequested) { _applyStatus.ForeColor = Warn; _applyStatus.Text = "Stopped waiting. Your connections are saved and keep starting in the background."; }
            else if (ok) { _applyStatus.ForeColor = Good; _applyStatus.Text = "All done. Your connections are saved."; }
            else { _applyStatus.ForeColor = Bad; _applyStatus.Text = "Something needs your attention. See the details below."; }
        }
        finally
        {
            _applying = false;
            _cancelBtn.Visible = false;
            _applyBtn.Text = "Run again";
            _applyBtn.Enabled = true;
            _next.Enabled = true; _back.Enabled = true;
            AppTheme.StyleButton(_applyBtn, true);
        }
    }

    readonly Label _toolsNote = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 8) };

    /// <summary>Only tools that are really installed on this PC are ticked; the others are greyed out.</summary>
    void RefreshToolChoices()
    {
        void Set(CheckBox box, string name, bool installed, string extra = "")
        {
            box.Enabled = installed;
            box.Checked = installed;
            box.Text = installed ? name + extra : name + " (not installed)";
        }
        Set(_putty, "PuTTY", ToolDetector.PuttyInstalled);
        Set(_winscp, "WinSCP", ToolDetector.WinScpInstalled);
        Set(_moba, "MobaXterm", ToolDetector.MobaXtermInstalled, " (close it first)");
        _toolsNote.Text = !_putty.Enabled && !_winscp.Enabled && !_moba.Enabled
            ? "None of PuTTY, WinSCP or MobaXterm was found on this PC. Install one of them, then run setup again. Your connections are still saved and will start with SSMConnect."
            : "Only the tools found on this PC are ticked.";
    }

    void Log(string line) => _log.AppendText(line + Environment.NewLine);

    async Task<bool> ApplyAsync(CancellationToken ct)
    {
        var chosen = _list.CheckedItems.Cast<ListViewItem>().Select(i => _servers[i.Index]).ToList();
        if (chosen.Count == 0) { MessageBox.Show(this, "Tick at least one server on the previous page.", "SSMConnect"); return false; }
        var prod = chosen.Where(s => s.IsProd).Select(s => s.Name).ToList();
        if (prod.Count > 0 &&
            MessageBox.Show(this, "These are PRODUCTION servers:\n\n" + string.Join("\n", prod) + "\n\nSet up connections to them?",
                            "Production servers", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { Log("Cancelled at the production-server question. Nothing was changed."); return false; }

        try
        {
            ApplyAwsFields();
            // Servers of other accounts / regions stay as they are; only this account + region is replaced.
            var kept = _cfg.Tunnels.Where(t => !(t.Profile == _cfg.Profile && t.Region == _cfg.Region)).ToList();
            var used = new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(l => l.Port));
            foreach (var k in kept) used.Add(k.LocalPort);
            var tunnels = new List<TunnelConfig>();
            foreach (var s in chosen)
            {
                var existing = _cfg.Tunnels.FirstOrDefault(t => t.InstanceId == s.InstanceId && t.Profile == _cfg.Profile && t.Region == _cfg.Region);
                var port = existing?.LocalPort ?? NextFreePort(used, tunnels, s.IsWindows ? 3390 : 2201);
                var typed = s.IsWindows ? "Administrator" : UserFor(s) ?? s.SuggestedUser;
                var keyFile = s.IsWindows ? null : KeyPathFor(s) ?? existing?.KeyFile;
                // two accounts can have a server with the same name: keep the saved session names unique
                var label = kept.Any(k => k.Name == s.Name) || tunnels.Any(k => k.Name == s.Name) ? $"{s.Name} ({_cfg.Profile})" : s.Name;
                tunnels.Add(new TunnelConfig
                {
                    Profile = _cfg.Profile, Region = _cfg.Region,
                    Name = existing?.Name ?? label, InstanceId = s.InstanceId, LocalPort = port, RemotePort = s.IsWindows ? 3389 : 22, Protocol = s.IsWindows ? "rdp" : "ssh",
                    UserName = s.IsWindows ? (existing?.UserName ?? typed) : typed,
                    KeyFile = keyFile ?? (s.IsWindows ? existing?.KeyFile : null),
                });
            }
            if (ConfigValidator.ValidateGlobal(_cfg) is { } globalProblem) { Log("Error: " + globalProblem); return false; }
            var all = kept.Concat(tunnels).ToList();
            foreach (var t in all)
                if (ConfigValidator.ValidateTunnel(t, all) is { } problem) { Log($"Error ({t.Name}): {problem}"); return false; }
            _cfg.Tunnels = all;
            ConfigStore.Save(_cfg);
            foreach (var t in tunnels.Where(x => !x.IsRdp))
                Log($"  {ToolConfigurator.SessionName(t)}: user '{t.UserName}', key " + (string.IsNullOrWhiteSpace(t.KeyFile) ? "NOT SET - go Back to step 4 (Match SSH keys), use Browse for this server, then Apply again" : t.KeyFile));
            Log($"Saved {tunnels.Count} connection(s) for account {_cfg.Profile} ({_cfg.Region}); {all.Count} in total. Settings file: {ConfigStore.ConfigPath}");

            ToolConfigurator.RemoveExcept(all); // drop sessions for servers no longer chosen; keep (and update) the rest
            if (_putty.Checked)
            {
                ToolConfigurator.ConfigurePutty(all);
                Log("PuTTY sessions saved.");
                var unconverted = all.Where(t => !t.IsRdp && !string.IsNullOrWhiteSpace(t.KeyFile) && KeyConverter.EnsurePpk(t.KeyFile!) == null).Select(t => t.Name).ToList();
                if (unconverted.Count > 0)
                    Log("Note: these keys could not be used by PuTTY / WinSCP (a password-protected or non-RSA key): " + string.Join(", ", unconverted) + ". Pick a .ppk file for them in step 4.");
            }
            if (_winscp.Checked) { ToolConfigurator.ConfigureWinScp(all); Log("WinSCP sessions saved."); }
            if (_moba.Checked)
            {
                if (Process.GetProcessesByName("MobaXterm").Length > 0) Log("MobaXterm is running - close it and run Apply again to add sessions.");
                else Log(ToolConfigurator.ConfigureMobaXterm(all) ? "MobaXterm sessions saved (backup: MobaXterm.ini.ssmconnect.bak)." : "MobaXterm not found - skipped.");
            }

            var live = StatusStore.Load();
            if (live == null || DateTime.UtcNow - live.UpdatedUtc > TimeSpan.FromSeconds(15))
            {
                Log("Connections are stopped. Start them from the tray icon or the dashboard's Connections menu, then open your session.");
                Log("Done.");
                return true;
            }

            Log("Waiting for tunnels to come up...");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            var reported = new HashSet<string>();
            while (DateTime.UtcNow < deadline && reported.Count < tunnels.Count && !ct.IsCancellationRequested)
            {
                var st = StatusStore.Load();
                foreach (var t in tunnels)
                {
                    if (reported.Contains(t.Name)) continue;
                    var ts = st?.Tunnels.FirstOrDefault(x => x.Name == t.Name);
                    if (ts == null) continue;
                    if (ts.State == TunnelStates.Connected && IsListening(t.LocalPort))
                    {
                        Log(t.IsRdp ? $"  [UP]    {t.Name}  ->  open it from the dashboard: Open Remote Desktop" : $"  [UP]    {t.Name}  ->  open saved session '{ToolConfigurator.SessionName(t)}'");
                        reported.Add(t.Name);
                    }
                    else if (ts.State == TunnelStates.Error)
                    {
                        Log($"  [ERROR] {t.Name}: {ts.Message}");
                        reported.Add(t.Name);
                    }
                }
                if (reported.Count < tunnels.Count) { try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { } }
            }
            foreach (var t in tunnels.Where(t => !reported.Contains(t.Name)))
                Log($"  [WAIT]  {t.Name}: still connecting. The dashboard turns green when it is ready.");
            Log(ct.IsCancellationRequested ? "Stopped waiting. The connections keep starting in the background." : "Done.");
            return true;
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
            return false;
        }
    }

    static int NextFreePort(HashSet<int> used, List<TunnelConfig> planned, int start)
    {
        var port = start;
        while (used.Contains(port) || planned.Any(t => t.LocalPort == port)) port++;
        return port;
    }

    static bool IsListening(int port)
    {
        try { return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port); }
        catch { return false; }
    }

    // ---- Navigation --------------------------------------------------------------------------
    static readonly string[] Titles =
    {
        "Step 1 of 5 - Check requirements",
        "Step 2 of 5 - Connect to AWS",
        "Step 3 of 5 - Pick your servers",
        "Step 4 of 5 - Match SSH keys",
        "Step 5 of 5 - Save connections",
    };

    static readonly string[] Subtitles =
    {
        "Checking the AWS tools SSMConnect relies on.",
        "Choose the AWS account and region where your servers run.",
        "Tick the servers you want to connect to.",
        "SSMConnect finds the SSH key for each Linux server.",
        "Saving your connections to your SSH and file-transfer tools.",
    };

    void OnNext()
    {
        if (_idx == _steps.Count - 1) { Close(); return; }
        if (_idx == 1)
        {
            if (SelectedProfile is not { } pr) { _authResult.ForeColor = Bad; _authResult.Text = "Choose an AWS account first, or click \"Add AWS account...\"."; return; }
            if (!_connected) { _authResult.ForeColor = Bad; _authResult.Text = "Click \"Connect and test\" so we know the account works."; return; }
            if (_region.SelectedItem is not string) { _authResult.ForeColor = Bad; _authResult.Text = "Choose the region where your servers run."; return; }
            ApplyAwsFields();
            // remember the region on the AWS profile too, so other AWS tools agree with SSMConnect
            if (!string.Equals(pr.Region, _cfg.Region, StringComparison.Ordinal)) { try { AwsProfiles.SaveRegion(pr.Name, _cfg.Region); } catch { } }
        }
        if (_idx == 2 && _list.CheckedItems.Count == 0) { MessageBox.Show(this, "Tick at least one server first.", "SSMConnect"); return; }
        if (_idx == 3)
        {
            _keyGrid.EndEdit();
            if (_keyServers.Any(x => UserFor(x) == null)) { MessageBox.Show(this, "Every server needs an SSH user (for example ubuntu or ec2-user).", "SSMConnect"); return; }
            var noKey = _keyServers.Where(x => KeyPathFor(x) == null).Select(x => x.Name).ToList();
            if (noKey.Count > 0 &&
                MessageBox.Show(this, "These servers do not have a key file yet:\n\n" + string.Join("\n", noKey.Take(12)) +
                                      "\n\nThey can still be saved, but PuTTY / WinSCP will ask for the key when you connect. Use Browse next to a server to add its key.\n\nContinue without a key for them?",
                                "Keys missing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        Go(_idx + 1);
    }

    async void Go(int index)
    {
        _idx = Math.Clamp(index, 0, _steps.Count - 1);
        for (var i = 0; i < _steps.Count; i++) _steps[i].Visible = i == _idx;
        _title.Text = Titles[_idx];
        _hdr.Set(Titles[_idx][(Titles[_idx].IndexOf(" - ", StringComparison.Ordinal) + 3)..], $"Step {_idx + 1} of 5   |   {Subtitles[_idx]}", _idx);
        _back.Enabled = _idx > 0;
        _next.Text = _idx == _steps.Count - 1 ? "Finish" : "Next >";
        _next.Enabled = true;
        if (_idx == 0) RefreshPrereqs();
        if (_idx == 2)
        {
            LoadCachedServers();
            var key = _cfg.Profile + "|" + _cfg.Region;
            if (_autoFoundFor != key) { _autoFoundFor = key; _ = FindServersAsync(); } // no button to press: it starts by itself
        }
        if (_idx == 3) EnterKeysStep();
        if (_idx != 4) _applyCts?.Cancel();
        if (_idx == 4) { RefreshToolChoices(); _applyBtn.Text = "Start now"; _applyStatus.Text = ""; _log.Clear(); _ = RunApplyFlowAsync(countdown: true); }
        if (_idx == 1 && !_profilesLoaded)
        {
            _profilesLoaded = true;
            ReloadProfiles(_cfg.Profile);
            if (_profiles.Count == 0)
            {
                await Task.Yield();
                MessageBox.Show(this, "No AWS accounts are set up on this PC yet.\n\nLet's add one now. You will need either your company's sign-in URL, or an access key from the AWS console.", "Add an AWS account", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenAddAccount();
            }
        }
    }
}
