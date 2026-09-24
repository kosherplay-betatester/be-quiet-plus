using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>A 36 px red banner across the top of the screen listing the active alerts.</summary>
public static class AlertOverlay
{
    public const float BannerHeight = 36;
    private const float MaxFontSize = 19, MinFontSize = 10;

    public static void Draw(SKCanvas canvas, IReadOnlyList<Alert> alerts)
    {
        if (alerts is null || alerts.Count == 0) return;

        float w = DockRenderer.Width;
        var banner = new SKRect(0, 0, w, BannerHeight);

        // Soft shadow under the banner so it separates from whatever is below.
        using (var shadow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, BannerHeight), new SKPoint(0, BannerHeight + 8),
                [SKColors.Black.WithAlpha(0xA0), SKColors.Black.WithAlpha(0)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(new SKRect(0, BannerHeight, w, BannerHeight + 8), shadow);
        }

        using (var bg = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, BannerHeight),
                [new SKColor(0xFF, 0x4A, 0x3F), new SKColor(0xD9, 0x22, 0x18)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(banner, bg);
        }
        using (var hl = Theme.Fill(SKColors.White.WithAlpha(0x40)))
            canvas.DrawRect(new SKRect(0, 0, w, 1), hl);
        using (var dark = Theme.Fill(new SKColor(0x8C, 0x10, 0x0A)))
            canvas.DrawRect(new SKRect(0, BannerHeight - 1, w, BannerHeight), dark);

        // Warning triangle
        const float iconLeft = 10, iconSize = 24;
        float iconTop = (BannerHeight - iconSize * 0.88f) / 2;
        DrawWarningIcon(canvas, new SKRect(iconLeft, iconTop, iconLeft + iconSize, iconTop + iconSize * 0.88f));

        // Text: shrink to fit, then ellipsize as a last resort.
        string text = string.Join("  ·  ", alerts.Select(a => a.Text));
        float textLeft = iconLeft + iconSize + 9, maxWidth = w - textLeft - 10;
        float size = MaxFontSize;
        using var font = Theme.Font(Theme.Bold, size);
        while (size > MinFontSize && font.MeasureText(text) > maxWidth)
        {
            size -= 0.5f;
            font.Size = size;
        }
        text = Theme.Ellipsize(text, font, maxWidth);

        var m = font.Metrics;
        float baseline = BannerHeight / 2 - (m.Ascent + m.Descent) / 2 + 0.5f;
        using var textPaint = Theme.Fill(SKColors.White);
        canvas.DrawText(text, textLeft, baseline, SKTextAlign.Left, font, textPaint);
    }

    /// <summary>White rounded triangle with a red exclamation mark, drawn with paths (no emoji font needed).</summary>
    private static void DrawWarningIcon(SKCanvas canvas, SKRect r)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(r.MidX, r.Top);
        builder.LineTo(r.Right, r.Bottom);
        builder.LineTo(r.Left, r.Bottom);
        builder.Close();
        using var tri = builder.Detach();

        using var fill = Theme.Fill(SKColors.White);
        using var outline = Theme.Stroke(SKColors.White, 3f);
        canvas.DrawPath(tri, fill);
        canvas.DrawPath(tri, outline); // rounds the corners

        var red = new SKColor(0xE0, 0x28, 0x1E);
        float cx = r.MidX;
        using var bar = Theme.Stroke(red, 2.6f);
        canvas.DrawLine(cx, r.Top + r.Height * 0.36f, cx, r.Top + r.Height * 0.64f, bar);
        using var dot = Theme.Fill(red);
        canvas.DrawCircle(cx, r.Top + r.Height * 0.82f, 1.6f, dot);
    }
}
