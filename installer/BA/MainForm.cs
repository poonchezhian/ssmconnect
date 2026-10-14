// Copyright 2026 Poonchezhian Periasamy <poonchezhian@gmail.com>
// Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;

namespace SSMConnect.BA
{
    // ------------------------------------------------------------------ look and feel
    internal static class Theme
    {
        public static readonly Color Navy1 = Color.FromArgb(10, 31, 62);
        public static readonly Color Navy2 = Color.FromArgb(22, 78, 150);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212);
        public static readonly Color AccentHover = Color.FromArgb(0, 99, 177);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Ink = Color.FromArgb(28, 36, 49);
        public static readonly Color Muted = Color.FromArgb(94, 104, 120);
        public static readonly Color Line = Color.FromArgb(228, 232, 238);
        public static readonly Color BarBack = Color.FromArgb(245, 247, 250);

        public static Font Body(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style);
        }
        public static Font Semi(float size)
        {
            try { return new Font("Segoe UI Semibold", size, FontStyle.Regular); }
            catch { return new Font("Segoe UI", size, FontStyle.Bold); }
        }
        public static Color Blend(Color from, Color to, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(from.R + (to.R - from.R) * t), (int)(from.G + (to.G - from.G) * t), (int)(from.B + (to.B - from.B) * t));
        }
        /// <summary>The SSMConnect logo (identical to the app and the .ico file).</summary>
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, r.Width * 0.24f))
            using (var b = new LinearGradientBrush(r, Color.FromArgb(70, 160, 240), Color.FromArgb(0, 120, 212), 45f))
                g.FillPath(b, path);
            Arch(g, r, 0.27f, 0.085f);
            Arch(g, r, 0.12f, 0.07f);
            g.SmoothingMode = old;
        }
        static void Arch(Graphics g, RectangleF r, float rad, float stroke)
        {
            float cx = r.X + r.Width / 2, yc = r.Y + .47f * r.Height, yb = r.Y + .74f * r.Height, rr = rad * r.Width;
            using (var path = new GraphicsPath())
            {
                path.AddLine(cx - rr, yb, cx - rr, yc);
                path.AddArc(cx - rr, yc - rr, rr * 2, rr * 2, 180, 180);
                path.AddLine(cx + rr, yc, cx + rr, yb);
                using (var pen = new Pen(Color.White, Math.Max(1.2f, stroke * r.Width)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawPath(pen, path);
            }
        }
        public static Icon LogoIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); DrawLogo(g, new RectangleF(1, 1, 30, 30)); }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal class SmoothPanel : Panel
    {
        public SmoothPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// <summary>Flat, modern button: primary (filled), secondary (outlined) or link (text only).</summary>
    internal sealed class FlatButton : Button
    {
        public enum Kind { Primary, Secondary, Link }
        Kind _kind;
        public FlatButton(Kind kind)
        {
            _kind = kind;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.Body(8.5f, FontStyle.Regular);
            Height = 34;
            Restyle();
        }
        void Restyle()
        {
            FlatAppearance.BorderSize = _kind == Kind.Secondary ? 1 : 0;
            FlatAppearance.BorderColor = Theme.Line;
            FlatAppearance.MouseOverBackColor = _kind == Kind.Primary ? Theme.AccentHover : (_kind == Kind.Secondary ? Color.FromArgb(240, 244, 249) : Theme.BarBack);
            FlatAppearance.MouseDownBackColor = FlatAppearance.MouseOverBackColor;
            if (!Enabled)
            {
                BackColor = _kind == Kind.Primary ? Color.FromArgb(190, 200, 214) : Theme.BarBack;
                ForeColor = Color.FromArgb(140, 150, 165);
                return;
            }
            switch (_kind)
            {
                case Kind.Primary: BackColor = Theme.Accent; ForeColor = Color.White; Font = Theme.Semi(8.5f); break;
                case Kind.Secondary: BackColor = Color.White; ForeColor = Theme.Ink; break;
                default: BackColor = Theme.BarBack; ForeColor = Theme.Muted; break;
            }
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Restyle(); }
        public int FitWidth()
        {
            var w = TextRenderer.MeasureText(Text, Font).Width + 44;
            return Math.Max(_kind == Kind.Link ? 72 : 104, w);
        }
    }

    /// <summary>Slim progress bar that glides to its target and shows a moving highlight while the work is open-ended.</summary>
    internal sealed class SmoothBar : Control
    {
        readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();
        double _shown, _target;
        int _phase;
        bool _indeterminate;
        public SmoothBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 8;
            _t.Interval = 16;
            _t.Tick += (s, e) =>
            {
                if (!Visible) return;
                if (_indeterminate) _phase = (_phase + 7) % (Math.Max(Width, 1) + 120);
                else
                {
                    var d = _target - _shown;
                    if (Math.Abs(d) < 0.05) { if (_shown == _target) return; _shown = _target; }
                    else _shown += d * 0.14 + (d > 0 ? 0.03 : -0.03);
                }
                Invalidate();
            };
            _t.Start();
        }
        public void SetTarget(double percent) { _indeterminate = false; _target = Math.Max(0, Math.Min(100, percent)); }
        public void Reset() { _shown = 0; _target = 0; _indeterminate = false; Invalidate(); }
        public void Indeterminate() { _indeterminate = true; _phase = 0; }
        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(track, Height / 2f))
            using (var b = new SolidBrush(Theme.Line)) g.FillPath(b, p);
            if (_indeterminate)
            {
                var w = 120;
                var r = new RectangleF(_phase - w, 0, w, Height - 1);
                var clip = new RectangleF(0, 0, Width - 1, Height - 1);
                r.Intersect(clip);
                if (r.Width > 2)
                    using (var p = Theme.Round(r, Height / 2f))
                    using (var b = new LinearGradientBrush(new RectangleF(_phase - w, 0, w, Height), Theme.Accent, Theme.Navy2, 0f)) g.FillPath(b, p);
                return;
            }
            var fw = (float)((Width - 1) * _shown / 100.0);
            if (fw >= Height - 1)
                using (var p = Theme.Round(new RectangleF(0, 0, fw, Height - 1), Height / 2f))
                using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(fw, 2), Height), Color.FromArgb(40, 150, 235), Theme.Accent, 0f)) g.FillPath(b, p);
        }
    }

    internal enum ItemState { Waiting, Active, Done, Present, Off }

    /// <summary>The list of things being set up, each with a live status.</summary>
    internal sealed class ComponentList : Control
    {
        public sealed class Item { public string Id, Name, Detail; public ItemState State; public bool Present, Selected = true, Required; }
        public readonly List<Item> Items = new List<Item>();
        readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer();
        float _angle;
        public string DoneText = "Done", ActiveText = "Installing...", WaitText = "Waiting";
        public bool PreviewOnly;
        const int RowH = 46;

        public ComponentList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _t.Interval = 30;
            _t.Tick += (s, e) =>
            {
                if (!Visible) return;
                foreach (var i in Items) if (i.State == ItemState.Active) { _angle = (_angle + 14) % 360; Invalidate(); return; }
            };
            _t.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) _t.Dispose(); base.Dispose(disposing); }

        Item ItemAt(Point p)
        {
            int n = p.Y / RowH;
            return n >= 0 && n < Items.Count ? Items[n] : null;
        }
        bool CanToggle(Item i) { return PreviewOnly && i != null && !i.Present && !i.Required; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = CanToggle(ItemAt(e.Location)) ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var i = ItemAt(e.Location);
            if (CanToggle(i)) { i.Selected = !i.Selected; Invalidate(); }
        }
        public void Set(params string[] idNameDetail)
        {
            Items.Clear();
            for (int i = 0; i + 2 < idNameDetail.Length; i += 3)
                Items.Add(new Item { Id = idNameDetail[i], Name = idNameDetail[i + 1], Detail = idNameDetail[i + 2], State = ItemState.Waiting });
            Height = Items.Count * RowH + 2;
            Invalidate();
        }
        public void SetState(string id, ItemState st)
        {
            foreach (var i in Items) if (i.Id == id) i.State = st;
            Invalidate();
        }
        public void FinishRemaining()
        {
            foreach (var i in Items) if (i.State == ItemState.Waiting || i.State == ItemState.Active) i.State = ItemState.Present;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.White);
            using (var nameFont = Theme.Semi(9f))
            using (var small = Theme.Body(8f, FontStyle.Regular))
            using (var status = Theme.Body(8.5f, FontStyle.Regular))
            {
                for (int n = 0; n < Items.Count; n++)
                {
                    var it = Items[n];
                    int y = n * RowH;
                    var box = new RectangleF(0.5f, y + 2.5f, Width - 2, RowH - 6);
                    using (var path = Theme.Round(box, 8))
                    {
                        using (var b = new SolidBrush(it.State == ItemState.Active ? Color.FromArgb(240, 247, 255) : Color.FromArgb(249, 250, 252))) g.FillPath(b, path);
                        using (var pen = new Pen(it.State == ItemState.Active ? Color.FromArgb(160, 200, 240) : Theme.Line)) g.DrawPath(pen, path);
                    }
                    var ic = new RectangleF(14, y + (RowH - 22) / 2f, 22, 22);
                    string st = null; Color sc = Theme.Muted;
                    if (PreviewOnly)
                    {
                        if (it.Present) { DrawIcon(g, ic, ItemState.Present); st = "Already installed"; sc = Theme.Green; }
                        else
                        {
                            DrawBox(g, ic, it.Selected, it.Required);
                            st = it.Required ? "Required" : (it.Selected ? "Will be installed" : "Skipped");
                            sc = it.Selected ? Theme.Accent : Theme.Muted;
                        }
                    }
                    else
                    {
                        DrawIcon(g, ic, it.State);
                        if (it.State == ItemState.Done) { st = DoneText; sc = Theme.Green; }
                        else if (it.State == ItemState.Present) { st = "Already installed"; sc = Theme.Green; }
                        else if (it.State == ItemState.Off) { st = "Skipped"; sc = Theme.Muted; }
                        else if (it.State == ItemState.Active) { st = ActiveText; sc = Theme.Accent; }
                        else st = WaitText;
                    }
                    TextRenderer.DrawText(g, it.Name, nameFont, new Point(46, y + 6), Theme.Ink, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, it.Detail, small, new Point(46, y + 25), Theme.Muted, TextFormatFlags.NoPadding);
                    if (st != null)
                    {
                        var sz = TextRenderer.MeasureText(st, status);
                        TextRenderer.DrawText(g, st, status, new Point(Width - sz.Width - 16, y + (RowH - sz.Height) / 2 - 1), sc, TextFormatFlags.NoPadding);
                    }
                }
            }
        }
        void DrawBox(Graphics g, RectangleF r, bool on, bool required)
        {
            var box = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            using (var p = Theme.Round(box, 5))
            {
                if (on)
                {
                    using (var b = new SolidBrush(required ? Color.FromArgb(120, 170, 220) : Theme.Accent)) g.FillPath(b, p);
                    using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(r.X + 6, r.Y + 11.5f), new PointF(r.X + 9.5f, r.Y + 15), new PointF(r.X + 16, r.Y + 7.5f) });
                }
                else using (var pen = new Pen(Color.FromArgb(170, 180, 195), 2f)) g.DrawPath(pen, p);
            }
        }
        void DrawIcon(Graphics g, RectangleF r, ItemState st)
        {
            if (st == ItemState.Done || st == ItemState.Present)
            {
                using (var b = new SolidBrush(Theme.Green)) g.FillEllipse(b, r);
                using (var p = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new PointF(r.X + 6, r.Y + 11.5f), new PointF(r.X + 9.5f, r.Y + 15), new PointF(r.X + 16, r.Y + 7.5f) });
            }
            else if (st == ItemState.Active)
            {
                using (var p = new Pen(Theme.Line, 2.5f)) g.DrawEllipse(p, r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
                using (var p = new Pen(Theme.Accent, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3, _angle, 100);
            }
            else
            {
                using (var p = new Pen(st == ItemState.Off ? Color.FromArgb(215, 220, 228) : Color.FromArgb(190, 198, 210), 2f)) g.DrawEllipse(p, r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3);
            }
        }
    }

    /// <summary>Left-hand brand panel: logo, name and the step tracker.</summary>
    internal sealed class BrandPanel : SmoothPanel
    {
        public string Version = "";
        public string[] Steps = new[] { "Welcome", "License", "Install", "Finish" };
        int _stage;
        public int Stage { get { return _stage; } set { _stage = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new LinearGradientBrush(ClientRectangle, Theme.Navy1, Theme.Navy2, 70f)) g.FillRectangle(b, ClientRectangle);
            // soft decorative circles
            using (var b = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
            {
                g.FillEllipse(b, Width - 90, Height - 140, 220, 220);
                g.FillEllipse(b, -70, -60, 150, 150);
            }
            Theme.DrawLogo(g, new RectangleF(28, 30, 46, 46));

            using (var f = Theme.Semi(14f)) TextRenderer.DrawText(g, "SSMConnect", f, new Point(26, 88), Color.White, TextFormatFlags.NoPadding);
            using (var f = Theme.Body(8.5f, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "Reach your AWS servers", f, new Point(28, 120), Color.FromArgb(185, 208, 235), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "without the hassle.", f, new Point(28, 136), Color.FromArgb(185, 208, 235), TextFormatFlags.NoPadding);
            }

            // step tracker
            int y0 = 206, gap = 40;
            using (var f = Theme.Body(8.5f, FontStyle.Regular))
            using (var fb = Theme.Semi(8.5f))
            {
                for (int i = 0; i < Steps.Length; i++)
                {
                    int cy = y0 + i * gap;
                    if (i < Steps.Length - 1)
                        using (var p = new Pen(Color.FromArgb(i < _stage ? 150 : 60, 255, 255, 255), 2f)) g.DrawLine(p, 38, cy + 11, 38, cy + gap - 11);
                    var c = new RectangleF(28, cy - 10, 20, 20);
                    if (i < _stage)
                    {
                        using (var b = new SolidBrush(Theme.Green)) g.FillEllipse(b, c);
                        using (var p = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLines(p, new[] { new PointF(c.X + 5, c.Y + 10.5f), new PointF(c.X + 8.5f, c.Y + 14), new PointF(c.X + 15, c.Y + 6.5f) });
                    }
                    else if (i == _stage)
                    {
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, c);
                        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, c.X + 6, c.Y + 6, 8, 8);
                    }
                    else
                    {
                        using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), 2f)) g.DrawEllipse(p, c.X + 1, c.Y + 1, 18, 18);
                    }
                    TextRenderer.DrawText(g, Steps[i], i == _stage ? fb : f, new Point(60, cy - 9),
                        i == _stage ? Color.White : (i < _stage ? Color.FromArgb(210, 225, 245) : Color.FromArgb(140, 168, 205)), TextFormatFlags.NoPadding);
                }
            }
            using (var f = Theme.Body(7.5f, FontStyle.Regular))
                TextRenderer.DrawText(g, "Version " + Version, f, new Point(28, Height - 30), Color.FromArgb(140, 168, 205), TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ the setup window
    /// <summary>One window for the whole setup: welcome, licence, progress, finish.</summary>
    internal sealed class MainForm : Form
    {
        const int BrandW = 210, PageW = 490, PageH = 384, BarH = 56, Pad = 32, InnerW = 426;

        readonly SetupApp _ba;
        readonly string _version;
        readonly bool _uninstallMode;
        readonly BrandPanel _brand = new BrandPanel();
        readonly SmoothPanel _page = new SmoothPanel();
        readonly SmoothPanel _bottom = new SmoothPanel();
        readonly Label _title = new Label();
        readonly Label _text = new Label();
        readonly Label _status = new Label();
        readonly ComponentList _list = new ComponentList();
        readonly RichTextBox _license = new RichTextBox();
        readonly CheckBox _accept = new CheckBox();
        readonly CheckBox _launch = new CheckBox();
        readonly SmoothBar _bar = new SmoothBar();
        readonly FlatButton _b1 = new FlatButton(FlatButton.Kind.Primary);
        readonly FlatButton _b2 = new FlatButton(FlatButton.Kind.Secondary);
        readonly FlatButton _b3 = new FlatButton(FlatButton.Kind.Link);
        readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer();
        bool _busy;
        readonly Dictionary<string, bool> _present = new Dictionary<string, bool>();
        readonly HashSet<string> _skip = new HashSet<string>();
        bool _installOnlyAfterRemove;
        bool _ready;
        bool _closing;
        int _animStart;
        Action _b1Click, _b2Click, _b3Click;
        int _lastStatus;
        string _verb = "Installing";   // wording follows the action actually running
        readonly bool _silent;         // Display = none (/quiet): no window, runs by itself
        readonly bool _auto;           // /quiet or /passive: no questions asked
        readonly bool _continued;      // started again by this setup after it removed an older copy
        bool _updateMode;              // "Update now" from the app, or Upgrade chosen on the existing-copy page: upgrade in place
        readonly bool _purge;          // command line RemoveSettings=1: an unattended uninstall also deletes the saved settings
        readonly RadioButton _rbUp = new RadioButton();
        readonly RadioButton _rbUn = new RadioButton();
        readonly Label _rbUpText = new Label();
        readonly Label _rbUnText = new Label();
        readonly CheckBox _keep = new CheckBox();   // "Keep my settings" (uninstall)
        string _backupDir;             // temporary copy of the saved settings while an old copy is removed
        bool _keepSettings;            // an uninstall that is running should hand the settings back afterwards
        bool _upgradeChosen, _fallbackTried;
        volatile bool _msiStarted;   // true once the SSMConnect MSI itself began running (nothing is changed on the PC before that)
        bool _sameVersion;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OSVERSIONINFOEX
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Csd;
            public ushort SpMajor, SpMinor, Suite; public byte ProductType, Reserved;
        }
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVERSIONINFOEX v);

        /// <summary>Windows 10 1607 / Server 2016 (build 14393) or later, 64-bit. Windows Installer itself reports
        /// the wrong version number to packages, so the check is made here, where the real number is available.</summary>
        static bool OsSupported()
        {
            try
            {
                var v = new OSVERSIONINFOEX { Size = Marshal.SizeOf(typeof(OSVERSIONINFOEX)) };
                if (RtlGetVersion(ref v) != 0) return true;
                return Environment.Is64BitOperatingSystem && (v.Major > 10 || (v.Major == 10 && v.Build >= 14393));
            }
            catch { return true; }
        }

        public MainForm(SetupApp ba)
        {
            _ba = ba;
            var asm = Assembly.GetExecutingAssembly().GetName().Version;
            _version = asm.Major + "." + asm.Minor + "." + asm.Build;
            _uninstallMode = ba.Command.Action == LaunchAction.Uninstall;
            _silent = ba.Command.Display == Display.None;
            _auto = ba.Command.Display != Display.Full;
            try
            {
                foreach (var a in ba.Command.GetCommandLineArgs())
                {
                    if (string.Equals(a, "/ssmupdate", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-ssmupdate", StringComparison.OrdinalIgnoreCase)) _updateMode = true;
                    if (string.Equals(a, "RemoveSettings=1", StringComparison.OrdinalIgnoreCase)) _purge = true;
                }
            }
            catch { }
            var cont = Environment.GetEnvironmentVariable("SSMCONNECT_CONTINUE");
            if (!string.IsNullOrEmpty(cont))
            {
                _continued = true;
                var xy = cont.Split(',');
                int x, y;
                if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                { StartPosition = FormStartPosition.Manual; Location = new Point(x, y); }
            }
            if (_silent)
            {
                ShowInTaskbar = false; Opacity = 0; FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual; Location = new Point(-32000, -32000);
            }

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body(8.5f, FontStyle.Regular);
            Text = "SSMConnect Setup";
            BackColor = Color.White;
            ClientSize = new Size(BrandW + PageW, PageH + BarH);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            if (!_continued && !_silent) StartPosition = FormStartPosition.CenterScreen;
            if (!_silent) Opacity = 0;   // fades in once shown

            _brand.SetBounds(0, 0, BrandW, PageH + BarH);
            _brand.Version = _version;
            _page.SetBounds(BrandW, 0, PageW, PageH);
            _page.BackColor = Color.White;
            _bottom.SetBounds(BrandW, PageH, PageW, BarH);
            _bottom.BackColor = Theme.BarBack;
            _bottom.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, _bottom.Width, 0); };

            _title.SetBounds(Pad, 30, InnerW, 32);
            _title.Font = Theme.Semi(13f);
            _title.ForeColor = Theme.Ink;
            _text.SetBounds(Pad, 70, InnerW, 50);
            _text.ForeColor = Theme.Muted;
            _text.Font = Theme.Body(8.5f, FontStyle.Regular);
            _list.SetBounds(Pad, 130, InnerW, 140);
            _license.SetBounds(Pad, 112, InnerW, 200);
            _license.ReadOnly = true;
            _license.BackColor = Color.White;
            _license.BorderStyle = BorderStyle.FixedSingle;
            _license.Font = Theme.Body(8.5f, FontStyle.Regular);
            _accept.SetBounds(Pad, 326, InnerW, 24);
            _accept.Text = "I have read and accept the license terms";
            _accept.ForeColor = Theme.Ink;
            _accept.Cursor = Cursors.Hand;
            _accept.CheckedChanged += (s, e) => _b1.Enabled = _accept.Checked;
            _launch.SetBounds(Pad, 150, InnerW, 24);
            _launch.Text = "Open SSMConnect now";
            _launch.Checked = true;
            _launch.ForeColor = Theme.Ink;
            _launch.Cursor = Cursors.Hand;
            _rbUp.SetBounds(Pad, 128, InnerW, 24);
            _rbUp.Font = Theme.Semi(9.5f); _rbUp.ForeColor = Theme.Ink; _rbUp.Cursor = Cursors.Hand; _rbUp.Checked = true;
            _rbUpText.SetBounds(Pad + 24, 154, InnerW - 24, 38);
            _rbUn.SetBounds(Pad, 204, InnerW, 24);
            _rbUn.Font = Theme.Semi(9.5f); _rbUn.ForeColor = Theme.Ink; _rbUn.Cursor = Cursors.Hand;
            _rbUnText.SetBounds(Pad + 24, 230, InnerW - 24, 38);
            foreach (var l in new[] { _rbUpText, _rbUnText }) { l.ForeColor = Theme.Muted; l.Font = Theme.Body(8.5f, FontStyle.Regular); }
            _keep.SetBounds(Pad + 24, 272, InnerW - 24, 40);
            _keep.Text = "Keep my settings (servers, keys and saved sessions) so a future install picks them up";
            _keep.Checked = true; _keep.ForeColor = Theme.Ink; _keep.Cursor = Cursors.Hand;
            _rbUp.CheckedChanged += (s, e) => ChoiceChanged();
            _rbUn.CheckedChanged += (s, e) => ChoiceChanged();
            ChoiceVisible(false);
            _bar.SetBounds(Pad, 296, InnerW, 8);
            _status.SetBounds(Pad, 312, InnerW, 22);
            _status.ForeColor = Theme.Muted;
            _status.Font = Theme.Body(8.5f, FontStyle.Regular);

            _page.Controls.AddRange(new Control[] { _title, _text, _rbUp, _rbUpText, _rbUn, _rbUnText, _keep, _list, _license, _accept, _launch, _bar, _status });
            _bottom.Controls.AddRange(new Control[] { _b1, _b2, _b3 });
            Controls.AddRange(new Control[] { _brand, _page, _bottom });

            _b1.Click += (s, e) => { if (_b1Click != null) _b1Click(); };
            _b2.Click += (s, e) => { if (_b2Click != null) _b2Click(); };
            _b3.Click += (s, e) => { if (_b3Click != null) _b3Click(); };

            try { Icon = Theme.LogoIcon(); } catch { }
            try
            {
                var rtf = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "License.rtf");
                if (File.Exists(rtf)) _license.LoadFile(rtf, RichTextBoxStreamType.RichText);
            }
            catch { }

            _anim.Interval = 15;
            _anim.Tick += (s, e) => AnimateTick();

            _ba.DetectComplete += (s, e) => OnUi(() => AfterDetect());
            _ba.DetectPackageComplete += (s, e) => { lock (_present) _present[e.PackageId] = e.State == PackageState.Present; };
            _ba.PlanPackageBegin += (s, e) => { if (!_uninstallMode && _skip.Contains(e.PackageId)) e.State = RequestState.None; };
            _ba.PlanComplete += OnPlanComplete;
            _ba.ApplyComplete += OnApplyComplete;
            _ba.Progress += (s, e) => OnUi(() => SetProgress(e.OverallPercentage));
            _ba.ExecutePackageBegin += (s, e) => { if (e.PackageId == "SSMConnect") _msiStarted = true; OnUi(() => { _list.SetState(e.PackageId, ItemState.Active); _status.Text = _verb + " " + Friendly(e.PackageId) + "..."; }); };
            _ba.ExecutePackageComplete += (s, e) => OnUi(() => _list.SetState(e.PackageId, e.Status >= 0 ? ItemState.Done : ItemState.Waiting));
            _ba.Error += (s, e) => { _lastStatus = e.ErrorCode; };
            // The .NET runtime is not embedded in this setup file. When it is needed and not found next to the setup file, download it
            // (without this handler Burn reports "file not found", error 0x80070002).
            _ba.ResolveSource += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.DownloadSource) && !File.Exists(e.LocalSource)) e.Result = Result.Download;
            };

            Page(0, "Getting ready", "Checking this PC...", false, false, false, false);
            Buttons(null, null, null, null, null, null);
            _ready = true;

            FormClosing += OnClosing;
            Shown += (s, e) =>
            {
                _ba.Engine.CloseSplashScreen();
                if (!_silent) FadeIn();
                Run(() => _ba.Engine.Detect());
            };
        }

        static string Friendly(string id)
        {
            if (id == "AwsCli") return "AWS CLI";
            if (id == "SessionManagerPlugin") return "the Session Manager plugin";
            if (id == "DotNetDesktopRuntime") return ".NET Desktop Runtime";
            if (id == "SSMConnect") return "SSMConnect";
            return id;
        }

        void OnUi(Action a) { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); }
        static void Run(Action a) { ThreadPool.QueueUserWorkItem(_ => a()); }

        // ---------------------------------------------------------------- motion
        void FadeIn()
        {
            var t = new System.Windows.Forms.Timer { Interval = 15 };
            int start = Environment.TickCount;
            t.Tick += (s, e) =>
            {
                var p = (Environment.TickCount - start) / 220.0;
                if (p >= 1) { Opacity = 1; t.Stop(); t.Dispose(); return; }
                Opacity = p;
            };
            t.Start();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy) { e.Cancel = true; return; }
            if (_closing || _silent || Opacity < 0.05) return;
            e.Cancel = true;
            _closing = true;
            var t = new System.Windows.Forms.Timer { Interval = 15 };
            int start = Environment.TickCount;
            t.Tick += (s, ev) =>
            {
                var p = (Environment.TickCount - start) / 160.0;
                if (p >= 1) { t.Stop(); t.Dispose(); Close(); return; }
                Opacity = 1 - p;
            };
            t.Start();
        }

        void BeginTransition()
        {
            if (!_ready || _silent || !IsHandleCreated) return;
            _animStart = Environment.TickCount;
            _page.Left = BrandW + 36;
            SetFade(0);
            _anim.Start();
        }

        void AnimateTick()
        {
            var p = (Environment.TickCount - _animStart) / 260.0;
            if (p >= 1) { p = 1; _anim.Stop(); }
            var e = 1 - Math.Pow(1 - p, 3);          // ease-out
            _page.Left = BrandW + (int)Math.Round(36 * (1 - e));
            SetFade(e);
        }

        void SetFade(double t)
        {
            _title.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _text.ForeColor = Theme.Blend(Color.White, Theme.Muted, t);
            _status.ForeColor = Theme.Blend(Color.White, Theme.Muted, t);
            _accept.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _launch.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _rbUp.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _rbUn.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _keep.ForeColor = Theme.Blend(Color.White, Theme.Ink, t);
            _rbUpText.ForeColor = Theme.Blend(Color.White, Theme.Muted, t);
            _rbUnText.ForeColor = Theme.Blend(Color.White, Theme.Muted, t);
        }

        void ChoiceVisible(bool v)
        {
            _rbUp.Visible = v; _rbUpText.Visible = v; _rbUn.Visible = v; _rbUnText.Visible = v; _keep.Visible = v;
        }

        void ChoiceChanged()
        {
            if (!_rbUp.Visible) return;
            _keep.Enabled = _rbUn.Checked;
            _b1.Text = _rbUn.Checked ? "Uninstall" : (_sameVersion ? "Reinstall" : "Upgrade");
            LayoutButtons();
        }

        void SetProgress(int pct)
        {
            _bar.SetTarget(pct);
        }

        // ---------------------------------------------------------------- pages
        void Buttons(string t1, Action c1, string t2, Action c2, string t3, Action c3)
        {
            _b1.Visible = t1 != null; _b1.Text = t1 ?? ""; _b1Click = c1; _b1.Enabled = true;
            _b2.Visible = t2 != null; _b2.Text = t2 ?? ""; _b2Click = c2;
            _b3.Visible = t3 != null; _b3.Text = t3 ?? ""; _b3Click = c3;
            AcceptButton = t1 != null ? _b1 : null;
            LayoutButtons();
        }

        void LayoutButtons()
        {
            int right = PageW - 24, y = (BarH - _b1.Height) / 2;
            if (_b1.Visible) { _b1.Width = _b1.FitWidth(); _b1.Location = new Point(right - _b1.Width, y); right = _b1.Left - 10; }
            if (_b2.Visible) { _b2.Width = _b2.FitWidth(); _b2.Location = new Point(right - _b2.Width, y); }
            if (_b3.Visible) { _b3.Width = _b3.FitWidth(); _b3.Location = new Point(18, y); }
        }

        /// <summary>stage: 0 welcome, 1 licence, 2 install/uninstall, 3 finished.</summary>
        void Page(int stage, string title, string text, bool license, bool progress, bool launch, bool list)
        {
            _brand.Steps = new[] { "Welcome", "License", _uninstallMode || _verb == "Uninstalling" ? "Uninstall" : (_updateMode ? "Update" : "Install"), "Finish" };
            _brand.Stage = stage;
            _title.Text = title;
            _text.Text = text;
            _license.Visible = license;
            _accept.Visible = license;
            _bar.Visible = progress;
            _status.Visible = progress;
            _launch.Visible = launch;
            _list.Visible = list;
            ChoiceVisible(false);
            _text.Height = list ? 56 : (license ? 26 : 130);
            _status.Text = "";
            BeginTransition();
        }

        void ShowComponents(bool preview)
        {
            _list.PreviewOnly = preview;
            _list.Set(
                "SessionManagerPlugin", "Session Manager plugin", "Opens the secure tunnel to your servers",
                "SSMConnect", "SSMConnect", "Finds your servers and keeps connections ready");
            foreach (var i in _list.Items)
            {
                bool p;
                lock (_present) p = _present.TryGetValue(i.Id, out p) && p;
                i.Present = p && i.Id != "SSMConnect";
                i.Required = i.Id == "SSMConnect";
            }
            _list.ActiveText = _verb + "...";
            _list.DoneText = _verb == "Uninstalling" ? "Removed" : (_verb == "Updating" ? "Updated" : "Installed");
        }

        // ---------------------------------------------------------------- flow
        void AfterDetect()
        {
            if (!_uninstallMode && !OsSupported())
            {
                Page(0, "This PC is not supported", "SSMConnect needs 64-bit Windows 10 (version 1607 or later), Windows 11, or Windows Server 2016 or later with the desktop experience.", false, false, false, false);
                Buttons("Close", () => Close(), null, null, null, null);
                _ba.ExitCode = unchecked((int)0x80070659); // ERROR_INSTALL_PACKAGE_REJECTED
                if (_silent) Close();
                return;
            }
            if (_updateMode && !_uninstallMode && !_silent)
            {
                // the running app is closed first so its files can be replaced; Windows Installer then upgrades in place
                foreach (var p in Process.GetProcessesByName("SSMConnect.Wizard")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                StartInstall();
                return;
            }
            if (_auto) { if (_uninstallMode) { _keepSettings = !_purge; StartUninstall(); } else StartInstall(); return; }
            if (_uninstallMode)
            {
                Page(0, "Uninstall SSMConnect", "This removes SSMConnect from this PC. The AWS CLI and Session Manager plugin are left in place.", false, false, false, false);
                _text.Height = 50;
                _keep.SetBounds(Pad, 150, InnerW, 40);
                _keep.Visible = true; _keep.Enabled = true;
                Buttons("Uninstall", () => { _keepSettings = _keep.Checked; StartUninstall(); }, "Cancel", () => Close(), null, null);
                return;
            }

            var found = _continued ? new List<InstalledCopy>() : ExistingInstall.Find();
            if (found.Count > 0) { ShowExisting(found[0]); return; }
            ShowWelcome();
        }

        /// <summary>SSMConnect is already installed: choose between an in-place upgrade and an uninstall.</summary>
        void ShowExisting(InstalledCopy copy)
        {
            var inst = copy.Version ?? "";
            Version have, mine;
            bool newer = Version.TryParse(inst, out have) && Version.TryParse(_version, out mine) && have > mine;
            _sameVersion = string.Equals(inst, _version, StringComparison.OrdinalIgnoreCase);
            Page(0, "SSMConnect is already installed",
                "Version " + (inst.Length == 0 ? "(unknown)" : inst) + " is on this PC. What would you like to do?", false, false, false, false);
            _text.Height = 50;
            _rbUp.Text = newer ? "Upgrade" : (_sameVersion ? "Reinstall version " + _version : "Upgrade to version " + _version);
            _rbUpText.Text = newer
                ? "A newer version (" + inst + ") is already installed, so this older setup cannot replace it."
                : "Replaces the program files in place. Your servers, keys and saved sessions are kept as they are.";
            _rbUn.Text = "Uninstall SSMConnect";
            _rbUnText.Text = "Removes SSMConnect from this PC. The AWS CLI and Session Manager plugin are left in place.";
            _keep.SetBounds(Pad + 24, 272, InnerW - 24, 40);
            ChoiceVisible(true);
            _rbUp.Enabled = !newer;
            _rbUp.Checked = !newer;
            _rbUn.Checked = newer;
            Buttons("Upgrade", ApplyChoice, "Cancel", () => Close(), null, null);
            ChoiceChanged();
        }

        void ApplyChoice()
        {
            if (_rbUn.Checked) { RemoveExisting(false, _keep.Checked); return; }
            _updateMode = true;
            _upgradeChosen = true;
            // the running app is closed first so its files can be replaced; Windows Installer then upgrades in place
            foreach (var p in Process.GetProcessesByName("SSMConnect.Wizard")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            BackupSettings();   // safety copy only; handed back below if anything ever removes the originals
            StartInstall();
        }

        void ShowWelcome()
        {
            _verb = "Installing";
            Page(0, "Welcome to SSMConnect",
                (_continued ? "The previous version was removed" + (Directory.Exists(DataDir) ? " and your settings were kept" : "") + ". " : "") +
                "Connect to your AWS servers over SSH or Remote Desktop, with no ports to open. Items already on this PC show a green tick. The AWS CLI is downloaded from Amazon the first time you open SSMConnect, if it is missing.",
                false, false, false, true);
            ShowComponents(true);
            Buttons("Next", ShowLicense, null, null, "Cancel", () => Close());
        }

        void ShowLicense()
        {
            Page(1, "License terms", "Please read the license, then tick the box to continue.", true, false, false, false);
            _license.Top = 104; _license.Height = 210;
            _accept.Checked = false;
            Buttons("Install", StartInstall, "Back", ShowWelcome, "Cancel", () => Close());
            _b1.Enabled = false;
        }

        void RemoveExisting(bool thenInstall, bool keep)
        {
            _installOnlyAfterRemove = thenInstall;
            _busy = true;
            _verb = "Uninstalling";
            if (keep) BackupSettings();
            Page(2, thenInstall ? "Replacing the old version" : "Uninstalling SSMConnect",
                thenInstall ? "The in-place upgrade could not finish, so setup is replacing the old copy instead. Your settings are kept. Windows may ask you to approve this."
                            : "Please wait. Windows may ask you to approve this.", false, true, false, false);
            _bar.Reset();
            _bar.Indeterminate();
            Buttons(null, null, null, null, null, null);
            Run(() =>
            {
                var err = ExistingInstall.RemoveAll(s => OnUi(() => _status.Text = s));
                if (keep) RestoreSettings(false);
                OnUi(() =>
                {
                    _busy = false;
                    _bar.Reset();
                    if (err != null)
                    {
                        Page(3, "Could not remove it", err, false, false, false, false);
                        Buttons("Try again", () => { _upgradeChosen = false; AfterDetect(); }, "Close", () => Close(), null, null);
                        return;
                    }
                    if (!_installOnlyAfterRemove)
                    {
                        _ba.ExitCode = 0;
                        Page(3, "SSMConnect was uninstalled", keep ? "Your settings were kept. If you install SSMConnect again, it picks them up." : "Your settings were removed too.", false, false, false, false);
                        Buttons("Close", () => Close(), null, null, null, null);
                        return;
                    }
                    // Start a fresh copy of setup: the removed copy shared this setup's package cache, so continuing in
                    // this process would look for files that were just deleted.
                    try
                    {
                        var self = _ba.Engine.StringVariables["WixBundleOriginalSource"];
                        var psi = new ProcessStartInfo(self) { UseShellExecute = false };
                        psi.EnvironmentVariables["SSMCONNECT_CONTINUE"] = Left + "," + Top;
                        Process.Start(psi);
                        _ba.ExitCode = 0;
                        _closing = true; // hand over without a fade so the new window appears in place
                        Close();
                    }
                    catch (Exception ex)
                    {
                        Page(3, "Old copy removed", "Please run the setup file again to finish installing. (" + ex.Message + ")", false, false, false, false);
                        Buttons("Close", () => Close(), null, null, null, null);
                    }
                });
            });
        }

        void StartInstall()
        {
            _busy = true;
            _msiStarted = false;
            _verb = _updateMode ? "Updating" : "Installing";
            Page(2, _updateMode ? "Updating SSMConnect" : "Installing SSMConnect", _updateMode ? "Your servers, keys and saved sessions are kept. This takes about a minute." : "Sit back. This usually takes a minute or two.", false, true, false, true);
            // carry over what was ticked on the welcome page
            var chosen = new Dictionary<string, ComponentList.Item>();
            foreach (var i in _list.Items) chosen[i.Id] = i;
            ShowComponents(false);
            _skip.Clear();
            foreach (var i in _list.Items)
            {
                ComponentList.Item old;
                if (i.Present) i.State = ItemState.Present;
                else if (chosen.TryGetValue(i.Id, out old) && !old.Selected && !old.Required) { i.State = ItemState.Off; _skip.Add(i.Id); }
            }
            _bar.Reset();
            _status.Text = "Preparing...";
            Buttons(null, null, null, null, null, null);
            Run(() => _ba.Engine.Plan(LaunchAction.Install));
        }

        void StartUninstall()
        {
            _busy = true;
            _verb = "Uninstalling";
            Page(2, "Uninstalling SSMConnect", _keepSettings ? "Removing the app. Your settings are kept." : "Removing the app and its saved settings.", false, true, false, true);
            if (_keepSettings) BackupSettings();
            ShowComponents(false);
            // only SSMConnect itself is removed
            _list.Set("SSMConnect", "SSMConnect", _keepSettings ? "The app (settings are kept)" : "The app and its saved settings");
            _list.ActiveText = "Removing...";
            _list.DoneText = "Removed";
            _bar.Reset();
            _status.Text = "Preparing...";
            Buttons(null, null, null, null, null, null);
            Run(() => _ba.Engine.Plan(LaunchAction.Uninstall));
        }

        void OnPlanComplete(object sender, PlanCompleteEventArgs e)
        {
            if (e.Status >= 0)
            {
                var h = IntPtr.Zero;
                Invoke((Action)(() => h = Handle));
                _ba.Engine.Apply(h);
            }
            else
            {
                OnUi(() => Failed(e.Status));
            }
        }

        void OnApplyComplete(object sender, ApplyCompleteEventArgs e)
        {
            OnUi(() =>
            {
                _busy = false;
                if (e.Status < 0) { Failed(e.Status); return; }
                RestoreSettings(!_uninstallMode);
                _ba.ExitCode = 0;
                _bar.SetTarget(100);
                _list.FinishRemaining();
                if (_silent) { Close(); return; }
                if (_auto && !_uninstallMode) { Close(); return; }
                // let the bar finish its glide before the page changes
                var t = new System.Windows.Forms.Timer { Interval = 450 };
                t.Tick += (s, ev) =>
                {
                    t.Stop(); t.Dispose();
                    if (_uninstallMode)
                    {
                        Page(3, "SSMConnect was uninstalled", _keepSettings ? "Your settings were kept. If you install SSMConnect again, it picks them up." : "Thank you for using SSMConnect.", false, false, false, false);
                        Buttons("Close", () => Close(), null, null, null, null);
                        return;
                    }
                    if (_updateMode)
                    {
                        Page(3, "SSMConnect was updated", "You now have version " + _version + ". Opening it for you...", false, false, false, false);
                        Buttons("Open SSMConnect", FinishClicked, null, null, null, null);
                        var open = new System.Windows.Forms.Timer { Interval = 1800 };
                        open.Tick += (s2, e2) => { open.Stop(); open.Dispose(); _launch.Checked = true; FinishClicked(); };
                        open.Start();
                        return;
                    }
                    Page(3, "You're all set", "SSMConnect " + _version + " is installed. Open it from the Start menu at any time.", false, false, true, false);
                    _launch.Top = 130;
                    Buttons("Finish", FinishClicked, null, null, null, null);
                };
                t.Start();
            });
        }

        void Failed(int status)
        {
            _busy = false;
            if (_uninstallMode && _keepSettings) RestoreSettings(false);
            // Only replace the old copy if the MSI step had started. A failure earlier (for example the .NET runtime could not be
            // downloaded) leaves the working install untouched; removing it then would leave the user with no app at all.
            if (_upgradeChosen && !_msiStarted) RestoreSettings(true);
            if (_upgradeChosen && !_fallbackTried && !_silent && _msiStarted)
            {
                // the in-place upgrade did not complete: replace the old copy instead, with the settings carried over
                _fallbackTried = true;
                _updateMode = false;
                RemoveExisting(true, true);
                return;
            }
            _ba.ExitCode = status;
            if (_silent) { Close(); return; }
            if (!_uninstallMode && !_msiStarted)
            {
                Page(3, "Setup did not finish",
                    "Setup could not get everything it needs (code 0x" + status.ToString("X8") + "). SSMConnect itself was not changed" + (_upgradeChosen ? ", so your current version still works" : "") +
                    ". This PC needs internet access to download the .NET 8 runtime from Microsoft. Check the connection and run setup again. Details are in %TEMP% (files named SSMConnect_*.log).",
                    false, false, false, false);
                Buttons("Close", () => Close(), null, null, null, null);
                return;
            }
            Page(3, _verb == "Uninstalling" ? "Uninstall did not finish" : "Setup did not finish",
                "Something went wrong while " + _verb.ToLowerInvariant() + " (code 0x" + status.ToString("X8") + (_lastStatus != 0 ? ", error " + _lastStatus : "") + "). The details are in the setup log in %TEMP% (files named SSMConnect_*.log). Running setup again is safe.",
                false, false, false, false);
            Buttons("Close", () => Close(), null, null, null, null);
        }

        // ---------------------------------------------------------------- saved settings (kept across uninstall / upgrade)
        static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSMConnect"); } }

        void BackupSettings()
        {
            try
            {
                var src = DataDir;
                if (!Directory.Exists(src)) return;
                // never replace a good backup with an already-emptied folder
                if (_backupDir != null && !File.Exists(Path.Combine(src, "config.json"))) return;
                var dst = _backupDir ?? Path.Combine(Path.GetTempPath(), "ssmconnect-keep-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                CopyDir(src, dst);
                _backupDir = dst;
            }
            catch { }
        }

        /// <summary>Puts the saved settings back. With onlyIfMissing the backup is used only when the originals are gone.</summary>
        void RestoreSettings(bool onlyIfMissing)
        {
            try
            {
                if (_backupDir == null || !Directory.Exists(_backupDir)) return;
                if (!(onlyIfMissing && File.Exists(Path.Combine(DataDir, "config.json")))) CopyDir(_backupDir, DataDir);
                Directory.Delete(_backupDir, true);
                _backupDir = null;
            }
            catch { }
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
                try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); } catch { }
            foreach (var d in Directory.GetDirectories(from))
                CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        void FinishClicked()
        {
            if (_launch.Checked)
            {
                try
                {
                    var root = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var exe = Path.Combine(root, "SSMConnect", "SSMConnect.Wizard.exe");
                    // explorer.exe starts it un-elevated, as the signed-in user
                    Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + exe + "\"");
                }
                catch { }
            }
            Close();
        }
    }
}
