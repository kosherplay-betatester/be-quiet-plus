using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>A full-screen page for the 320×240 dock display.</summary>
public interface IDockScreen
{
    string Name { get; }

    /// <summary>Draws the page onto a 320×240 canvas that has already been cleared to <see cref="Theme.Background"/>.</summary>
    void Render(SKCanvas canvas, ScreenContext ctx);
}
