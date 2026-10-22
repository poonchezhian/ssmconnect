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

/// <summary>Grey = off, Red = stopped, Amber = connecting, Green = connected, Blue = ready (signed in, nothing open yet).</summary>
public enum CardLevel { Grey, Red, Amber, Green, Blue }

public sealed record CardAction(string Id, string Text, bool Enabled);

public sealed class CardData
{
    public string Name = "", StatusText = "", Detail = "", Message = "", Port = "", InstanceId = "", Since = "";
    public string Environment = "", Spec = "", OsKey = "", UsageTip = "";
    public bool IsWindows, IsProd, KnownType;
    public CardLevel Level;
    /// <summary>CPU, memory and disk usage in percent; -1 when there is no reading.</summary>
    public int Cpu = -1, Ram = -1, Disk = -1;
    public List<CardAction> Actions = new();
}

/// <summary>One server as a glass card: OS logo, name, size, usage rings, status and the buttons that open the tools. The whole card is tinted and lit from behind by its status.</summary>
public sealed class ServerCard : Control
{
    static readonly Color BadFg = Color.FromArgb(145, 0, 20);
    static readonly Color BadBg = Color.FromArgb(250, 214, 214);

    static (Color Top, Color Bot, Color Edge, Color Glow, Color Text) Palette(CardLevel l) => l switch
    {
        CardLevel.Green => (Color.FromArgb(232, 247, 237), Color.FromArgb(208, 238, 219), Color.FromArgb(70, 176, 108), Color.FromArgb(46, 190, 100), Color.FromArgb(0, 96, 44)),
        CardLevel.Blue => (Color.FromArgb(234, 244, 254), Color.FromArgb(211, 229, 249), Color.FromArgb(84, 140, 224), Color.FromArgb(60, 130, 230), Color.FromArgb(14, 70, 150)),
        CardLevel.Amber => (Color.FromArgb(255, 246, 222), Color.FromArgb(252, 232, 190), Color.FromArgb(232, 164, 24), Color.FromArgb(242, 172, 30), Color.FromArgb(125, 70, 0)),
        CardLevel.Red => (Color.FromArgb(254, 232, 232), Color.FromArgb(250, 210, 210), Color.FromArgb(208, 66, 66), Color.FromArgb(228, 64, 64), BadFg),
        _ => (Color.FromArgb(244, 246, 247), Color.FromArgb(230, 233, 235), Color.FromArgb(166, 174, 178), Color.Empty, Color.FromArgb(70, 78, 82)),
    };

    CardData _d = new();
    readonly ToolTip _tip = new() { AutoPopDelay = 20000 };
    readonly List<(RectangleF Rect, string Id, bool Enabled)> _hits = new();
    readonly List<(RectangleF Rect, string Tip)> _ringHits = new();
    int _hover = -1, _down = -1, _ringHover = -1;
    float _t, _target;          // hover glow of the button under the pointer, eased in and out
    int _fadeIdx = -1;
    System.Windows.Forms.Timer? _anim, _pulse;
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
                SetTip(BaseTip());
                UpdatePulse();
                Invalidate();
            }
        }
    }

    string _tipShown = "\u0001";
    void SetTip(string text)
    {
        if (_tipShown == text) return;
        _tipShown = text;
        _tip.SetToolTip(this, text);
    }

    string BaseTip() => _d.Name + "\n" + (_d.KnownType ? (_d.IsWindows ? "Windows, Remote Desktop" : "Linux, SSH") : "Server") + ", local port " + _d.Port + "\n" + _d.InstanceId + "\n\n" + _d.Message;

    static bool SameAs(CardData a, CardData b) =>
        a.Name == b.Name && a.StatusText == b.StatusText && a.Detail == b.Detail && a.Message == b.Message && a.Port == b.Port && a.Level == b.Level && a.Since == b.Since &&
        a.IsWindows == b.IsWindows && a.IsProd == b.IsProd && a.Environment == b.Environment && a.KnownType == b.KnownType && a.InstanceId == b.InstanceId &&
        a.Spec == b.Spec && a.OsKey == b.OsKey && a.UsageTip == b.UsageTip && a.Cpu == b.Cpu && a.Ram == b.Ram && a.Disk == b.Disk &&
        a.Actions.Count == b.Actions.Count && a.Actions.Zip(b.Actions).All(p => p.First == p.Second);

    public static int HeightFor(float scale) => (int)(150 * scale);

    void UpdatePulse()
    {
        if (_d.Level == CardLevel.Amber)
        {
            if (_pulse == null)
            {
                _pulse = new System.Windows.Forms.Timer { Interval = 90 };
                _pulse.Tick += (_, _) => { if (Visible) Invalidate(); };
            }
            _pulse.Start();
        }
        else _pulse?.Stop();
    }

    protected override void Dispose(bool disposing) { if (disposing) { _tip.Dispose(); _anim?.Dispose(); _pulse?.Dispose(); } base.Dispose(disposing); }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        var d = Math.Max(1f, rad * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    static Color RingColor(int pct) => pct > 85 ? Color.FromArgb(214, 64, 60) : pct >= 60 ? Color.FromArgb(232, 160, 20) : Color.FromArgb(46, 160, 90);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent?.BackColor ?? AppTheme.Page);
        var s = DeviceDpi / 96f;
        var pal = Palette(_d.Level);
        float pad = 8 * s;
        var card = new RectangleF(pad, pad, Width - 2 * pad - 1, Height - 2 * pad - 1);

        // light from behind the card (status colour), or a soft shadow when the server is off
        if (pal.Glow != Color.Empty)
        {
            var pulse = _d.Level == CardLevel.Amber ? 0.55f + 0.45f * (0.5f + 0.5f * (float)Math.Sin(Environment.TickCount64 / 320.0)) : 1f;
            for (var k = 6; k >= 1; k--)
            {
                using var gp = Round(RectangleF.Inflate(card, k * s, k * s), (14 + k) * s);
                var a = (int)(86 * pulse * (7 - k) / 7f * 0.55f);
                using var gb = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 255), pal.Glow));
                g.FillPath(gb, gp);
            }
        }
        else
        {
            using var sp = Round(new RectangleF(card.X, card.Y + 2 * s, card.Width, card.Height), 14 * s);
            using var sb = new SolidBrush(Color.FromArgb(18, 0, 0, 0));
            g.FillPath(sb, sp);
        }

        using (var path = Round(card, 14 * s))
        {
            using (var br = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), pal.Top, pal.Bot, 90f)) g.FillPath(br, path);
            // glossy upper edge
            var st = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            using (var gl = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height * 0.42f + 2), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(gl, card.X, card.Y, card.Width, card.Height * 0.42f);
            g.Restore(st);
            using var pen = new Pen(_selected ? AppTheme.Accent : Color.FromArgb(150, pal.Edge), _selected ? 2f : 1f);
            g.DrawPath(pen, path);
        }

        float left = card.X + 14 * s, right = card.Right - 14 * s;

        // operating-system logo in a pressed-in round well
        var well = new RectangleF(left, card.Y + 14 * s, 48 * s, 48 * s);
        DrawWell(g, well, s);

        float tx = well.Right + 12 * s;
        using var nameFont = AppTheme.Semi(10.5f);
        using var small = AppTheme.Body(8.5f);
        using var smallBold = AppTheme.Semi(8.5f);
        using var tiny = AppTheme.Body(8f);

        // environment tag: red for production, quiet grey for the rest
        float tagRight = right;
        if (!string.IsNullOrWhiteSpace(_d.Environment))
        {
            var envText = _d.IsProd ? "PROD" : _d.Environment.ToUpperInvariant();
            if (envText.Length > 12) envText = envText.Substring(0, 12);
            var w = TextRenderer.MeasureText(envText, tiny).Width + 14 * s;
            var r = new RectangleF(right - w, card.Y + 14 * s, w, 20 * s);
            using (var p = Round(r, 10 * s)) using (var b = new SolidBrush(_d.IsProd ? BadBg : Color.FromArgb(232, 240, 240))) g.FillPath(b, p);
            TextRenderer.DrawText(g, envText, tiny, Rectangle.Round(r), _d.IsProd ? BadFg : Color.FromArgb(70, 96, 100), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            tagRight = right - w - 8 * s;
        }
        TextRenderer.DrawText(g, _d.Name, nameFont, new Rectangle((int)tx, (int)(card.Y + 12 * s), (int)(tagRight - tx), (int)(22 * s)), AppTheme.Ink,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        // one line: system, size
        var spec = string.IsNullOrEmpty(_d.Spec) ? (_d.KnownType ? (_d.IsWindows ? "Windows" : "Linux") : "Server") : _d.Spec;
        TextRenderer.DrawText(g, spec, small, new Rectangle((int)tx, (int)(card.Y + 35 * s), (int)(right - tx), (int)(18 * s)),
            Color.FromArgb(84, 100, 106), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        // status text, then the CPU / RAM / disk usage bars on a row of their own
        float rowY = card.Y + 55 * s;
        var textRect = new Rectangle((int)tx, (int)rowY, (int)(right - tx), (int)(20 * s));
        var stateText = _d.StatusText + (string.IsNullOrEmpty(_d.Detail) ? "" : "  ·  " + _d.Detail);
        TextRenderer.DrawText(g, stateText, smallBold, textRect, pal.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        _ringHits.Clear();
        float uw = 58 * s, uh = 20 * s, ugap = 6 * s, uy = card.Y + 79 * s;
        DrawUsage(g, new RectangleF(tx, uy, uw, uh), _d.Cpu, "cpu", s, "CPU", _d.UsageTip);
        DrawUsage(g, new RectangleF(tx + uw + ugap, uy, uw, uh), _d.Ram, "ram", s, "Memory", _d.UsageTip);
        DrawUsage(g, new RectangleF(tx + 2 * (uw + ugap), uy, uw, uh), _d.Disk, "disk", s, "Disk", _d.UsageTip);

        // tool buttons: one row, equal columns, always visible
        _hits.Clear();
        var n = _d.Actions.Count;
        float bh = 30 * s, by = card.Bottom - 14 * s - bh, gap = 8 * s;
        if (n > 0)
        {
            var bw = (card.Right - 14 * s - left - gap * (n - 1)) / n;
            for (var i = 0; i < n; i++) _hits.Add((new RectangleF(left + i * (bw + gap), by, bw, bh), _d.Actions[i].Id, _d.Actions[i].Enabled));
        }
        for (var i = 0; i < _hits.Count; i++)
        {
            var (r, id, en) = _hits[i];
            var a = _d.Actions[i];
            var hot = en && i == _fadeIdx ? _t : 0f;
            var baseC = id == "signin" ? ButtonLook.ToneColor(ButtonTone.Warning) : i == 0 ? AppTheme.Accent : Color.FromArgb(214, 236, 233);
            var (cell, text, shadow) = ButtonLook.PaintSurface(g, r, 9 * s, baseC, hot, en && i == _down, !en, s);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            var tr = Rectangle.Round(cell);
            TextRenderer.DrawText(g, a.Text, smallBold, new Rectangle(tr.X, tr.Y + 1, tr.Width, tr.Height), shadow, flags);
            TextRenderer.DrawText(g, a.Text, smallBold, tr, text, flags);
        }
    }

    /// <summary>A round white recess (darker rim at the top, light lip at the bottom) holding the operating-system logo.</summary>
    void DrawWell(Graphics g, RectangleF well, float s)
    {
        // light lip underneath makes it look sunk into the card
        using (var lip = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillEllipse(lip, well.X, well.Y + 1.5f * s, well.Width, well.Height);
        using (var rim = new LinearGradientBrush(new RectangleF(well.X, well.Y - 1, well.Width, well.Height + 2), Color.FromArgb(150, 158, 162), Color.FromArgb(226, 231, 233), 90f))
            g.FillEllipse(rim, well);
        var inner = RectangleF.Inflate(well, -2.2f * s, -2.2f * s);
        inner.Offset(0, 0.8f * s);
        using (var fill = new LinearGradientBrush(new RectangleF(inner.X, inner.Y - 1, inner.Width, inner.Height + 2), Color.FromArgb(244, 246, 247), Color.White, 90f))
            g.FillEllipse(fill, inner);
        var img = OsIcons.Get(_d.OsKey);
        if (img != null)
        {
            var sz = 28 * s;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, new RectangleF(well.X + (well.Width - sz) / 2f, well.Y + (well.Height - sz) / 2f + 0.8f * s, sz, sz));
            g.InterpolationMode = old;
        }
        else
        {
            using var f = AppTheme.Semi(11f);
            TextRenderer.DrawText(g, ">_", f, Rectangle.Round(well), Color.FromArgb(90, 104, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Colour of a usage bar: the same green / amber / red as the buttons, soft grey while there is no reading.</summary>
    internal static Color UsageColor(int pct) => pct < 0 ? Color.FromArgb(222, 229, 230) : RingColor(pct);

    /// <summary>One small button-like bar: an icon (CPU chip, memory stick or disk) and the percentage in use.</summary>
    void DrawUsage(Graphics g, RectangleF r, int pct, string kind, float s, string name, string note)
    {
        var (cell, text, shadow) = ButtonLook.PaintSurface(g, r, 6 * s, UsageColor(pct), 0f, false, false, s);
        var ic = new RectangleF(cell.X + 7 * s, cell.Y + (cell.Height - 11 * s) / 2f, 11 * s, 11 * s);
        UsageGlyph(g, kind, ic, pct < 0 ? Color.FromArgb(120, 134, 138) : Color.White, s);
        using var f = AppTheme.Semi(7.5f);
        var label = pct >= 0 ? pct + "%" : "--";
        var tr = new Rectangle((int)(ic.Right + 3 * s), (int)cell.Y, (int)(cell.Right - ic.Right - 6 * s), (int)cell.Height);
        TextRenderer.DrawText(g, label, f, tr, pct < 0 ? Color.FromArgb(104, 118, 122) : text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var tip = pct >= 0 ? $"{name}: {pct}% in use" : name + ": no reading yet" + (string.IsNullOrEmpty(note) ? "" : "\n" + note);
        _ringHits.Add((r, tip));
    }

    /// <summary>Tiny line icons: "cpu" (chip with pins), "ram" (memory stick) and "disk" (drive cylinder).</summary>
    internal static void UsageGlyph(Graphics g, string kind, RectangleF r, Color c, float s)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.2f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new SolidBrush(c);
        switch (kind)
        {
            case "cpu":
            {
                var body = RectangleF.Inflate(r, -2.2f * s, -2.2f * s);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                g.FillRectangle(fill, body.X + body.Width * 0.3f, body.Y + body.Height * 0.3f, body.Width * 0.4f, body.Height * 0.4f);
                for (var k = 0; k < 3; k++)
                {
                    var t = body.X + body.Width * (0.2f + 0.3f * k);
                    g.DrawLine(pen, t, r.Y, t, body.Y); g.DrawLine(pen, t, body.Bottom, t, r.Bottom);
                    var u = body.Y + body.Height * (0.2f + 0.3f * k);
                    g.DrawLine(pen, r.X, u, body.X, u); g.DrawLine(pen, body.Right, u, r.Right, u);
                }
                break;
            }
            case "ram":
            {
                var body = new RectangleF(r.X, r.Y + 2.6f * s, r.Width, r.Height - 6f * s);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                for (var k = 0; k < 3; k++) g.FillRectangle(fill, body.X + 1.6f * s + k * 3.2f * s, body.Y + 1.8f * s, 2f * s, body.Height - 3.6f * s);
                for (var k = 0; k < 4; k++) g.DrawLine(pen, body.X + 1.2f * s + k * 3f * s, body.Bottom, body.X + 1.2f * s + k * 3f * s, body.Bottom + 1.8f * s);
                break;
            }
            default:
            {
                var w = r.Width - 2f * s; var x = r.X + 1f * s; var h = 3f * s;
                g.DrawEllipse(pen, x, r.Y + 0.6f * s, w, h);
                g.DrawLine(pen, x, r.Y + 0.6f * s + h / 2f, x, r.Bottom - 2.6f * s);
                g.DrawLine(pen, x + w, r.Y + 0.6f * s + h / 2f, x + w, r.Bottom - 2.6f * s);
                g.DrawArc(pen, x, r.Bottom - 2.6f * s - h, w, h, 0, 180);
                g.DrawArc(pen, x, r.Y + 0.6f * s + (r.Height - 3.2f * s - h) / 2f, w, h, 0, 180);
                break;
            }
        }
        g.SmoothingMode = old;
    }

    void FadeTo(float target)
    {
        _target = target;
        if (_anim == null)
        {
            _anim = new System.Windows.Forms.Timer { Interval = 15 };
            _anim.Tick += (_, _) =>
            {
                var d = _target - _t;
                if (Math.Abs(d) < 0.04f) { _t = _target; _anim?.Stop(); if (_t == 0f) _fadeIdx = -1; } else _t += d * 0.35f;
                Invalidate();
            };
        }
        _anim.Start();
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
        if (i != _hover) { _hover = i; if (i >= 0) _fadeIdx = i; FadeTo(i >= 0 && _hits[i].Enabled ? 1f : 0f); Invalidate(); }
        Cursor = i >= 0 && _hits[i].Enabled ? Cursors.Hand : Cursors.Default;
        var ring = -1;
        for (var k = 0; k < _ringHits.Count; k++) if (_ringHits[k].Rect.Contains(e.Location)) { ring = k; break; }
        if (ring != _ringHover)
        {
            _ringHover = ring;
            SetTip(ring >= 0 ? _ringHits[ring].Tip : BaseTip());
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; FadeTo(0f); Invalidate(); }
        if (_down != -1) { _down = -1; Invalidate(); }
        if (_ringHover != -1) { _ringHover = -1; SetTip(BaseTip()); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var i = HitIndex(e.Location);
        Picked?.Invoke(this);
        if (i >= 0 && _hits[i].Enabled && e.Button == MouseButtons.Left) { _down = i; Invalidate(); ActionClicked?.Invoke(this, _hits[i].Id); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_down != -1) { _down = -1; Invalidate(); }
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (HitIndex(PointToClient(Cursor.Position)) < 0) Activated?.Invoke(this);
    }
}
