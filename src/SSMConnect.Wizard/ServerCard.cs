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

public enum CardLevel { Grey, Red, Amber, Green }

public sealed record CardAction(string Id, string Text, bool Enabled);

public sealed class CardData
{
    public string Name = "", StatusText = "", Message = "", Port = "", InstanceId = "", Since = "";
    public string Environment = "";
    public bool IsWindows, IsProd, KnownType;
    public CardLevel Level;
    public List<CardAction> Actions = new();
}

/// <summary>One server as a card: operating system, status, port and the buttons that open it.</summary>
public sealed class ServerCard : Control
{
    static readonly Color GoodBg = Color.FromArgb(206, 238, 214), GoodFg = Color.FromArgb(0, 85, 35);
    static readonly Color WarnBg = Color.FromArgb(255, 234, 186), WarnFg = Color.FromArgb(115, 65, 0);
    static readonly Color BadBg = Color.FromArgb(250, 214, 214), BadFg = Color.FromArgb(145, 0, 20);
    static readonly Color GreyBg = Color.FromArgb(228, 228, 228), GreyFg = Color.FromArgb(55, 55, 55);

    CardData _d = new();
    readonly ToolTip _tip = new() { AutoPopDelay = 20000 };
    readonly List<(RectangleF Rect, string Id, bool Enabled)> _hits = new();
    int _hover = -1;
    bool _selected;

    public event Action<ServerCard, string>? ActionClicked;
    public event Action<ServerCard>? Picked;
    public event Action<ServerCard>? Activated;

    public ServerCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Default;
        TabStop = true;
    }

    public bool Selected { get => _selected; set { if (_selected != value) { _selected = value; Invalidate(); } } }

    public CardData Data
    {
        get => _d;
        set
        {
            var changed = !SameAs(_d, value);
            _d = value;
            if (changed)
            {
                _tip.SetToolTip(this, value.Name + "\n" + (value.KnownType ? (value.IsWindows ? "Windows, Remote Desktop" : "Linux, SSH") : "Server") + ", local port " + value.Port + "\n" + value.InstanceId + "\n\n" + value.Message);
                Invalidate();
            }
        }
    }

    static bool SameAs(CardData a, CardData b) =>
        a.Name == b.Name && a.StatusText == b.StatusText && a.Message == b.Message && a.Port == b.Port && a.Level == b.Level && a.Since == b.Since &&
        a.IsWindows == b.IsWindows && a.IsProd == b.IsProd && a.Environment == b.Environment && a.KnownType == b.KnownType && a.InstanceId == b.InstanceId &&
        a.Actions.Count == b.Actions.Count && a.Actions.Zip(b.Actions).All(p => p.First == p.Second);

    public static int HeightFor(float scale) => (int)(104 * scale);

    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }

    static (Color Bg, Color Fg, Color Bar) Palette(CardLevel l) => l switch
    {
        CardLevel.Green => (GoodBg, GoodFg, Color.FromArgb(34, 170, 80)),
        CardLevel.Amber => (WarnBg, WarnFg, Color.FromArgb(240, 170, 20)),
        CardLevel.Red => (BadBg, BadFg, Color.FromArgb(205, 40, 50)),
        _ => (GreyBg, GreyFg, Color.FromArgb(150, 150, 150)),
    };

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        var d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? AppTheme.Page);
        var s = DeviceDpi / 96f;
        var (bg, fg, bar) = Palette(_d.Level);
        var card = new RectangleF(1, 1, Width - 3, Height - 3);

        using (var path = Round(card, 10 * s))
        {
            using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
            // status strip on the left edge
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            using (var b = new SolidBrush(bar)) g.FillRectangle(b, card.X, card.Y, 6 * s, card.Height);
            g.Restore(state);
            using var pen = new Pen(_selected ? AppTheme.Accent : AppTheme.Line, _selected ? 2f : 1f);
            g.DrawPath(pen, path);
        }

        float x0 = 22 * s;
        // operating-system avatar
        var av = new RectangleF(x0, 16 * s, 38 * s, 38 * s);
        var avBg = !_d.KnownType ? GreyBg : _d.IsWindows ? AppTheme.WinBg : AppTheme.LinuxBg;
        var avFg = !_d.KnownType ? GreyFg : _d.IsWindows ? AppTheme.WinFg : AppTheme.LinuxFg;
        using (var b = new SolidBrush(avBg)) g.FillEllipse(b, av);
        DrawOsGlyph(g, av, avFg, _d.IsWindows, _d.KnownType, s);

        float tx = x0 + 50 * s;
        float right = Width - 16 * s;
        using var nameFont = AppTheme.Semi(10f);
        using var small = AppTheme.Body(8.5f);
        using var tiny = AppTheme.Body(8f);

        // environment tag (from the server's AWS tags): red for production, a quiet grey for everything else
        if (!string.IsNullOrWhiteSpace(_d.Environment))
        {
            var envText = _d.IsProd ? "PROD" : _d.Environment.ToUpperInvariant();
            if (envText.Length > 12) envText = envText.Substring(0, 12);
            var w = TextRenderer.MeasureText(envText, tiny).Width + 14 * s;
            var r = new RectangleF(right - w, 17 * s, w, 20 * s);
            using (var p = Round(r, 10 * s)) using (var b = new SolidBrush(_d.IsProd ? BadBg : Color.FromArgb(232, 240, 240))) g.FillPath(b, p);
            TextRenderer.DrawText(g, envText, tiny, Rectangle.Round(r), _d.IsProd ? BadFg : Color.FromArgb(70, 96, 100), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right -= w + 8 * s;
        }
        TextRenderer.DrawText(g, _d.Name, nameFont, new Rectangle((int)tx, (int)(16 * s), (int)(right - tx), (int)(22 * s)), AppTheme.Ink,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        // second line: the operating system, or - when something is not right - what is wrong
        var osText = (!_d.KnownType ? "Server" : _d.IsWindows ? "Windows" : "Linux") + (string.IsNullOrEmpty(_d.Port) ? "" : "  -  local port " + _d.Port);
        var problem = _d.Level != CardLevel.Green;
        TextRenderer.DrawText(g, problem && !string.IsNullOrEmpty(_d.Message) ? _d.Message : osText, small,
            new Rectangle((int)tx, (int)(38 * s), (int)(Width - tx - 14 * s), (int)(18 * s)),
            problem && _d.Level == CardLevel.Red ? BadFg : Color.FromArgb(94, 110, 114), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // bottom row: status pill on the left, action buttons on the right
        float rowY = Height - 38 * s, rowH = 26 * s;
        var pillText = "●  " + _d.StatusText;
        var pw = TextRenderer.MeasureText(pillText, small).Width + 20 * s;
        var pill = new RectangleF(x0, rowY, pw, rowH);
        using (var p = Round(pill, rowH / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
        TextRenderer.DrawText(g, pillText, small, Rectangle.Round(pill), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        _hits.Clear();
        float bx = Width - 14 * s;
        for (int i = _d.Actions.Count - 1; i >= 0; i--)
        {
            var a = _d.Actions[i];
            var w = Math.Max(72 * s, TextRenderer.MeasureText(a.Text, small).Width + 24 * s);
            var r = new RectangleF(bx - w, rowY, w, rowH);
            bx -= w + 8 * s;
            _hits.Insert(0, (r, a.Id, a.Enabled));
        }
        for (int i = 0; i < _hits.Count; i++)
        {
            var (r, id, en) = _hits[i];
            var a = _d.Actions[i];
            var hot = en && i == _hover;
            using (var p = Round(r, 7 * s))
            using (var b = new SolidBrush(!en ? Color.FromArgb(240, 244, 244) : hot ? AppTheme.AccentHover : AppTheme.Accent)) g.FillPath(b, p);
            TextRenderer.DrawText(g, a.Text, small, Rectangle.Round(r), en ? Color.White : Color.FromArgb(150, 162, 164),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    static void DrawOsGlyph(Graphics g, RectangleF av, Color fg, bool windows, bool known, float s)
    {
        var cx = av.X + av.Width / 2; var cy = av.Y + av.Height / 2;
        using var b = new SolidBrush(fg);
        if (known && windows)
        {
            float q = 7 * s, gap = 2 * s;
            g.FillRectangle(b, cx - q - gap / 2, cy - q - gap / 2, q, q); g.FillRectangle(b, cx + gap / 2, cy - q - gap / 2, q, q);
            g.FillRectangle(b, cx - q - gap / 2, cy + gap / 2, q, q); g.FillRectangle(b, cx + gap / 2, cy + gap / 2, q, q);
            return;
        }
        using var pen = new Pen(fg, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(cx - 8 * s, cy - 6 * s), new PointF(cx - 2 * s, cy), new PointF(cx - 8 * s, cy + 6 * s) });
        g.DrawLine(pen, cx + 1 * s, cy + 6.5f * s, cx + 8 * s, cy + 6.5f * s);
    }

    int HitIndex(Point p)
    {
        for (int i = 0; i < _hits.Count; i++) if (_hits[i].Rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = HitIndex(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
        Cursor = i >= 0 && _hits[i].Enabled ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var i = HitIndex(e.Location);
        Picked?.Invoke(this);
        if (i >= 0 && _hits[i].Enabled && e.Button == MouseButtons.Left) ActionClicked?.Invoke(this, _hits[i].Id);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (HitIndex(PointToClient(Cursor.Position)) < 0) Activated?.Invoke(this);
    }
}
