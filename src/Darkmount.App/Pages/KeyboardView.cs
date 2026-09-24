using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Darkmount.App.Pages;

/// <summary>A key on the keyboard picture: id, position/size in layout units (1 unit = one standard key), label.</summary>
public sealed record KeyShape(int Id, RectangleF Rect, string Label);

/// <summary>
/// Draws the keyboard from layout geometry, scaled to fit. Keys can be selected (single or multi with Ctrl/drag),
/// tinted with per-key colours (lighting) and marked (e.g. remapped keys).
/// </summary>
public sealed class KeyboardView : Control
{
    IReadOnlyList<KeyShape> _keys = [];
    readonly HashSet<int> _selected = [];
    readonly Dictionary<int, Color> _colors = [];
    readonly HashSet<int> _marked = [];
    int _hover = -1;
    RectangleF _bounds;
    float _scale = 1;
    PointF _offset;
    Point? _dragStart;
    Rectangle _dragRect;

    public KeyboardView()
    {
        DoubleBuffered = true;
        BackColor = Ui.Back;
        MinimumSize = new Size(600, 200);
    }

    /// <summary>Allow selecting several keys (Ctrl+click, drag a box). Otherwise a click selects exactly one key.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool MultiSelect { get; set; }

    public event Action? SelectionChanged;

    public IReadOnlyCollection<int> SelectedKeys => _selected;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<KeyShape> KeyShapes
    {
        get => _keys;
        set
        {
            _keys = value;
            _bounds = value.Count == 0 ? RectangleF.Empty : value.Select(k => k.Rect).Aggregate(RectangleF.Union);
            Relayout();
            Invalidate();
        }
    }

    public void SetColors(IReadOnlyDictionary<int, Color> colors)
    {
        _colors.Clear();
        foreach (var (id, c) in colors) _colors[id] = c;
        Invalidate();
    }

    public void SetMarked(IEnumerable<int> ids)
    {
        _marked.Clear();
        _marked.UnionWith(ids);
        Invalidate();
    }

    public void Select(IEnumerable<int> ids)
    {
        _selected.Clear();
        _selected.UnionWith(ids);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    void Relayout()
    {
        if (_bounds.Width <= 0 || Width <= 0) return;
        const float pad = 8;
        _scale = Math.Min((Width - 2 * pad) / _bounds.Width, (Height - 2 * pad) / _bounds.Height);
        _offset = new PointF(pad + (Width - 2 * pad - _bounds.Width * _scale) / 2 - _bounds.X * _scale,
                             pad + (Height - 2 * pad - _bounds.Height * _scale) / 2 - _bounds.Y * _scale);
    }

    RectangleF ToScreen(RectangleF r) =>
        new(_offset.X + r.X * _scale + 1.5f, _offset.Y + r.Y * _scale + 1.5f, r.Width * _scale - 3, r.Height * _scale - 3);

    int HitTest(Point p) => _keys.FirstOrDefault(k => ToScreen(k.Rect).Contains(p))?.Id ?? -1;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float fontSize = Math.Clamp(_scale * 0.22f, 6f, 11f);
        using var font = new Font("Segoe UI", fontSize);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        foreach (var key in _keys)
        {
            var r = ToScreen(key.Rect);
            bool selected = _selected.Contains(key.Id);
            using var path = Rounded(r, Math.Max(3, _scale * 0.12f));

            var fill = _colors.TryGetValue(key.Id, out var c)
                ? Color.FromArgb(255, Blend(c, Ui.Panel, 0.55f))
                : key.Id == _hover ? Ui.PanelHover : Color.FromArgb(34, 38, 45);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (_colors.TryGetValue(key.Id, out var glow))
            {
                using var bar = new SolidBrush(glow);
                g.FillRectangle(bar, r.X + 3, r.Bottom - Math.Max(3, _scale * 0.08f) - 2, r.Width - 6, Math.Max(3, _scale * 0.08f));
            }

            using (var pen = new Pen(selected ? Ui.Accent : Color.FromArgb(58, 63, 73), selected ? 2.5f : 1f)) g.DrawPath(pen, path);
            if (_marked.Contains(key.Id))
            {
                using var dot = new SolidBrush(Ui.Accent);
                g.FillEllipse(dot, r.Right - 9, r.Top + 4, 5, 5);
            }
            using var text = new SolidBrush(selected ? Color.White : Ui.Text);
            g.DrawString(key.Label, font, text, r, fmt);
        }

        if (_dragStart is not null && _dragRect.Width > 2)
        {
            using var sel = new Pen(Ui.Accent) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(sel, _dragRect);
        }
    }

    static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R * (1 - t) + b.R * t), (int)(a.G * (1 - t) + b.G * t), (int)(a.B * (1 - t) + b.B * t));

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is { } start && MultiSelect && e.Button == MouseButtons.Left)
        {
            _dragRect = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Invalidate();
            return;
        }
        int hover = HitTest(e.Location);
        if (hover != _hover) { _hover = hover; Invalidate(); }
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { _dragStart = e.Location; _dragRect = Rectangle.Empty; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool ctrl = (ModifierKeys & Keys.Control) != 0;
        if (MultiSelect && _dragRect.Width > 6 && _dragRect.Height > 6)
        {
            if (!ctrl) _selected.Clear();
            foreach (var k in _keys.Where(k => _dragRect.IntersectsWith(Rectangle.Round(ToScreen(k.Rect))))) _selected.Add(k.Id);
        }
        else
        {
            int id = HitTest(e.Location);
            if (id >= 0)
            {
                if (MultiSelect && ctrl) { if (!_selected.Remove(id)) _selected.Add(id); }
                else { _selected.Clear(); _selected.Add(id); }
            }
        }
        _dragStart = null;
        _dragRect = Rectangle.Empty;
        Invalidate();
        SelectionChanged?.Invoke();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }
}
