using System.Diagnostics;
using Darkmount.Dock;
using Darkmount.QLink;

// DockBench: uploads test frames with the real library and reports timing and nudges.
// Usage: DockBench [--frames N] [--chunk BYTES] [--nudge RepeatRequest|TruncatedRepeat|GetState] [--nudge-ms MS]
string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
int frames = int.Parse(Arg("--frames", "5")), chunk = int.Parse(Arg("--chunk", "4000")), nudgeMs = int.Parse(Arg("--nudge-ms", "600"));
var mode = Enum.Parse<NudgeMode>(Arg("--nudge", "RepeatRequest"));

if (Process.GetProcessesByName("IO_Center").Length > 0 || Process.GetProcessesByName("DarkmountHub").Length > 0)
{
    Console.WriteLine("Close IO Center and Darkmount Hub first.");
    return 2;
}
using var t = HidSharpTransport.TryOpen() ?? throw new InvalidOperationException("Keyboard not found");
using var q = new QLinkClient(t) { NudgeAfterMs = nudgeMs, NudgeMode = mode };
q.Pump(200);
q.OpenSession();
Console.WriteLine($"Session {q.Sid}, active {q.IsActive}; nudge {mode} after {nudgeMs} ms; chunk {chunk}");
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

var dock = new MediaDock(q);
var up = new FrameUploader(dock) { ChunkSize = chunk };
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
    int before = q.Nudges;
    var sw = Stopwatch.StartNew();
    var result = up.Upload(frame);
    Console.WriteLine($"frame {f + 1}: {result} in {sw.ElapsedMilliseconds} ms, nudges {q.Nudges - before}");
    q.KeepAlive();
}
return 0;
