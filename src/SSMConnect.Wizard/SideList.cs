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

/// <summary>The left panel: organizations (one company sign-in each) and the SHOW filters, painted as a soft translucent sidebar.</summary>
sealed class SideList : Control
{
    public sealed record Org(string Key, string Label, int Count, CardLevel Dot);
    public sealed record Filt(string Id, string Label, int Count, CardLevel? Dot);

    public static Color DotColor(CardLevel l) => l switch
    {
        CardLevel.Green => Color.FromArgb(46, 190, 100),
        CardLevel.Blue => Color.FromArgb(60, 130, 230),
        CardLevel.Amber => Color.FromArgb(242, 172, 30),
        CardLevel.Red => Color.FromArgb(228, 64, 64),
        _ => Color.FromArgb(150, 158, 162),
    };

    List<Org> _orgs = new();
    List<Filt> _filts = new();
    string? _selOrg;
    string _selFilt = "all";
    string _sig = "";
    readonly List<(RectangleF R, string Id, bool IsOrg)> _hits = new();
    int _hover = -1;

    public event Action<string>? OrgPicked;
    public event Action<string>? FilterPicked;

    public SideList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(List<Org> orgs, string? selectedOrg, List<Filt> filters, string selectedFilter)
    {
        var sig = string.Join("|", orgs.Select(o => o.Key + ":" + o.Label + ":" + o.Count + ":" + o.Dot)) + "#" + selectedOrg + "#" +
                  string.Join("|", filters.Select(f => f.Id + ":" + f.Count)) + "#" + selectedFilter;
        if (sig == _sig) return;
        _sig = sig;
        _orgs = orgs; _selOrg = selectedOrg; _filts = filters; _selFilt = selectedFilter;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var s = DeviceDpi / 96f;
        using (var bg = new LinearGradientBrush(new Rectangle(0, -1, Width, Height + 2), Color.FromArgb(241, 247, 248), Color.FromArgb(228, 238, 240), 90f)) g.FillRectangle(bg, ClientRectangle);
        using (var line = new Pen(Color.FromArgb(206, 220, 222))) g.DrawLine(line, Width - 1, 0, Width - 1, Height);

        // app name
        AppTheme.DrawLogo(g, new RectangleF(18 * s, 16 * s, 30 * s, 30 * s));
        using (var f1 = AppTheme.Semi(13f)) TextRenderer.DrawText(g, "SSMConnect", f1, new Rectangle((int)(56 * s), (int)(14 * s), (int)(Width - 64 * s), (int)(22 * s)), AppTheme.Ink, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        using (var f2 = AppTheme.Body(8f)) TextRenderer.DrawText(g, "AWS SSM, made simple", f2, new Rectangle((int)(56 * s), (int)(34 * s), (int)(Width - 64 * s), (int)(16 * s)), Color.FromArgb(94, 110, 114), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        _hits.Clear();
        float y = 70 * s;
        using var head = AppTheme.Semi(7.5f);
        using var item = AppTheme.Body(9.5f);
        using var itemSel = AppTheme.Semi(9.5f);
        using var cnt = AppTheme.Semi(8.5f);

        TextRenderer.DrawText(g, "ORGANIZATIONS", head, new Rectangle((int)(20 * s), (int)y, (int)(Width - 30 * s), (int)(16 * s)), Color.FromArgb(104, 122, 128), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        y += 22 * s;
        foreach (var o in _orgs)
        {
            var r = new RectangleF(10 * s, y, Width - 20 * s, 32 * s);
            DrawRow(g, r, _selOrg == o.Key, _hits.Count == _hover, s);
            _hits.Add((r, o.Key, true));
            DrawDot(g, new RectangleF(r.X + 12 * s, r.Y + (r.Height - 10 * s) / 2f, 10 * s, 10 * s), DotColor(o.Dot), s);
            TextRenderer.DrawText(g, o.Label, _selOrg == o.Key ? itemSel : item, new Rectangle((int)(r.X + 32 * s), (int)r.Y, (int)(r.Width - 32 * s - 44 * s), (int)r.Height), AppTheme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, o.Count.ToString(), cnt, new Rectangle((int)(r.Right - 44 * s), (int)r.Y, (int)(36 * s), (int)r.Height), Color.FromArgb(94, 110, 114), TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            y += 34 * s;
        }

        y += 16 * s;
        TextRenderer.DrawText(g, "SHOW", head, new Rectangle((int)(20 * s), (int)y, (int)(Width - 30 * s), (int)(16 * s)), Color.FromArgb(104, 122, 128), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        y += 22 * s;
        foreach (var fl in _filts)
        {
            var r = new RectangleF(10 * s, y, Width - 20 * s, 30 * s);
            DrawRow(g, r, _selFilt == fl.Id, _hits.Count == _hover, s);
            _hits.Add((r, fl.Id, false));
            if (fl.Dot is CardLevel dl) DrawDot(g, new RectangleF(r.X + 12 * s, r.Y + (r.Height - 10 * s) / 2f, 10 * s, 10 * s), DotColor(dl), s);
            TextRenderer.DrawText(g, fl.Label, _selFilt == fl.Id ? itemSel : item, new Rectangle((int)(r.X + 32 * s), (int)r.Y, (int)(r.Width - 32 * s - 44 * s), (int)r.Height), AppTheme.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, fl.Count.ToString(), cnt, new Rectangle((int)(r.Right - 44 * s), (int)r.Y, (int)(36 * s), (int)r.Height), Color.FromArgb(94, 110, 114), TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            y += 32 * s;
        }
    }

    static void DrawRow(Graphics g, RectangleF r, bool selected, bool hover, float s)
    {
        if (!selected && !hover) return;
        using var p = AppTheme.RoundRect(r, 9 * s);
        using (var b = new SolidBrush(Color.FromArgb(selected ? 205 : 110, 255, 255, 255))) g.FillPath(b, p);
        if (selected) using (var pen = new Pen(Color.FromArgb(70, 110, 140, 146))) g.DrawPath(pen, p);
    }

    static void DrawDot(Graphics g, RectangleF r, Color c, float s)
    {
        using (var halo = new SolidBrush(Color.FromArgb(55, c))) g.FillEllipse(halo, RectangleF.Inflate(r, 3 * s, 3 * s));
        using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
    }

    int HitAt(Point p)
    {
        for (var i = 0; i < _hits.Count; i++) if (_hits[i].R.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = HitAt(e.Location);
        if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var i = HitAt(e.Location);
        if (i < 0 || e.Button != MouseButtons.Left) return;
        var h = _hits[i];
        if (h.IsOrg) OrgPicked?.Invoke(h.Id); else FilterPicked?.Invoke(h.Id);
    }
}

/// <summary>The colour key along the bottom of the window: what each card colour and ring colour means.</summary>
sealed class LegendBar : Control
{
    public LegendBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var s = DeviceDpi / 96f;
        using (var bg = new LinearGradientBrush(new Rectangle(0, -1, Width, Height + 2), Color.FromArgb(250, 252, 252), Color.FromArgb(238, 244, 245), 90f)) g.FillRectangle(bg, ClientRectangle);
        using (var line = new Pen(AppTheme.Line)) g.DrawLine(line, 0, 0, Width, 0);
        using var f = AppTheme.Body(8.5f);
        float x = 22 * s;
        float cy = Height / 2f;
        void Item(CardLevel l, string text)
        {
            var d = 10 * s;
            using (var halo = new SolidBrush(Color.FromArgb(55, SideList.DotColor(l)))) g.FillEllipse(halo, x - 3 * s, cy - d / 2 - 3 * s, d + 6 * s, d + 6 * s);
            using (var b = new SolidBrush(SideList.DotColor(l))) g.FillEllipse(b, x, cy - d / 2, d, d);
            var w = TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, text, f, new Rectangle((int)(x + d + 7 * s), 0, w + 4, Height), Color.FromArgb(60, 72, 78), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            x += d + 7 * s + w + 22 * s;
        }
        Item(CardLevel.Green, "Connected");
        Item(CardLevel.Blue, "Ready");
        Item(CardLevel.Amber, "Signing in or connecting");
        Item(CardLevel.Red, "Stopped after 3 tries");
        Item(CardLevel.Grey, "Switched off");

        // usage bar colours
        var ringText = "Usage (CPU, RAM, disk):";
        var rw = TextRenderer.MeasureText(ringText, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        var need = rw + 3 * (22 * s + 6 * s + 56 * s) + 20 * s;
        if (Width - x < need) return;
        x = Width - need - 6 * s;
        TextRenderer.DrawText(g, ringText, f, new Rectangle((int)x, 0, rw + 4, Height), Color.FromArgb(94, 110, 114), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        x += rw + 12 * s;
        void Swatch(int pct, string text)
        {
            var sw = new RectangleF(x, cy - 7 * s, 22 * s, 14 * s);
            ButtonLook.PaintSurface(g, sw, 4 * s, ServerCard.UsageColor(pct), 0f, false, false, s);
            TextRenderer.DrawText(g, text, f, new Rectangle((int)(x + 22 * s + 6 * s), 0, (int)(56 * s), Height), Color.FromArgb(60, 72, 78), TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            x += 22 * s + 6 * s + 56 * s;
        }
        Swatch(30, "under 60%");
        Swatch(70, "60-85%");
        Swatch(95, "over 85%");
    }
}
