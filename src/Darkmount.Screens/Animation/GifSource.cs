using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// Plays an animated image (GIF, animated WebP, …) one frame per call, looping. Frames are composited
/// correctly for partial-frame animations using the codec's required-frame information.
/// </summary>
public sealed class GifSource : IFrameSource
{
    private readonly SKCodec _codec;
    private readonly SKImageInfo _info;
    private readonly SKCodecFrameInfo[] _frames;
    private SKBitmap? _last;      // composited bitmap of frame _lastIndex
    private int _lastIndex = -1;
    private int _next;

    public GifSource(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Animation file not found.", path);
        _codec = SKCodec.Create(path, out var result)
            ?? throw new InvalidDataException($"Cannot decode '{Path.GetFileName(path)}' ({result}).");
        _info = new SKImageInfo(_codec.Info.Width, _codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _frames = _codec.FrameInfo;
        if (_frames.Length == 0) _frames = [default]; // still image: a single frame
    }

    public int FrameCount => _frames.Length;

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        int index = _next;
        _next = (_next + 1) % _frames.Length;

        var bitmap = Composite(index);
        using (var black = new SKPaint { Color = SKColors.Black })
            canvas.DrawRect(bounds, black);
        FrameDrawing.DrawCover(canvas, bitmap, bounds);
    }

    private SKBitmap Composite(int index)
    {
        if (index == _lastIndex && _last != null) return _last;

        int required = _frames.Length > 1 ? _frames[index].RequiredFrame : -1;
        var bitmap = new SKBitmap(_info);
        SKCodecOptions options;
        if (required >= 0)
        {
            // Start from the composited required frame (usually the previous one; otherwise rebuild it).
            var prior = required == _lastIndex && _last != null ? _last : Composite(required);
            prior.CopyTo(bitmap);
            options = new SKCodecOptions(index, required);
        }
        else
        {
            bitmap.Erase(SKColors.Transparent);
            options = new SKCodecOptions(index);
        }

        var result = _codec.GetPixels(_info, bitmap.GetPixels(), options);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidDataException($"Frame {index} failed to decode ({result}).");
        }
        bitmap.NotifyPixelsChanged();

        if (!ReferenceEquals(_last, bitmap)) _last?.Dispose();
        _last = bitmap;
        _lastIndex = index;
        return bitmap;
    }

    public void Dispose()
    {
        _last?.Dispose();
        _codec.Dispose();
    }
}

/// <summary>Shows every image in a folder (alphabetical), one per call, looping.</summary>
public sealed class FolderSource : IFrameSource
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"];
    private readonly string[] _files;
    private int _next;

    public FolderSource(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder not found: {folder}");
        _files = Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_files.Length == 0) throw new FileNotFoundException($"No images in {folder}");
    }

    public int Count => _files.Length;

    public void DrawNextFrame(SKCanvas canvas, SKRect bounds)
    {
        // Skip unreadable files, but give up after one full pass.
        for (int attempt = 0; attempt < _files.Length; attempt++)
        {
            string file = _files[_next];
            _next = (_next + 1) % _files.Length;
            using var bitmap = TryDecode(file);
            if (bitmap == null) continue;
            using (var black = new SKPaint { Color = SKColors.Black })
                canvas.DrawRect(bounds, black);
            FrameDrawing.DrawCover(canvas, bitmap, bounds);
            return;
        }
        canvas.Clear(Theme.Background);
        FrameDrawing.DrawCaption(canvas, bounds, "No readable images in folder");
    }

    private static SKBitmap? TryDecode(string file)
    {
        try
        {
            return SKBitmap.Decode(file); // first frame for animated files
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() { }
}
