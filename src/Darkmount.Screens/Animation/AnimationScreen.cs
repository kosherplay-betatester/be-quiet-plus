using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>Full-screen ambient animation; every <see cref="Render"/> advances the source by one frame.</summary>
public sealed class AnimationScreen : IDockScreen, IDisposable
{
    private readonly IFrameSource _source;

    public AnimationScreen(IFrameSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    public string Name => "Animation";

    public void Render(SKCanvas canvas, ScreenContext ctx)
    {
        var bounds = new SKRect(0, 0, DockRenderer.Width, DockRenderer.Height);
        canvas.Save();
        canvas.ClipRect(bounds);
        try
        {
            _source.DrawNextFrame(canvas, bounds);
        }
        catch (Exception ex)
        {
            canvas.Clear(Theme.Background);
            FrameDrawing.DrawCaption(canvas, bounds, "Animation error: " + ex.Message);
        }
        canvas.Restore();
    }

    public void Dispose() => _source.Dispose();
}
