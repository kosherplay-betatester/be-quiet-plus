using System.Diagnostics;
using Darkmount.Dock;
using Darkmount.QLink;

// DockBench: uploads test frames with the real library and reports timing.
// Usage: DockBench [--frames N] [--window N] | --readkeys
string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
int frames = int.Parse(Arg("--frames", "5")), window = int.Parse(Arg("--window", "4"));

if (args.Contains("--lamps"))
{
    // Per-key RGB over the standard LampArray interface (MI_03). Hands control back to the keyboard at the end.
    using var dev = Darkmount.Keyboard.Lamps.LampArrayDevice.Open() ?? throw new InvalidOperationException("LampArray not found");
    var lamps = dev.Lamps;
    Console.WriteLine($"{lamps.Count} lamps, min update interval {dev.Attributes.MinUpdateIntervalMicroseconds} µs");
    int minX = lamps.Min(l => l.PositionX), maxX = lamps.Max(l => l.PositionX);
    try
    {
        dev.SetAutonomousMode(false);
        var t0 = Stopwatch.StartNew();
        dev.SetAll(new(255, 0, 0));
        Console.WriteLine($"all red in {t0.ElapsedMilliseconds} ms");
        Thread.Sleep(1500);
        t0.Restart();
        dev.SetAll(new(0, 255, 0));
        Console.WriteLine($"all green in {t0.ElapsedMilliseconds} ms");
        Thread.Sleep(1500);

        // Rainbow wave: every lamp, every frame, as fast as the keyboard allows.
        var clock = Stopwatch.StartNew();
        int frameCount = 0;
        long worst = 0;
        while (clock.ElapsedMilliseconds < 6000)
        {
            double secs = clock.ElapsedMilliseconds / 1000.0;
            var colors = new Dictionary<int, Darkmount.Keyboard.Lamps.LampColor>(lamps.Count);
            foreach (var l in lamps)
            {
                double hue = ((l.PositionX - minX) / (double)Math.Max(1, maxX - minX) + secs * 0.5) % 1.0;
                var (r, g, b) = Hsv(hue);
                colors[l.Id] = new(r, g, b);
            }
            var f = Stopwatch.StartNew();
            dev.SetColors(colors);
            worst = Math.Max(worst, f.ElapsedMilliseconds);
            frameCount++;
        }
        Console.WriteLine($"rainbow wave: {frameCount} full frames in 6 s = {frameCount / 6.0:F1} fps (worst frame {worst} ms)");
    }
    finally
    {
        dev.SetAutonomousMode(true);
        Console.WriteLine("Handed lighting back to the keyboard.");
    }
    return 0;

    static (byte, byte, byte) Hsv(double h)
    {
        double x = h * 6, f = x - Math.Floor(x);
        byte v = 255, p = 0, q = (byte)(255 * (1 - f)), u = (byte)(255 * f);
        return ((int)x % 6) switch { 0 => (v, u, p), 1 => (q, v, p), 2 => (p, v, u), 3 => (p, q, v), 4 => (u, p, v), _ => (v, p, q) };
    }
}

if (Process.GetProcessesByName("IO_Center").Length > 0 || Process.GetProcessesByName("DarkmountHub").Length > 0)
{
    Console.WriteLine("Close IO Center and Darkmount Hub first.");
    return 2;
}
using var t = HidSharpTransport.TryOpen() ?? throw new InvalidOperationException("Keyboard not found");
using var q = new QLinkClient(t);
q.Pump(200);
q.OpenSession();
Console.WriteLine($"Session {q.Sid}, active {q.IsActive}; window {window}");
if (args.Contains("--readkeys"))
{
    // Read-only: back up the eight display-key images and save upright PNG previews.
    var keys = new Darkmount.Keyboard.DisplayKeys(q);
    Console.WriteLine($"Numpad connected: {keys.IsConnected()}");
    var sw2 = Stopwatch.StartNew();
    int saved = Darkmount.Keyboard.DisplayKeyBackup.BackupOnce(keys);
    Console.WriteLine($"Backup: {saved} key image(s) saved to {Darkmount.Keyboard.DisplayKeyBackup.DefaultFolder} in {sw2.ElapsedMilliseconds} ms");
    var outDir = Arg("--out", Path.Combine(Path.GetTempPath(), "dmh-keys"));
    Directory.CreateDirectory(outDir);
    foreach (var file in Directory.GetFiles(Darkmount.Keyboard.DisplayKeyBackup.DefaultFolder, "key*.jpg"))
    {
        using var upright = Darkmount.Keyboard.DisplayKeys.DecodeStored(File.ReadAllBytes(file));
        if (upright is null) continue;
        using var png = upright.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".png"), png.ToArray());
    }
    Console.WriteLine($"Previews in {outDir}");
    return 0;
}

if (args.Contains("--size"))
{
    // Does the dock accept a smaller image (e.g. 160x120)? Uploads 3 numbered frames of that size.
    int w = int.Parse(Arg("--w", "160")), h = int.Parse(Arg("--h", "120"));
    var md = new MediaDock(q);
    var guard = new DockConfigGuard(DockConfigGuard.DefaultPath);
    var original = guard.Resolve(md.GetConfig());
    SkiaSharp.SKColor[] colours = [SkiaSharp.SKColors.DarkRed, SkiaSharp.SKColors.DarkGreen, SkiaSharp.SKColors.DarkBlue];
    try
    {
        for (int f = 0; f < 3; f++)
        {
            using var bmp = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
            using (var cv = new SkiaSharp.SKCanvas(bmp))
            {
                cv.Clear(colours[f]);
                using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI", SkiaSharp.SKFontStyle.Bold), h * 0.6f);
                using var ink = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true };
                cv.DrawText($"{f + 1}", w / 2f, h * 0.75f, SkiaSharp.SKTextAlign.Center, font, ink);
                cv.DrawRect(0, 0, w * 0.15f, h * 0.15f, new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White });
            }
            var px = new byte[w * h * 2];
            var span = bmp.GetPixelSpan();
            for (int i = 0, o = 0; i < span.Length; i += 4, o += 2)
            {
                int v = (span[i] >> 3) << 11 | (span[i + 1] >> 2) << 5 | (span[i + 2] >> 3);
                px[o] = (byte)v; px[o + 1] = (byte)(v >> 8);
            }
            var sw = Stopwatch.StartNew();
            string result;
            try
            {
                md.SetImage(MediaDock.SlotScreensaver, 0, MediaDock.ImageHeader(w, h, px.Length), 8000);
                md.SetImageData(MediaDock.SlotScreensaver, px);
                result = "accepted";
            }
            catch (Exception e) when (e is TimeoutException or QLinkException) { result = $"FAILED ({e.Message})"; }
            if (f == 0) md.SetConfig(DockConfigGuard.Running(original));
            Console.WriteLine($"frame {f + 1} ({w}x{h}, {px.Length / 1024} KB): {result} in {sw.ElapsedMilliseconds} ms");
            for (int i = 0; i < 12; i++) { q.KeepAlive(); q.Pump(500); }
        }
    }
    finally
    {
        md.SetConfig(original);
        Console.WriteLine("Restored the dock settings.");
    }
    return 0;
}

if (args.Contains("--cycle"))
{
    // Numbered colour frames with the app's real uploader; the dock is set exactly like the app sets it.
    bool large = !args.Contains("--small");
    int interval = int.Parse(Arg("--interval", "2500"));
    var md = new MediaDock(q);
    var guard = new DockConfigGuard(DockConfigGuard.DefaultPath);
    var original = guard.Resolve(md.GetConfig());
    var uploader = new FrameUploader(md) { LargeWrites = large };
    uploader.Log += m => Console.WriteLine("  " + m);
    SkiaSharp.SKColor[] colours = [SkiaSharp.SKColors.DarkRed, SkiaSharp.SKColors.DarkGreen, SkiaSharp.SKColors.DarkBlue, SkiaSharp.SKColors.DarkGoldenrod];
    var clock = Stopwatch.StartNew();
    try
    {
        for (int f = 0; f < frames; f++)
        {
            var sw = Stopwatch.StartNew();
            using var bmp = new SkiaSharp.SKBitmap(320, 240, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
            using (var cv = new SkiaSharp.SKCanvas(bmp))
            {
                cv.Clear(colours[f % colours.Length]);
                using var big = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI", SkiaSharp.SKFontStyle.Bold), 110);
                using var small = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Segoe UI", SkiaSharp.SKFontStyle.Bold), 26);
                using var ink = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true };
                cv.DrawText($"{f + 1}", 160, 150, SkiaSharp.SKTextAlign.Center, big, ink);
                cv.DrawText(large ? "LARGE" : "SMALL", 160, 215, SkiaSharp.SKTextAlign.Center, small, ink);
            }
            var result = uploader.Upload(Rgb565.FromBitmap(bmp));
            if (f == 0) md.SetConfig(DockConfigGuard.Running(original));
            Console.WriteLine($"[{clock.ElapsedMilliseconds / 1000.0,5:F1}s] frame {f + 1} ({(large ? "large" : "small")} writes): {result} in {sw.ElapsedMilliseconds} ms");
            while (sw.ElapsedMilliseconds < interval) { q.KeepAlive(); q.Pump((int)Math.Clamp(interval - sw.ElapsedMilliseconds, 1, 500)); }
        }
    }
    finally
    {
        md.SetConfig(original);
        Console.WriteLine("Restored the dock settings.");
    }
    return 0;
}

var dock = new MediaDock(q);
var up = new FrameUploader(dock) { Window = window };
up.Log += m => Console.WriteLine("  " + m);

var frame = new byte[FrameUploader.FrameBytes];
for (int f = 0; f < frames; f++)
{
    // Horizontal colour bands that move each frame (RGB565 LE).
    ushort[] colours = [0xF800, 0x07E0, 0x001F, 0xFFE0, 0xF81F, 0x07FF];
    for (int y = 0; y < 240; y++)
    {
        ushort c = colours[((y / 40) + f) % colours.Length];
        for (int x = 0; x < 320; x++) { frame[(y * 320 + x) * 2] = (byte)c; frame[(y * 320 + x) * 2 + 1] = (byte)(c >> 8); }
    }
    var sw = Stopwatch.StartNew();
    var result = up.Upload(frame);
    Console.WriteLine($"frame {f + 1}: {result} in {sw.ElapsedMilliseconds} ms");
    q.KeepAlive();
}
return 0;

