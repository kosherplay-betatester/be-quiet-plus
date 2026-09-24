using System.Buffers.Binary;
using System.Diagnostics;
using SkiaSharp;

namespace DockProbe;

/// <summary>
/// Answers: does the dock redraw on partial writes, or only when the final byte arrives?
/// How long is the device busy after a completed image? Which way is the panel rotated?
/// </summary>
public static class Commit
{
    const int W = 320, H = 240, Stride = W * 2;
    const byte Slot = 0;

    public static void Run(QLinkClient q)
    {
        var original = ConfigGuard.Original(q);
        var quick = ConfigGuard.Quick(original);
        try
        {
            q.SendReliable(QLinkClient.FeatMediaDock, 3, quick);

            // STEP 1: complete image with orientation markers.
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            cv.Clear(new SKColor(40, 40, 40));
            using var red = new SKPaint { Color = SKColors.Red };
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var big = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 44);
            using var small = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            cv.DrawRect(0, 0, 50, 50, red);
            cv.DrawText("RED = TOP-LEFT", 60, 32, SKTextAlign.Left, small, white);
            cv.DrawText("STEP 1", W / 2f, 80, SKTextAlign.Center, big, white);
            cv.Flush();
            var frame1 = Formats.ToRgb565(bmp);

            Console.WriteLine("STEP 1: full image (grey, red square, 'STEP 1')");
            Formats.Upload(q, Slot, Formats.Rgb565, frame1);
            Console.WriteLine($"  device busy for {q.WaitReady()} ms after completing the image");
            Hold(q, 10);

            // STEP 2: partial write in the middle only (does NOT touch the last byte).
            cv.DrawRect(0, 100, W, 60, white);
            cv.DrawText("STEP 2", W / 2f, 146, SKTextAlign.Center, big, black);
            cv.Flush();
            var frame2 = Formats.ToRgb565(bmp);
            Console.WriteLine("STEP 2: partial write, white band with 'STEP 2' in the middle (last byte untouched)");
            WriteRange(q, frame2, 100 * Stride, 160 * Stride);
            Console.WriteLine($"  device busy for {q.WaitReady()} ms");
            Hold(q, 10);

            // STEP 3: write the bottom rows, including the final byte.
            using var blue = new SKPaint { Color = SKColors.Blue };
            cv.DrawRect(0, 200, W, 40, blue);
            cv.DrawText("STEP 3", W / 2f, 232, SKTextAlign.Center, small, white);
            cv.Flush();
            var frame3 = Formats.ToRgb565(bmp);
            Console.WriteLine("STEP 3: blue bar with 'STEP 3' at the bottom (includes the final byte)");
            WriteRange(q, frame3, 200 * Stride, H * Stride);
            Console.WriteLine($"  device busy for {q.WaitReady()} ms after touching the final byte");
            Hold(q, 10);
            Console.WriteLine($"Stalls: {string.Join(", ", q.Stalls.Select(x => $"{x.durationMs} ms @ {x.atMs}"))}");
        }
        finally
        {
            q.WaitReady();
            q.SendReliable(QLinkClient.FeatMediaDock, 3, original);
            Console.WriteLine("Restored original dock config.");
        }
    }

    static void WriteRange(QLinkClient q, byte[] px, int from, int to, int chunk = 4000)
    {
        var buf = new byte[5 + chunk];
        buf[0] = Slot;
        for (int c = from; c < to; c += chunk)
        {
            int len = Math.Min(chunk, to - c);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
            px.AsSpan(c, len).CopyTo(buf.AsSpan(5));
            q.SendReliable(QLinkClient.FeatMediaDock, 7, buf.AsSpan(0, 5 + len));
        }
    }

    static void Hold(QLinkClient q, int seconds)
    {
        for (int i = 0; i < seconds; i++) { q.KeepAlive(); q.Pump(1000); }
    }
}
