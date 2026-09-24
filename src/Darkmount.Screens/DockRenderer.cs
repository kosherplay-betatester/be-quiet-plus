using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>Renders a screen plus the alert overlay into a dock-sized bitmap.</summary>
public static class DockRenderer
{
    public const int Width = 320, Height = 240;

    public static SKBitmap Render(IDockScreen screen, ScreenContext ctx)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(ctx);
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Theme.Background);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, Width, Height));
        screen.Render(canvas, ctx);
        canvas.Restore();
        AlertOverlay.Draw(canvas, ctx.Alerts);
        canvas.Flush();
        return bitmap;
    }
}
