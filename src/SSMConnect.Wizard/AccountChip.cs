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

/// <summary>
/// The account switcher in the dashboard header: a soft, glassy pill showing the current AWS account. Clicking it opens a
/// menu (see <see cref="AccountMenuRenderer"/>) with the other accounts.
/// </summary>
public sealed class AccountChip : Control
{
    string _title = "";
    bool _hover, _down;

    public AccountChip()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public string Title { get => _title; set { _title = value ?? ""; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }

    internal static Bitmap Avatar(string name, int size, Color fill, Color text)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, 0, 0, size - 1, size - 1);
        var initial = string.IsNullOrWhiteSpace(name) ? "+" : name.Trim().Substring(0, 1).ToUpperInvariant();
        using var f = new Font("Segoe UI Semibold", size * 0.42f, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, initial, f, new Rectangle(0, 0, size, size), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var s = DeviceDpi / 96f;
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Round(r, r.Height / 2))
        {
            using (var b = new SolidBrush(Color.FromArgb(_down ? 70 : _hover ? 56 : 36, 255, 255, 255))) g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(Focused || _hover ? 150 : 90, 255, 255, 255), 1f);
            g.DrawPath(pen, path);
        }
        var d = r.Height - 12 * s;
        var av = new RectangleF(r.X + 6 * s, r.Y + 6 * s, d, d);
        using (var b = new SolidBrush(AppTheme.Mint)) g.FillEllipse(b, av);
        using (var f = new Font("Segoe UI Semibold", 11f))
            TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(_title) ? "+" : _title.Trim().Substring(0, 1).ToUpperInvariant(), f, Rectangle.Round(av), AppTheme.Teal900,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var tx = (int)(av.Right + 10 * s);
        var right = (int)(r.Right - 30 * s);
        using (var f = new Font("Segoe UI", 7f))
            TextRenderer.DrawText(g, "AWS ACCOUNT", f, new Rectangle(tx, (int)(r.Y + 6 * s), right - tx, (int)(12 * s)), Color.FromArgb(190, 228, 224), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var f = new Font("Segoe UI Semibold", 10f))
            TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(_title) ? "Add an AWS account" : _title, f, new Rectangle(tx, (int)(r.Y + 19 * s), right - tx, (int)(20 * s)), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        // chevron
        var cx = r.Right - 18 * s; var cy = r.Y + r.Height / 2f;
        using var pc = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pc, new[] { new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s) });
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        var d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Soft, rounded look for the account menu so it matches the rest of the app instead of the plain white Windows menu.</summary>
public sealed class AccountMenuRenderer : ToolStripProfessionalRenderer
{
    public AccountMenuRenderer() : base(new AccountColors()) { RoundedEdges = false; }

    sealed class AccountColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuBorder => AppTheme.Line;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Color.FromArgb(232, 243, 242);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(6, 2, e.Item.Width - 12, e.Item.Height - 4);
        using var path = new GraphicsPath();
        var d = 12;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        using var b = new SolidBrush(Color.FromArgb(226, 241, 239));
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Tag as string == "accent" ? AppTheme.Accent : AppTheme.Ink;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(AppTheme.Line);
        e.Graphics.DrawLine(p, 14, e.Item.Height / 2, e.Item.Width - 14, e.Item.Height / 2);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(AppTheme.Line);
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
}
