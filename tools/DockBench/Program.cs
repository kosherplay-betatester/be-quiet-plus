using System.Diagnostics;
using Darkmount.Dock;
using Darkmount.QLink;

// DockBench: uploads test frames with the real library and reports timing.
// Usage: DockBench [--frames N] [--window N] | --readkeys
string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
int frames = int.Parse(Arg("--frames", "5")), window = int.Parse(Arg("--window", "4"));

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
