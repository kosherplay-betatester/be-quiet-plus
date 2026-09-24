using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>Shared drawing helpers for animation sources.</summary>
internal static class FrameDrawing
{
    public static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    public static readonly SKSamplingOptions Upscale = new(SKCubicResampler.Mitchell);

    /// <summary>Draws <paramref name="image"/> so it fills <paramref name="bounds"/>, cropping overflow, keeping aspect.</summary>
    public static void DrawCover(SKCanvas canvas, SKImage image, SKRect bounds)
    {
        canvas.DrawImage(image, CoverSource(image.Width, image.Height, bounds), bounds,
            image.Width < bounds.Width || image.Height < bounds.Height ? Upscale : Smooth, null);
    }

    public static void DrawCover(SKCanvas canvas, SKBitmap bitmap, SKRect bounds)
    {
        using var image = SKImage.FromBitmap(bitmap);
        DrawCover(canvas, image, bounds);
    }

    /// <summary>The centred source rectangle with the aspect ratio of <paramref name="bounds"/>.</summary>
    public static SKRect CoverSource(int width, int height, SKRect bounds)
    {
        float scale = Math.Max(bounds.Width / width, bounds.Height / height);
        float sw = bounds.Width / scale, sh = bounds.Height / scale;
        float sx = (width - sw) / 2, sy = (height - sh) / 2;
        return new SKRect(sx, sy, sx + sw, sy + sh);
    }

    /// <summary>A translucent bar at the bottom with a one-line message (used for errors).</summary>
    public static void DrawCaption(SKCanvas canvas, SKRect bounds, string text)
    {
        using var font = Theme.Font(Theme.SemiBold, 12);
        float h = 24;
        var bar = new SKRect(bounds.Left, bounds.Bottom - h, bounds.Right, bounds.Bottom);
        using (var bg = Theme.Fill(SKColors.Black.WithAlpha(0xB8)))
            canvas.DrawRect(bar, bg);
        using var paint = Theme.Fill(Theme.Text);
        string fitted = Theme.Ellipsize(text, font, bar.Width - 16);
        var m = font.Metrics;
        canvas.DrawText(fitted, bar.MidX, bar.MidY - (m.Ascent + m.Descent) / 2, SKTextAlign.Center, font, paint);
    }
}
