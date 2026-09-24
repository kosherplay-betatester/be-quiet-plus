using Darkmount.Screens;
using SkiaSharp;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Darkmount.App.Sources;

/// <summary>
/// Reads the current media session (Spotify, browsers, Media Player, ...) through Windows'
/// GlobalSystemMediaTransportControlsSessionManager. Call <see cref="Poll"/> from a background thread: every WinRT call
/// is bounded by a 2 s timeout, failures return null, and artwork is fetched only when the track changes.
/// Thread-safe (polls are serialised).
/// </summary>
public sealed class MediaSessionReader : IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ManagerRetry = TimeSpan.FromSeconds(30);
    private const int MaxArtworkBytes = 16 * 1024 * 1024;
    private const int FailuresBeforeReconnect = 3;

    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTime _managerFailedAt = DateTime.MinValue;
    private int _failures;
    private bool _disposed;

    // Artwork cache for the current track.
    private string? _artKey;
    private byte[]? _art;
    private int _artAttempts;

    /// <summary>
    /// The session Windows reports as current (a playing session wins over a paused current one), or null when nothing
    /// is available. Blocks for at most a few seconds; never throws.
    /// </summary>
    public MediaInfo? Poll()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            try
            {
                var info = PollCore();
                _failures = 0;
                return info;
            }
            catch (Exception)
            {
                // Timeouts or a disconnected WinRT server: after repeated failures request a fresh manager.
                if (++_failures >= FailuresBeforeReconnect)
                {
                    _manager = null;
                    _failures = 0;
                    _managerFailedAt = DateTime.UtcNow - ManagerRetry + TimeSpan.FromSeconds(5);
                }
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _manager = null;
            _art = null;
        }
    }

    private MediaInfo? PollCore()
    {
        var manager = Manager();
        if (manager is null) return null;
        var session = PickSession(manager);
        if (session is null) return null;

        var props = Wait(session.TryGetMediaPropertiesAsync());
        if (props is null) return null;
        string title = props.Title?.Trim() ?? "";
        string artist = (string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist)?.Trim() ?? "";
        string? album = string.IsNullOrWhiteSpace(props.AlbumTitle) ? null : props.AlbumTitle.Trim();
        if (title.Length == 0 && artist.Length == 0) return null;

        var playback = session.GetPlaybackInfo();
        bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        double rate = playback?.PlaybackRate ?? 1;

        var timeline = session.GetTimelineProperties();
        TimeSpan duration = TimeSpan.Zero, position = TimeSpan.Zero;
        if (timeline is not null)
        {
            duration = timeline.EndTime - timeline.StartTime;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            position = CurrentPosition(timeline.Position, timeline.StartTime, timeline.EndTime, timeline.LastUpdatedTime,
                DateTimeOffset.Now, playing, rate);
        }

        string? app = FriendlyAppName(session.SourceAppUserModelId);
        var art = Artwork($"{session.SourceAppUserModelId}\n{title}\n{artist}\n{album}", props.Thumbnail);
        return new MediaInfo(title.Length > 0 ? title : "Unknown title", artist, album, app, position, duration, playing, art);
    }

    private GlobalSystemMediaTransportControlsSessionManager? Manager()
    {
        if (_manager is not null) return _manager;
        if (DateTime.UtcNow - _managerFailedAt < ManagerRetry) return null;
        try
        {
            _manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
        }
        catch (Exception)
        {
            _manager = null;
        }
        if (_manager is null) _managerFailedAt = DateTime.UtcNow;
        return _manager;
    }

    /// <summary>The current session, unless it is not playing while another session is.</summary>
    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        if (current is not null && IsPlaying(current)) return current;
        try
        {
            foreach (var s in manager.GetSessions())
                if (IsPlaying(s)) return s;
        }
        catch (Exception)
        {
            // Fall back to the current session.
        }
        return current;
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- artwork

    /// <summary>
    /// Artwork for the track identified by <paramref name="key"/>. Fetched twice per new track (apps often publish the
    /// new title before the new thumbnail) and retried a few times while missing; otherwise served from the cache.
    /// </summary>
    private byte[]? Artwork(string key, IRandomAccessStreamReference? thumbnail)
    {
        if (key != _artKey)
        {
            _artKey = key;
            _art = null;
            _artAttempts = 0;
        }
        bool fetch = _artAttempts < 2 || (_art is null && _artAttempts < 5);
        if (!fetch) return _art;
        _artAttempts++;
        if (thumbnail is null) return _art;

        byte[]? png = null;
        try
        {
            var raw = ReadAll(thumbnail);
            if (raw is not null) png = EncodeArtwork(raw);
        }
        catch (Exception)
        {
            png = null;
        }
        // Keep the old array when nothing changed so the screen does not decode it again.
        if (png is not null && (_art is null || !png.AsSpan().SequenceEqual(_art))) _art = png;
        return _art;
    }

    private static byte[]? ReadAll(IRandomAccessStreamReference reference)
    {
        using var stream = Wait(reference.OpenReadAsync());
        if (stream is null || stream.Size == 0 || stream.Size > MaxArtworkBytes) return null;
        using var reader = new DataReader(stream);
        uint loaded = Wait(reader.LoadAsync((uint)stream.Size));
        if (loaded == 0) return null;
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Decodes any image format and re-encodes it as PNG, downscaled to at most <paramref name="maxSize"/> px.</summary>
    internal static byte[]? EncodeArtwork(byte[] encoded, int maxSize = 256)
    {
        if (encoded is not { Length: > 0 }) return null;
        try
        {
            using var src = SKBitmap.Decode(encoded);
            if (src is null || src.Width <= 0 || src.Height <= 0) return null;
            float scale = Math.Min(1f, (float)maxSize / Math.Max(src.Width, src.Height));
            SKBitmap? resized = null;
            try
            {
                var bitmap = src;
                if (scale < 1)
                {
                    int w = Math.Max(1, (int)Math.Round(src.Width * scale)), h = Math.Max(1, (int)Math.Round(src.Height * scale));
                    resized = src.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    if (resized is null) return null;
                    bitmap = resized;
                }
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- timeline

    /// <summary>
    /// Position relative to the start, extrapolated from the app's last timeline update while playing (many apps
    /// update it only every few seconds or on seek). Clamped to [0, duration] when the duration is known.
    /// </summary>
    internal static TimeSpan CurrentPosition(TimeSpan position, TimeSpan start, TimeSpan end, DateTimeOffset lastUpdated,
        DateTimeOffset now, bool playing, double rate)
    {
        var pos = position - start;
        if (playing)
        {
            var elapsed = now - lastUpdated;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(12))
            {
                double r = rate is > 0 and < 16 ? rate : 1;
                pos += TimeSpan.FromTicks((long)(elapsed.Ticks * r));
            }
        }
        var duration = end - start;
        if (duration > TimeSpan.Zero && pos > duration) pos = duration;
        return pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }

    // ---------------------------------------------------------------- app names

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spotify"] = "Spotify",
        ["SpotifyMusic"] = "Spotify",
        ["MSEdge"] = "Edge",
        ["MicrosoftEdge"] = "Edge",
        ["Chrome"] = "Chrome",
        ["Firefox"] = "Firefox",
        ["308046B0AF4A39CB"] = "Firefox",
        ["6F193CCC56814779"] = "Firefox",
        ["Brave"] = "Brave",
        ["Opera"] = "Opera",
        ["vlc"] = "VLC",
        ["ZuneMusic"] = "Media Player",
        ["ZuneVideo"] = "Movies & TV",
        ["wmplayer"] = "Windows Media Player",
        ["AppleMusicWin"] = "Apple Music",
        ["iTunes"] = "iTunes",
        ["AmazonMusic"] = "Amazon Music",
        ["TIDAL"] = "TIDAL",
        ["Deezer"] = "Deezer",
        ["MusicBee"] = "MusicBee",
        ["AIMP"] = "AIMP",
        ["Discord"] = "Discord",
    };

    /// <summary>
    /// A short display name from a SourceAppUserModelId: "Spotify.exe" → "Spotify",
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" → "Spotify", "MSEdge" → "Edge", "308046B0AF4A39CB" → "Firefox".
    /// </summary>
    internal static string? FriendlyAppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        string name = aumid.Trim();

        int bang = name.IndexOf('!');
        if (bang >= 0)
        {
            string app = name[(bang + 1)..], package = name[..bang];
            int underscore = package.IndexOf('_');
            if (underscore > 0) package = package[..underscore];
            name = app.Length == 0 || app.Equals("App", StringComparison.OrdinalIgnoreCase) ? package : app;
        }

        name = Path.GetFileName(name.Replace('/', '\\'));
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];

        if (KnownApps.TryGetValue(name, out var friendly)) return friendly;
        return name.Length > 0 ? name : null;
    }

    // ---------------------------------------------------------------- WinRT helpers

    private static T Wait<T>(IAsyncOperation<T> operation) =>
        operation.AsTask().WaitAsync(CallTimeout).GetAwaiter().GetResult();
}
