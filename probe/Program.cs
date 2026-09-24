using System.Buffers.Binary;
using System.Diagnostics;
using DockProbe;

const byte ImgScreensaver = 0, ImgArtwork = 1;

var argv = args.ToList();
bool Flag(string name) => argv.Remove(name);
int Opt(string name, int def)
{
    int i = argv.IndexOf(name);
    if (i < 0 || i + 1 >= argv.Count) return def;
    int v = int.Parse(argv[i + 1]);
    argv.RemoveRange(i, 2);
    return v;
}

bool verbose = Flag("-v");
bool force = Flag("--force");
int contExtra = Opt("--contlen", 2);
int W = Opt("--w", 320), H = Opt("--h", 240);
string mode = argv.FirstOrDefault() ?? "help";

if (mode is "help" or "-h" or "--help")
{
    Console.WriteLine("""
        DockProbe - safe test tool for the be quiet! Dark Mount media dock

          info                         read-only: versions, features, dock state/config, image slot headers
          artwork [--frames N] [--chunk B] [--w 320 --h 240]
                                       upload N test frames to the ARTWORK slot, B bytes per request
                                       (49 = web-app style; >49 = multi-frame requests)
          readback [--type 0|1]        read the header of an image slot (0 screensaver, 1 artwork)

          -v          print raw packets
          --contlen   LENGTH convention for continuation frames (2 or 6), default 2
          --force     run even if IO Center is running
        """);
    return 0;
}

if (mode == "selftest")
{
    // CRC-16/MODBUS check value for "123456789" is 0x4B37.
    var crc = QLinkClient.Crc16Modbus("123456789"u8);
    Console.WriteLine($"CRC check: 0x{crc:X4} {(crc == 0x4B37 ? "OK" : "FAIL")}");
    return crc == 0x4B37 ? 0 : 1;
}

var ioCenter = Process.GetProcesses()
    .Where(p => p.ProcessName.Contains("IO_Center", StringComparison.OrdinalIgnoreCase)
             || p.ProcessName.Contains("IOCenterService", StringComparison.OrdinalIgnoreCase))
    .Select(p => p.ProcessName).Distinct().ToList();
if (ioCenter.Count > 0 && !force)
{
    Console.WriteLine($"IO Center is still running ({string.Join(", ", ioCenter)}). Close it and stop the service first.");
    return 2;
}

if (mode == "drain")
{
    // Listen only: print every frame the keyboard sends for N seconds (no session, nothing written).
    var d = QLinkClient.FindDevice()!;
    using var s = d.Open();
    s.ReadTimeout = 1000;
    var buf = new byte[65];
    var clock = Stopwatch.StartNew();
    int seconds = Opt("--seconds", 60), count = 0;
    long last = 0;
    while (clock.ElapsedMilliseconds < seconds * 1000L)
    {
        try
        {
            s.Read(buf, 0, 65);
            count++;
            last = clock.ElapsedMilliseconds;
            if (count <= 5 || count % 20 == 0)
                Console.WriteLine($"[{last / 1000.0:F1}s] #{count} sid {buf[3]:X2} req {buf[5]} {buf[6]}/{buf[7]} status {buf[4]}");
        }
        catch (TimeoutException) { if (clock.ElapsedMilliseconds - last > 15000 && count > 0) break; }
    }
    Console.WriteLine($"Received {count} frames; last at {last / 1000.0:F1}s");
    return 0;
}

var dev = QLinkClient.FindDevice();
if (dev is null)
{
    Console.WriteLine("Dark Mount vendor interface not found.");
    return 1;
}
Console.WriteLine($"Found: {dev.GetProductName()}  {dev.DevicePath}");

using var q = QLinkClient.Open(dev);
q.Verbose = verbose;
q.ContinuationLengthExtra = contExtra;
q.OnNotification = f => { if (f.Feature == 18) return;
    Console.WriteLine($"  [notification] feature {f.Feature} id {f.Command} data {Convert.ToHexString(f.Data)}"); };

q.OpenSession();
Console.WriteLine($"Session: SID {q.Sid}, state {(q.SessionState == 1 ? "Active" : "Inactive")}, timeout {q.SessionTimeout}s");

if (q.SessionState != 1)
{
    var a = q.Send(QLinkClient.FeatRoot, 6);
    Console.WriteLine($"Active session: SID {a[0]}, client type {a[2]} (1 = desktop IO Center, 2 = web)");
    Console.WriteLine("Requesting Active state...");
    q.Send(QLinkClient.FeatRoot, 7, [1]);
    q.Pump(2000);
    if (q.SessionState != 1)
    {
        Console.WriteLine("Could not become Active. Is IO Center (or IO Center Web in a browser) still connected?");
        return 3;
    }
    Console.WriteLine("Session is now Active.");
}

switch (mode)
{
    case "info": Info(); break;
    case "readback": ReadHeader((byte)Opt("--type", ImgArtwork)); break;
    case "artwork": Artwork(Opt("--frames", 1), Opt("--chunk", 49)); break;
    case "verify": Verify(); break;
    case "setconfig":
        var cfg = Convert.FromHexString(argv[1]);
        if (cfg.Length != 9) throw new ArgumentException("config must be 9 bytes of hex");
        q.Send(QLinkClient.FeatMediaDock, 3, cfg);
        Console.WriteLine($"Dock config set to {Convert.ToHexString(q.Send(QLinkClient.FeatMediaDock, 2))}");
        break;
    case "live": Live(Opt("--frames", 10), Opt("--chunk", 4000), Flag("--header"), Opt("--delay", 300)); break;
    case "sample": Sample((byte)Opt("--type", ImgScreensaver)); break;
    case "formats": Formats.Run(q, Opt("--anim", 40)); break;
    case "clear":
        Formats.Upload(q, ImgScreensaver, Formats.Rgb565, new byte[320 * 240 * 2]);
        Console.WriteLine("Screensaver slot reset to a black 320x240 RGB565 image.");
        break;
    case "speed": Speed(Opt("--frames", 6)); break;
    case "commit": Commit.Run(q); break;
    case "sequence": Sequence.Run(q, Opt("--hold", 10)); break;
    case "trigger": Sequence.Trigger(q, Opt("--hold", 12)); break;
    case "headercost": Sequence.HeaderCost(q); break;
    case "latency": Sequence.Latency(q, Opt("--frames", 4)); break;
    default: Console.WriteLine($"Unknown mode '{mode}'"); return 1;
}
return 0;

// Full-frame raw RGB565 throughput without re-sending the header, for several request sizes.
void Speed(int frames)
{
    var a = TestPattern(0, 320, 240);
    var b = TestPattern(4, 320, 240);
    Formats.Upload(q, ImgScreensaver, Formats.Rgb565, a);
    foreach (var chunk in new[] { 4000, 8000, 16000, 32000 })
    {
        try
        {
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
                UploadImage(ImgScreensaver, f % 2 == 0 ? b : a, chunk, sendHeader: false);
            sw.Stop();
            Console.WriteLine($"  chunk {chunk,5}: {frames * 1000.0 / sw.ElapsedMilliseconds:F2} full frames/s " +
                              $"({frames * a.Length / 1024.0 / sw.Elapsed.TotalSeconds:F0} KB/s)");
        }
        catch (Exception e) { Console.WriteLine($"  chunk {chunk,5}: failed: {e.Message}"); q.Pump(500); }
        q.KeepAlive();
    }
}

// Reads a few 54-byte samples spread across a slot and reports how many are non-zero.
void Sample(byte type)
{
    ReadHeader(type);
    var req = new byte[9];
    req[0] = type;
    int nonZero = 0, samples = 16, size = type == ImgScreensaver ? 153600 : 9800;
    for (int i = 0; i < samples; i++)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(1), (uint)(9 + (long)size * i / samples));
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(5), 54);
        var d = q.Send(QLinkClient.FeatMediaDock, 6, req);
        if (d.Any(b => b != 0)) nonZero++;
        if (i < 3) Console.WriteLine($"  sample {i}: {Convert.ToHexString(d, 0, Math.Min(16, d.Length))}");
    }
    Console.WriteLine($"{nonZero}/{samples} samples contain image data");
}

// Reads the whole artwork slot back and reports which test pattern (if any) it holds.
void Verify()
{
    const int size = 70 * 70 * 2;
    var data = new byte[size];
    var req = new byte[9];
    req[0] = ImgArtwork;
    for (int off = 0; off < size; off += 54)
    {
        int len = Math.Min(54, size - off);
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(1), (uint)(9 + off));
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(5), (uint)len);
        q.Send(QLinkClient.FeatMediaDock, 6, req).AsSpan(0, len).CopyTo(data.AsSpan(off));
    }
    var candidates = Enumerable.Range(0, 3).Select(i => ($"70x70 test frame {i + 1}", TestPattern(i, 70, 70)))
        .Append(("first 9800 bytes of the 320x240 test frame", TestPattern(0, 320, 240)[..size]));
    var match = candidates.FirstOrDefault(c => c.Item2.AsSpan().SequenceEqual(data));
    Console.WriteLine(match.Item1 is null
        ? $"Artwork slot holds something else (first bytes {Convert.ToHexString(data, 0, 16)})"
        : $"Artwork slot holds: {match.Item1}");
}

void Info()
{
    var v = q.Send(QLinkClient.FeatRoot, 5);
    Console.WriteLine($"QLink version: {v[3]}.{v[2]}.{v[1] << 8 | v[0]}");

    var f = q.Send(QLinkClient.FeatRoot, 4);
    Console.WriteLine("Features: " + string.Join(", ", Enumerable.Range(0, f[0]).Select(i => $"{f[1 + i * 2]} v{f[2 + i * 2]}")));

    Console.WriteLine($"Device info: {Convert.ToHexString(q.Send(QLinkClient.FeatDeviceInfo, 1))}");

    var s = q.Send(QLinkClient.FeatMediaDock, 1);
    Console.WriteLine($"Media dock: {(s[0] == 1 ? "connected" : "disconnected")}, position {(s[1] switch { 1 => "left", 2 => "right", _ => "none" })}");

    var c = q.Send(QLinkClient.FeatMediaDock, 2);
    Console.WriteLine($"Dock config: menu color #{c[0]:X2}{c[1]:X2}{c[2]:X2}, clock {(c[3] == 1 ? "24h" : "12h")}, " +
                      $"screensaver {(c[4] switch { 0 => "off", 1 => "clock", 2 => "image", _ => c[4].ToString() })} after {BitConverter.ToUInt16(c, 5)}s, " +
                      $"screen off after {BitConverter.ToUInt16(c, 7)}s");

    var n = q.Send(QLinkClient.FeatNumpad, 1);
    Console.WriteLine($"Numpad: {(n[0] == 1 ? "connected" : "disconnected")}, position {n[1]}");

    ReadHeader(ImgScreensaver);
    ReadHeader(ImgArtwork);

    try { Console.WriteLine($"Playback info (raw): {Convert.ToHexString(q.Send(QLinkClient.FeatMediaDock, 10))}"); }
    catch (Exception e) { Console.WriteLine($"Playback info: {e.Message}"); }
}

void ReadHeader(byte type)
{
    var req = new byte[9];
    req[0] = type;
    BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(5), 9);
    try
    {
        var h = q.Send(QLinkClient.FeatMediaDock, 6, req);
        if (h.Length < 9) { Console.WriteLine($"Slot {type}: short header {Convert.ToHexString(h)}"); return; }
        Console.WriteLine($"Slot {type} ({(type == 0 ? "screensaver" : "artwork")}): {BitConverter.ToUInt32(h, 0)} bytes, " +
                          $"{BitConverter.ToUInt16(h, 4)}x{BitConverter.ToUInt16(h, 6)}, format {h[8]}");
    }
    catch (Exception e) { Console.WriteLine($"Slot {type}: {e.Message}"); }
}

void Artwork(int frames, int chunk)
{
    Console.WriteLine($"Uploading {frames} {W}x{H} frame(s) to the ARTWORK slot, {chunk} bytes per request.");
    for (int i = 0; i < frames; i++)
    {
        var px = TestPattern(i, W, H);
        var sw = Stopwatch.StartNew();
        UploadImage(ImgArtwork, px, chunk);
        sw.Stop();
        Console.WriteLine($"  frame {i + 1}: {sw.ElapsedMilliseconds} ms  ({px.Length / 1024.0 / sw.Elapsed.TotalSeconds:F1} KB/s, {1000.0 / sw.ElapsedMilliseconds:F2} fps)");
    }
    ReadHeader(ImgArtwork);
    Console.WriteLine("Holding the session for 10 s so you can look at the dock...");
    for (int i = 0; i < 10; i++) { q.KeepAlive(); q.Pump(1000); }
}

// Screensaver-slot live test: full frame, then only the changed byte range per frame.
void Live(int frames, int chunk, bool headerEachFrame, int delayMs)
{
    var original = ConfigGuard.Original(q);
    var quick = ConfigGuard.Quick(original);
    try
    {
        var prev = TestPattern(0, W, H);
        var sw = Stopwatch.StartNew();
        UploadImage(ImgScreensaver, prev, chunk);
        Console.WriteLine($"  full frame: {sw.ElapsedMilliseconds} ms");
        q.Send(QLinkClient.FeatMediaDock, 3, quick);
        Console.WriteLine("Screensaver set to appear after 1 s. Watch the dock...");
        for (int i = 0; i < 3; i++) { q.KeepAlive(); q.Pump(1000); }

        for (int f = 1; f <= frames; f++)
        {
            var px = TestPattern(f, W, H);
            int from = 0, to = px.Length;
            while (from < to && px[from] == prev[from]) from++;
            while (to > from && px[to - 1] == prev[to - 1]) to--;
            sw.Restart();
            UploadImage(ImgScreensaver, px, chunk, from, to, headerEachFrame);
            Console.WriteLine($"  frame {f}: {to - from} bytes changed, {sw.ElapsedMilliseconds} ms");
            prev = px;
            q.KeepAlive();
            q.Pump(delayMs);
        }
        Console.WriteLine("Holding 5 s...");
        for (int i = 0; i < 5; i++) { q.KeepAlive(); q.Pump(1000); }
    }
    finally
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                q.Pump(500);
                q.Send(QLinkClient.FeatMediaDock, 3, original);
                Console.WriteLine("Restored original dock config.");
                break;
            }
            catch (Exception e) when (attempt < 3) { Console.WriteLine($"Restore attempt {attempt} failed: {e.Message}"); }
        }
    }
}

void UploadImage(byte type, byte[] pixels, int chunk, int from = 0, int to = -1, bool sendHeader = true)
{
    if (to < 0) to = pixels.Length;
    if (sendHeader)
    {
        var header = new byte[5 + 9];
        header[0] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), (uint)(pixels.Length + 9));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(9), (ushort)W);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(11), (ushort)H);
        header[13] = 1; // RGB565
        q.Send(QLinkClient.FeatMediaDock, 7, header);
    }

    var buf = new byte[5 + chunk];
    buf[0] = type;
    for (int c = from; c < to; c += chunk)
    {
        int len = Math.Min(chunk, to - c);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(1), (uint)(9 + c));
        pixels.AsSpan(c, len).CopyTo(buf.AsSpan(5));
        var t = Stopwatch.StartNew();
        try { q.Send(QLinkClient.FeatMediaDock, 7, buf.AsSpan(0, 5 + len)); }
        catch (TimeoutException) { Console.WriteLine($"    timeout writing offset {c} ({len} bytes)"); throw; }
        if (t.ElapsedMilliseconds > 300) Console.WriteLine($"    slow write at offset {c}: {t.ElapsedMilliseconds} ms");
    }
}

// Colour bars with a white band whose position moves with the frame number.
static byte[] TestPattern(int frame, int W, int H)
{
    ReadOnlySpan<(int r, int g, int b)> bars =
        [(255, 255, 255), (255, 255, 0), (0, 255, 255), (0, 255, 0), (255, 0, 255), (255, 0, 0), (0, 0, 255), (0, 0, 0)];
    var px = new byte[W * H * 2];
    int band = frame * H / 8 % H;
    for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        var (r, g, b) = y >= band && y < band + H / 12 ? (255, 255, 255) : bars[x * bars.Length / W];
        ushort v = (ushort)((r >> 3) << 11 | (g >> 2) << 5 | (b >> 3));
        px[(y * W + x) * 2] = (byte)v;
        px[(y * W + x) * 2 + 1] = (byte)(v >> 8);
    }
    return px;
}
