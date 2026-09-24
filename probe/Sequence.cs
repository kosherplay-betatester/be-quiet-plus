using System.Buffers.Binary;
using System.Diagnostics;
using SkiaSharp;

namespace DockProbe;

/// <summary>
/// Decides how the dock picks up image changes:
///   A) full RED image uploaded BEFORE the screensaver is enabled
///   B) full GREEN image re-uploaded while the screensaver is showing
///   C) partial write: BLUE band in the middle (final byte untouched)
///   D) partial write: YELLOW bar at the bottom (includes the final byte)
/// </summary>
public static class Sequence
{
    const int W = 320, H = 240, Stride = W * 2;
    const byte Slot = 0;

    public static void Run(QLinkClient q, int holdSeconds)
    {
        var original = ConfigGuard.Original(q);
        var clock = Stopwatch.StartNew();
        void Log(string s) => Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] {s}");
        try
        {
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 40);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var corner = new SKPaint { Color = SKColors.White };

            byte[] Render(SKColor bg, string text)
            {
                cv.Clear(bg);
                cv.DrawRect(0, 0, 40, 40, corner); // white square marks the top-left corner
                cv.DrawText(text, W / 2f, 70, SKTextAlign.Center, font, ink);
                cv.Flush();
                return Formats.ToRgb565(bmp);
            }

            Log("A: uploading full RED image (screensaver still off)");
            Formats.Upload(q, Slot, Formats.Rgb565, Render(SKColors.DarkRed, "A  RED"));
            Log("A: upload complete, now enabling the screensaver (1 s)");
            q.SendReliable(QLinkClient.FeatMediaDock, 3, ConfigGuard.Quick(original));
            Hold(q, holdSeconds);

            Log("B: uploading full GREEN image while the screensaver shows");
            var green = Render(SKColors.DarkGreen, "B  GREEN");
            Formats.Upload(q, Slot, Formats.Rgb565, green);
            Log("B: upload complete");
            Hold(q, holdSeconds);

            Log("C: partial write, BLUE band in the middle (final byte untouched)");
            cv.DrawRect(0, 100, W, 60, new SKPaint { Color = SKColors.Blue });
            cv.DrawText("C  BLUE", W / 2f, 145, SKTextAlign.Center, font, ink);
            cv.Flush();
            var c = Formats.ToRgb565(bmp);
            WriteRange(q, c, 100 * Stride, 160 * Stride);
            Log("C: done");
            Hold(q, holdSeconds);

            Log("D: partial write, YELLOW bar at the bottom (includes the final byte)");
            cv.DrawRect(0, 190, W, 50, new SKPaint { Color = SKColors.Goldenrod });
            cv.DrawText("D  YELLOW", W / 2f, 232, SKTextAlign.Center, font, ink);
            cv.Flush();
            WriteRange(q, Formats.ToRgb565(bmp), 190 * Stride, H * Stride);
            Log("D: done");
            Hold(q, holdSeconds);

            Log($"Stalls: {string.Join(", ", q.Stalls.Select(x => $"{x.durationMs} ms @ {x.atMs / 1000.0:F1}s"))}");
        }
        finally
        {
            q.WaitReady();
            q.SendReliable(QLinkClient.FeatMediaDock, 3, original);
            Log("Restored original dock config.");
        }
    }

    /// <summary>
    /// Looks for a cheap redraw trigger after partial writes:
    ///   E1 BLUE band written, then only the 9-byte header re-sent
    ///   E2 header, then only the changed rows (YELLOW bar, includes the final byte)
    /// </summary>
    public static void Trigger(QLinkClient q, int holdSeconds)
    {
        var original = ConfigGuard.Original(q);
        var clock = Stopwatch.StartNew();
        void Log(string s) => Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] {s}");
        try
        {
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 40);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };

            cv.Clear(SKColors.DarkGreen);
            cv.DrawText("GREEN", W / 2f, 70, SKTextAlign.Center, font, ink);
            cv.Flush();
            Log("Uploading full GREEN image, then enabling the screensaver");
            Formats.Upload(q, Slot, Formats.Rgb565, Formats.ToRgb565(bmp));
            q.SendReliable(QLinkClient.FeatMediaDock, 3, ConfigGuard.Quick(original));
            Hold(q, holdSeconds);

            cv.DrawRect(0, 100, W, 60, new SKPaint { Color = SKColors.Blue });
            cv.DrawText("E1 BLUE", W / 2f, 145, SKTextAlign.Center, font, ink);
            cv.Flush();
            var e1 = Formats.ToRgb565(bmp);
            var sw = Stopwatch.StartNew();
            WriteRange(q, e1, 100 * Stride, 160 * Stride);
            SendHeader(q, e1.Length);
            Log($"E1: blue band + header only, {sw.ElapsedMilliseconds} ms");
            Hold(q, holdSeconds);

            cv.DrawRect(0, 190, W, 50, new SKPaint { Color = SKColors.Goldenrod });
            cv.DrawText("E2 YELLOW", W / 2f, 232, SKTextAlign.Center, font, ink);
            cv.Flush();
            var e2 = Formats.ToRgb565(bmp);
            sw.Restart();
            SendHeader(q, e2.Length);
            WriteRange(q, e2, 190 * Stride, H * Stride);
            Log($"E2: header + yellow rows only, {sw.ElapsedMilliseconds} ms");
            Hold(q, holdSeconds);

            Log($"Stalls: {string.Join(", ", q.Stalls.Select(x => $"{x.durationMs} ms @ {x.atMs / 1000.0:F1}s"))}");
        }
        finally
        {
            q.WaitReady();
            q.SendReliable(QLinkClient.FeatMediaDock, 3, original);
            Log("Restored original dock config.");
        }
    }

    /// <summary>Times header-only and full uploads for several announced image sizes (no config change).</summary>
    public static void HeaderCost(QLinkClient q)
    {
        foreach (var (w, h) in new[] { (320, 240), (320, 120), (320, 60), (160, 120), (70, 70) })
        {
            var px = new byte[w * h * 2];
            var hdr = new byte[5 + 9];
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(5), (uint)(px.Length + 9));
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(9), (ushort)w);
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(11), (ushort)h);
            hdr[13] = Formats.Rgb565;
            try
            {
                var sw = Stopwatch.StartNew();
                q.Send(QLinkClient.FeatMediaDock, 7, hdr);
                long header = sw.ElapsedMilliseconds;
                for (int c = 0; c < px.Length; c += 4000)
                {
                    var buf = new byte[5 + Math.Min(4000, px.Length - c)];
                    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
                    q.Send(QLinkClient.FeatMediaDock, 7, buf);
                }
                Console.WriteLine($"  {w}x{h}: header {header} ms, full upload {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e) { Console.WriteLine($"  {w}x{h}: {e.Message}"); q.WaitReady(); }
        }
    }

    static void SendHeader(QLinkClient q, int pixelBytes)
    {
        var header = new byte[5 + 9];
        header[0] = Slot;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), (uint)(pixelBytes + 9));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(9), W);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(11), H);
        header[13] = Formats.Rgb565;
        q.SendReliable(QLinkClient.FeatMediaDock, 7, header);
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
