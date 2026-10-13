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
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(232, 243, 242);
        b.FlatAppearance.MouseDownBackColor = b.FlatAppearance.MouseOverBackColor;
        void Paint()
        {
            if (!b.Enabled) { b.BackColor = primary ? Color.FromArgb(178, 207, 204) : Page; b.ForeColor = Color.FromArgb(135, 150, 152); return; }
            b.BackColor = primary ? Accent : Color.White;
            b.ForeColor = primary ? Color.White : Ink;
        }
        b.Font = primary ? Semi() : Body();
        b.EnabledChanged += (_, _) => Paint();
        b.TextChanged += (_, _) => { };
        Paint();
    }

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
