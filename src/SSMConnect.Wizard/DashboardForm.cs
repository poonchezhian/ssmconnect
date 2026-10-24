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
/// Main window and tray icon. Sign in once per organization; every server's secure connection opens together when SSMConnect
/// starts and stays open until Stop connections or Exit (green = connected, amber = connecting, red = stopped after 3 tries, grey = off).
/// The window hides to the system tray when closed; the connections keep running.
/// </summary>
public sealed class DashboardForm : Form
{
    enum Level { Grey, Red, Amber, Green, Blue }

    sealed record Row(string Name, string InstanceId, int Port, string StatusText, string Message, DateTime? Since, Level Level, string Detail = "");

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

    readonly bool _startHidden;
    bool _allowShow, _reallyExit, _hintShown;
    readonly TunnelManager _mgr = new();
    readonly string? _loadWarning;
    AppConfig _cfg;
    DateTime _cfgStamp = DateTime.MinValue;

    // UI
    readonly Pill _svcPill = NewPill(), _authPill = NewPill(), _reqPill = NewPill();
    readonly SideList _side = new() { Dock = DockStyle.Fill };
    readonly Label _orgTitle = new() { AutoSize = false, AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = AppTheme.Ink };
    readonly Label _orgSub = new() { AutoSize = false, AutoEllipsis = true, ForeColor = Color.FromArgb(84, 100, 106) };
    readonly GlassText _search = new() { PlaceholderText = "Search servers" };
    readonly Button _btnSignIn = new() { Text = "Sign in again" }, _btnUsage = new() { Text = "Refresh usage" };
    readonly Button _btnDisconnect = new() { Text = "Stop connections" }, _btnAdd = new() { Text = "Add / change servers..." };
    readonly LegendBar _legend = new() { Dock = DockStyle.Bottom, Height = 38 };
    string _filter = "all";
    bool _signInIsPrimary;
    HashSet<string> _signedOut = new();      // organizations (top-level sign-ins) whose sign-in has expired
    bool _authChecked;
    readonly Dictionary<string, (DateTimeOffset? When, DateTime Checked)> _expiry = new();
    // server size and usage (cached in usage.json)
    readonly Dictionary<string, UsageInfo> _usage = UsageProbe.Load();
    readonly HashSet<string> _usageBusy = new();
    readonly SemaphoreSlim _usageGate = new(3);
    DateTime _nextUsageCheck = DateTime.MinValue, _nextSpecCheck = DateTime.MinValue;
    readonly HashSet<string> _launching = new();
    Dictionary<string, string> _osMap = new();
    readonly Dictionary<Control, string> _tipText = new();
    /// <summary>The scrolling area for the server cards. Cards are placed by <see cref="LayoutCards"/> (no flow layout), so the scroll height is always exact.</summary>
    sealed class CardFlow : Panel
    {
        public CardFlow() { DoubleBuffered = true; }
    }
    readonly CardFlow _cardHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.Page, Padding = new Padding(14, 6, 4, 0) };
    bool _laying;
    readonly List<ServerCard> _cards = new();
    readonly List<Label> _groupHeads = new();
    // account switcher (top right of the header)
    readonly AccountChip _chip = new() { Width = 290, Height = 46 };
    readonly List<(string Key, string Label, int Count)> _prim = new();
    string? _view;              // primary account (sign-in) whose servers are shown
    bool _viewChosen;           // false until the first fill picks the current account
    List<ProfileInfo> _profList = new();
    DateTime _profStamp = DateTime.MinValue;
    List<string> _rowGroups = new();   // account (profile) of each row, same order as _rows
    int _selected = -1;
    // update banner
    readonly Panel _updateBar = new() { Dock = DockStyle.Top, Height = 46, Visible = false, BackColor = Color.FromArgb(224, 242, 241), Padding = new Padding(14, 0, 14, 0) };
    readonly Label _updateText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(4, 70, 66) };
    readonly Button _updateNow = new() { Text = "Update now" }, _updateNotes = new() { Text = "What's new" }, _updateLater = new() { Text = "Later" };
    UpdateInfo? _update;
    bool _updating;
    readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60 * 60 * 1000 };
    string? _puttyPath, _winscpPath, _mobaPath;
    DateTime _toolsChecked = DateTime.MinValue;
    readonly Label _detail = new() { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 8, 0, 0), ForeColor = Color.FromArgb(40, 40, 40) };
    readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
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
    int _authGen;   // raised by every sign-in, so a check that started before it cannot put the organization back to signed out
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
        ClientSize = new Size(1200, 740);
        MinimumSize = new Size(960, 560);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { }

        BuildLayout();
        BuildMenu();
        BuildTray();

        RefreshToolPaths(true); // find PuTTY, WinSCP and MobaXterm right at launch
        _ = Handle; // create the window handle now so background threads can marshal to the UI even while hidden
        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        StartStatusWatcher();
        _mgr.Changed += () => { try { BeginInvoke(RefreshAll); } catch { } };
        _mgr.Start();
        StartServerWatch();
        StartUpdateWatch();
        Shown += async (_, _) => { if (RefreshToolPaths(true)) RefreshAll(); LayoutCards(); if (_loadWarning != null) MessageBox.Show(this, _loadWarning, "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Warning); await FirstRunAsync(); };
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
    /// <summary>A rounded status pill (status colour dot + text). Chips for organization accounts use the same shape without the dot.</summary>
    sealed class Pill : Label
    {
        public bool ShowDot = true;
        public Pill()
        {
            AutoSize = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? AppTheme.Page);
            var s = DeviceDpi / 96f;
            using (var path = AppTheme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), (Height - 2) / 2f))
            using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (ShowDot)
            {
                var tw = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                float dot = 9 * s, gap = 9 * s;
                var x0 = Math.Max(10 * s, (Width - (dot + gap + tw)) / 2f);
                using (var b = new SolidBrush(ForeColor)) g.FillEllipse(b, x0, (Height - dot) / 2f, dot, dot);
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(x0 + dot + gap), 0, Math.Max(10, Width - (int)(x0 + dot + gap) - (int)(8 * s)), Height), ForeColor, flags);
            }
            else TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, flags | TextFormatFlags.HorizontalCenter);
        }
    }

    static Pill NewPill() => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = 40,
        Margin = new Padding(0, 0, 10, 0),
        Font = new Font("Segoe UI Semibold", 9.5f),
    };

    void BuildLayout()
    {
        BackColor = AppTheme.Page;

        // ---- left: organizations, filters and the three status pills
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 252, BackColor = Color.FromArgb(228, 238, 240) };
        var pillHost = new Panel { Dock = DockStyle.Bottom, Height = 124, BackColor = Color.FromArgb(228, 238, 240) };
        foreach (var p in new[] { _svcPill, _authPill, _reqPill }) { p.Dock = DockStyle.None; p.Margin = Padding.Empty; pillHost.Controls.Add(p); }
        void LayoutPills()
        {
            var f = DeviceDpi / 96f;
            int x = (int)(14 * f), w = Math.Max(60, pillHost.ClientSize.Width - 2 * x), h = (int)(32 * f), gap = (int)(6 * f);
            var y = (int)(6 * f);
            foreach (var p in new[] { _svcPill, _authPill, _reqPill }) { p.SetBounds(x, y, w, h); y += h + gap; }
        }
        pillHost.Resize += (_, _) => LayoutPills();
        pillHost.HandleCreated += (_, _) => LayoutPills();
        sidebar.Controls.Add(_side);       // Fill first, then the edge-docked panel
        sidebar.Controls.Add(pillHost);
        _side.OrgPicked += key => { if (key != _view) SwitchAccount(key); };
        _side.FilterPicked += id => { _filter = id; _selected = -1; RefreshAll(); };

        // ---- right: toolbar, cards, notes, bottom bar, colour key
        var right = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Page };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.FromArgb(249, 252, 252) };
        toolbar.Paint += (_, e) => { using var p = new Pen(AppTheme.Line); e.Graphics.DrawLine(p, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1); };
        foreach (Control c in new Control[] { _orgTitle, _orgSub, _search, _btnUsage, _btnSignIn }) toolbar.Controls.Add(c);
        void LayoutToolbar()
        {
            var f = DeviceDpi / 96f;
            int m = (int)(22 * f), bh = (int)(34 * f), top = (int)(14 * f), gap = (int)(10 * f);
            var x = toolbar.ClientSize.Width - m;
            _btnSignIn.SetBounds(x - (int)(190 * f), top, (int)(190 * f), bh); x = _btnSignIn.Left - gap;
            _btnUsage.SetBounds(x - (int)(130 * f), top, (int)(130 * f), bh); x = _btnUsage.Left - gap;
            var sw = Math.Min((int)(220 * f), Math.Max((int)(120 * f), x - m - (int)(240 * f)));
            var sh = (int)(32 * f); _search.SetBounds(x - sw, top + (bh - sh) / 2, sw, sh); x = _search.Left - gap;
            var tw = Math.Max((int)(120 * f), x - m);
            _orgTitle.SetBounds(m, (int)(10 * f), tw, (int)(34 * f));
            _orgSub.SetBounds(m, (int)(48 * f), Math.Max((int)(200 * f), toolbar.ClientSize.Width - 2 * m), (int)(24 * f));
        }
        toolbar.Resize += (_, _) => LayoutToolbar();
        toolbar.HandleCreated += (_, _) => LayoutToolbar();
        _search.TextChanged += (_, _) => { _selected = -1; RefreshAll(); };
        _btnSignIn.Click += async (_, _) => await SignInOrgAsync(_view);
        _btnUsage.Click += (_, _) => RefreshUsageNow();
        SetTip(_btnUsage, "Read CPU, memory and disk use of every reachable server in this organization now. Servers in use refresh by themselves about every 5 minutes.");
        SetTip(_btnSignIn, "Sign in to this organization again in your own browser. One sign-in covers all of its AWS accounts.");

        var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = AppTheme.Page, Padding = new Padding(22, 0, 14, 0) };
        _detail.AutoSize = false; _detail.Dock = DockStyle.Fill; _detail.TextAlign = ContentAlignment.MiddleLeft; _detail.MaximumSize = Size.Empty; _detail.Margin = Padding.Empty;
        detailHost.Controls.Add(_detail);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.White };
        bar.Paint += (_, e) => { using var p = new Pen(AppTheme.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        foreach (var b in new[] { _btnDisconnect, _btnAdd }) { b.AutoSize = false; b.Height = 38; bar.Controls.Add(b); }
        _btnDisconnect.Width = 150; _btnAdd.Width = 250;
        _btnAdd.Tag = "primary";
        SetTip(_btnAdd, "Open the setup wizard to find servers in your AWS accounts and choose which ones to connect to.");
        SetTip(_btnDisconnect, "Switch every connection off, or back on. Connections open together when SSMConnect starts and stay open until you stop them or close the app. Your servers in AWS are not touched.");
        _btnAdd.Click += (_, _) => OpenSetup();
        _btnDisconnect.Click += async (_, _) => await ServiceActionAsync(_mgr.IsRunning ? "stop" : "start");
        void LayoutBar()
        {
            var f = DeviceDpi / 96f;
            int y = (bar.Height - _btnAdd.Height) / 2;
            _btnDisconnect.Location = new Point((int)(22 * f), y);
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

        right.Controls.Add(_cardHost);   // Fill first (front of the stack), then the edge-docked panels
        right.Controls.Add(toolbar);
        right.Controls.Add(detailHost);
        right.Controls.Add(bar);
        right.Controls.Add(_legend);
        Controls.Add(right);
        Controls.Add(sidebar);
        Controls.Add(_updateBar);
        _cardHost.SizeChanged += (_, _) => LayoutCards();
    }

    void LayoutCards()
    {
        if (_laying) return;
        _laying = true;
        try
        {
            var f = DeviceDpi / 96f;
            var gap = (int)(4 * f);
            var rowGap = (int)(4 * f);
            var cardH = ServerCard.HeightFor(f);
            var headH = (int)(26 * f);
            var headTop = (int)(8 * f);
            var padL = _cardHost.Padding.Left;
            var padT = _cardHost.Padding.Top;
            var items = _cardHost.Controls.Cast<Control>().Where(c => c is ServerCard || (c is Label l && _groupHeads.Contains(l))).ToList();
            var offY = _cardHost.AutoScrollPosition.Y;

            int Run(int availW, bool apply)
            {
                var cols = Math.Max(1, availW / (int)(360 * f));
                var w = Math.Max((int)(330 * f), (availW - gap * (cols - 1)) / cols);
                int y = padT, col = 0;
                foreach (var c in items)
                {
                    if (c is ServerCard)
                    {
                        if (apply) { c.SetBounds(padL + col * (w + gap), y + offY, w, cardH); }
                        if (++col == cols) { col = 0; y += cardH + rowGap; }
                    }
                    else
                    {
                        if (col > 0) { col = 0; y += cardH + rowGap; }
                        y += headTop;
                        if (apply) c.SetBounds(padL, y + offY, Math.Max(availW, 200), headH);
                        y += headH + (int)(2 * f);
                    }
                }
                if (col > 0) y += cardH + rowGap;
                return y + (int)(12 * f);
            }

            // reserve the scroll bar's width up front when the cards do not fit, so the column count never flips back and forth
            var avail = _cardHost.Width - padL - _cardHost.Padding.Right - 2;
            if (Run(avail, false) > _cardHost.ClientSize.Height) avail -= SystemInformation.VerticalScrollBarWidth;
            var total = Run(avail, false);
            _cardHost.SuspendLayout();
            _cardHost.AutoScrollMinSize = new Size(0, total);
            offY = _cardHost.AutoScrollPosition.Y;
            Run(avail, true);
            _cardHost.ResumeLayout();
        }
        finally { _laying = false; }
    }

    // ---- server cards ------------------------------------------------------------------------
    bool RefreshToolPaths(bool force = false)
    {
        if (!force && DateTime.UtcNow - _toolsChecked < TimeSpan.FromMinutes(2)) return false;
        _toolsChecked = DateTime.UtcNow;
        var (p, w, m) = (_puttyPath, _winscpPath, _mobaPath);
        _puttyPath = ToolDetector.PuttyPath;
        _winscpPath = ToolDetector.WinScpPath;
        _mobaPath = ToolDetector.MobaXtermPath;
        return p != _puttyPath || w != _winscpPath || m != _mobaPath;
    }

    List<CardAction> ActionsFor(TunnelConfig? t, bool up)
    {
        if (t == null) return new();
        if (t.IsRdp) return new() { new("rdp", "Remote Desktop", up), new("pwd", "Get password", up) };
        var list = new List<CardAction>();
        if (_puttyPath != null) list.Add(new("putty", "PuTTY", up));
        if (_winscpPath != null) list.Add(new("winscp", "WinSCP", up));
        if (_mobaPath != null) list.Add(new("moba", "MobaXterm", up));
        if (list.Count == 0) list.Add(new("copy", "Copy session name", true));
        return list;
    }

    void SelectCard(int index)
    {
        _selected = index;
        for (var i = 0; i < _cards.Count; i++) _cards[i].Selected = i == index;
        ShowDetail();
    }

    async void OnCardAction(ServerCard card, string id)
    {
        var i = _cards.IndexOf(card);
        if (i < 0) return;
        SelectCard(i);
        var t = SelectedTunnel();
        if (t == null) return;
        var session = ToolConfigurator.SessionName(t);
        try
        {
            if (id == "signin") { await SignInOrgAsync(PKey(t.Profile)); return; }
            if (id is "putty" or "winscp" or "moba" or "rdp")
            {
                var guard = t.InstanceId + "|" + t.LocalPort;
                if (!_launching.Add(guard)) return;   // already opening this one
                try
                {
                    if (!await EnsureOpenAsync(t))
                    {
                        _mgr.StateOf(t, out var why);
                        MessageBox.Show(this, $"The secure connection to \"{t.Name}\" did not open.\n\n{why}", "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    // the tunnel is green, but the login must work too: find the key, test the user, fix the saved sessions
                    if (id != "rdp" && !await LoginCheck.EnsureAsync(this, t, _cfg)) return;
                    if (id is "putty" or "winscp" && !PpkReady(t)) return;
                }
                finally { _launching.Remove(guard); }
            }
            switch (id)
            {
                case "rdp": OpenRdp(); break;
                case "pwd": _ = CopyWindowsPasswordAsync(); break;
                case "putty" when _puttyPath != null:
                {
                    var ppk = string.IsNullOrWhiteSpace(t.KeyFile) ? null : KeyConverter.EnsurePpk(t.KeyFile!);
                    var pargs = $"-load \"{session}\" -l \"{t.UserName}\"" + (ppk != null ? $" -i \"{ppk}\"" : "");
                    Process.Start(new ProcessStartInfo(_puttyPath, pargs) { UseShellExecute = false });
                    break;
                }
                case "winscp" when _winscpPath != null: Process.Start(new ProcessStartInfo(_winscpPath, $"\"{session}\"") { UseShellExecute = false }); break;
                case "moba" when _mobaPath != null: _ = OpenMobaAsync(t, _mobaPath); break;
                case "transfer": OpenTransfer(t); break;
                case "copy":
                    Clipboard.SetText(session);
                    MessageBox.Show(this, $"The saved session name \"{session}\" is on your clipboard. Open PuTTY, WinSCP or MobaXterm and choose it from the list of saved sessions.", "Session name copied");
                    break;
            }
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the tool: " + ex.Message, "SSMConnect"); }
    }

    /// <summary>PuTTY and WinSCP only read .ppk keys. False (with a plain explanation) when this server's key cannot be turned into one.</summary>
    bool PpkReady(TunnelConfig t)
    {
        if (!string.IsNullOrWhiteSpace(t.KeyFile) && KeyConverter.EnsurePpk(t.KeyFile!) != null) return true;
        MessageBox.Show(this, "PuTTY and WinSCP can only use .ppk keys, and SSMConnect could not convert this server's key (it may be protected by a passphrase or use a newer key format).\n\nMobaXterm and Copy files work with it. Or open the key in PuTTYgen, save it as a .ppk, and choose that file for this server.", "SSMConnect");
        return false;
    }

    /// <summary>Opens the server's secure connection if it is not open yet and waits (up to about a minute) until its local port answers.</summary>
    async Task<bool> EnsureOpenAsync(TunnelConfig t)
    {
        if (!_mgr.IsRunning) _mgr.Start();
        _mgr.Open(t);
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            var st = _mgr.StateOf(t, out _);
            if (st == TunnelStates.Connected && _mgr.IsOpen(t)) return true;
            if (st == TunnelStates.Stopped) return false;
            await Task.Delay(400);
        }
        return false;
    }

    /// <summary>
    /// Opens the server in MobaXterm. When MobaXterm is closed, the saved sessions are (re)written first and the
    /// session is opened by name; when it is already running its settings file cannot be edited safely, so the
    /// connection is opened in a new tab with an ssh command instead.
    /// </summary>
    async Task OpenMobaAsync(TunnelConfig t, string exe)
    {
        var session = ToolConfigurator.SessionName(t);
        try
        {
            var running = Process.GetProcessesByName("MobaXterm").Length > 0;
            string args;
            if (!running && ToolConfigurator.ConfigureMobaXterm(_cfg.Tunnels))
                args = $"-bookmark \"{session}\"";
            else
            {
                var key = "";
                if (!string.IsNullOrWhiteSpace(t.KeyFile) && !t.KeyFile!.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
                {
                    var k = t.KeyFile!.Replace('\\', '/');
                    if (k.Length > 2 && k[1] == ':') k = "/drives/" + char.ToLowerInvariant(k[0]) + k.Substring(2);
                    key = $" -i '{k}'";
                }
                args = $"-newtab \"ssh -p {t.LocalPort}{key} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null {t.UserName}@127.0.0.1\"";
            }
            AppLog.Info($"MobaXterm: {exe} {args}");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            await Task.Delay(5000);
            if (Process.GetProcessesByName("MobaXterm").Length == 0)
            {
                AppLog.Warn("MobaXterm did not start with those arguments; starting it plainly.");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
                try { Clipboard.SetText(session); } catch { }
                MessageBox.Show(this, $"MobaXterm opened, but it did not take the connection. The session name \"{session}\" is on your clipboard.", "SSMConnect");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("MobaXterm launch failed: " + ex);
            MessageBox.Show(this, "Could not open MobaXterm: " + ex.Message, "SSMConnect");
        }
    }

    void OpenTransfer(TunnelConfig? preselect = null)
    {
        var ssh = _cfg.Tunnels.Where(x => !x.IsRdp && x.Enabled).ToList();
        if (ssh.Count == 0) { MessageBox.Show(this, "Set up at least one Linux server in SSMConnect first.", "SSMConnect"); return; }
        bool Avail(int port)
        {
            var t = ssh.FirstOrDefault(x => x.LocalPort == port);
            if (t == null || _signedOut.Contains(PKey(t.Profile))) return false;
            var st = _mgr.StateOf(t, out _);
            return st == TunnelStates.Ready || st == TunnelStates.Connected;
        }
        if (!ssh.Any(x => Avail(x.LocalPort)))
        { MessageBox.Show(this, "No servers are available right now. Sign in (Sign in again) until the cards turn green, then try again.", "SSMConnect"); return; }
        // Servers are grouped by their top-level organization (the company sign-in), and only one group is linked at a time.
        var orgs = ssh.GroupBy(x => PKey(x.Profile))
            .Select(g => new TransferDialog.Org(g.Key, PLabel(g.First().Profile), g.ToList()))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
        // Each server is listed with the AWS account it lives in, like the dashboard's account sections.
        string Label(TunnelConfig t)
        {
            var acct = _cfg.AliasOr(t.Profile, t.Profile);
            return string.IsNullOrWhiteSpace(acct) ? t.Name : $"{t.Name}    -  {acct}";
        }
        using var dlg = new TransferDialog(orgs, Avail, Label, preselect, _view, EnsureOpenAsync, (tt, owner) => LoginCheck.EnsureAsync(owner ?? this, tt, _cfg));
        dlg.ShowDialog(this);
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
        aws.DropDownItems.Add(Item("&Manage accounts (rename, remove)...", (_, _) => ManageAccounts()));
        aws.DropDownItems.Add(Item("&Sign in again", async (_, _) => await SignInAsync()));

        var conns = new ToolStripMenuItem("&Connections");
        conns.DropDownItems.AddRange(new ToolStripItem[] { _mStart, _mStop, _mRestart });
        _mStart.Click += async (_, _) => await ServiceActionAsync("start");
        _mStop.Click += async (_, _) => await ServiceActionAsync("stop");
        _mRestart.Click += async (_, _) => await ServiceActionAsync("restart");

        var servers = new ToolStripMenuItem("&Servers");
        servers.DropDownItems.Add(Item("Scan for &new or changed servers", async (_, _) => await ScanForChangesAsync(force: true, interactive: true)));
        servers.DropDownItems.Add(Item("What &changed since the last scan...", (_, _) => ShowServerChanges()));
        servers.DropDownItems.Add(new ToolStripSeparator());
        var mAuto = new ToolStripMenuItem("Read CPU, memory and disk use &automatically") { CheckOnClick = true, Checked = _cfg.ReadUsage };
        var mProd = new ToolStripMenuItem("...also on &production servers") { CheckOnClick = true, Checked = _cfg.ReadUsageOnProd, Enabled = _cfg.ReadUsage };
        mAuto.Click += (_, _) => { SaveUsageSetting(a => a.ReadUsage = mAuto.Checked); mProd.Enabled = mAuto.Checked; };
        mProd.Click += (_, _) => SaveUsageSetting(a => a.ReadUsageOnProd = mProd.Checked);
        servers.DropDownItems.Add(mAuto);
        servers.DropDownItems.Add(mProd);
        servers.DropDownItems.Add(new ToolStripSeparator());
        servers.DropDownItems.Add(Item("&File transfer: browse and copy files...", (_, _) => OpenTransfer(SelectedTunnel())));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&About SSMConnect", (_, _) => ShowAbout()));
        help.DropDownItems.Add(Item("Check for &updates", async (_, _) => await CheckForUpdateAsync(manual: true)));
        help.DropDownItems.Add(Item("Open &log file", (_, _) => OpenLog()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&Uninstall SSMConnect...", (_, _) => UninstallClicked()));

        menu.Items.AddRange(new ToolStripItem[] { file, aws, conns, servers, help });
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    void SaveUsageSetting(Action<AppConfig> apply)
    {
        try { var c = ConfigStore.Load() ?? _cfg; apply(c); ConfigStore.Save(c); apply(_cfg); }
        catch (Exception ex) { AppLog.Warn("Could not save the usage setting: " + ex.Message); }
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
        _icons[Level.Blue] = MakeStatusIcon(Color.FromArgb(60, 130, 230));

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

        // pills at the bottom of the sidebar
        if (running) SetPill(_svcPill, "Connections: on", Level.Green, "Every connection opens when SSMConnect starts and stays open until you stop them or close the app.");
        else SetPill(_svcPill, "Connections: switched off", Level.Red, "Click Start connections to switch them on.");
        var haveAws = AwsCli.FindAws() != null;
        var havePlugin = AwsCli.FindPlugin() != null;
        if (haveAws && havePlugin) SetPill(_reqPill, "AWS tools: installed", Level.Green, "AWS CLI and the Session Manager plugin are installed.");
        else SetPill(_reqPill, "AWS tools: missing " + (haveAws ? "plugin" : havePlugin ? "CLI" : "CLI + plugin"), Level.Red, "Click Add / change servers to install them automatically.");
        SetPill(_authPill, _authText, _authLevel, _authTip);
        if (!_authBusy && DateTime.UtcNow >= _nextAuthCheck) _ = CheckAuthAsync();

        // one row per server (every organization), then the window shows the chosen organization with the chosen filter
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
            else if (_signedOut.Contains(PKey(t.Profile))) row = new Row(label, t.InstanceId, t.LocalPort, "Signed out", "Your company sign-in has expired. Click Sign in again.", null, Level.Red, "sign in needed");
            else if (!running) row = new Row(label, t.InstanceId, t.LocalPort, "Paused", "Connections are switched off. Click Start connections.", null, Level.Grey);
            else if (stale || s == null) row = new Row(label, t.InstanceId, t.LocalPort, "Connecting", "Opening secure session to the server...", null, Level.Amber, "starting up");
            else row = s.State switch
            {
                TunnelStates.Connected when listeners.Contains(t.LocalPort) =>
                    new Row(label, t.InstanceId, t.LocalPort, "Connected", s.Message, s.SinceUtc, Level.Green,
                        s.Clients > 0 ? (s.Clients == 1 ? "1 tool attached" : s.Clients + " tools attached") : ""),
                TunnelStates.Connected =>
                    new Row(label, t.InstanceId, t.LocalPort, "Checking", "Tunnel reported connected; confirming the local port is open...", s.SinceUtc, Level.Amber),
                TunnelStates.Ready => new Row(label, t.InstanceId, t.LocalPort, "Ready", s.Message, null, Level.Blue),
                TunnelStates.Stopped => new Row(label, t.InstanceId, t.LocalPort, "Stopped", s.Message, s.SinceUtc, Level.Red, "stopped after 3 tries"),
                TunnelStates.Error => new Row(label, t.InstanceId, t.LocalPort, "Problem", s.Message, s.SinceUtc, Level.Red, s.Message),
                TunnelStates.Retrying => new Row(label, t.InstanceId, t.LocalPort, "Connecting", s.Message, s.SinceUtc, Level.Amber, $"try {Math.Max(1, s.Attempt)} of 3"),
                TunnelStates.Disabled => new Row(label, t.InstanceId, t.LocalPort, "Off", s.Message, null, Level.Grey),
                _ => new Row(label, t.InstanceId, t.LocalPort, "Connecting", s.Message, s.SinceUtc, Level.Amber, "opening the secure session"),
            };
            rows.Add(row);
        }

        // the chosen organization, then the filter and the search box
        var inOrg = Enumerable.Range(0, ordered.Count).Where(i => PKey(ordered[i].Profile) == _view).ToList();
        var search = _search.Text.Trim();
        bool Passes(int i)
        {
            var r = rows[i];
            var ok = _filter switch
            {
                "connected" => r.Level == Level.Green,
                "ready" => r.Level == Level.Blue,
                "attn" => r.Level == Level.Red || r.Level == Level.Amber,
                "prod" => EnvironmentClassifier.IsProd(EnvOf(ordered[i])),
                _ => true,
            };
            if (!ok) return false;
            return search.Length == 0 || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || r.InstanceId.Contains(search, StringComparison.OrdinalIgnoreCase);
        }
        var shown = inOrg.Where(Passes).ToList();
        var viewProfiles = shown.Select(i => ordered[i].Profile).ToList();
        var memberCount = ViewMembers().Count;
        _rowGroups = viewProfiles.Distinct().Count() > 1 || (memberCount > 1 && _filter == "all" && search.Length == 0) ? viewProfiles : new List<string>();
        UpdateGrid(shown.Select(i => rows[i]).ToList());
        UpdateSide(ordered, rows, inOrg);
        UpdateToolbar(ordered, rows, inOrg);
        AutoUsage(ordered, rows);

        // overall (tray icon and tooltip cover every server)
        var up = rows.Count(r => r.Level == Level.Green || r.Level == Level.Blue);
        var total = rows.Count(r => r.StatusText != "Off");
        var connected = rows.Count(r => r.Level == Level.Green);
        Level overall;
        if (!running) overall = Level.Grey;
        else if (total == 0) overall = Level.Amber;
        else if (rows.Any(r => r.Level == Level.Red)) overall = Level.Red;
        else if (up == total) overall = Level.Green;
        else overall = Level.Amber;

        string tip = overall switch
        {
            Level.Green => $"SSMConnect: {connected} connected",
            Level.Red => $"SSMConnect: attention needed ({up} of {total} available)",
            Level.Grey => "SSMConnect: connections switched off",
            _ => total == 0 ? "SSMConnect: no servers set up" : $"SSMConnect: {up} of {total} available, connecting...",
        };
        _tray.Icon = _icons[overall];
        _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        _trayHeader.Text = tip;
        Text = connected > 0 ? $"SSMConnect Dashboard - {connected} connected" : "SSMConnect Dashboard";

        foreach (var m in new[] { _mStart, _trayStart }) m.Enabled = !_busy && !running;
        foreach (var m in new[] { _mStop, _trayStop }) m.Enabled = !_busy && running;
        foreach (var m in new[] { _mRestart, _trayRestart }) m.Enabled = !_busy;
        _btnDisconnect.Text = running ? "Stop connections" : "Start connections";
        _btnDisconnect.Enabled = !_busy;

        NotifyOnChange(overall, rows);
        _overall = overall;
    }

    /// <summary>Colour of an organization's dot in the sidebar: the most important thing going on among its servers.</summary>
    CardLevel OrgDot(string key, List<TunnelConfig> ordered, List<Row> rows)
    {
        if (_signedOut.Contains(key)) return CardLevel.Red;
        var mine = Enumerable.Range(0, ordered.Count).Where(i => PKey(ordered[i].Profile) == key).Select(i => rows[i]).Where(r => r.StatusText != "Off").ToList();
        if (mine.Any(r => r.Level == Level.Red)) return CardLevel.Red;
        if (mine.Any(r => r.Level == Level.Amber)) return CardLevel.Amber;
        if (mine.Any(r => r.Level == Level.Green)) return CardLevel.Green;
        if (mine.Any(r => r.Level == Level.Blue)) return CardLevel.Blue;
        return CardLevel.Grey;
    }

    void UpdateSide(List<TunnelConfig> ordered, List<Row> rows, List<int> inOrg)
    {
        var orgs = _prim.Select(p => new SideList.Org(p.Key, p.Label, p.Count, OrgDot(p.Key, ordered, rows))).ToList();
        var mine = inOrg.Select(i => (T: ordered[i], R: rows[i])).ToList();
        var filters = new List<SideList.Filt>
        {
            new("all", "All servers", mine.Count, null),
            new("connected", "Connected", mine.Count(x => x.R.Level == Level.Green), CardLevel.Green),
            new("attn", "Needs attention", mine.Count(x => x.R.Level == Level.Red || x.R.Level == Level.Amber), CardLevel.Amber),
            new("prod", "PROD only", mine.Count(x => EnvironmentClassifier.IsProd(EnvOf(x.T))), CardLevel.Red),
        };
        _side.Set(orgs, _view, filters, _filter);
    }

    DateTimeOffset? OrgExpiry(string? key)
    {
        if (key == null) return null;
        if (_expiry.TryGetValue(key, out var c) && DateTime.UtcNow - c.Checked < TimeSpan.FromSeconds(30)) return c.When;
        DateTimeOffset? when = null;
        try
        {
            var session = Profiles().Where(p => AwsProfiles.PrimaryKey(p) == key).Select(p => p.SsoSession).FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (session != null) when = AwsSso.TokenExpiry(session);
        }
        catch { }
        _expiry[key] = (when, DateTime.UtcNow);
        return when;
    }

    static string Span(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "less than a minute";
        if (t.TotalHours < 1) return (int)t.TotalMinutes + " min";
        return (int)t.TotalHours + " h " + t.Minutes + " min";
    }

    void UpdateToolbar(List<TunnelConfig> ordered, List<Row> rows, List<int> inOrg)
    {
        var label = _prim.FirstOrDefault(x => x.Key == _view).Label ?? "No organization yet";
        if (_orgTitle.Text != label) _orgTitle.Text = label;
        var out_ = _view != null && _signedOut.Contains(_view);
        var servers = inOrg.Count;
        var connected = inOrg.Count(i => rows[i].Level == Level.Green);
        string sub;
        if (_cfg.Tunnels.Count == 0) sub = "No servers yet. Click \"Add / change servers...\" to find your servers.";
        else if (out_) sub = $"Signed out  \u00B7  sign in to connect  \u00B7  {servers} server{(servers == 1 ? "" : "s")}";
        else
        {
            var exp = OrgExpiry(_view);
            var left = exp.HasValue ? "  \u00B7  " + Span(exp.Value - DateTimeOffset.UtcNow) + " left" : "";
            var used = inOrg.Select(i => UsageOf(ordered[i])?.UsageUtc ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();
            sub = $"Signed in{left}  \u00B7  {servers} server{(servers == 1 ? "" : "s")}  \u00B7  {connected} connected  \u00B7  " +
                  (used > DateTime.MinValue ? "usage updated " + Ago(used) + " ago" : "usage not read yet");
        }
        if (_orgSub.Text != sub) _orgSub.Text = sub;

        var text = out_ ? "Sign in to " + label : "Sign in again";
        if (_btnSignIn.Text != text) _btnSignIn.Text = text;
        if (_signInIsPrimary != out_) { _signInIsPrimary = out_; ButtonLook.Attach(_btnSignIn, out_); _btnSignIn.Font = out_ ? AppTheme.Semi() : AppTheme.Body(); }
        _btnSignIn.Enabled = _view != null;
        int busy; lock (_usageBusy) busy = _usageBusy.Count;
        var usageText = busy > 0 ? "Reading usage..." : "Refresh usage";
        if (_btnUsage.Text != usageText) _btnUsage.Text = usageText;
        _btnUsage.Enabled = busy == 0 && !out_ && inOrg.Count > 0;
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
            Level.Blue => (Color.FromArgb(214, 230, 250), Color.FromArgb(14, 70, 150)),
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
        RefreshDiscoMaps();
        var f = DeviceDpi / 96f;
        _cardHost.SuspendLayout();
        try
        {
            while (_cards.Count > rows.Count) { var c = _cards[^1]; _cards.RemoveAt(_cards.Count - 1); _cardHost.Controls.Remove(c); c.Dispose(); }
            while (_cards.Count < rows.Count)
            {
                var c = new ServerCard { Width = (int)(360 * f), Height = ServerCard.HeightFor(f) };
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
                var available = r.Level == Level.Green || r.Level == Level.Blue;
                var info = tun == null ? null : UsageOf(tun);
                var os = info != null && info.OsName.Length > 0 ? info.OsName : OsOf(tun);
                var spec = info?.SpecLine() ?? "";
                if (spec.Length == 0 && os.Length > 0) spec = os;
                _cards[i].Data = new CardData
                {
                    Name = r.Name, InstanceId = r.InstanceId, Port = r.Port.ToString(), StatusText = r.StatusText, Detail = r.Detail, Message = r.Message,
                    Since = r.Since.HasValue ? Ago(r.Since.Value) : "", KnownType = tun != null, IsWindows = tun?.IsRdp ?? false,
                    Environment = EnvOf(tun), IsProd = EnvironmentClassifier.IsProd(EnvOf(tun)),
                    Spec = spec, OsKey = OsIcons.KeyFor(os, tun?.IsRdp ?? false),
                    Cpu = info?.CpuPct ?? -1, Ram = info?.RamPct ?? -1, Disk = info?.DiskPct ?? -1,
                    UsageTip = info == null ? "Click Refresh usage to read it." : info.Note,
                    Level = r.Level switch { Level.Green => CardLevel.Green, Level.Blue => CardLevel.Blue, Level.Amber => CardLevel.Amber, Level.Red => CardLevel.Red, _ => CardLevel.Grey },
                    Actions = ActionsFor(tun, available),
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
            head.Text = $"{_cfg.AliasOr(names[g], names[g]).ToUpperInvariant()}    {count} server{(count == 1 ? "" : "s")}";
            if (!_cardHost.Controls.Contains(head)) _cardHost.Controls.Add(head);
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
        return p == null ? _cfg.AliasOr("key:" + profile, profile) : AwsProfiles.PrimaryLabel(_cfg, p, Profiles());
    }

    /// <summary>The AWS accounts (organizations) under the sign-in being shown, one per AWS account even if an account was saved twice.</summary>
    List<ProfileInfo> ViewMembers()
    {
        if (_view == null) return new List<ProfileInfo>();
        var group = Profiles().Where(x => AwsProfiles.PrimaryKey(x) == _view).ToList();
        return AwsProfiles.OneEach(group, _cfg.Tunnels.Select(t => t.Profile).ToHashSet(StringComparer.Ordinal));
    }

    int ServersIn(ProfileInfo m)
    {
        var all = Profiles();
        return _cfg.Tunnels.Count(t => t.Profile == m.Name || (m.AccountId != null && all.FirstOrDefault(p => p.Name == t.Profile)?.AccountId == m.AccountId));
    }

    void SetTip(Control c, string text)
    {
        if (_tipText.TryGetValue(c, out var old) && old == text) return;
        _tipText[c] = text;
        _tips.SetToolTip(c, text);
    }

    /// <summary>Keeps the account switcher in step with the saved servers. It lists primary accounts only; the current one is the default.</summary>
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
        _prim.Clear();
        _prim.AddRange(prim);
        _chip.Title = prim.FirstOrDefault(x => x.Key == _view).Label ?? "";
    }

    void SwitchAccount(string key)
    {
        if (key == _view) return;
        _view = key;
        _viewChosen = true;
        // remember it as the current account (also what the setup wizard opens on)
        var mine = _cfg.Tunnels.FirstOrDefault(t => PKey(t.Profile) == key)?.Profile;
        if (mine != null)
        {
            try { var c = ConfigStore.Load() ?? _cfg; c.Profile = mine; ConfigStore.Save(c); _cfg.Profile = mine; _cfgStamp = DateTime.MinValue; } catch { }
        }
        _selected = -1;
        RefreshAll();
    }

    void ManageAccounts()
    {
        ShowDashboard();
        using var dlg = new AccountsDialog();
        dlg.ShowDialog(this);
        if (!dlg.Changed) return;
        _cfg = ConfigStore.Load() ?? _cfg;
        _cfgStamp = DateTime.MinValue;
        _profStamp = DateTime.MinValue;
        _viewChosen = false;
        _nextAuthCheck = DateTime.MinValue;
        _mgr.Poke();
        RefreshAll();
    }

    // what kind of server each one is: its saved environment, else what the last scan read from the server's tags
    Dictionary<string, string> _envMap = new();
    DateTime _envStamp = DateTime.MinValue;

    void RefreshDiscoMaps()
    {
        try
        {
            var st = File.Exists(DiscoveryCache.Path) ? File.GetLastWriteTimeUtc(DiscoveryCache.Path) : DateTime.MinValue;
            if (st == _envStamp) return;
            _envStamp = st;
            _envMap = new Dictionary<string, string>(StringComparer.Ordinal);
            _osMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rec in DiscoveryCache.Load().Scans)
                foreach (var sv in rec.Servers)
                {
                    if (!string.IsNullOrWhiteSpace(sv.Environment)) _envMap[rec.Profile + "|" + sv.InstanceId] = sv.Environment;
                    if (!string.IsNullOrWhiteSpace(sv.OsName)) _osMap[rec.Profile + "|" + sv.InstanceId] = sv.OsName;
                }
        }
        catch { }
    }

    string OsOf(TunnelConfig? t)
    {
        if (t == null) return "";
        RefreshDiscoMaps();
        return _osMap.TryGetValue(t.Profile + "|" + t.InstanceId, out var o) ? o : "";
    }

    string EnvOf(TunnelConfig? t)
    {
        if (t == null) return "";
        if (!string.IsNullOrWhiteSpace(t.Environment)) return EnvironmentClassifier.Normalise(t.Environment);
        RefreshDiscoMaps();
        return _envMap.TryGetValue(t.Profile + "|" + t.InstanceId, out var e) ? e : EnvironmentClassifier.Classify(Array.Empty<KeyValuePair<string, string>>(), t.Name);
    }

    void ShowDetail()
    {
        // only worth a line of text when something is not right
        if (_selected < 0 || _selected >= _rows.Count)
        {
            // nothing selected: point out organization accounts under this sign-in that have no servers added yet
            var members = ViewMembers();
            var empty = members.Count(m => ServersIn(m) == 0);
            _detail.Text = members.Count > 1 && empty > 0 && _cfg.Tunnels.Count > 0
                ? $"{empty} more account{(empty == 1 ? "" : "s")} under this sign-in {(empty == 1 ? "has" : "have")} no servers added yet. Click Add / change servers to include {(empty == 1 ? "it" : "them")}."
                : "";
            return;
        }
        if (_rows[_selected].Level == Level.Green) { _detail.Text = ""; return; }
        var r = _rows[_selected];
        _detail.Text = $"{r.Name}: {r.Message}";
    }

    async Task CheckAuthAsync()
    {
        var gen = _authGen;
        _authBusy = true;
        try
        {
            if (AwsCli.FindAws() == null) { _authText = "Sign-in: install AWS CLI"; _authLevel = Level.Red; _authTip = "The AWS CLI is not installed."; return; }
            if (_cfg.Tunnels.Count == 0) { _authText = "Sign-in: not set up yet"; _authLevel = Level.Grey; _authTip = "Set up your connections to choose an AWS profile."; return; }
            // one check per organization: one company sign-in covers all of its AWS accounts
            var orgs = _cfg.Tunnels.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Profile)).GroupBy(t => PKey(t.Profile)).ToList();
            if (orgs.Count == 0) { _authText = "Sign-in: no account chosen"; _authLevel = Level.Amber; _authTip = "Click Add / change servers to choose an AWS account."; return; }
            var failed = new HashSet<string>();
            var names = new List<string>();
            var firstProblem = "";
            foreach (var g in orgs)
            {
                var first = g.First();
                var r = await AwsCli.WhoAmIAsync(new AppConfig { Profile = first.Profile, Region = first.Region });
                if (!r.Ok) { failed.Add(g.Key); names.Add(PLabel(first.Profile)); if (firstProblem.Length == 0) firstProblem = AwsCli.FriendlyError(r.Error); }
            }
            if (gen != _authGen) return;   // a sign-in finished while this check ran: its answer is out of date
            _signedOut = failed;
            _authChecked = true;
            if (failed.Count == 0)
            {
                _authText = "AWS sign-in: OK";
                _authLevel = Level.Green;
                _authTip = "Signed in: " + string.Join(", ", orgs.Select(o => PLabel(o.First().Profile)));
            }
            else
            {
                _authText = failed.Count == 1 ? "Sign-in needed (1 org)" : $"Sign-in needed ({failed.Count} orgs)";
                _authLevel = Level.Red;
                _authTip = "Sign in again: " + string.Join(", ", names) + ". " + firstProblem;
            }
        }
        catch (Exception ex) { _authText = "AWS sign-in: unknown"; _authLevel = Level.Amber; _authTip = ex.Message; }
        finally { _authBusy = false; _nextAuthCheck = gen != _authGen ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(45); }
    }

    // ---- server size and usage ---------------------------------------------------------------
    UsageInfo? UsageOf(TunnelConfig t)
    {
        lock (_usage) return _usage.TryGetValue(UsageProbe.KeyOf(t), out var u) ? u : null;
    }

    /// <summary>Reads usage on its own for servers that are in use (a tool attached): right away when one connects, then about every 5 minutes. Sizes are read once a day.</summary>
    void AutoUsage(List<TunnelConfig> ordered, List<Row> rows)
    {
        if (!_authChecked || DateTime.UtcNow < _nextUsageCheck) return;
        _nextUsageCheck = DateTime.UtcNow.AddSeconds(30);
        var inUse = new List<TunnelConfig>();
        for (var i = 0; i < ordered.Count && i < rows.Count; i++)
        {
            var t = ordered[i];
            if (!t.Enabled || _signedOut.Contains(PKey(t.Profile)) || rows[i].Level != Level.Green) continue;
            if (!_cfg.ReadUsage || (!_cfg.ReadUsageOnProd && EnvironmentClassifier.IsProd(t.Environment))) continue;   // production is only read when asked
            var info = UsageOf(t);
            if (info == null || DateTime.UtcNow - info.UsageUtc > TimeSpan.FromMinutes(5)) inUse.Add(t);
        }
        if (inUse.Count > 0) QueueUsage(inUse, wantUsage: true);
        if (DateTime.UtcNow >= _nextSpecCheck)
        {
            _nextSpecCheck = DateTime.UtcNow.AddMinutes(10);
            var need = new List<TunnelConfig>();
            foreach (var t in ordered)
            {
                if (!t.Enabled || _signedOut.Contains(PKey(t.Profile))) continue;
                var info = UsageOf(t);
                if (info == null || DateTime.UtcNow - info.SpecUtc > TimeSpan.FromHours(24)) need.Add(t);
            }
            if (need.Count > 0) QueueUsage(need, wantUsage: false);
        }
    }

    /// <summary>The Refresh usage button: reads every reachable server of the organization being shown.</summary>
    void RefreshUsageNow()
    {
        if (_view == null) return;
        var list = _cfg.Tunnels.Where(t => t.Enabled && PKey(t.Profile) == _view && !_signedOut.Contains(PKey(t.Profile))).ToList();
        if (list.Count == 0) return;
        QueueUsage(list, wantUsage: true);
        RefreshAll();
    }

    void QueueUsage(List<TunnelConfig> list, bool wantUsage)
    {
        foreach (var t in list)
        {
            var key = UsageProbe.KeyOf(t);
            lock (_usageBusy) { if (!_usageBusy.Add(key)) continue; }
            _ = Task.Run(async () =>
            {
                await _usageGate.WaitAsync();
                try
                {
                    UsageInfo? prior;
                    lock (_usage) _usage.TryGetValue(key, out prior);
                    var copy = prior?.Clone() ?? new UsageInfo();
                    if (prior == null || DateTime.UtcNow - prior.SpecUtc > TimeSpan.FromHours(24)) copy = await UsageProbe.ReadSpecAsync(t, copy);
                    if (wantUsage) copy = await UsageProbe.ReadUsageAsync(t, copy);
                    Dictionary<string, UsageInfo> snapshot;
                    lock (_usage) { _usage[key] = copy; snapshot = new Dictionary<string, UsageInfo>(_usage); }
                    UsageProbe.Save(snapshot);
                }
                catch (Exception ex) { AppLog.Warn("Usage read failed for " + t.Name + ": " + ex.Message); }
                finally
                {
                    lock (_usageBusy) _usageBusy.Remove(key);
                    _usageGate.Release();
                    try { BeginInvoke(RefreshAll); } catch { }
                }
            });
        }
    }

    // ---- actions -----------------------------------------------------------------------------
    async Task ServiceActionAsync(string verb)
    {
        if (_busy) return;
        _busy = true;
        _orgSub.Text = verb == "start" ? "Switching connections on..." : verb == "stop" ? "Switching connections off..." : "Restarting connections...";
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
    /// Servers > What changed since the last scan. Saved PuTTY / WinSCP sessions stay exactly as they are until the person changes them.
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
                if (interactive) MessageBox.Show(this, "No AWS account is set up yet. Click Add / change servers first.", "SSMConnect");
                return;
            }
            var changes = await Task.Run(() => DiscoveryMonitor.RunAsync(cfg, force));
            var expired = DiscoveryMonitor.NeedSignIn;
            if (changes.Count > 0 && !interactive) NotifyChanges(changes);
            else if (changes.Count > 0)
                MessageBox.Show(this, $"Scan finished. {(changes.Count == 1 ? "1 change" : changes.Count + " changes")}:\n\n" + string.Join("\n", changes.Take(12)) + (changes.Count > 12 ? $"\n...and {changes.Count - 12} more (Servers > What changed since the last scan)." : ""), "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (interactive)
                MessageBox.Show(this, "Scan finished. Nothing has changed since the last scan." +
                    (expired.Count > 0 ? "\n\nNew AWS accounts under " + string.Join(", ", expired) + " could not be checked because that sign-in has expired. Use AWS > Sign in again, then scan again." : ""),
                    "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(this, "No server changes to review (" + when + ").\n\nSSMConnect checks once a day in the background. Use Servers > Scan for new or changed servers to check immediately.", "SSMConnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _cfgStamp = DateTime.MinValue;
        _nextAuthCheck = DateTime.MinValue;
        RefreshAll();
    }

    /// <summary>Menu / tray entry: signs in the organization that needs it (the one being shown first).</summary>
    async Task SignInAsync()
    {
        string? key = _view != null && _signedOut.Contains(_view) ? _view : (_signedOut.Count > 0 ? _signedOut.First() : _view);
        ShowDashboard();
        await SignInOrgAsync(key);
    }

    /// <summary>
    /// Signs in one organization (top-level company sign-in) through the person's own browser. One sign-in covers every AWS
    /// account under it. Afterwards servers that had given up get another 3 tries.
    /// </summary>
    async Task SignInOrgAsync(string? key)
    {
        if (key == null) { MessageBox.Show(this, "No AWS account is set up yet. Click Add / change servers.", "SSMConnect"); return; }
        if (AwsCli.FindAws() == null) { MessageBox.Show(this, "The AWS CLI is not installed yet. Click Add / change servers.", "SSMConnect"); return; }
        var members = Profiles().Where(p => AwsProfiles.PrimaryKey(p) == key).ToList();
        var sso = members.FirstOrDefault(p => p.Kind == ProfileKind.Sso);
        if (sso == null)
        {
            MessageBox.Show(this, "This organization uses access keys, so there is nothing to sign in to. If it fails, remove the account in AWS > Manage accounts and add it again.", "AWS sign-in", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var session = members.Select(p => p.SsoSession).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
        var allSessions = members.Select(p => p.SsoSession).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct(StringComparer.Ordinal).ToList();
        // sign in only the sessions that have no valid sign-in yet; when all look fine, the first one is renewed
        var needed = allSessions.Where(x => AwsSso.ReadAccessToken(x) == null).ToList();
        if (needed.Count == 0 && allSessions.Count > 0) needed.Add(allSessions[0]);
        if (needed.Count > 0) session = needed[0];
        using var dlg = new SignInDialog(PLabel(sso.Name), session, sso.Name, _cfg.PreferredBrowser, needed);
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            if (dlg.ChosenBrowserId != (_cfg.PreferredBrowser ?? ""))
            {
                try { var c = ConfigStore.Load() ?? _cfg; c.PreferredBrowser = dlg.ChosenBrowserId; ConfigStore.Save(c); _cfg.PreferredBrowser = dlg.ChosenBrowserId; _cfgStamp = DateTime.MinValue; } catch { }
            }
            _authGen++;
            _signedOut.Remove(key);
            _expiry.Remove(key);
            _mgr.ResetAll();
            _nextAuthCheck = DateTime.MinValue;
            _nextSpecCheck = DateTime.MinValue;
            _nextUsageCheck = DateTime.MinValue;
        }
        _mgr.Poke();
        Activate();
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
            MessageBox.Show(this, "This connection is not open yet. Click Remote Desktop on the server card and wait a few seconds.", "Remote Desktop");
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
