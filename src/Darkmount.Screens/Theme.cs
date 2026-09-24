using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>Colours, typefaces and shared drawing helpers for all dock screens.</summary>
public static class Theme
{
    // Surfaces
    public static readonly SKColor Background = new(0x0B, 0x0D, 0x10);
    public static readonly SKColor Panel = new(0x14, 0x18, 0x1E);
    public static readonly SKColor PanelBorder = new(0x22, 0x28, 0x31);
    public static readonly SKColor GraphBackground = new(0x0E, 0x11, 0x15);
    public static readonly SKColor Grid = new(0x24, 0x2A, 0x33);
    public static readonly SKColor Track = new(0x23, 0x29, 0x31);

    // Text
    public static readonly SKColor Text = new(0xF4, 0xF6, 0xF8);
    public static readonly SKColor TextSecondary = new(0xA7, 0xB0, 0xBB);
    public static readonly SKColor TextDim = new(0x6B, 0x75, 0x80);

    // Accents
    public static readonly SKColor Cpu = new(0xFF, 0x8A, 0x1F);
    public static readonly SKColor Gpu = new(0x4C, 0xD9, 0x64);
    public static readonly SKColor Mem = new(0x3D, 0x9B, 0xFF);
    public static readonly SKColor Fps = new(0xB2, 0x6B, 0xFF);
    public static readonly SKColor Warn = new(0xFF, 0x9F, 0x0A);
    public static readonly SKColor Alert = new(0xFF, 0x3B, 0x30);

    public const double TempWarn = 80, TempCritical = 90;
    public const double MemWarn = 85, MemCritical = 95;

    // Typefaces (fall back to the platform default when a family is missing)
    public static readonly SKTypeface Regular = Load("Segoe UI", SKFontStyle.Normal);
    public static readonly SKTypeface SemiBold = Load("Segoe UI Semibold", SKFontStyle.Normal, "Segoe UI", SKFontStyle.Bold);
    public static readonly SKTypeface Bold = Load("Segoe UI", SKFontStyle.Bold);
    public static readonly SKTypeface Black = Load("Segoe UI Black", SKFontStyle.Normal, "Segoe UI", SKFontStyle.Bold);

    public static SKFont Font(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.Antialias, // panel subpixel order is unknown: no LCD AA
        Subpixel = true,
        Hinting = SKFontHinting.Slight,
    };

    public static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    public static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };

    /// <summary>White/orange/red depending on the temperature.</summary>
    public static SKColor TempColor(double? celsius) => celsius switch
    {
        >= TempCritical => Alert,
        >= TempWarn => Warn,
        _ => Text,
    };

    /// <summary>Bar colour for a memory usage percentage.</summary>
    public static SKColor MemColor(double percent) => percent switch
    {
        >= MemCritical => Alert,
        >= MemWarn => Warn,
        _ => Mem,
    };

    /// <summary>Draws a rounded panel with a hairline border.</summary>
    public static void DrawPanel(SKCanvas canvas, SKRect rect, float radius = 8)
    {
        using var fill = Fill(Panel);
        canvas.DrawRoundRect(rect, radius, radius, fill);
        using var border = Stroke(PanelBorder, 1);
        var r = rect;
        r.Inflate(-0.5f, -0.5f);
        canvas.DrawRoundRect(r, radius, radius, border);
    }

    /// <summary>Truncates <paramref name="text"/> with an ellipsis so it fits <paramref name="maxWidth"/>.</summary>
    public static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        const string ell = "…";
        for (int len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len].TrimEnd() + ell;
            if (font.MeasureText(candidate) <= maxWidth) return candidate;
        }
        return ell;
    }

    private static SKTypeface Load(string family, SKFontStyle style, string? fallbackFamily = null, SKFontStyle? fallbackStyle = null)
    {
        var tf = SKTypeface.FromFamilyName(family, style);
        if (tf != null && string.Equals(tf.FamilyName, family, StringComparison.OrdinalIgnoreCase)) return tf;
        if (fallbackFamily != null)
        {
            var fb = SKTypeface.FromFamilyName(fallbackFamily, fallbackStyle ?? style);
            if (fb != null) return fb;
        }
        return tf ?? SKTypeface.Default;
    }
}
