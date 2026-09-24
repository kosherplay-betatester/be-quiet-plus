using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>A source of animation frames. Each call draws the next frame, filling <paramref name="bounds"/>.</summary>
public interface IFrameSource : IDisposable
{
    void DrawNextFrame(SKCanvas canvas, SKRect bounds);
}

public enum AnimationKind { Plasma, Matrix, Starfield, Gif, Video, Folder }
