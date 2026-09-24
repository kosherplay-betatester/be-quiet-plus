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

    /// <summary>Full uploads with long timeouts, printing the latency of the header and the slowest chunks.</summary>
    public static void Latency(QLinkClient q, int frames)
    {
        var px = new byte[W * H * 2];
        for (int f = 0; f < frames; f++)
        {
            Array.Fill(px, (byte)(f * 40));
            var lat = new List<(int offset, long ms)>();
            var hdr = new byte[5 + 9];
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(5), (uint)(px.Length + 9));
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(9), W);
            BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(11), H);
            hdr[13] = Formats.Rgb565;
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            Timed(q, hdr, -1, lat);
            for (int c = 0; c < px.Length; c += 4000)
            {
                var buf = new byte[5 + Math.Min(4000, px.Length - c)];
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
                px.AsSpan(c, buf.Length - 5).CopyTo(buf.AsSpan(5));
                Timed(q, buf, c, lat);
            }
            var worst = lat.Skip(1).OrderByDescending(x => x.ms).Take(3);
            Console.WriteLine($"frame {f + 1}: total {total.ElapsedMilliseconds} ms, header {lat[0].ms} ms, first chunk {lat[1].ms} ms, " +
                              $"slowest chunks {string.Join(", ", worst.Select(x => $"{x.ms} ms @ {x.offset}"))}");
            q.KeepAlive();
        }
    }

    /// <summary>
    /// Does a continuous stream of full uploads keep the dock on its menu? Uploads numbered solid-colour frames
    /// every <paramref name="intervalMs"/> with the screensaver set to <paramref name="idleSeconds"/>.
    /// </summary>
    public static void Cycle(QLinkClient q, int frames, int intervalMs, int idleSeconds)
    {
        var original = ConfigGuard.Original(q);
        var clock = Stopwatch.StartNew();
        void Log(string s) => Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] {s}");
        SKColor[] colours = [SKColors.DarkRed, SKColors.DarkGreen, SKColors.DarkBlue, SKColors.DarkGoldenrod, SKColors.Purple, SKColors.Teal];
        string[] names = ["RED", "GREEN", "BLUE", "YELLOW", "PURPLE", "TEAL"];
        try
        {
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 64);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (int f = 0; f < frames; f++)
            {
                var sw = Stopwatch.StartNew();
                cv.Clear(colours[f % colours.Length]);
                cv.DrawText($"{f + 1} {names[f % names.Length]}", W / 2f, H / 2f + 22, SKTextAlign.Center, font, ink);
                cv.Flush();
                Formats.Upload(q, Slot, Formats.Rgb565, Formats.ToRgb565(bmp));
                if (f == 0) q.SendReliable(QLinkClient.FeatMediaDock, 3, ConfigGuard.Quick(original) is var c ? SetIdle(c, idleSeconds) : c);
                Log($"frame {f + 1} ({names[f % names.Length]}) uploaded in {sw.ElapsedMilliseconds} ms");
                while (sw.ElapsedMilliseconds < intervalMs) { q.KeepAlive(); q.Pump(Math.Min(500, (int)(intervalMs - sw.ElapsedMilliseconds))); }
            }
        }
        finally
        {
            q.WaitReady();
            q.SendReliable(QLinkClient.FeatMediaDock, 3, original);
            Log("Restored original dock config.");
        }
    }

    /// <summary>
    /// The web app's method: header, then 49-byte single-packet chunks, each waiting for its reply (no repeats).
    /// Reports slow replies and timeouts, then shows the frame via the screensaver.
    /// </summary>
    public static void WebSafe(QLinkClient q, int frames, int chunk)
    {
        var original = ConfigGuard.Original(q);
        var clock = Stopwatch.StartNew();
        void Log(string s) => Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] {s}");
        SKColor[] colours = [SKColors.DarkRed, SKColors.DarkGreen, SKColors.DarkBlue, SKColors.DarkGoldenrod];
        string[] names = ["RED", "GREEN", "BLUE", "YELLOW"];
        try
        {
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 64);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (int f = 0; f < frames; f++)
            {
                cv.Clear(colours[f % colours.Length]);
                cv.DrawText($"{f + 1} {names[f % names.Length]}", W / 2f, H / 2f + 22, SKTextAlign.Center, font, ink);
                cv.Flush();
                var px = Formats.ToRgb565(bmp);
                var sw = Stopwatch.StartNew();
                int slow = 0, requests = 0;
                long worst = 0;
                var hdr = new byte[5 + 9];
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(5), (uint)(px.Length + 9));
                BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(9), W);
                BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(11), H);
                hdr[13] = Formats.Rgb565;
                void Timed(byte[] payload, int offset)
                {
                    var t = Stopwatch.StartNew();
                    try { q.Send(QLinkClient.FeatMediaDock, 7, payload, timeoutMs: 8000); }
                    catch (TimeoutException) { Log($"  TIMEOUT at offset {offset} after {requests} requests"); throw; }
                    requests++;
                    worst = Math.Max(worst, t.ElapsedMilliseconds);
                    if (t.ElapsedMilliseconds > 300) { slow++; Log($"  slow reply at offset {offset}: {t.ElapsedMilliseconds} ms"); }
                }
                Timed(hdr, -1);
                for (int c = 0; c < px.Length; c += chunk)
                {
                    var buf = new byte[5 + Math.Min(chunk, px.Length - c)];
                    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
                    px.AsSpan(c, buf.Length - 5).CopyTo(buf.AsSpan(5));
                    Timed(buf, c);
                }
                if (f == 0) q.Send(QLinkClient.FeatMediaDock, 3, SetIdle(ConfigGuard.Quick(original), 1));
                Log($"frame {f + 1} ({names[f % names.Length]}): {requests} requests in {sw.ElapsedMilliseconds} ms, {slow} slow, worst {worst} ms");
                for (int i = 0; i < 4; i++) { q.KeepAlive(); q.Pump(1000); }
            }
        }
        finally
        {
            q.Pump(300);
            q.Send(QLinkClient.FeatMediaDock, 3, original, timeoutMs: 8000);
            Log("Restored original dock config.");
        }
    }

    /// <summary>Single-packet chunks with up to <paramref name="window"/> requests in flight (in order, never repeated).</summary>
    public static void Windowed(QLinkClient q, int frames, int window)
    {
        var original = ConfigGuard.Original(q);
        var clock = Stopwatch.StartNew();
        void Log(string s) => Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] {s}");
        SKColor[] colours = [SKColors.DarkRed, SKColors.DarkGreen, SKColors.DarkBlue, SKColors.DarkGoldenrod];
        string[] names = ["RED", "GREEN", "BLUE", "YELLOW"];
        try
        {
            using var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var cv = new SKCanvas(bmp);
            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 64);
            using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (int f = 0; f < frames; f++)
            {
                cv.Clear(colours[f % colours.Length]);
                cv.DrawText($"{f + 1} {names[f % names.Length]} w{window}", W / 2f, H / 2f + 22, SKTextAlign.Center, font, ink);
                cv.Flush();
                var px = Formats.ToRgb565(bmp);
                var hdr = new byte[5 + 9];
                BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(5), (uint)(px.Length + 9));
                BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(9), W);
                BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(11), H);
                hdr[13] = Formats.Rgb565;
                var sw = Stopwatch.StartNew();
                q.Send(QLinkClient.FeatMediaDock, 7, hdr, timeoutMs: 8000);
                long header = sw.ElapsedMilliseconds;
                var chunks = new List<byte[]>();
                for (int c = 0; c < px.Length; c += 50)
                {
                    var buf = new byte[5 + Math.Min(50, px.Length - c)];
                    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
                    px.AsSpan(c, buf.Length - 5).CopyTo(buf.AsSpan(5));
                    chunks.Add(buf);
                }
                var (ok, maxWait) = q.SendWindowed(QLinkClient.FeatMediaDock, 7, chunks, window, 3000);
                if (f == 0) q.Send(QLinkClient.FeatMediaDock, 3, SetIdle(ConfigGuard.Quick(original), 1));
                Log($"frame {f + 1} ({names[f % names.Length]}, window {window}): {(ok ? "OK" : "FAILED")} in {sw.ElapsedMilliseconds} ms (header {header} ms, worst reply wait {maxWait} ms)");
                for (int i = 0; i < 4; i++) { q.KeepAlive(); q.Pump(1000); }
            }
        }
        finally
        {
            q.Pump(300);
            q.Send(QLinkClient.FeatMediaDock, 3, original, timeoutMs: 8000);
            Log("Restored original dock config.");
        }
    }

    static byte[] SetIdle(byte[] config, int seconds)
    {
        var c = (byte[])config.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(5), (ushort)seconds);
        return c;
    }

    static void Timed(QLinkClient q, byte[] payload, int offset, List<(int, long)> lat)
    {
        var sw = Stopwatch.StartNew();
        try { q.Send(QLinkClient.FeatMediaDock, 7, payload, timeoutMs: 30000); }
        catch (TimeoutException) { Console.WriteLine($"    offset {offset}: NO reply within 30 s"); throw; }
        long ms = sw.ElapsedMilliseconds;
        if (ms > 400) Console.WriteLine($"    offset {offset}: {ms} ms");
        lat.Add((offset, ms));
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
