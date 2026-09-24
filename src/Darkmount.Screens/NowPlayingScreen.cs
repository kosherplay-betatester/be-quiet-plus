using System.Globalization;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// The current media session: album art on the left (over a blurred, darkened copy of itself as the backdrop), title,
/// artist and app on the right, and a progress bar with elapsed/total time and a play/pause badge at the bottom.
/// The accent colour is taken from the artwork. Shows a "Nothing playing" card when <see cref="ScreenContext.Media"/> is null.
/// </summary>
public sealed class NowPlayingScreen : IDockScreen
{
    /// <summary>Where the album art is drawn (130 px square).</summary>
    internal static readonly SKRect ArtRect = new(14, 14, 144, 144);

    private const float ArtRadius = 10;
    private const float ColumnLeft = 158, ColumnRight = 308;
    private const float BarLeft = 64, BarRight = 306, BarY = 183;
    private static readonly SKColor DefaultAccent = Theme.Fps;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly object _gate = new();
    private byte[]? _artSource;
    private SKImage? _art, _backdrop;
    private SKColor _accent = DefaultAccent;

    public string Name => "Now playing";

    public void Render(SKCanvas canvas, ScreenContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Media is not { } media)
        {
            DrawIdle(canvas);
            return;
        }

        lock (_gate)
        {
            LoadArtwork(media.ArtworkPng);
            DrawBackdrop(canvas);
            DrawArtwork(canvas);
            DrawDetails(canvas, media);
            DrawTransport(canvas, media);
        }
    }

    // ---------------------------------------------------------------- artwork

    /// <summary>Decodes the artwork once per new byte array and prepares the blurred backdrop and accent colour.</summary>
    private void LoadArtwork(byte[]? png)
    {
        if (ReferenceEquals(png, _artSource)) return;
        _artSource = png;
        _art?.Dispose();
        _backdrop?.Dispose();
        _art = _backdrop = null;
        _accent = DefaultAccent;
        if (png is not { Length: > 0 }) return;

        try
        {
            // Decode once and reuse every frame. ToRasterImage may return the same instance: only dispose a distinct one.
            var encoded = SKImage.FromEncodedData(png);
            var raster = encoded?.ToRasterImage();
            if (encoded is not null && !ReferenceEquals(encoded, raster)) encoded.Dispose();
            _art = raster;
        }
        catch (Exception)
        {
            _art = null;
        }
        if (_art is null) return;

        _accent = VividColor(_art) ?? DefaultAccent;
        _backdrop = BuildBackdrop(_art);
    }

    private static SKImage? BuildBackdrop(SKImage art)
    {
        using var surface = SKSurface.Create(new SKImageInfo(DockRenderer.Width, DockRenderer.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return null;
        var c = surface.Canvas;
        c.Clear(Theme.Background);
        using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(22, 22, SKShaderTileMode.Clamp) })
            ScreenKit.DrawCover(c, art, new SKRect(-24, -24, DockRenderer.Width + 24, DockRenderer.Height + 24), blur);
        using var shade = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, DockRenderer.Height),
                [SKColors.Black.WithAlpha(0x96), SKColors.Black.WithAlpha(0xC4)], SKShaderTileMode.Clamp),
        };
        c.DrawRect(0, 0, DockRenderer.Width, DockRenderer.Height, shade);
        return surface.Snapshot();
    }

    /// <summary>The most vivid colour of the artwork, brightened so it reads well on the dark backdrop.</summary>
    internal static SKColor? VividColor(SKImage image)
    {
        const int n = 12;
        using var small = new SKBitmap(new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(small))
            c.DrawImage(image, new SKRect(0, 0, n, n), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);

        float bestScore = 0, bh = 0, bs = 0, bv = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                small.GetPixel(x, y).ToHsv(out float h, out float s, out float v);
                if (s < 30 || v < 35) continue;
                float score = s * v;
                if (score > bestScore) (bestScore, bh, bs, bv) = (score, h, s, v);
            }
        if (bestScore <= 0) return null;
        return SKColor.FromHsv(bh, Math.Clamp(bs, 55, 80), Math.Max(bv, 95));
    }

    // ---------------------------------------------------------------- layers

    private void DrawBackdrop(SKCanvas canvas)
    {
        if (_backdrop is not null)
        {
            canvas.DrawImage(_backdrop, 0, 0);
            return;
        }
        // No artwork: a soft glow of the accent colour from the top-left corner.
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(40, 30), 300,
                [_accent.WithAlpha(0x38), _accent.WithAlpha(0x00)], SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, DockRenderer.Width, DockRenderer.Height, glow);
    }

    private void DrawArtwork(SKCanvas canvas)
    {
        var r = ArtRect;
        using (var shadow = Theme.Fill(SKColors.Black.WithAlpha(0xA0)))
        {
            shadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6);
            canvas.DrawRoundRect(new SKRect(r.Left, r.Top + 4, r.Right, r.Bottom + 4), ArtRadius, ArtRadius, shadow);
        }

        canvas.Save();
        using (var rr = new SKRoundRect(r, ArtRadius))
            canvas.ClipRoundRect(rr, SKClipOperation.Intersect, true);
        if (_art is not null)
        {
            ScreenKit.DrawCover(canvas, _art, r);
        }
        else
        {
            using var bg = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
                    [ScreenKit.Blend(_accent, Theme.Panel, 0.55f), ScreenKit.Blend(_accent, Theme.Background, 0.85f)],
                    SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(r, bg);
            var note = new SKRect(r.MidX - 32, r.MidY - 32, r.MidX + 32, r.MidY + 32);
            ScreenKit.DrawMusicNote(canvas, note, ScreenKit.Lighten(_accent, 0.55f).WithAlpha(0xE0));
        }
        canvas.Restore();

        using var border = Theme.Stroke(SKColors.White.WithAlpha(0x26), 1);
        var b = r;
        b.Inflate(-0.5f, -0.5f);
        canvas.DrawRoundRect(b, ArtRadius, ArtRadius, border);
    }

    /// <summary>App, title (up to 2 lines), artist and album, vertically centred next to the art.</summary>
    private void DrawDetails(SKCanvas canvas, MediaInfo m)
    {
        float width = ColumnRight - ColumnLeft;
        using var appFont = Theme.Font(Theme.Bold, 11);
        using var titleFont = Theme.Font(Theme.Bold, 21);
        using var artistFont = Theme.Font(Theme.SemiBold, 15);
        using var albumFont = Theme.Font(Theme.Regular, 12.5f);

        var title = ScreenKit.Wrap(string.IsNullOrWhiteSpace(m.Title) ? "Unknown title" : m.Title, titleFont, width, 2);
        string artist = ScreenKit.Ellipsize(m.Artist?.Trim() ?? "", artistFont, width);
        string album = m.Album is { Length: > 0 } a && a != m.Title ? ScreenKit.Ellipsize(a.Trim(), albumFont, width) : "";

        // Line advances (baseline to baseline).
        const float appToTitle = 29, titleLine = 25, titleToArtist = 23, artistToAlbum = 19;
        float height = appToTitle + (title.Count - 1) * titleLine
                       + (artist.Length > 0 ? titleToArtist : 0) + (album.Length > 0 ? artistToAlbum : 0);
        // Cap-height aware centring: from the app line's cap top to the last line's baseline.
        float top = ArtRect.MidY - (height + 8) / 2 + 8;
        top = Math.Clamp(top, ArtRect.Top + 9, ArtRect.Top + 30);
        float y = top;

        // App line with a small equalizer when playing.
        float x = ColumnLeft;
        var appColor = ScreenKit.Lighten(_accent, 0.35f);
        if (m.Playing)
        {
            ScreenKit.DrawEqualizer(canvas, new SKRect(x, y - 9, x + 11, y), appColor);
            x += 17;
        }
        string app = (m.AppName is { Length: > 0 } n ? n : "Now playing").ToUpperInvariant();
        using (var appPaint = Theme.Fill(appColor))
            ScreenKit.DrawText(canvas, ScreenKit.Ellipsize(app, appFont, ColumnRight - x), x, y, appFont, appPaint);

        y += appToTitle;
        using (var titlePaint = Theme.Fill(Theme.Text))
        {
            for (int i = 0; i < title.Count; i++)
            {
                if (i > 0) y += titleLine;
                ScreenKit.DrawText(canvas, title[i], ColumnLeft, y, titleFont, titlePaint);
            }
        }
        if (artist.Length > 0)
        {
            y += titleToArtist;
            using var p = Theme.Fill(new SKColor(0xD5, 0xDA, 0xE1));
            ScreenKit.DrawText(canvas, artist, ColumnLeft, y, artistFont, p);
        }
        if (album.Length > 0 && y + artistToAlbum <= ArtRect.Bottom + 4)
        {
            y += artistToAlbum;
            using var p = Theme.Fill(Theme.TextSecondary.WithAlpha(0xD0));
            ScreenKit.DrawText(canvas, album, ColumnLeft, y, albumFont, p);
        }
    }

    /// <summary>Play/pause badge, progress bar and times.</summary>
    private void DrawTransport(SKCanvas canvas, MediaInfo m)
    {
        // Badge: accent disc with ▶ while playing, grey disc with ❚❚ while paused.
        var badge = new SKPoint(ArtRect.Left + 20, BarY + 9);
        using (var disc = Theme.Fill(m.Playing ? _accent : SKColors.White.WithAlpha(0x2E)))
            canvas.DrawCircle(badge, 20, disc);
        if (m.Playing) ScreenKit.DrawPlay(canvas, badge, 15, ScreenKit.Darken(_accent, 0.82f));
        else ScreenKit.DrawPause(canvas, badge, 15, Theme.Text);

        using var timeFont = Theme.Font(Theme.SemiBold, 14);
        float timeBaseline = BarY + 26;

        if (m.Duration <= TimeSpan.Zero)
        {
            // Live stream / unknown length: LIVE pill + elapsed time.
            var pill = new SKRect(BarLeft, BarY - 9, BarLeft + 50, BarY + 9);
            using (var pillPaint = Theme.Fill(m.Playing ? Theme.Alert : Theme.TextDim))
                canvas.DrawRoundRect(pill, 9, 9, pillPaint);
            using var liveFont = Theme.Font(Theme.Bold, 11.5f);
            using var white = Theme.Fill(SKColors.White);
            canvas.DrawCircle(pill.Left + 11, pill.MidY, 3, white);
            canvas.DrawText("LIVE", pill.Left + 18, pill.MidY + 4, SKTextAlign.Left, liveFont, white);
            if (m.Position > TimeSpan.Zero)
            {
                using var p = Theme.Fill(Theme.TextSecondary);
                canvas.DrawText(FormatTime(m.Position), pill.Right + 10, pill.MidY + 5, SKTextAlign.Left, timeFont, p);
            }
            if (!m.Playing) DrawPausedLabel(canvas, timeBaseline);
            return;
        }

        double t = Math.Clamp(m.Position.TotalSeconds / m.Duration.TotalSeconds, 0, 1);
        var track = new SKRect(BarLeft, BarY - 3, BarRight, BarY + 3);
        using (var trackPaint = Theme.Fill(SKColors.White.WithAlpha(0x2A)))
            canvas.DrawRoundRect(track, 3, 3, trackPaint);

        var fillColor = m.Playing ? _accent : Theme.TextSecondary;
        float fx = track.Left + (float)(track.Width * t);
        if (fx > track.Left + 1)
        {
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(new SKPoint(track.Left, 0), new SKPoint(Math.Max(fx, track.Left + 6), 0),
                    [ScreenKit.Darken(fillColor, 0.35f), fillColor], SKShaderTileMode.Clamp),
            };
            canvas.DrawRoundRect(new SKRect(track.Left, track.Top, Math.Max(fx, track.Left + 6), track.Bottom), 3, 3, fill);
        }
        using (var knobShadow = Theme.Fill(SKColors.Black.WithAlpha(0x70)))
        {
            knobShadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2);
            canvas.DrawCircle(fx, BarY + 1, 6.5f, knobShadow);
        }
        using (var knob = Theme.Fill(m.Playing ? SKColors.White : Theme.TextSecondary))
            canvas.DrawCircle(fx, BarY, 6.5f, knob);

        using (var elapsed = Theme.Fill(Theme.Text))
            canvas.DrawText(FormatTime(m.Position), BarLeft, timeBaseline, SKTextAlign.Left, timeFont, elapsed);
        using (var total = Theme.Fill(Theme.TextSecondary))
            canvas.DrawText(FormatTime(m.Duration), BarRight, timeBaseline, SKTextAlign.Right, timeFont, total);
        if (!m.Playing) DrawPausedLabel(canvas, timeBaseline);
    }

    private static void DrawPausedLabel(SKCanvas canvas, float baseline)
    {
        using var font = Theme.Font(Theme.Bold, 11.5f);
        using var paint = Theme.Fill(Theme.TextSecondary);
        canvas.DrawText("PAUSED", (BarLeft + BarRight) / 2, baseline - 1, SKTextAlign.Center, font, paint);
    }

    // ---------------------------------------------------------------- nothing playing

    private static void DrawIdle(SKCanvas canvas)
    {
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(160, 88), 170,
                [DefaultAccent.WithAlpha(0x30), DefaultAccent.WithAlpha(0x00)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, DockRenderer.Width, DockRenderer.Height, glow);
        }

        var card = new SKRect(160 - 46, 34, 160 + 46, 34 + 92);
        Theme.DrawPanel(canvas, card, 16);
        ScreenKit.DrawMusicNote(canvas, new SKRect(card.MidX - 28, card.MidY - 28, card.MidX + 28, card.MidY + 28),
            ScreenKit.Blend(DefaultAccent, Theme.TextDim, 0.35f));

        using var title = Theme.Font(Theme.Bold, 22);
        using var titlePaint = Theme.Fill(Theme.Text);
        canvas.DrawText("Nothing playing", 160, 166, SKTextAlign.Center, title, titlePaint);
        using var sub = Theme.Font(Theme.Regular, 13.5f);
        using var subPaint = Theme.Fill(Theme.TextDim);
        canvas.DrawText("Play music or a video in any app", 160, 190, SKTextAlign.Center, sub, subPaint);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>m:ss, or h:mm:ss from one hour.</summary>
    internal static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? string.Create(Inv, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(Inv, $"{t.Minutes}:{t.Seconds:00}");
    }
}
