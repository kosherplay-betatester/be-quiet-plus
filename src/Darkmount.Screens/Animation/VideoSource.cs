using SkiaSharp;
using Windows.Media.Editing;
using Windows.Storage;

namespace Darkmount.Screens;

/// <summary>
/// Shows a video as a slideshow: one frame every <see cref="Interval"/> of video time, looping.
/// Frames come from WinRT <see cref="MediaComposition.GetThumbnailAsync"/> (Media Foundation decoders) and the
/// next one is fetched in the background so <see cref="DrawNextFrame"/> is fast.
/// </summary>
public sealed class VideoSource : IFrameSource
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(8);

    private readonly MediaComposition _composition;
    private readonly TimeSpan _duration;
    private readonly int _thumbW, _thumbH;
    private readonly object _gate = new();
    private TimeSpan _position;
    private Task<SKBitmap?>? _pending;
    private SKBitmap? _current;
    private bool _disposed;

    public VideoSource(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Video file not found.", path);
        var file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
        var clip = MediaClip.CreateFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        _composition = new MediaComposition();
        _composition.Clips.Add(clip);
        _duration = clip.OriginalDuration;
        if (_duration <= TimeSpan.Zero) throw new InvalidDataException("Video has no duration.");

        // Request a thumbnail with the video's aspect that covers the dock (320×240).
        var props = clip.GetVideoEncodingProperties();
        double vw = props.Width > 0 ? props.Width : 320, vh = props.Height > 0 ? props.Height : 240;
        double scale = Math.Max(DockRenderer.Width / vw, DockRenderer.Height / vh);
        _thumbW = Math.Max(1, (int)Math.Ceiling(vw * scale));
        _thumbH = Math.Max(1, (int)Math.Ceiling(vh * scale));

        // Fail fast (so the caller can fall back) if the first frame cannot be grabbed.
        _current = Grab(TimeSpan.Zero) ?? throw new InvalidDataException("Cannot decode video frames.");
        _position = Interval;
    }

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        if (_pending == null)
        {
            // First call: show the frame grabbed by the constructor and start prefetching.
            _pending = StartFetch();
        }
        else if (_pending.Wait(WaitLimit))
        {
            var frame = _pending.IsCompletedSuccessfully ? _pending.Result : null;
            if (frame != null)
            {
                _current?.Dispose();
                _current = frame;
            }
            _pending = StartFetch();
        }
        // else: decoder is slow; keep showing the current frame and wait for the same fetch next time.

        if (_current != null) Draw(canvas, bounds, _current);
        else
        {
            canvas.Clear(SKColors.Black);
            FrameDrawing.DrawCaption(canvas, bounds, "Video frame unavailable");
        }
    }

    private static void Draw(SKCanvas canvas, SKRect bounds, SKBitmap bitmap)
    {
        using (var black = new SKPaint { Color = SKColors.Black })
            canvas.DrawRect(bounds, black);
        FrameDrawing.DrawCover(canvas, bitmap, bounds);
    }

    private Task<SKBitmap?> StartFetch()
    {
        var at = _position;
        _position += Interval;
        if (_position >= _duration) _position = TimeSpan.Zero;
        return Task.Run(() => Grab(at));
    }

    private SKBitmap? Grab(TimeSpan at)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            try
            {
                using var stream = _composition
                    .GetThumbnailAsync(at, _thumbW, _thumbH, VideoFramePrecision.NearestFrame)
                    .AsTask().GetAwaiter().GetResult();
                using var net = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                net.CopyTo(ms);
                return SKBitmap.Decode(ms.ToArray());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        var pending = _pending;
        try { pending?.Wait(WaitLimit); } catch (AggregateException) { }
        lock (_gate) _disposed = true;
        if (pending is { IsCompletedSuccessfully: true }) pending.Result?.Dispose();
        _current?.Dispose();
        _current = null;
    }
}
