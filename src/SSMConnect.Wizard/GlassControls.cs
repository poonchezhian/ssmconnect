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

/// <summary>Shared drawing for the rounded, white "glass" input boxes.</summary>
static class InputLook
{
    public static void Paint(Graphics g, Size size, float s, bool focus, bool enabled, bool hot)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1f, 1f, size.Width - 3f, size.Height - 3f);
        using var path = AppTheme.RoundRect(r, 8 * s);
        using (var b = new SolidBrush(enabled ? Color.White : Color.FromArgb(239, 243, 243))) g.FillPath(b, path);
        if (enabled) { using var hl = new Pen(Color.FromArgb(22, 5, 50, 56), 1f); g.DrawLine(hl, r.X + 8 * s, r.Y + 1f, r.Right - 8 * s, r.Y + 1f); }
        var border = focus ? AppTheme.Accent : hot ? AppTheme.Mix(Color.FromArgb(196, 212, 213), AppTheme.Accent, 0.5f) : Color.FromArgb(196, 212, 213);
        using var pen = new Pen(border, focus ? 1.6f : 1f);
        g.DrawPath(pen, path);
    }

    public static void Chevron(Graphics g, RectangleF r, float s, bool enabled)
    {
        var cx = r.X + r.Width / 2f; var cy = r.Y + r.Height / 2f;
        using var pen = new Pen(enabled ? Color.FromArgb(70, 100, 104) : Color.FromArgb(170, 182, 184), 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s) });
    }

    public static ContextMenuStrip Menu(Font font, int minWidth) =>
        new() { Renderer = new AccountMenuRenderer(), ShowImageMargin = false, Font = font, MinimumSize = new Size(minWidth, 0) };
}

/// <summary>The item list of a <see cref="GlassSelect"/> (the same few calls a ComboBox list has).</summary>
public sealed class GlassItems : IEnumerable<string>
{
    readonly List<string> _l = new();
    readonly Action _changed;
    public GlassItems(Action changed) { _changed = changed; }
    public int Count => _l.Count;
    public string this[int i] => _l[i];
    public int Add(string s) { _l.Add(s); _changed(); return _l.Count - 1; }
    public void Insert(int i, string s) { _l.Insert(i, s); _changed(); }
    public void Clear() { _l.Clear(); _changed(); }
    public bool Contains(string s) => _l.Contains(s);
    public IEnumerator<string> GetEnumerator() => _l.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _l.GetEnumerator();
}

/// <summary>A drop-down list drawn as a rounded glass box (replaces the grey Windows combo box).</summary>
public sealed class GlassSelect : Control
{
    public GlassItems Items { get; }
    int _sel = -1;
    bool _hot, _focus;
    /// <summary>The item at this index (for example "Select the server to connect") is shown in a muted colour.</summary>
    public int MutedIndex { get; set; } = -1;
    public event EventHandler? SelectedIndexChanged;

    public GlassSelect()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Items = new GlassItems(() => { if (_sel >= Items.Count) _sel = -1; Invalidate(); });
    }

    protected override Size DefaultSize => new(220, 30);

    public int SelectedIndex
    {
        get => _sel;
        set
        {
            var v = value >= 0 && value < Items.Count ? value : -1;
            if (v == _sel) return;
            _sel = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override string Text { get => _sel >= 0 && _sel < Items.Count ? Items[_sel] : ""; set { } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? Color.White);
        var s = DeviceDpi / 96f;
        InputLook.Paint(g, Size, s, _focus, Enabled, _hot && Enabled);
        var col = !Enabled ? Color.FromArgb(150, 163, 165) : _sel == MutedIndex ? Color.FromArgb(104, 120, 124) : AppTheme.Ink;
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(11 * s), 0, (int)(Width - 38 * s), Height), col,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        InputLook.Chevron(g, new RectangleF(Width - 28 * s, 0, 22 * s, Height), s, Enabled);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); _focus = true; Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _focus = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) ShowMenu(); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Up || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4 or Keys.Down) { e.Handled = true; ShowMenu(); }
    }

    void ShowMenu()
    {
        if (!Enabled || Items.Count == 0) return;
        var m = InputLook.Menu(Font, Width);
        for (var i = 0; i < Items.Count; i++)
        {
            var idx = i;
            var it = new ToolStripMenuItem(Items[i]) { Checked = i == _sel, Padding = new Padding(4, 5, 4, 5) };
            it.Click += (_, _) => SelectedIndex = idx;
            m.Items.Add(it);
        }
        m.Closed += (_, _) => BeginInvoke(new Action(() => m.Dispose()));
        m.Show(this, new Point(0, Height + 2));
    }
}

/// <summary>A one-line text box inside a rounded glass frame. Optionally shows a chevron that offers a short list of choices.</summary>
public class GlassText : Panel
{
    readonly TextBox _tb = new() { BorderStyle = BorderStyle.None, BackColor = Color.White };
    bool _focus;
    bool _chevron;

    public List<string> Choices { get; } = new();
    public event Action? ChoicePicked;
    public new event KeyEventHandler? KeyDown { add => _tb.KeyDown += value; remove => _tb.KeyDown -= value; }

    public GlassText()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(_tb);
        _tb.GotFocus += (_, _) => { _focus = true; Invalidate(); };
        _tb.LostFocus += (_, _) => { _focus = false; Invalidate(); };
        _tb.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
    }

    protected override Size DefaultSize => new(160, 30);

    public bool Chevron { get => _chevron; set { _chevron = value; PerformLayout(); Invalidate(); } }
    public override string Text { get => _tb.Text; set => _tb.Text = value ?? ""; }
    public string PlaceholderText { get => _tb.PlaceholderText; set => _tb.PlaceholderText = value; }
    public bool UseSystemPasswordChar { get => _tb.UseSystemPasswordChar; set => _tb.UseSystemPasswordChar = value; }
    public bool ReadOnly { get => _tb.ReadOnly; set => _tb.ReadOnly = value; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var s = DeviceDpi / 96f;
        int padL = (int)(11 * s), padR = (int)((_chevron ? 28 : 11) * s);
        _tb.Width = Math.Max(10, Width - padL - padR);
        _tb.Left = padL;
        _tb.Top = Math.Max(0, (Height - _tb.Height) / 2);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _tb.BackColor = Enabled ? Color.White : Color.FromArgb(239, 243, 243);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Color.White);
        var s = DeviceDpi / 96f;
        InputLook.Paint(g, Size, s, _focus, Enabled, false);
        if (_chevron) InputLook.Chevron(g, new RectangleF(Width - 28 * s, 0, 22 * s, Height), s, Enabled);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var s = DeviceDpi / 96f;
        if (_chevron && Enabled && e.Button == MouseButtons.Left && e.X >= Width - 30 * s) ShowChoices();
        else _tb.Focus();
    }

    void ShowChoices()
    {
        if (Choices.Count == 0) return;
        var m = InputLook.Menu(Font, Width);
        foreach (var c in Choices.ToList())
        {
            var it = new ToolStripMenuItem(c) { Checked = string.Equals(c, _tb.Text.Trim(), StringComparison.Ordinal), Padding = new Padding(4, 5, 4, 5) };
            it.Click += (_, _) => { _tb.Text = c; ChoicePicked?.Invoke(); };
            m.Items.Add(it);
        }
        m.Closed += (_, _) => BeginInvoke(new Action(() => m.Dispose()));
        m.Show(this, new Point(0, Height + 2));
    }
}

/// <summary>A slim glass progress bar (teal, glossy) for long jobs such as copying files.</summary>
public sealed class GlassProgress : Control
{
    int _min, _max = 100, _val;
    public GlassProgress() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override Size DefaultSize => new(200, 22);
    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }
    public int Value { get => _val; set { _val = Math.Clamp(value, _min, Math.Max(_min, _max)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.White);
        var s = DeviceDpi / 96f;
        var h = Math.Min(Height - 2f, 14 * s);
        var r = new RectangleF(0.5f, (Height - h) / 2f, Width - 2f, h);
        using (var tp = AppTheme.RoundRect(r, h / 2f))
        {
            using (var tb = new SolidBrush(Color.FromArgb(222, 232, 233))) g.FillPath(tb, tp);
            using var tpen = new Pen(Color.FromArgb(196, 212, 213)); g.DrawPath(tpen, tp);
        }
        var frac = _max > _min ? (_val - _min) / (float)(_max - _min) : 0f;
        if (frac <= 0f) return;
        var fr = new RectangleF(r.X, r.Y, Math.Max(h, r.Width * frac), h);
        using var fp = AppTheme.RoundRect(fr, h / 2f);
        using (var fb = new LinearGradientBrush(new RectangleF(fr.X, fr.Y - 1, fr.Width, fr.Height + 2), AppTheme.Mix(AppTheme.Accent, Color.White, 0.25f), AppTheme.Mix(AppTheme.Accent, Color.Black, 0.1f), 90f)) g.FillPath(fb, fp);
        var st = g.Save();
        g.SetClip(fp, CombineMode.Intersect);
        using (var gb = new LinearGradientBrush(new RectangleF(fr.X, fr.Y - 1, fr.Width, fr.Height * 0.5f + 2), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90f))
            g.FillRectangle(gb, fr.X, fr.Y, fr.Width, fr.Height * 0.5f);
        g.Restore(st);
        using var pen = new Pen(AppTheme.Mix(AppTheme.Accent, Color.Black, 0.3f)); g.DrawPath(pen, fp);
    }
}

/// <summary>Glass styling for a details list: soft header band, tinted selection and faint zebra rows.</summary>
static class GlassList
{
    public static void Apply(ListView lv)
    {
        lv.OwnerDraw = true;
        lv.FullRowSelect = true;
        lv.BackColor = Color.White;
        try { typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lv, true); } catch { }

        lv.DrawColumnHeader += (_, e) =>
        {
            var g = e.Graphics;
            var b = e.Bounds;
            using (var br = new LinearGradientBrush(new Rectangle(b.X, b.Y - 1, b.Width + 1, b.Height + 2), Color.FromArgb(244, 249, 249), Color.FromArgb(226, 239, 238), 90f)) g.FillRectangle(br, b);
            using (var pen = new Pen(AppTheme.Line)) g.DrawLine(pen, b.X, b.Bottom - 1, b.Right, b.Bottom - 1);
            using var sep = new Pen(Color.FromArgb(205, 221, 222));
            g.DrawLine(sep, b.Right - 1, b.Y + 6, b.Right - 1, b.Bottom - 7);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            if (e.Header?.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            using var f = AppTheme.Semi(9f);
            TextRenderer.DrawText(g, e.Header?.Text ?? "", f, new Rectangle(b.X + 10, b.Y, b.Width - 20, b.Height), Color.FromArgb(5, 70, 66), flags);
        };
        lv.DrawItem += (_, e) => { };   // each cell is painted in DrawSubItem
        lv.DrawSubItem += (_, e) =>
        {
            var g = e.Graphics;
            var b = e.Bounds;
            var sel = e.Item?.Selected == true;
            var back = sel ? Color.FromArgb(214, 236, 233) : (e.ItemIndex % 2 == 1 ? Color.FromArgb(248, 251, 251) : Color.White);
            using (var br = new SolidBrush(back)) g.FillRectangle(br, b);
            var x = b.X + 10;
            if (e.ColumnIndex == 0 && lv.SmallImageList != null && e.Item != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < lv.SmallImageList.Images.Count)
            {
                var img = lv.SmallImageList.Images[e.Item.ImageIndex];
                g.DrawImage(img, x, b.Y + (b.Height - img.Height) / 2, img.Width, img.Height);
                x += img.Width + 8;
            }
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            var hdr = e.ColumnIndex < lv.Columns.Count ? lv.Columns[e.ColumnIndex] : null;
            if (hdr?.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(g, e.SubItem?.Text ?? "", lv.Font, new Rectangle(x, b.Y, Math.Max(4, b.Right - x - 10), b.Height), AppTheme.Ink, flags);
        };
    }
}
