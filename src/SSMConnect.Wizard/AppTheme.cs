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

using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SSMConnect.Wizard;

/// <summary>Shared look of the app windows: a deep-teal theme (the installer is blue, so the two are easy to tell apart).</summary>
public static class AppTheme
{
    public static readonly Color Teal900 = Color.FromArgb(5, 46, 52);
    public static readonly Color Teal700 = Color.FromArgb(14, 116, 112);
    public static readonly Color Accent = Color.FromArgb(13, 148, 136);
    public static readonly Color AccentHover = Color.FromArgb(15, 118, 110);
    public static readonly Color Mint = Color.FromArgb(110, 231, 183);
    public static readonly Color Page = Color.FromArgb(243, 247, 247);
    public static readonly Color Line = Color.FromArgb(214, 225, 226);
    public static readonly Color Ink = Color.FromArgb(22, 40, 46);

    // operating-system badges (kept apart from the green / amber / red status colours)
    public static readonly Color WinBg = Color.FromArgb(214, 232, 252), WinFg = Color.FromArgb(10, 70, 150);
    public static readonly Color LinuxBg = Color.FromArgb(236, 226, 250), LinuxFg = Color.FromArgb(88, 40, 150);

    static readonly HashSet<string> PrimaryTexts = new(StringComparer.Ordinal)
    {
        "Next >", "Finish", "Find my servers", "Install missing components automatically", "Start connections",
        "Add / change servers...", "Sign in (SSO)", "Start now", "Connect and test", "Run again",
    };

    /// <summary>Body text size shared by every window (the installer uses the same Segoe UI scale).</summary>
    public const float BodySize = 9f;
    public static Font Body(float size = BodySize, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Font Semi(float size = BodySize) => new("Segoe UI Semibold", size);

    /// <summary>The SSMConnect logo: blue rounded tile with a white tunnel arch. Same drawing as the installer and the .ico file.</summary>
    public static void DrawLogo(Graphics g, RectangleF r)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        {
            float d = r.Width * 0.24f * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
            using var b = new LinearGradientBrush(r, Color.FromArgb(70, 160, 240), Color.FromArgb(0, 120, 212), 45f);
            g.FillPath(b, path);
        }
        Arch(g, r, 0.27f, 0.085f);
        Arch(g, r, 0.12f, 0.07f);
        g.SmoothingMode = old;
    }

    // an inverted-U "tunnel" arch: legs go down to the base line, round caps
    static void Arch(Graphics g, RectangleF r, float rad, float stroke)
    {
        float cx = r.X + r.Width / 2, yc = r.Y + .47f * r.Height, yb = r.Y + .74f * r.Height, rr = rad * r.Width;
        using var path = new GraphicsPath();
        path.AddLine(cx - rr, yb, cx - rr, yc);
        path.AddArc(cx - rr, yc - rr, rr * 2, rr * 2, 180, 180);
        path.AddLine(cx + rr, yc, cx + rr, yb);
        using var pen = new Pen(Color.White, Math.Max(1.2f, stroke * r.Width)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawPath(pen, path);
    }

    /// <summary>The logo as a window / tray icon, optionally with a status dot in the corner.</summary>
    public static Icon MakeLogoIcon(Color? status = null)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            DrawLogo(g, new RectangleF(1, 1, 30, 30));
            if (status is { } c)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var ring = new SolidBrush(Color.White); g.FillEllipse(ring, 15, 15, 17, 17);
                using var dot = new SolidBrush(c); g.FillEllipse(dot, 17, 17, 13, 13);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static Icon? _appIcon;
    public static Icon AppIcon => _appIcon ??= MakeLogoIcon();

    public static void Style(Control root)
    {
        if (root is Form f) { try { f.Icon = AppIcon; } catch { } }
        foreach (Control c in root.Controls)
        {
            if (c is Button b) StyleButton(b, b.Tag as string == "primary" || PrimaryTexts.Contains(b.Text));
            Style(c);
        }
    }

    public static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = b.FlatAppearance.MouseDownBackColor = Page;
        b.Font = primary ? Semi() : Body();
        ButtonLook.Attach(b, primary);
    }

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        var d = Math.Max(0.1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Quiet, flat table in the app font: no heavy borders, soft header, light row lines.</summary>
    public static void StyleGrid(DataGridView g)
    {
        g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false; g.AllowUserToResizeColumns = false;
        g.RowHeadersVisible = false;
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Color.FromArgb(232, 240, 240);
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 34;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(232, 243, 242), ForeColor = Color.FromArgb(5, 70, 66), Font = Semi(), Padding = new Padding(6, 0, 0, 0), SelectionBackColor = Color.FromArgb(232, 243, 242), SelectionForeColor = Color.FromArgb(5, 70, 66) };
        g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Ink, Font = Body(), Padding = new Padding(6, 0, 0, 0), SelectionBackColor = Color.FromArgb(240, 248, 247), SelectionForeColor = Ink };
        g.RowTemplate.Height = 36;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.Font = Body();
    }

    /// <summary>Rows a little taller than the default, so lists breathe.</summary>
    public static void RoomyRows(ListView lv, int height = 28)
    {
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, height) };
    }
}

/// <summary>Top banner: logo tile, title, a line of help text and (in the wizard) the step tracker.</summary>
public sealed class HeaderBand : Panel
{
    public string Title = "", Sub = "";
    public string[] Steps = Array.Empty<string>();
    int _step = -1;
    public HeaderBand() { DoubleBuffered = true; ResizeRedraw = true; }

    public void Set(string title, string sub, int step = -1)
    {
        Title = title; Sub = sub; _step = step;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var s = DeviceDpi / 96f;
        using (var b = new LinearGradientBrush(ClientRectangle, AppTheme.Teal900, AppTheme.Teal700, 0f)) g.FillRectangle(b, ClientRectangle);
        using (var b = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
        {
            g.FillEllipse(b, Width - 170 * s, -90 * s, 260 * s, 260 * s);
            g.FillEllipse(b, Width - 300 * s, 30 * s, 150 * s, 150 * s);
        }
        AppTheme.DrawLogo(g, new RectangleF(24 * s, (Height - 46 * s) / 2, 46 * s, 46 * s));

        int x = (int)(82 * s);
        using (var f = new Font("Segoe UI Semibold", 14f))
            TextRenderer.DrawText(g, Title, f, new Point(x, (int)(Height / 2 - 28 * s)), Color.White, TextFormatFlags.NoPadding);
        using (var f = new Font("Segoe UI", 8.5f))
            TextRenderer.DrawText(g, Sub, f, new Point(x, (int)(Height / 2 + 6 * s)), Color.FromArgb(190, 228, 224), TextFormatFlags.NoPadding);

        if (_step >= 0 && Steps.Length > 0)
        {
            float gap = 34 * s, r = 7 * s;
            float x0 = Width - 28 * s - gap * (Steps.Length - 1) - r, cy = Height / 2f;
            for (int i = 0; i < Steps.Length; i++)
            {
                float cx = x0 + i * gap;
                if (i < Steps.Length - 1)
                    using (var p = new Pen(Color.FromArgb(i < _step ? 170 : 70, 255, 255, 255), 2f)) g.DrawLine(p, cx + r, cy, cx + gap - r, cy);
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                if (i < _step) { using var b = new SolidBrush(AppTheme.Mint); g.FillEllipse(b, rect); }
                else if (i == _step)
                {
                    using var b = new SolidBrush(Color.White); g.FillEllipse(b, cx - r - 2 * s, cy - r - 2 * s, (r + 2 * s) * 2, (r + 2 * s) * 2);
                    using var b2 = new SolidBrush(AppTheme.Accent); g.FillEllipse(b2, cx - 3.5f * s, cy - 3.5f * s, 7 * s, 7 * s);
                }
                else { using var p = new Pen(Color.FromArgb(120, 255, 255, 255), 2f); g.DrawEllipse(p, rect); }
            }
        }
    }
}

/// <summary>A button that stays hoverable (so its tooltip always shows) while it has nothing to do, and can carry a small drawn icon.</summary>
public sealed class ActionButton : Button
{
    /// <summary>False dims the button and ignores clicks, but unlike Enabled=false it still shows its tooltip.</summary>
    public bool Available
    {
        get => _available;
        set { if (_available != value) { _available = value; Invalidate(); } }
    }
    bool _available = true;

    /// <summary>"play", "stop", "restart" or "plus"; drawn to the left of the text.</summary>
    public string Glyph { get; set; } = "";

    protected override void OnClick(EventArgs e) { if (_available) base.OnClick(e); }
}

/// <summary>Rounded, softly shaded buttons with hover and pressed states, drawn on top of the standard Button so every existing click handler keeps working.</summary>
static class ButtonLook
{
    sealed class State { public bool Hover, Down, Attached, Primary, Kbd; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, State> States = new();

    public static void Attach(Button b, bool primary)
    {
        var st = States.GetOrCreateValue(b);
        st.Primary = primary;
        if (st.Attached) { b.Invalidate(); return; }
        st.Attached = true;
        try { typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(b, true); } catch { }
        b.MouseEnter += (_, _) => { st.Hover = true; b.Invalidate(); };
        b.MouseLeave += (_, _) => { st.Hover = false; st.Down = false; b.Invalidate(); };
        b.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { st.Down = true; b.Invalidate(); } };
        b.MouseUp += (_, _) => { st.Down = false; b.Invalidate(); };
        b.EnabledChanged += (_, _) => b.Invalidate();
        b.GotFocus += (_, _) => { st.Kbd = Control.MouseButtons == MouseButtons.None; b.Invalidate(); };
        b.LostFocus += (_, _) => b.Invalidate();
        b.Paint += (_, e) => Draw(b, e.Graphics, st);
    }

    static void Draw(Button b, Graphics g, State st)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(b.Parent?.BackColor ?? AppTheme.Page);
        var s = b.DeviceDpi / 96f;
        var off = !b.Enabled || (b is ActionButton { Available: false });
        var hot = !off && st.Hover;
        var down = !off && st.Down;
        var r = new RectangleF(0.5f, 0.5f, b.Width - 2f, b.Height - 3.5f);
        float rad = Math.Min(10 * s, r.Height / 2);

        Color top, bottom, border, text;
        if (st.Primary)
        {
            if (off) { top = bottom = border = Color.FromArgb(184, 210, 207); text = Color.FromArgb(244, 250, 249); }
            else if (down) { top = AppTheme.AccentHover; bottom = AppTheme.Mix(AppTheme.AccentHover, Color.Black, 0.12f); border = bottom; text = Color.White; }
            else if (hot) { top = AppTheme.Mix(AppTheme.Accent, Color.White, 0.26f); bottom = AppTheme.Mix(AppTheme.Accent, Color.White, 0.06f); border = AppTheme.Accent; text = Color.White; }
            else { top = AppTheme.Mix(AppTheme.Accent, Color.White, 0.14f); bottom = AppTheme.Accent; border = AppTheme.AccentHover; text = Color.White; }
        }
        else
        {
            if (off) { top = bottom = Color.FromArgb(246, 249, 249); border = Color.FromArgb(226, 234, 235); text = Color.FromArgb(150, 163, 165); }
            else if (down) { top = Color.FromArgb(214, 234, 232); bottom = Color.FromArgb(224, 240, 238); border = AppTheme.Accent; text = AppTheme.Teal900; }
            else if (hot) { top = Color.FromArgb(240, 249, 248); bottom = Color.FromArgb(222, 241, 239); border = Color.FromArgb(110, 190, 183); text = AppTheme.Teal900; }
            else { top = Color.White; bottom = Color.FromArgb(243, 248, 248); border = Color.FromArgb(205, 220, 221); text = AppTheme.Ink; }
        }

        // a soft shadow under raised buttons
        if (!off && !down)
        {
            var sh = new RectangleF(r.X, r.Y + 1.5f * s, r.Width, r.Height);
            using var sp = AppTheme.RoundRect(sh, rad);
            using var sb = new SolidBrush(Color.FromArgb(st.Primary ? 46 : 22, 5, 60, 56));
            g.FillPath(sb, sp);
        }
        using (var path = AppTheme.RoundRect(r, rad))
        {
            using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, 90f)) g.FillPath(br, path);
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        // optional icon + text, centred together
        var glyph = (b as ActionButton)?.Glyph ?? "";
        var cell = new Rectangle(0, 0, b.Width, b.Height - 3);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (glyph.Length > 0)
        {
            float icon = 12 * s, gap = 8 * s;
            var tw = TextRenderer.MeasureText(g, b.Text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            var x0 = (b.Width - (icon + gap + tw)) / 2f;
            DrawGlyph(g, glyph, new RectangleF(x0, (b.Height - 3 - icon) / 2f, icon, icon), text, s);
            TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle((int)(x0 + icon + gap), 0, tw + 4, cell.Height), text, flags);
        }
        else TextRenderer.DrawText(g, b.Text, b.Font, cell, text, flags | TextFormatFlags.HorizontalCenter);

        if (b.Focused && st.Kbd && !off)
        {
            var fr = new RectangleF(r.X + 3 * s, r.Y + 3 * s, r.Width - 6 * s, r.Height - 6 * s);
            using var fp = AppTheme.RoundRect(fr, Math.Max(2f, rad - 3 * s));
            using var pen = new Pen(Color.FromArgb(st.Primary ? 150 : 110, st.Primary ? Color.White : AppTheme.Accent)) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, fp);
        }
    }

    static void DrawGlyph(Graphics g, string kind, RectangleF r, Color c, float s)
    {
        using var brush = new SolidBrush(c);
        using var pen = new Pen(c, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        switch (kind)
        {
            case "play":
                g.FillPolygon(brush, new[] { new PointF(r.X + 2 * s, r.Y), new PointF(r.X + 2 * s, r.Bottom), new PointF(r.Right, cy) });
                break;
            case "stop":
                using (var p = AppTheme.RoundRect(new RectangleF(r.X + 1 * s, r.Y + 1 * s, r.Width - 2 * s, r.Height - 2 * s), 2 * s)) g.FillPath(brush, p);
                break;
            case "plus":
                g.DrawLine(pen, cx, r.Y + 1 * s, cx, r.Bottom - 1 * s);
                g.DrawLine(pen, r.X + 1 * s, cy, r.Right - 1 * s, cy);
                break;
            case "restart":
            {
                float rad = r.Width / 2 - 1.5f * s;
                var ring = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
                const float startDeg = 30f, sweep = 285f;
                g.DrawArc(pen, ring, startDeg, sweep);
                var th = (startDeg + sweep) * Math.PI / 180.0;          // where the arc ends
                float ex = cx + rad * (float)Math.Cos(th), ey = cy + rad * (float)Math.Sin(th);
                float tx = -(float)Math.Sin(th), ty = (float)Math.Cos(th);   // direction of travel (clockwise)
                float nx = -ty, ny = tx, a = 3.6f * s;
                g.FillPolygon(brush, new[]
                {
                    new PointF(ex + tx * a * 1.2f, ey + ty * a * 1.2f),
                    new PointF(ex + nx * a, ey + ny * a),
                    new PointF(ex - nx * a, ey - ny * a),
                });
                break;
            }
        }
    }
}

/// <summary>A soft teal card with an "i" badge, a short title and a few lines of plain-language help.</summary>
public sealed class HintCard : Control
{
    string _title = "", _body = "";
    public HintCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 92;
    }

    public void Set(string title, string body) { _title = title; _body = body; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? AppTheme.Page);
        var s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        using (var path = AppTheme.RoundRect(r, 12 * s))
        {
            using (var br = new LinearGradientBrush(r, Color.FromArgb(234, 247, 245), Color.FromArgb(221, 240, 237), 0f)) g.FillPath(br, path);
            using var pen = new Pen(Color.FromArgb(188, 221, 217));
            g.DrawPath(pen, path);
        }
        var ic = new RectangleF(18 * s, (Height - 34 * s) / 2f, 34 * s, 34 * s);
        using (var br = new SolidBrush(AppTheme.Accent)) g.FillEllipse(br, ic);
        using (var f = AppTheme.Semi(13f))
            TextRenderer.DrawText(g, "i", f, Rectangle.Round(ic), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int tx = (int)(66 * s), tw = Width - tx - (int)(18 * s);
        using (var f = AppTheme.Semi(10f))
            TextRenderer.DrawText(g, _title, f, new Rectangle(tx, (int)(12 * s), tw, (int)(22 * s)), AppTheme.Teal900, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var f = AppTheme.Body(9f))
            TextRenderer.DrawText(g, _body, f, new Rectangle(tx, (int)(36 * s), tw, Height - (int)(44 * s)), Color.FromArgb(38, 82, 82), TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
    }
}
