using System.Runtime.InteropServices;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// Smooth colourful plasma. Computed per pixel at low resolution and scaled up with cubic filtering.
/// Optional <paramref name="caption"/> is shown in a bar at the bottom (used for error messages).
/// </summary>
public sealed class PlasmaSource(string? caption = null) : IFrameSource
{
    private const int W = 96, H = 72;
    private const double Step = 0.45; // animation time per frame; large enough to change visibly at 0.5 fps
    private readonly SKBitmap _bitmap = new(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque));
    private readonly byte[] _pixels = new byte[W * H * 4];
    private static readonly SKColor[] Palette = BuildPalette(
        0x0B0A2A, 0x2A1B6E, 0x7B2FBF, 0xE0307A, 0xFF7A3D, 0xFFC857, 0x2EC4B6, 0x1B4F9C, 0x0B0A2A);
    private long _frame;

    public string? Caption { get; } = caption;

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        double t = _frame++ * Step;
        Compute(t);
        Marshal.Copy(_pixels, 0, _bitmap.GetPixels(), _pixels.Length);
        _bitmap.NotifyPixelsChanged();
        FrameDrawing.DrawCover(canvas, _bitmap, bounds);
        if (Caption != null) FrameDrawing.DrawCaption(canvas, bounds, Caption);
    }

    private void Compute(double t)
    {
        // Two moving centres for the radial terms.
        double c1x = W * (0.5 + 0.35 * Math.Sin(t * 0.37)), c1y = H * (0.5 + 0.35 * Math.Cos(t * 0.29));
        double c2x = W * (0.5 + 0.40 * Math.Cos(t * 0.23 + 1)), c2y = H * (0.5 + 0.30 * Math.Sin(t * 0.31 + 2));
        double hue = t * 0.06;
        int i = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                double v = Math.Sin(x * 0.15 + t)
                         + Math.Sin(y * 0.17 - t * 0.8)
                         + Math.Sin((x * 0.09 + y * 0.11) + t * 0.6)
                         + Math.Sin(Math.Sqrt((x - c1x) * (x - c1x) + (y - c1y) * (y - c1y)) * 0.19 - t)
                         + Math.Sin(Math.Sqrt((x - c2x) * (x - c2x) + (y - c2y) * (y - c2y)) * 0.14 + t * 0.7);
                double u = v / 5 * 0.5 + 0.5; // 0..1

                // Cyclic palette lookup with slow palette rotation (classic plasma colour cycling).
                double p = u * 1.25 + hue;
                var c = Palette[(int)((p - Math.Floor(p)) * (Palette.Length - 1))];
                _pixels[i++] = c.Red;
                _pixels[i++] = c.Green;
                _pixels[i++] = c.Blue;
                _pixels[i++] = 255;
            }
        }
    }

    private static SKColor[] BuildPalette(params int[] stops)
    {
        var lut = new SKColor[512];
        int segments = stops.Length - 1;
        for (int i = 0; i < lut.Length; i++)
        {
            double pos = (double)i / (lut.Length - 1) * segments;
            int k = Math.Min((int)pos, segments - 1);
            double f = pos - k;
            f = f * f * (3 - 2 * f); // smoothstep between stops
            SKColor a = new((uint)(0xFF000000 | (uint)stops[k])), b = new((uint)(0xFF000000 | (uint)stops[k + 1]));
            lut[i] = new SKColor((byte)(a.Red + (b.Red - a.Red) * f), (byte)(a.Green + (b.Green - a.Green) * f),
                (byte)(a.Blue + (b.Blue - a.Blue) * f));
        }
        return lut;
    }

    public void Dispose() => _bitmap.Dispose();
}

/// <summary>Green "digital rain" of katakana and digits.</summary>
public sealed class MatrixSource : IFrameSource
{
    private const float CellW = 13, CellH = 16;
    private const int StepsPerFrame = 3;

    private static readonly char[] Glyphs = BuildGlyphs(out TypefaceWithGlyphs);
    private static readonly SKTypeface TypefaceWithGlyphs;

    private readonly Random _rng = new(1977);
    private int _cols, _rows;
    private char[,] _chars = new char[0, 0];
    private float[] _head = [], _speed = [], _length = [];

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        EnsureGrid(bounds);
        for (int s = 0; s < StepsPerFrame; s++) Step();

        using (var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, bounds.Top), new SKPoint(0, bounds.Bottom),
                [new SKColor(0x00, 0x0A, 0x04), new SKColor(0x00, 0x03, 0x01)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(bounds, bg);
        }

        using var font = new SKFont(TypefaceWithGlyphs, CellH - 2) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { IsAntialias = true };
        using var glow = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f) };

        // Faint static layer so the screen never looks empty.
        paint.Color = new SKColor(0x00, 0xFF, 0x66, 0x12);
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                DrawChar(canvas, _chars[c, r], c, r, bounds, font, paint);

        for (int c = 0; c < _cols; c++)
        {
            int head = (int)MathF.Floor(_head[c]);
            int len = (int)_length[c];
            for (int k = len; k >= 0; k--)
            {
                int r = head - k;
                if (r < 0 || r >= _rows) continue;
                char ch = _chars[c, r];
                if (k == 0)
                {
                    glow.Color = new SKColor(0x6B, 0xFF, 0x9A, 0xC0);
                    DrawChar(canvas, ch, c, r, bounds, font, glow);
                    paint.Color = new SKColor(0xE6, 0xFF, 0xEC);
                }
                else
                {
                    float f = 1f - (float)k / (len + 1); // 1 near head → 0 at tail
                    byte g = (byte)(90 + 165 * f);
                    paint.Color = new SKColor((byte)(20 * f * f), g, (byte)(40 + 60 * f), (byte)(255 * Math.Clamp(f * 1.4f, 0.12f, 1f)));
                }
                DrawChar(canvas, ch, c, r, bounds, font, paint);
            }
        }
    }

    private void DrawChar(SKCanvas canvas, char ch, int col, int row, SKRect bounds, SKFont font, SKPaint paint)
    {
        float x = bounds.Left + col * CellW + CellW / 2 + 1;
        float y = bounds.Top + (row + 1) * CellH - 3;
        canvas.DrawText(ch.ToString(), x, y, SKTextAlign.Center, font, paint);
    }

    private void EnsureGrid(SKRect bounds)
    {
        int cols = (int)Math.Ceiling(bounds.Width / CellW), rows = (int)Math.Ceiling(bounds.Height / CellH);
        if (cols == _cols && rows == _rows) return;
        _cols = cols;
        _rows = rows;
        _chars = new char[cols, rows];
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                _chars[c, r] = Glyphs[_rng.Next(Glyphs.Length)];
        _head = new float[cols];
        _speed = new float[cols];
        _length = new float[cols];
        for (int c = 0; c < cols; c++)
        {
            Respawn(c);
            _head[c] = _rng.Next(-rows, rows); // staggered start
        }
    }

    private void Respawn(int c)
    {
        _head[c] = -_rng.Next(0, 6);
        _speed[c] = 0.7f + (float)_rng.NextDouble() * 1.1f;
        _length[c] = 6 + _rng.Next(0, 12);
    }

    private void Step()
    {
        for (int c = 0; c < _cols; c++)
        {
            _head[c] += _speed[c];
            if (_head[c] - _length[c] > _rows) Respawn(c);
        }
        // Mutate a few characters every step.
        int mutations = _cols * _rows / 18;
        for (int i = 0; i < mutations; i++)
            _chars[_rng.Next(_cols), _rng.Next(_rows)] = Glyphs[_rng.Next(Glyphs.Length)];
    }

    private static bool HasGlyphs(SKTypeface tf, string text)
    {
        using var font = new SKFont(tf, 12);
        return font.ContainsGlyphs(text);
    }

    private static char[] BuildGlyphs(out SKTypeface typeface)
    {
        var kana = new List<char>();
        for (char ch = 'ｦ'; ch <= 'ﾝ'; ch++) kana.Add(ch);
        const string digits = "0123456789";
        string kanaStr = new(kana.ToArray());
        foreach (var family in new[] { "Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Gothic" })
        {
            var tf = SKTypeface.FromFamilyName(family, SKFontStyle.Bold);
            if (tf != null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase) && HasGlyphs(tf, kanaStr + digits))
            {
                typeface = tf;
                return (kanaStr + kanaStr + digits + "Z:.=*+-<>").ToCharArray();
            }
        }
        typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold) ?? SKTypeface.Default;
        return "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ$+-*/=%\"'#&_(),.;:?!|{}<>[]^~".ToCharArray();
    }

    public void Dispose() { }
}

/// <summary>Warp-speed starfield with motion streaks.</summary>
public sealed class StarfieldSource : IFrameSource
{
    private const int Count = 420;
    private const float Speed = 0.045f;     // z travel per frame
    private const float StreakLen = 0.09f;  // z extent of each streak

    private readonly Random _rng = new(4242);
    private readonly float[] _x = new float[Count], _y = new float[Count], _z = new float[Count];
    private readonly SKColor[] _tint = new SKColor[Count];
    private long _frame;

    public StarfieldSource()
    {
        for (int i = 0; i < Count; i++) Spawn(i, (float)_rng.NextDouble() * 0.98f + 0.02f);
    }

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        _frame++;
        for (int i = 0; i < Count; i++)
        {
            _z[i] -= Speed;
            if (_z[i] <= 0.02f) Spawn(i, 1f);
        }

        float cx = bounds.MidX, cy = bounds.MidY;
        float focal = bounds.Width * 0.42f;

        // Deep space background with a slowly drifting nebula tint.
        float hueShift = (float)((_frame * 0.07) % (2 * Math.PI));
        var nebula = new SKColor((byte)(26 + 18 * MathF.Sin(hueShift)), (byte)(20), (byte)(70 + 20 * MathF.Cos(hueShift)));
        using (var bg = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), bounds.Width * 0.75f,
                [nebula, new SKColor(0x06, 0x07, 0x14), new SKColor(0x01, 0x01, 0x05)], [0, 0.45f, 1], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(bounds, bg);
        }

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var glow = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f),
        };

        // Far stars first so near streaks draw on top.
        var order = Enumerable.Range(0, Count).OrderByDescending(i => _z[i]);
        foreach (int i in order)
        {
            float z1 = _z[i], z0 = Math.Min(1.2f, z1 + StreakLen);
            var head = new SKPoint(cx + _x[i] / z1 * focal, cy + _y[i] / z1 * focal);
            var tail = new SKPoint(cx + _x[i] / z0 * focal, cy + _y[i] / z0 * focal);
            if (!bounds.Contains(tail) && !bounds.Contains(head)) continue;

            float near = 1 - z1; // 0 far … 1 near
            byte a = (byte)(60 + 195 * Math.Clamp(near * 1.2f, 0, 1));
            float width = 0.6f + 1.8f * near * near;

            if (near > 0.55f)
            {
                glow.Color = _tint[i].WithAlpha((byte)(a / 3));
                glow.StrokeWidth = width * 1.8f;
                canvas.DrawLine(tail, head, glow);
            }

            paint.Shader = SKShader.CreateLinearGradient(tail, head,
                [_tint[i].WithAlpha(0), _tint[i].WithAlpha(a)], SKShaderTileMode.Clamp);
            paint.StrokeWidth = width;
            canvas.DrawLine(tail, head, paint);
            paint.Shader?.Dispose();
            paint.Shader = null;
        }
    }

    private void Spawn(int i, float z)
    {
        // Avoid the exact centre so no stars sit motionless in the middle.
        double angle = _rng.NextDouble() * Math.PI * 2;
        double radius = 0.04 + Math.Sqrt(_rng.NextDouble()) * 0.9;
        _x[i] = (float)(Math.Cos(angle) * radius);
        _y[i] = (float)(Math.Sin(angle) * radius * 0.8);
        _z[i] = z;
        _tint[i] = _rng.Next(10) switch
        {
            0 => new SKColor(0xFF, 0xD9, 0xA8), // warm
            1 or 2 => new SKColor(0x9C, 0xC8, 0xFF), // blue
            _ => new SKColor(0xF2, 0xF6, 0xFF),
        };
    }

    public void Dispose() { }
}
