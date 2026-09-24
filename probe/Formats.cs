using System.Buffers.Binary;
using System.Diagnostics;
using SkiaSharp;

namespace DockProbe;

/// <summary>Tests which compressed image formats the media dock decodes, and how fast.</summary>
public static class Formats
{
    const int W = 320, H = 240;
    const byte Screensaver = 0;
    public const byte Rgb565 = 1, Png = 2, Jpeg = 3, WebP = 4;

    /// <summary>Uploads an encoded image blob (header + payload) to an image slot.</summary>
    public static void Upload(QLinkClient q, byte slot, byte format, byte[] payload, int chunk = 4000)
    {
        var header = new byte[5 + 9];
        header[0] = slot;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), (uint)(payload.Length + 9));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(9), W);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(11), H);
        header[13] = format;
        q.SendReliable(QLinkClient.FeatMediaDock, 7, header);

        var buf = new byte[5 + chunk];
        buf[0] = slot;
        for (int c = 0; c < payload.Length; c += chunk)
        {
            int len = Math.Min(chunk, payload.Length - c);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
            payload.AsSpan(c, len).CopyTo(buf.AsSpan(5));
            q.SendReliable(QLinkClient.FeatMediaDock, 7, buf.AsSpan(0, 5 + len));
        }
    }

    public static byte[] Encode(SKBitmap bmp, byte format, int quality = 85) => format switch
    {
        Jpeg => bmp.Encode(SKEncodedImageFormat.Jpeg, quality).ToArray(),
        Png => bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray(),
        WebP => bmp.Encode(SKEncodedImageFormat.Webp, quality).ToArray(),
        _ => ToRgb565(bmp),
    };

    public static byte[] ToRgb565(SKBitmap bmp)
    {
        var px = new byte[W * H * 2];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var c = bmp.GetPixel(x, y);
            ushort v = (ushort)((c.Red >> 3) << 11 | (c.Green >> 2) << 5 | (c.Blue >> 3));
            BinaryPrimitives.WriteUInt16LittleEndian(px.AsSpan((y * W + x) * 2), v);
        }
        return px;
    }

    /// <summary>Solid colour card with a big label, so the user can tell which format rendered.</summary>
    public static SKBitmap Card(string label, SKColor bg, string sub = "")
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var cv = new SKCanvas(bmp);
        using var shader = SKShader.CreateLinearGradient(new(0, 0), new(W, H), [bg, SKColors.Black], SKShaderTileMode.Clamp);
        cv.DrawRect(0, 0, W, H, new SKPaint { Shader = shader });
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 64);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        cv.DrawText(label, W / 2f, H / 2f + 20, SKTextAlign.Center, font, white);
        using var small = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 22);
        cv.DrawText(sub, W / 2f, H - 24, SKTextAlign.Center, small, white);
        return bmp;
    }

    /// <summary>One animation frame: a ball orbiting over a plasma-ish background.</summary>
    public static SKBitmap AnimFrame(int i)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var cv = new SKCanvas(bmp);
        float t = i / 10f;
        using var shader = SKShader.CreateSweepGradient(new(W / 2f, H / 2f),
            [SKColor.FromHsv(i * 12 % 360, 80, 60), SKColor.FromHsv((i * 12 + 120) % 360, 80, 60), SKColor.FromHsv((i * 12 + 240) % 360, 80, 60), SKColor.FromHsv(i * 12 % 360, 80, 60)]);
        cv.DrawRect(0, 0, W, H, new SKPaint { Shader = shader });
        using var ball = new SKPaint { Color = SKColors.White, IsAntialias = true };
        cv.DrawCircle(W / 2f + MathF.Cos(t) * 110, H / 2f + MathF.Sin(t) * 80, 24, ball);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 28);
        cv.DrawText($"frame {i}", 12, 34, SKTextAlign.Left, font, ball);
        return bmp;
    }

    public static void Run(QLinkClient q, int animFrames)
    {
        var original = ConfigGuard.Original(q);
        var quick = ConfigGuard.Quick(original);
        try
        {
            q.SendReliable(QLinkClient.FeatMediaDock, 3, quick);
            var tests = new (string name, byte fmt, SKColor color)[]
            {
                ("JPEG", Jpeg, SKColors.Red), ("PNG", Png, SKColors.Green), ("WEBP", WebP, SKColors.Blue),
            };
            foreach (var (name, fmt, color) in tests)
            {
                using var card = Card(name, color, $"format {fmt} test");
                var data = Encode(card, fmt);
                var sw = Stopwatch.StartNew();
                try
                {
                    Upload(q, Screensaver, fmt, data);
                    Console.WriteLine($"{name,-5} ({color.ToString()[..9]}): sent {data.Length,6} bytes in {sw.ElapsedMilliseconds,4} ms -> look at the dock now");
                }
                catch (Exception e) { Console.WriteLine($"{name,-5}: rejected after {sw.ElapsedMilliseconds} ms: {e.Message}"); }
                for (int s = 0; s < 6; s++) { q.KeepAlive(); q.Pump(1000); }
            }

            if (animFrames > 0)
            {
                Console.WriteLine($"JPEG animation test, {animFrames} frames as fast as possible...");
                var total = Stopwatch.StartNew();
                long bytes = 0;
                for (int i = 0; i < animFrames; i++)
                {
                    using var f = AnimFrame(i);
                    var jpg = Encode(f, Jpeg, 80);
                    bytes += jpg.Length;
                    Upload(q, Screensaver, Jpeg, jpg);
                }
                total.Stop();
                Console.WriteLine($"  {animFrames} frames in {total.ElapsedMilliseconds} ms = {animFrames * 1000.0 / total.ElapsedMilliseconds:F1} fps, avg {bytes / animFrames / 1024.0:F1} KB/frame");
                for (int s = 0; s < 3; s++) { q.KeepAlive(); q.Pump(1000); }
            }
        }
        finally
        {
            q.Pump(300);
            q.SendReliable(QLinkClient.FeatMediaDock, 3, original);
            Console.WriteLine("Restored original dock config.");
        }
    }
}
