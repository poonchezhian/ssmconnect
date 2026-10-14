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

using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using SSMConnect.Core;

namespace SSMConnect.Wizard;

/// <summary>
/// Main window and tray icon. Shows every configured connection live (green = connected, amber = connecting,
/// red = problem with the reason, grey = off), and offers start/stop of the connections, shortcuts, setup and uninstall.
/// The window hides to the system tray when closed; the tunnels keep running while SSMConnect is open (tray icon).
/// </summary>
public sealed class DashboardForm : Form
{
    enum Level { Grey, Red, Amber, Green }

    sealed record Row(string Name, string InstanceId, int Port, string StatusText, string Message, DateTime? Since, Level Level);

    sealed class BufferedListView : ListView
    {
        public BufferedListView() { DoubleBuffered = true; }
    }

    static string RepoUrl => $"https://github.com/{UpdateChecker.Repo}";
    const string BlogUrl = "https://medium.com/@poonchezhian";

    static readonly Color GoodBg = Color.FromArgb(206, 238, 214), GoodFg = Color.FromArgb(0, 85, 35);
    static readonly Color WarnBg = Color.FromArgb(255, 234, 186), WarnFg = Color.FromArgb(115, 65, 0);
    static readonly Color BadBg = Color.FromArgb(250, 214, 214), BadFg = Color.FromArgb(145, 0, 20);
    static readonly Color GreyBg = Color.FromArgb(228, 228, 228), GreyFg = Color.FromArgb(55, 55, 55);

    readonly HeaderBand _hdr = new() { Dock = DockStyle.Top, Height = 84 };
    readonly bool _startHidden;
    bool _allowShow, _reallyExit, _hintShown;
    readonly TunnelManager _mgr = new();
    readonly string? _loadWarning;
    AppConfig _cfg;
    DateTime _cfgStamp = DateTime.MinValue;

    // UI
    readonly Label _svcPill = NewPill(), _authPill = NewPill(), _reqPill = NewPill();
    sealed class CardFlow : FlowLayoutPanel
    {
        public CardFlow() { DoubleBuffered = true; }
    }
    readonly CardFlow _cardHost = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = AppTheme.Page, Padding = new Padding(0, 4, 0, 0) };
    readonly List<ServerCard> _cards = new();
    readonly List<Label> _groupHeads = new();
    // account switcher (top right of the header)
    sealed record AcctItem(string Text, string? Key, bool IsAdd)
    {
        public override string ToString() => Text;
    }
    readonly ComboBox _acct = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, FlatStyle = FlatStyle.Flat, BackColor = Color.White, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28 };
    string? _view;              // primary account (sign-in) whose servers are shown
    bool _viewChosen;           // false until the first fill picks the current account
    string _acctSig = "";
    List<ProfileInfo> _profList = new();
    DateTime _profStamp = DateTime.MinValue;
    List<string> _rowGroups = new();   // account (profile) of each row, same order as _rows
    int _selected = -1;
    readonly Button _btnStart = new() { Text = "Start" }, _btnStop = new() { Text = "Stop" }, _btnRestart = new() { Text = "Restart" }, _btnAdd = new() { Text = "Add / change servers..." };
    // update banner
    readonly Panel _updateBar = new() { Dock = DockStyle.Top, Height = 46, Visible = false, BackColor = Color.FromArgb(224, 242, 241), Padding = new Padding(14, 0, 14, 0) };
    readonly Label _updateText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(4, 70, 66) };
    readonly Button _updateNow = new() { Text = "Update now" }, _updateNotes = new() { Text = "What's new" }, _updateLater = new() { Text = "Later" };
    UpdateInfo? _update;
    bool _updating;
    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60 * 60 * 1000 };
    string? _puttyPath, _winscpPath;
    DateTime _toolsChecked = DateTime.MinValue;
    readonly Label _summary = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 0, 0, 8) };
    readonly Label _detail = new() { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 8, 0, 0), ForeColor = Color.FromArgb(40, 40, 40) };
    readonly ToolTip _tips = new() { AutoPopDelay = 15000 };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    readonly NotifyIcon _tray = new();
    readonly Dictionary<Level, Icon> _icons = new();
    readonly ToolStripMenuItem _trayStart = new("Start connections"), _trayStop = new("Stop connections"), _trayRestart = new("Restart connections");
    readonly ToolStripMenuItem _mStart = new("Start connections"), _mStop = new("Stop connections"), _mRestart = new("Restart connections");
    readonly ToolStripMenuItem _trayHeader = new("SSMConnect") { Enabled = false };
    FileSystemWatcher? _watcher;
    readonly System.Windows.Forms.Timer _scanTimer = new() { Interval = 30 * 60 * 1000 };
    bool _scanning, _balloonIsChanges;

    // state
    List<Row> _rows = new();
    Level _overall = Level.Grey;
    Level? _prevOverall;
    string _authText = "AWS sign-in: checking...";
    string _authTip = "";
    Level _authLevel = Level.Grey;
    bool _authBusy, _busy;
    List<string> _authFailed = new();
    DateTime _nextAuthCheck = DateTime.MinValue;

    public DashboardForm(bool startHidden)
    {
        _startHidden = startHidden;
        _cfg = ConfigStore.LoadOrRecover(out _loadWarning);
        try { ToolConfigurator.RestoreMissingSessions(_cfg.Tunnels); } catch { }   // settings kept from an earlier install
        _allowShow = !startHidden;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "SSMConnect Dashboard";
        Font = AppTheme.Body();
        ClientSize = new Size(980, 640);
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { }

        BuildLayout();
        BuildMenu();
        BuildTray();

        _ = Handle; // create the window handle now so background threads can marshal to the UI even while hidden
        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        StartStatusWatcher();
        _mgr.Changed += () => { try { BeginInvoke(RefreshAll); } catch { } };
        _mgr.Start();
        StartServerWatch();
        StartUpdateWatch();
        Shown += async (_, _) => { LayoutCards(); if (_loadWarning != null) MessageBox.Show(this, _loadWarning, "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Warning); await FirstRunAsync(); };
        FormClosing += OnFormClosing;
        FormClosed += (_, _) => { _timer.Stop(); _scanTimer.Stop(); _mgr.Stop(); _watcher?.Dispose(); _tray.Visible = false; _tray.Dispose(); };
        AppTheme.Style(this);
        RefreshAll();
    }

    // ---- window visibility -------------------------------------------------------------------
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && _allowShow);

    public void ShowDashboard()
    {
        _allowShow = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void ShowFromAnotherInstance() => ShowDashboard();

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit || e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
        if (!_hintShown)
        {
            _hintShown = true;
            Balloon(5000, "SSMConnect is still running", "Your connections stay up while SSMConnect runs. Click the tray icon to open the dashboard again.", ToolTipIcon.Info);
        }
    }

    // ---- layout ------------------------------------------------------------------------------
    static Label NewPill() => new()
    {
        AutoSize = true,
        Padding = new Padding(12, 6, 12, 6),
        Margin = new Padding(0, 0, 10, 0),
        Font = new Font("Segoe UI Semibold", 9.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    void BuildLayout()
    {
        var pills = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 0, 0, 10) };
        pills.Controls.AddRange(new Control[] { _svcPill, _authPill, _reqPill });

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        top.Controls.Add(pills);
        top.Controls.Add(_summary);

        var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = AppTheme.Page };
        _detail.AutoSize = false; _detail.Dock = DockStyle.Fill; _detail.TextAlign = ContentAlignment.MiddleLeft; _detail.MaximumSize = Size.Empty; _detail.Margin = Padding.Empty;
        detailHost.Controls.Add(_detail);

        // one bottom bar: the three connection controls share one size, the main action sits on the right
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };
        bar.Paint += (_, e) => { using var p = new Pen(AppTheme.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        foreach (var b in new[] { _btnStart, _btnStop, _btnRestart, _btnAdd }) { b.AutoSize = false; b.Height = 38; bar.Controls.Add(b); }
        _btnStart.Width = _btnStop.Width = _btnRestart.Width = 120; _btnAdd.Width = 230;
        _btnStart.Click += async (_, _) => await ServiceActionAsync("start");
        _btnStop.Click += async (_, _) => await ServiceActionAsync("stop");
        _btnRestart.Click += async (_, _) => await ServiceActionAsync("restart");
        _btnAdd.Click += (_, _) => OpenSetup();
        _btnAdd.Tag = "primary";
        void LayoutBar()
        {
            var f = DeviceDpi / 96f;
            int y = (bar.Height - _btnStart.Height) / 2, gap = (int)(10 * f), x = (int)(22 * f);
            foreach (var b in new[] { _btnStart, _btnStop, _btnRestart }) { b.Location = new Point(x, y); x += b.Width + gap; }
            _btnAdd.Location = new Point(bar.Width - (int)(22 * f) - _btnAdd.Width, y);
        }
        bar.Resize += (_, _) => LayoutBar();
        bar.HandleCreated += (_, _) => LayoutBar();

        // update banner
        _updateBar.Controls.Add(_updateText);
        var ub = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        foreach (var b in new[] { _updateNow, _updateNotes, _updateLater }) { b.AutoSize = true; b.MinimumSize = new Size(100, 32); b.Margin = new Padding(8, 0, 0, 0); b.Padding = new Padding(8, 0, 8, 0); ub.Controls.Add(b); }
        _updateBar.Controls.Add(ub);
        _updateNow.Tag = "primary";
        _updateNow.Click += async (_, _) => await InstallUpdateAsync();
        _updateNotes.Click += (_, _) => { if (_update != null) OpenUrl(_update.PageUrl); };
        _updateLater.Click += (_, _) => _updateBar.Visible = false;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 12, 22, 4) };
        host.Controls.Add(_cardHost);   // Fill first (back), then edge-docked panels
        host.Controls.Add(detailHost);
        host.Controls.Add(top);
        BackColor = AppTheme.Page;
        host.BackColor = AppTheme.Page;
        Controls.Add(host);
        Controls.Add(bar);
        Controls.Add(_updateBar);
        Controls.Add(_hdr);
        host.BringToFront();
        _cardHost.BringToFront();
        _hdr.Controls.Add(_acct);
        void PlaceAcct() { var f = DeviceDpi / 96f; _acct.Width = (int)(270 * f); _acct.Left = _hdr.Width - _acct.Width - (int)(24 * f); _acct.Top = (_hdr.Height - _acct.Height) / 2; }
        _hdr.Resize += (_, _) => PlaceAcct();
        _hdr.HandleCreated += (_, _) => PlaceAcct();
        _acct.SelectionChangeCommitted += (_, _) => OnAcctPicked();
        _acct.DrawItem += DrawAcctItem;
        _hdr.Set("SSMConnect", "Your AWS servers, one click away. Secure SSH and Remote Desktop through SSM, with no open ports.");
        _cardHost.SizeChanged += (_, _) => LayoutCards();
    }

    void LayoutCards()
    {
        var f = DeviceDpi / 96f;
        var avail = _cardHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
        var gap = (int)(12 * f);
        var cols = Math.Max(1, avail / (int)(350 * f));
        var w = Math.Max((int)(300 * f), (avail - gap * (cols - 1)) / cols);
        _cardHost.SuspendLayout();
        foreach (var c in _cards) { c.Width = w; c.Height = ServerCard.HeightFor(f); c.Margin = new Padding(0, 0, gap, gap); }
        foreach (var h in _groupHeads) { h.Width = Math.Max(avail, 200); h.Height = (int)(26 * f); h.Margin = new Padding(0, (int)(6 * f), 0, (int)(2 * f)); }
        _cardHost.ResumeLayout();
    }

    // ---- server cards ------------------------------------------------------------------------
    void RefreshToolPaths()
    {
        if (DateTime.UtcNow - _toolsChecked < TimeSpan.FromMinutes(2)) return;
        _toolsChecked = DateTime.UtcNow;
        _puttyPath = ToolDetector.PuttyPath;
        _winscpPath = ToolDetector.WinScpPath;
    }

    List<CardAction> ActionsFor(TunnelConfig? t, bool up)
    {
        if (t == null) return new();
        if (t.IsRdp) return new() { new("rdp", "Remote Desktop", up), new("pwd", "Get password", true) };
        var list = new List<CardAction>();
        if (_puttyPath != null) list.Add(new("putty", "PuTTY", up));
        if (_winscpPath != null) list.Add(new("winscp", "WinSCP", up));
        if (list.Count == 0) list.Add(new("copy", "Copy session name", true));
        return list;
    }

    void SelectCard(int index)
    {
        _selected = index;
        for (var i = 0; i < _cards.Count; i++) _cards[i].Selected = i == index;
        ShowDetail();
    }

    void OnCardAction(ServerCard card, string id)
    {
        var i = _cards.IndexOf(card);
        if (i < 0) return;
        SelectCard(i);
        var t = SelectedTunnel();
        if (t == null) return;
        var session = ToolConfigurator.SessionName(t);
        try
        {
            switch (id)
            {
                case "rdp": OpenRdp(); break;
                case "pwd": _ = CopyWindowsPasswordAsync(); break;
                case "putty" when _puttyPath != null: Process.Start(new ProcessStartInfo(_puttyPath, $"-load \"{session}\"") { UseShellExecute = false }); break;
                case "winscp" when _winscpPath != null: Process.Start(new ProcessStartInfo(_winscpPath, $"\"{session}\"") { UseShellExecute = false }); break;
                case "copy":
                    Clipboard.SetText(session);
                    MessageBox.Show(this, $"The saved session name \"{session}\" is on your clipboard. Open PuTTY, WinSCP or MobaXterm and choose it from the list of saved sessions.", "Session name copied");
                    break;
            }
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the tool: " + ex.Message, "SSMConnect"); }
    }

    void OnCardActivated(ServerCard card)
    {
        var i = _cards.IndexOf(card);
        if (i < 0 || i >= _rows.Count) return;
        MessageBox.Show(this, _rows[i].Message, _rows[i].Name);
    }

    // ---- menu bar ----------------------------------------------------------------------------
    void BuildMenu()
    {
        var menu = new MenuStrip { Font = AppTheme.Body() };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("Create &desktop shortcut", (_, _) => CreateDesktopShortcut()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Hide to tray", (_, _) => Close()));
        file.DropDownItems.Add(Item("E&xit", (_, _) => ExitApp()));

        var aws = new ToolStripMenuItem("&AWS");
        aws.DropDownItems.Add(Item("&Add AWS account...", (_, _) => OpenSetup(addAccount: true)));
        aws.DropDownItems.Add(Item("&Sign in again", async (_, _) => await SignInAsync()));

        var servers = new ToolStripMenuItem("&Servers");
        servers.DropDownItems.Add(Item("Scan for &new or changed servers", async (_, _) => await ScanForChangesAsync(force: true, interactive: true)));
        servers.DropDownItems.Add(Item("What &changed since the last scan...", (_, _) => ShowServerChanges()));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&About SSMConnect", (_, _) => ShowAbout()));
        help.DropDownItems.Add(Item("Check for &updates", async (_, _) => await CheckForUpdateAsync(manual: true)));
        help.DropDownItems.Add(Item("Open &log file", (_, _) => OpenLog()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&Uninstall SSMConnect...", (_, _) => UninstallClicked()));

        menu.Items.AddRange(new ToolStripItem[] { file, aws, servers, help });
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    static ToolStripMenuItem Item(string text, EventHandler click)
    {
        var i = new ToolStripMenuItem(text);
        i.Click += click;
        return i;
    }

    // ---- tray icon ---------------------------------------------------------------------------
    void BuildTray()
    {
        _icons[Level.Green] = MakeStatusIcon(Color.FromArgb(34, 170, 80));
        _icons[Level.Amber] = MakeStatusIcon(Color.FromArgb(240, 170, 20));
        _icons[Level.Red] = MakeStatusIcon(Color.FromArgb(205, 40, 50));
        _icons[Level.Grey] = MakeStatusIcon(Color.FromArgb(130, 130, 130));

        var menu = new ContextMenuStrip();
        _trayStart.Click += async (_, _) => await ServiceActionAsync("start");
        _trayStop.Click += async (_, _) => await ServiceActionAsync("stop");
        _trayRestart.Click += async (_, _) => await ServiceActionAsync("restart");
        menu.Items.Add(_trayHeader);
        menu.Items.Add(Item("Open dashboard", (_, _) => ShowDashboard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.AddRange(new ToolStripItem[] { _trayStart, _trayStop, _trayRestart });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Sign in to AWS (SSO)", async (_, _) => await SignInAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit tray icon", (_, _) => ExitApp()));

        _tray.ContextMenuStrip = menu;
        _tray.Icon = _icons[Level.Grey];
        _tray.Text = "SSMConnect";
        _tray.Visible = true;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
    }

    static Icon MakeStatusIcon(Color color) => AppTheme.MakeLogoIcon(color); // the SSMConnect logo with a status dot

    // ---- updates -----------------------------------------------------------------------------
    /// <summary>Checks GitHub shortly after launch and then once a day, quietly. Nothing opens by itself: a banner and one tray message appear.</summary>
    void StartUpdateWatch()
    {
        var first = new System.Windows.Forms.Timer { Interval = 30 * 1000 };
        first.Tick += (_, _) => { first.Stop(); first.Dispose(); if (UpdateChecker.DueForCheck()) _ = CheckForUpdateAsync(manual: false); };
        first.Start();
        _updateTimer.Tick += (_, _) => { if (!_updating && UpdateChecker.DueForCheck()) _ = CheckForUpdateAsync(manual: false); };
        _updateTimer.Start();
    }

    async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating) return;
        try
        {
            var info = await UpdateChecker.CheckAsync();
            if (info == null)
            {
                if (manual) MessageBox.Show(this, $"You have the latest version ({UpdateChecker.Current}).", "SSMConnect updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _update = info;
            _updateText.Text = $"SSMConnect {info.Version} is available (you have {UpdateChecker.Current}). Updating keeps your servers and settings.";
            _updateBar.Visible = true;
            _updateNow.Enabled = _updateNotes.Enabled = _updateLater.Enabled = true;
            if (!manual && UpdateChecker.ShouldNotify(info.Version))
                Balloon(8000, "SSMConnect update available", $"Version {info.Version} is ready. Open the dashboard and press Update now.", ToolTipIcon.Info);
            if (manual) { ShowDashboard(); }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Update check failed: " + ex.Message);
            if (manual) MessageBox.Show(this, "Could not reach GitHub to look for updates. Check your internet connection and try again.\n\n" + ex.Message, "SSMConnect updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async Task InstallUpdateAsync()
    {
        if (_update == null || _updating) return;
        var info = _update;
        if (MessageBox.Show(this,
                $"Update SSMConnect to {info.Version}?\n\nSSMConnect closes, the new version is installed (Windows will ask for permission), and it opens again by itself. Your servers, keys and saved sessions are kept. Your connections are down for about a minute.",
                "Update SSMConnect", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _updating = true;
        _updateNow.Enabled = _updateNotes.Enabled = _updateLater.Enabled = false;
        try
        {
            var progress = new Progress<int>(p => _updateText.Text = $"Downloading SSMConnect {info.Version}...  {p}%");
            _updateText.Text = $"Downloading SSMConnect {info.Version}...";
            var path = await UpdateChecker.DownloadAsync(info, progress);
            _updateText.Text = "Download verified. Starting the update...";
            Process.Start(new ProcessStartInfo(path, "/ssmupdate") { UseShellExecute = true });
            _reallyExit = true;
            Application.Exit();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _updateText.Text = "The update was cancelled at the Windows permission prompt. Nothing was changed.";
        }
        catch (Exception ex)
        {
            AppLog.Warn("Update failed: " + ex.Message);
            _updateText.Text = "The update did not start: " + ex.Message;
        }
        finally { _updating = false; _updateNow.Enabled = _updateNotes.Enabled = _updateLater.Enabled = true; }
    }

    void ExitApp()
    {
        var r = MessageBox.Show(this,
            "Exit SSMConnect?\n\nThis closes the tray icon AND stops all your connections, because they run inside SSMConnect. PuTTY / WinSCP sessions will not connect until you open SSMConnect again.",
            "Exit SSMConnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return;
        _reallyExit = true;
        Application.Exit();
    }

    // ---- live refresh ------------------------------------------------------------------------
    void StartStatusWatcher()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.DataDir);
            _watcher = new FileSystemWatcher(ConfigStore.DataDir, "status.json") { EnableRaisingEvents = true };
            FileSystemEventHandler h = (_, _) => { try { BeginInvoke(RefreshAll); } catch { } };
            _watcher.Changed += h;
            _watcher.Created += h;
            _watcher.Renamed += (_, _) => { try { BeginInvoke(RefreshAll); } catch { } };
        }
        catch { /* the 1-second timer still keeps the view fresh */ }
    }

    static HashSet<int> ActiveListeners()
    {
        try { return new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(l => l.Port)); }
        catch { return new HashSet<int>(); }
    }

    void RefreshAll()
    {
        try { Refresh_(); }
        catch { /* never let a refresh glitch take down the tray */ }
    }

    void Refresh_()
    {
        // config (reload only when changed)
        try
        {
            var stamp = File.Exists(ConfigStore.ConfigPath) ? File.GetLastWriteTimeUtc(ConfigStore.ConfigPath) : DateTime.MinValue;
            if (stamp != _cfgStamp) { _cfg = ConfigStore.Load() ?? _cfg; _cfgStamp = stamp; }
        }
        catch { }

        var running = _mgr.IsRunning;
        var status = StatusStore.Load();
        var stale = !running || status == null || DateTime.UtcNow - status.UpdatedUtc > TimeSpan.FromSeconds(20);
        var listeners = ActiveListeners();

        // connections pill
        if (running) SetPill(_svcPill, "Connections: running", Level.Green, "SSMConnect is keeping your tunnels open.");
        else SetPill(_svcPill, "Connections: stopped", Level.Red, "Connections are down. Use Connections > Start connections.");

        // requirements pill
        var haveAws = AwsCli.FindAws() != null;
        var havePlugin = AwsCli.FindPlugin() != null;
        if (haveAws && havePlugin) SetPill(_reqPill, "AWS tools: installed", Level.Green, "AWS CLI and the Session Manager plugin are installed.");
        else SetPill(_reqPill, "AWS tools: missing " + (haveAws ? "plugin" : havePlugin ? "CLI" : "CLI + plugin"), Level.Red, "Open Tools > Install or check AWS tools to install them automatically.");

        // auth pill (updated by background check)
        SetPill(_authPill, _authText, _authLevel, _authTip);
        if (!_authBusy && DateTime.UtcNow >= _nextAuthCheck) _ = CheckAuthAsync();

        // rows: servers of one primary account sit together, with their member accounts (organizations) under quiet headings
        var rows = new List<Row>();
        FillAccounts();
        var order = _cfg.Tunnels.Select(x => x.Profile).Distinct().ToList();
        var ordered = _cfg.Tunnels.OrderBy(x => order.IndexOf(x.Profile)).ToList();
        foreach (var t in ordered)
        {
            var label = t.Name;
            var s = status?.Tunnels.FirstOrDefault(x => x.InstanceId == t.InstanceId && x.LocalPort == t.LocalPort);
            Row row;
            if (!t.Enabled) row = new Row(label, t.InstanceId, t.LocalPort, "Off", "This connection is turned off.", null, Level.Grey);
            else if (!running) row = new Row(label, t.InstanceId, t.LocalPort, "Stopped", "Connections are stopped. Use the Connections menu or the tray icon to start them.", null, Level.Grey);
            else if (stale || s == null) row = new Row(label, t.InstanceId, t.LocalPort, "Starting", "Starting this connection...", null, Level.Amber);
            else row = s.State switch
            {
                TunnelStates.Connected when listeners.Contains(t.LocalPort) =>
                    new Row(label, t.InstanceId, t.LocalPort, "Connected", $"Ready. Open the saved session '{ToolConfigurator.SessionName(t)}' in PuTTY / WinSCP / MobaXterm.", s.SinceUtc, Level.Green),
                TunnelStates.Connected =>
                    new Row(label, t.InstanceId, t.LocalPort, "Checking", "Tunnel reported connected; confirming the local port is open...", s.SinceUtc, Level.Amber),
                TunnelStates.Error => new Row(label, t.InstanceId, t.LocalPort, "Problem", s.Message, s.SinceUtc, Level.Red),
                TunnelStates.Retrying => new Row(label, t.InstanceId, t.LocalPort, "Reconnecting", s.Message, s.SinceUtc, Level.Amber),
                TunnelStates.Disabled => new Row(label, t.InstanceId, t.LocalPort, "Off", s.Message, null, Level.Grey),
                _ => new Row(label, t.InstanceId, t.LocalPort, "Connecting", s.Message, s.SinceUtc, Level.Amber),
            };
            rows.Add(row);
        }
        // the window shows one primary account at a time; the summary and tray cover every connection
        var viewIdx = Enumerable.Range(0, ordered.Count).Where(i => PKey(ordered[i].Profile) == _view).ToList();
        var viewProfiles = viewIdx.Select(i => ordered[i].Profile).ToList();
        _rowGroups = viewProfiles.Distinct().Count() > 1 ? viewProfiles : new List<string>();
        UpdateGrid(viewIdx.Select(i => rows[i]).ToList());

        // overall
        var enabled = rows.Where(r => r.Level != Level.Grey || r.StatusText != "Off").ToList();
        var up = rows.Count(r => r.Level == Level.Green);
        var total = rows.Count(r => r.StatusText != "Off");
        Level overall;
        if (!running) overall = Level.Grey;
        else if (total == 0) overall = Level.Amber;
        else if (rows.Any(r => r.Level == Level.Red)) overall = Level.Red;
        else if (up == total) overall = Level.Green;
        else overall = Level.Amber;

        string tip, summary;
        switch (overall)
        {
            case Level.Green: tip = $"SSMConnect: all {total} connection(s) up"; summary = $"All {total} connection(s) are up."; break;
            case Level.Red: tip = $"SSMConnect: problem ({up} of {total} up)"; summary = $"{up} of {total} connection(s) up. Red cards show the reason."; break;
            case Level.Grey: tip = "SSMConnect: connections stopped"; summary = "Connections are stopped, so nothing is connected."; break;
            default: tip = total == 0 ? "SSMConnect: no connections set up" : $"SSMConnect: {up} of {total} up, connecting..."; summary = total == 0 ? "No connections are set up yet. Click \"Add / change servers...\"." : $"{up} of {total} connection(s) up. The rest are connecting..."; break;
        }
        _summary.Text = summary;
        _summary.ForeColor = overall switch { Level.Green => GoodFg, Level.Red => BadFg, Level.Amber => WarnFg, _ => GreyFg };
        _tray.Icon = _icons[overall];
        _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        _trayHeader.Text = tip;
        Text = overall == Level.Green ? "SSMConnect Dashboard - all connected" : "SSMConnect Dashboard";

        foreach (var m in new[] { _mStart, _trayStart }) m.Enabled = !_busy && !running;
        foreach (var m in new[] { _mStop, _trayStop }) m.Enabled = !_busy && running;
        foreach (var m in new[] { _mRestart, _trayRestart }) m.Enabled = !_busy;
        _btnStart.Enabled = !_busy && !running; _btnStop.Enabled = !_busy && running; _btnRestart.Enabled = !_busy;

        NotifyOnChange(overall, rows);
        _overall = overall;
        _ = enabled;
    }

    void NotifyOnChange(Level overall, List<Row> rows)
    {
        if (_prevOverall is Level prev && prev != overall)
        {
            if (overall == Level.Green)
                Balloon(3000, "SSMConnect", "All connections are up.", ToolTipIcon.Info);
            else if (prev == Level.Green && overall is Level.Red or Level.Amber)
            {
                var bad = rows.FirstOrDefault(r => r.Level == Level.Red) ?? rows.FirstOrDefault(r => r.Level == Level.Amber);
                Balloon(5000, "SSMConnect: connection lost", bad == null ? "A connection dropped." : $"{bad.Name}: {bad.Message}", ToolTipIcon.Warning);
            }
        }
        _prevOverall = overall;
    }

    static void SetPill(Label pill, string text, Level level, string tip)
    {
        var (bg, fg) = level switch
        {
            Level.Green => (GoodBg, GoodFg),
            Level.Amber => (WarnBg, WarnFg),
            Level.Red => (BadBg, BadFg),
            _ => (GreyBg, GreyFg),
        };
        if (pill.Text != text) pill.Text = text;
        if (pill.BackColor != bg) pill.BackColor = bg;
        if (pill.ForeColor != fg) pill.ForeColor = fg;
        if ((pill.Tag as string) != tip && pill.Parent != null)
        {
            pill.Tag = tip;
            new ToolTip().SetToolTip(pill, tip);
        }
    }

    static string Ago(DateTime utc)
    {
        var s = (int)Math.Max(0, (DateTime.UtcNow - utc).TotalSeconds);
        if (s < 60) return s + " s";
        if (s < 3600) return (s / 60) + " min";
        if (s < 86400) return (s / 3600) + " h";
        return (s / 86400) + " d";
    }

    void UpdateGrid(List<Row> rows)
    {
        _rows = rows;
        RefreshToolPaths();
        var f = DeviceDpi / 96f;
        _cardHost.SuspendLayout();
        try
        {
            while (_cards.Count > rows.Count) { var c = _cards[^1]; _cards.RemoveAt(_cards.Count - 1); _cardHost.Controls.Remove(c); c.Dispose(); }
            while (_cards.Count < rows.Count)
            {
                var c = new ServerCard { Width = (int)(340 * f), Height = ServerCard.HeightFor(f) };
                c.ActionClicked += OnCardAction;
                c.Picked += card => SelectCard(_cards.IndexOf(card));
                c.Activated += OnCardActivated;
                _cards.Add(c);
                _cardHost.Controls.Add(c);
                LayoutCards();
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var tun = _cfg.Tunnels.FirstOrDefault(t => t.InstanceId == r.InstanceId && t.LocalPort == r.Port);
                var up = r.Level == Level.Green;
                _cards[i].Data = new CardData
                {
                    Name = r.Name, InstanceId = r.InstanceId, Port = r.Port.ToString(), StatusText = r.StatusText, Message = r.Message,
                    Since = r.Since.HasValue ? Ago(r.Since.Value) : "", KnownType = tun != null, IsWindows = tun?.IsRdp ?? false,
                    IsProd = r.Name.Contains("prod", StringComparison.OrdinalIgnoreCase),
                    Level = r.Level switch { Level.Green => CardLevel.Green, Level.Amber => CardLevel.Amber, Level.Red => CardLevel.Red, _ => CardLevel.Grey },
                    Actions = ActionsFor(tun, up),
                };
            }
            ArrangeGroups(f);
            if (_selected >= _cards.Count) _selected = -1;
            for (var i = 0; i < _cards.Count; i++) _cards[i].Selected = i == _selected;
        }
        finally { _cardHost.ResumeLayout(); }
        ShowDetail();
    }

    /// <summary>With several accounts, puts a quiet heading above each account's servers.</summary>
    void ArrangeGroups(float f)
    {
        var names = _rowGroups.Count == _cards.Count ? _rowGroups.Distinct().ToList() : new List<string>();
        while (_groupHeads.Count > names.Count) { var h = _groupHeads[^1]; _groupHeads.RemoveAt(_groupHeads.Count - 1); _cardHost.Controls.Remove(h); h.Dispose(); }
        while (_groupHeads.Count < names.Count)
            _groupHeads.Add(new Label { AutoSize = false, TextAlign = ContentAlignment.BottomLeft, ForeColor = Color.FromArgb(105, 112, 120), Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = AppTheme.Page });
        var idx = 0;
        for (var g = 0; g < names.Count; g++)
        {
            var count = _rowGroups.Count(x => x == names[g]);
            var head = _groupHeads[g];
            head.Text = $"{names[g].ToUpperInvariant()}    {count} server{(count == 1 ? "" : "s")}";
            if (!_cardHost.Controls.Contains(head)) _cardHost.Controls.Add(head);
            _cardHost.SetFlowBreak(head, true);
            _cardHost.Controls.SetChildIndex(head, idx++);
            for (var i = 0; i < _cards.Count; i++)
                if (_rowGroups[i] == names[g]) _cardHost.Controls.SetChildIndex(_cards[i], idx++);
        }
        if (names.Count == 0) for (var i = 0; i < _cards.Count; i++) _cardHost.Controls.SetChildIndex(_cards[i], i);
        LayoutCards();
    }

    List<ProfileInfo> Profiles()
    {
        try
        {
            var st = File.Exists(AwsProfiles.ConfigPath) ? File.GetLastWriteTimeUtc(AwsProfiles.ConfigPath) : DateTime.MinValue;
            if (st != _profStamp || _profList.Count == 0) { _profList = AwsProfiles.List(); _profStamp = st; }
        }
        catch { }
        return _profList;
    }

    /// <summary>The primary account (company sign-in or access-key login) a saved server's profile belongs to.</summary>
    string PKey(string profile)
    {
        var p = Profiles().FirstOrDefault(x => x.Name == profile);
        return p == null ? "key:" + profile : AwsProfiles.PrimaryKey(p);
    }

    string PLabel(string profile)
    {
        var p = Profiles().FirstOrDefault(x => x.Name == profile);
        return p == null ? profile : AwsProfiles.PrimaryName(p);
    }

    /// <summary>Keeps the account drop-down in step with the saved servers. It lists primary accounts only; the current one is the default.</summary>
    void FillAccounts()
    {
        var prim = _cfg.Tunnels.Where(t => !string.IsNullOrWhiteSpace(t.Profile))
            .GroupBy(t => PKey(t.Profile)).Select(g => (Key: g.Key, Label: PLabel(g.First().Profile), Count: g.Count())).ToList();
        if (!_viewChosen || _view == null || !prim.Any(x => x.Key == _view))
        {
            var cur = string.IsNullOrWhiteSpace(_cfg.Profile) ? null : PKey(_cfg.Profile);
            _view = prim.Any(x => x.Key == cur) ? cur : prim.FirstOrDefault().Key;
            _viewChosen = prim.Count > 0;
        }
        var sig = string.Join("|", prim.Select(x => x.Key + ":" + x.Label + ":" + x.Count)) + "#" + _view;
        if (sig == _acctSig || _acct.DroppedDown) return;
        _acctSig = sig;
        _acct.BeginUpdate();
        _acct.Items.Clear();
        foreach (var x in prim) _acct.Items.Add(new AcctItem($"{x.Label}   ({x.Count})", x.Key, false));
        _acct.Items.Add(new AcctItem("+  Add AWS account...", null, true));
        _acct.SelectedIndex = prim.FindIndex(x => x.Key == _view);
        _acct.EndUpdate();
    }

    void DrawAcctItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 && _acct.SelectedItem is not AcctItem) { e.Graphics.FillRectangle(Brushes.White, e.Bounds); return; }
        var it = (e.Index >= 0 ? _acct.Items[e.Index] : _acct.SelectedItem) as AcctItem;
        var inList = (e.State & DrawItemState.ComboBoxEdit) == 0;
        var hot = inList && (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(hot ? AppTheme.Accent : Color.White)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (it == null) return;
        if (it.IsAdd && inList && !hot) { using var line = new Pen(AppTheme.Line); e.Graphics.DrawLine(line, e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Right - 8, e.Bounds.Top); }
        var fore = hot ? Color.White : it.IsAdd ? AppTheme.Accent : AppTheme.Ink;
        using var f = it.IsAdd ? AppTheme.Semi(9.5f) : AppTheme.Body(9.5f);
        TextRenderer.DrawText(e.Graphics, it.Text, f, new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void OnAcctPicked()
    {
        if (_acct.SelectedItem is not AcctItem it) return;
        if (it.IsAdd) { _acctSig = ""; OpenSetup(addAccount: true); return; }
        _view = it.Key;
        _viewChosen = true;
        // remember it as the current account (also what the setup wizard opens on)
        var mine = _cfg.Tunnels.FirstOrDefault(t => PKey(t.Profile) == it.Key)?.Profile;
        if (mine != null)
        {
            try { var c = ConfigStore.Load() ?? _cfg; c.Profile = mine; ConfigStore.Save(c); _cfg.Profile = mine; _cfgStamp = DateTime.MinValue; } catch { }
        }
        _acctSig = "";
        _selected = -1;
        RefreshAll();
    }

    void ShowDetail()
    {
        // only worth a line of text when something is not right
        if (_selected < 0 || _selected >= _rows.Count || _rows[_selected].Level == Level.Green) { _detail.Text = ""; return; }
        var r = _rows[_selected];
        _detail.Text = $"{r.Name}: {r.Message}";
    }

    async Task CheckAuthAsync()
    {
        _authBusy = true;
        try
        {
            if (AwsCli.FindAws() == null) { _authText = "AWS sign-in: install AWS CLI first"; _authLevel = Level.Red; _authTip = "The AWS CLI is not installed."; return; }
            if (_cfg.Tunnels.Count == 0) { _authText = "AWS sign-in: not set up yet"; _authLevel = Level.Grey; _authTip = "Set up your connections to choose an AWS profile."; return; }
            var accounts = _cfg.Tunnels.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Profile))
                                       .Select(t => t.Profile).Distinct().ToList();
            if (accounts.Count == 0) { _authText = "AWS sign-in: no account chosen"; _authLevel = Level.Amber; _authTip = "Use Tools > Set up / change connections to choose an AWS account."; return; }
            var failed = new List<string>();
            string firstProblem = "";
            foreach (var acct in accounts)
            {
                var r = await AwsCli.WhoAmIAsync(new AppConfig { Profile = acct, Region = _cfg.Tunnels.First(t => t.Profile == acct).Region });
                if (!r.Ok) { failed.Add(acct); if (firstProblem.Length == 0) firstProblem = AwsCli.FriendlyError(r.Error); }
            }
            _authFailed = failed;
            if (failed.Count == 0)
            {
                _authText = accounts.Count == 1 ? "AWS sign-in: OK" : $"AWS sign-in: OK ({accounts.Count} accounts)";
                _authLevel = Level.Green;
                _authTip = "Signed in: " + string.Join(", ", accounts);
            }
            else
            {
                _authText = "AWS sign-in needed: " + string.Join(", ", failed);
                _authLevel = Level.Red;
                _authTip = firstProblem + " Use Tools > Sign in to AWS.";
            }
        }
        catch (Exception ex) { _authText = "AWS sign-in: unknown"; _authLevel = Level.Amber; _authTip = ex.Message; }
        finally { _authBusy = false; _nextAuthCheck = DateTime.UtcNow.AddSeconds(45); }
    }

    // ---- actions -----------------------------------------------------------------------------
    async Task ServiceActionAsync(string verb)
    {
        if (_busy) return;
        _busy = true;
        _summary.Text = verb == "start" ? "Starting connections..." : verb == "stop" ? "Stopping connections..." : "Restarting connections...";
        try
        {
            await Task.Run(() =>
            {
                if (verb != "start") _mgr.Stop();
                if (verb != "stop") _mgr.Start();
            });
        }
        finally { _busy = false; RefreshAll(); }
    }

    // ---- passive server scan ------------------------------------------------------------------
    /// <summary>
    /// Once a day (the first time SSMConnect runs that day, and again if the PC stays on past midnight) every watched
    /// account is listed again in the background. Nothing opens: differences show as a tray notification and under
    /// Tools > Server changes. Saved PuTTY / WinSCP sessions stay exactly as they are until the person changes them.
    /// </summary>
    void StartServerWatch()
    {
        _tray.BalloonTipClicked += (_, _) => { if (_balloonIsChanges) ShowServerChanges(); };
        _scanTimer.Tick += async (_, _) => await ScanForChangesAsync(force: false, interactive: false);
        _scanTimer.Start();
        var first = new System.Windows.Forms.Timer { Interval = 20000 }; // let the tunnels come up first
        first.Tick += async (_, _) => { first.Stop(); first.Dispose(); await ScanForChangesAsync(force: false, interactive: false); };
        first.Start();
    }

    async Task ScanForChangesAsync(bool force, bool interactive)
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var cfg = ConfigStore.Load() ?? _cfg;
            if (DiscoveryMonitor.Accounts(cfg).Count == 0)
            {
                if (interactive) MessageBox.Show(this, "No AWS account is set up yet. Use Tools > Set up / change connections first.", "SSMConnect");
                return;
            }
            var changes = await Task.Run(() => DiscoveryMonitor.RunAsync(cfg, force));
            if (changes.Count > 0) NotifyChanges(changes);
            else if (interactive) MessageBox.Show(this, "Scan finished. Nothing has changed since the last scan.", "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { AppLog.Warn("Server scan failed: " + ex.Message); }
        finally { _scanning = false; }
    }

    void Balloon(int ms, string title, string text, ToolTipIcon icon)
    {
        _balloonIsChanges = false;
        _tray.ShowBalloonTip(ms, title, text, icon);
    }

    void NotifyChanges(List<string> changes)
    {
        _balloonIsChanges = true;
        var title = changes.Count == 1 ? "SSMConnect: 1 change in your AWS servers" : $"SSMConnect: {changes.Count} changes in your AWS servers";
        var text = changes[0] + (changes.Count > 1 ? $"  (+{changes.Count - 1} more)" : "") + "  Click to review.";
        // a tray notification does not take focus or open the dashboard
        _tray.ShowBalloonTip(10000, title, text.Length > 240 ? text.Substring(0, 237) + "..." : text, ToolTipIcon.Info);
    }

    void ShowServerChanges()
    {
        var pending = DiscoveryCache.TakePending();
        if (pending.Count == 0)
        {
            var scans = DiscoveryCache.Load().Scans.Where(s => s.ScannedUtc != default).ToList();
            var when = scans.Count == 0 ? "not scanned yet" : "last scanned " + scans.Max(s => s.ScannedUtc).ToLocalTime().ToString("g");
            MessageBox.Show(this, "No server changes to review (" + when + ").\n\nSSMConnect checks once a day in the background. Use Tools > Scan for server changes now to check immediately.", "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ShowDashboard();
        var shown = pending.Take(15).ToList();
        var body = string.Join("\n", shown.Select(x => "- " + x)) + (pending.Count > shown.Count ? $"\n...and {pending.Count - shown.Count} more." : "");
        if (MessageBox.Show(this, "Changes found in your AWS servers since the last scan:\n\n" + body +
                                  "\n\nYour saved PuTTY / WinSCP sessions have not been changed. Open the setup to review and update them?",
                            "Server changes", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            OpenSetup();
    }

    void OpenSetup(bool addAccount = false)
    {
        ShowDashboard();
        using (var wizard = new WizardForm(addAccount)) wizard.ShowDialog(this);
        _cfg = ConfigStore.Load() ?? _cfg;
        _viewChosen = false;   // the account the wizard finished on becomes the current one
        _acctSig = "";
        _cfgStamp = DateTime.MinValue;
        _nextAuthCheck = DateTime.MinValue;
        RefreshAll();
    }

    async Task SignInAsync()
    {
        if (AwsCli.FindAws() == null) { MessageBox.Show(this, "The AWS CLI is not installed yet. Use Tools > Install or check AWS tools.", "SSMConnect"); return; }
        var used = _cfg.Tunnels.Select(t => t.Profile).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var targets = (_authFailed.Count > 0 ? _authFailed : used).ToList();
        if (targets.Count == 0) { MessageBox.Show(this, "No AWS account is set up yet. Use Tools > Set up / change connections.", "SSMConnect"); return; }

        foreach (var name in targets)
        {
            var info = AwsProfiles.Find(name);
            if (info == null) { MessageBox.Show(this, $"The AWS profile \"{name}\" no longer exists on this PC. Use Tools > Add or manage AWS accounts to add it again.", "AWS sign-in", MessageBoxButtons.OK, MessageBoxIcon.Warning); continue; }
            if (info.Kind != ProfileKind.Sso)
            {
                MessageBox.Show(this, $"\"{name}\" uses access keys, so there is nothing to sign in to. If it fails, check the keys in Tools > Add or manage AWS accounts.", "AWS sign-in", MessageBoxButtons.OK, MessageBoxIcon.Information);
                continue;
            }
            Balloon(4000, "SSMConnect", $"A browser window will open. Sign in to AWS account \"{name}\" there.", ToolTipIcon.Info);
            var r = await AwsCli.SsoLoginAsync(new AppConfig { Profile = name });
            if (!r.Ok) MessageBox.Show(this, $"{name}: {AwsCli.FriendlyError(r.Error)}", "AWS sign-in", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _nextAuthCheck = DateTime.MinValue;
        _mgr.Poke();
        RefreshAll();
    }

    void CreateDesktopShortcut()
    {
        try
        {
            Shortcuts.CreateDesktopShortcut(Application.ExecutablePath);
            MessageBox.Show(this, "A desktop shortcut named \"SSMConnect\" was created.", "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not create the shortcut: " + ex.Message, "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ExplainTaskbarPin()
    {
        MessageBox.Show(this,
            "Windows does not allow programs to pin themselves to the taskbar, so do it in two clicks:\n\n" +
            "1. Look at the taskbar icon for this open SSMConnect window.\n" +
            "2. Right-click it and choose \"Pin to taskbar\".\n\n" +
            "You can also press the Windows key, type SSMConnect, right-click the result and choose \"Pin to taskbar\".",
            "Pin to taskbar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Kept in the registry (not the data folder) so the choice survives a reinstall.</summary>
    static bool AskAboutShortcut
    {
        get
        {
            try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\SSMConnect"); return !(k?.GetValue("AskDesktopShortcut") is int v && v == 0); }
            catch { return true; }
        }
        set
        {
            try { using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\SSMConnect"); k.SetValue("AskDesktopShortcut", value ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); }
            catch { }
        }
    }

    async Task FirstRunAsync()
    {
        if (_startHidden) return;
        await Task.Yield();
        if (_cfg.Tunnels.Count == 0) OpenSetup();

        try
        {
            var marker = UserCleanup.FirstRunMarker;
            if (File.Exists(marker)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));

            if (!AskAboutShortcut) return;
            var verify = new TaskDialogVerificationCheckBox("Don't ask me again");
            var page = new TaskDialogPage
            {
                Caption = "SSMConnect",
                Heading = "Add SSMConnect to your desktop?",
                Text = "Tip: to pin it to the taskbar, right-click its taskbar icon while it is open and choose \"Pin to taskbar\".",
                Icon = TaskDialogIcon.Information,
                Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
                DefaultButton = TaskDialogButton.Yes,
                Verification = verify,
            };
            var wantDesktop = TaskDialog.ShowDialog(this, page) == TaskDialogButton.Yes;
            if (verify.Checked) AskAboutShortcut = false;
            if (wantDesktop) CreateDesktopShortcut();
        }
        catch { }
    }

    TunnelConfig? SelectedTunnel()
    {
        if (_selected < 0 || _selected >= _rows.Count) return null;
        var r = _rows[_selected];
        return _cfg.Tunnels.FirstOrDefault(t => t.InstanceId == r.InstanceId && t.LocalPort == r.Port);
    }

    void OpenRdp()
    {
        var t = SelectedTunnel();
        if (t == null || !t.IsRdp) { MessageBox.Show(this, "Use the Remote Desktop button on a Windows server card. Windows servers are added with \"Add / change servers...\".", "Remote Desktop"); return; }
        if (!ActiveListeners().Contains(t.LocalPort))
        {
            MessageBox.Show(this, "This connection is not up yet. Click \"Start connections\", wait until its status turns green, then try again.", "Remote Desktop");
            return;
        }
        try { RdpLauncher.Open(t); }
        catch (Exception ex) { MessageBox.Show(this, "Could not start Remote Desktop: " + ex.Message, "Remote Desktop"); }
    }

    /// <summary>Decrypts the server's generated Administrator password with the key pair's .pem and puts it on the clipboard for 60 seconds.</summary>
    async Task CopyWindowsPasswordAsync()
    {
        var t = SelectedTunnel();
        if (t == null || !t.IsRdp) { MessageBox.Show(this, "Select a Windows server in the list first.", "Windows password"); return; }
        var pem = t.KeyFile;
        if (string.IsNullOrWhiteSpace(pem) || !File.Exists(pem) || !pem!.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            using var dlg = new OpenFileDialog { Title = "Choose the .pem key this server was launched with", Filter = "PEM key (*.pem)|*.pem|All files|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            pem = dlg.FileName;
        }
        try
        {
            var pw = await AwsCli.GetWindowsPasswordAsync(t, pem!);
            Clipboard.SetText(pw);
            MessageBox.Show(this, $"The Administrator password for {t.Name} is on your clipboard. Paste it into the Remote Desktop sign-in box.\nIt is cleared from the clipboard in 60 seconds.", "Windows password");
            _ = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => { try { BeginInvoke(() => { try { if (Clipboard.GetText() == pw) Clipboard.Clear(); } catch { } }); } catch { } });
            if (!string.Equals(t.KeyFile, pem, StringComparison.OrdinalIgnoreCase)) { t.KeyFile = pem; ConfigStore.Save(_cfg); }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows password", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(AppLog.LogPath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "No log file yet, or it could not be opened: " + ex.Message, "SSMConnect"); }
    }

    void OpenConfigFolder()
    {
        try { Directory.CreateDirectory(ConfigStore.DataDir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ConfigStore.DataDir + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the folder: " + ex.Message, "SSMConnect"); }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    void ShowAbout()
    {
        var av = typeof(DashboardForm).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        string built;
        try { built = File.GetLastWriteTime(Application.ExecutablePath).ToString("yyyy-MM-dd HH:mm"); }
        catch { built = "unknown"; }
        MessageBox.Show(this,
            $"SSMConnect {av.Major}.{av.Minor}.{av.Build}\nBuild {av.Build}  (built {built})\nSSH over AWS Systems Manager for PuTTY, WinSCP and MobaXterm\n\n" +
            "Copyright 2026 Poonchezhian Periasamy\npoonchezhian@gmail.com\n" +
            $"Blog: {BlogUrl}\nSource: {RepoUrl}\n\n" +
            "Licensed under the Apache License, Version 2.0.\n" +
            "Not affiliated with or endorsed by Amazon Web Services.",
            "About SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void UninstallClicked()
    {
        if (Uninstaller.FindUninstallCommand() == null)
        {
            MessageBox.Show(this,
                "SSMConnect is not registered as installed on this PC (this copy may be running from a build folder).\n\nUse Windows Settings > Apps > Installed apps to remove it.",
                "Uninstall SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var answer = MessageBox.Show(this,
            "Uninstall SSMConnect?\n\n" +
            "• Stops all connections and closes the tray icon\n" +
            "• Removes the SSMConnect sessions from PuTTY, WinSCP and MobaXterm\n" +
            "• Removes the shortcuts and the saved connection list\n\n" +
            "The AWS CLI, the Session Manager plugin and your AWS settings are left alone.",
            "Uninstall SSMConnect", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        try
        {
            _mgr.Stop();
            Uninstaller.Start();
            _reallyExit = true;
            Application.Exit();
        }
        catch (Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            _mgr.Start();
            MessageBox.Show(this, "The Windows permission prompt was cancelled, so nothing was removed.", "Uninstall SSMConnect");
        }
        catch (Exception ex)
        {
            _mgr.Start();
            MessageBox.Show(this, "Could not start the uninstaller: " + ex.Message, "Uninstall SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
