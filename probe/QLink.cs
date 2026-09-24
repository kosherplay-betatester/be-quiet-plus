using System.Diagnostics;
using HidSharp;

namespace DockProbe;

/// <summary>
/// Minimal QLink client for the be quiet! Dark Mount (VID 0x373F, PID 0x0001, usage page 0xFF00).
/// Protocol reverse-engineered from IO Center Web; see docs/QLINK_PROTOCOL.md.
/// Only an explicit allowlist of commands can be sent (display, session, read-only info).
/// </summary>
public sealed class QLinkClient : IDisposable
{
    public const int Vid = 0x373F;
    public const int Pid = 0x0001;
    public const int BootloaderPid = 0x0009;
    const int PacketSize = 64;

    public const byte FeatRoot = 1, FeatDeviceInfo = 3, FeatNumpad = 32, FeatMediaDock = 33;

    // (feature, command) pairs this tool is allowed to send. Everything else throws before touching USB.
    static readonly HashSet<(byte, byte)> Allowed =
    [
        (FeatRoot, 1), (FeatRoot, 2), (FeatRoot, 3), (FeatRoot, 4), (FeatRoot, 5), (FeatRoot, 6), (FeatRoot, 7), (FeatRoot, 8),
        (FeatDeviceInfo, 1), (FeatDeviceInfo, 2),
        (FeatMediaDock, 1), (FeatMediaDock, 2), (FeatMediaDock, 3), (FeatMediaDock, 4), (FeatMediaDock, 5),
        (FeatMediaDock, 6), (FeatMediaDock, 7), (FeatMediaDock, 10),
        (FeatNumpad, 1), (FeatNumpad, 2), (FeatNumpad, 3),
    ];

    readonly HidStream _stream;
    readonly byte[] _rx = new byte[PacketSize + 1];
    byte _reqId;

    public byte Sid { get; private set; }
    public byte SessionState { get; private set; }
    public byte SessionTimeout { get; private set; }
    public bool Verbose { get; set; }

    /// <summary>LENGTH byte convention for continuation frames: 2 (device convention) or 6 (web-app quirk).</summary>
    public int ContinuationLengthExtra { get; set; } = 2;

    public Action<Frame>? OnNotification { get; set; }

    QLinkClient(HidStream stream) => _stream = stream;

    public static HidDevice? FindDevice()
    {
        var all = DeviceList.Local.GetHidDevices(Vid).ToList();
        if (all.Any(d => d.ProductID == BootloaderPid))
            throw new InvalidOperationException("Keyboard is in BOOTLOADER mode (PID 0x0009). Refusing to talk to it.");
        return all
            .Where(d => d.ProductID == Pid && d.GetMaxOutputReportLength() == PacketSize + 1)
            .FirstOrDefault(d => d.DevicePath.Contains("mi_02", StringComparison.OrdinalIgnoreCase)
                                 || UsagePage(d) == 0xFF00);
    }

    static uint UsagePage(HidDevice d)
    {
        try { return d.GetReportDescriptor().DeviceItems.FirstOrDefault()?.Usages.GetAllValues().FirstOrDefault() >> 16 ?? 0; }
        catch { return 0; }
    }

    public static QLinkClient Open(HidDevice dev)
    {
        var s = dev.Open();
        s.ReadTimeout = 3000;
        s.WriteTimeout = 1000;
        return new QLinkClient(s);
    }

    // ---------------------------------------------------------------- framing

    public static ushort Crc16Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Builds a 65-byte HID output report (report ID 0 + 64-byte QLink frame).</summary>
    byte[] BuildFrame(byte feat, byte cmd, ReadOnlySpan<byte> data, int seq, int total)
    {
        var r = new byte[PacketSize + 1];
        var p = r.AsSpan(1);
        p[0] = (byte)(data.Length + (seq == 0 ? 6 : ContinuationLengthExtra));
        p[1] = total > 1 ? (byte)(seq | (seq == total - 1 ? 0 : 0x80)) : (byte)0;
        p[2] = Sid;
        if (seq == 0)
        {
            p[3] = 0;
            p[4] = _reqId;
            p[5] = feat;
            p[6] = cmd;
            data.CopyTo(p[7..]);
        }
        else data.CopyTo(p[3..]);
        var crc = Crc16Modbus(p[..62]);
        p[62] = (byte)crc;
        p[63] = (byte)(crc >> 8);
        return r;
    }

    public readonly record struct Frame(byte Length, byte Seq, byte Sid, byte Status, byte ReqId, byte Feature, byte Command, byte[] Data)
    {
        public bool IsNotification => ReqId == 0 && Seq == 0;
        public bool HasMore => (Seq & 0x80) != 0;
    }

    static Frame Parse(ReadOnlySpan<byte> p)
    {
        int seq = p[1];
        int start = (seq & 0x7F) > 0 ? 3 : 7;
        int end = Math.Clamp(p[0] + 1, start, 62);
        return new Frame(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[start..end].ToArray());
    }

    Frame ReadFrame()
    {
        int n = _stream.Read(_rx, 0, _rx.Length);
        // HidSharp returns report ID in byte 0 on Windows.
        var f = Parse(_rx.AsSpan(n == PacketSize + 1 ? 1 : 0));
        if (Verbose) Console.WriteLine($"  <- {Convert.ToHexString(_rx, 0, Math.Min(n, 20))}...");
        // ROOT notification 1 = SessionStateChanged [state]
        if (f.IsNotification && f.Feature == FeatRoot && f.Command == 1 && f.Data.Length > 0)
            SessionState = f.Data[0];
        return f;
    }

    // ---------------------------------------------------------------- request / response

    public class QLinkException(byte status, string msg) : Exception(msg) { public byte Status { get; } = status; }

    static readonly string[] StatusNames =
    [
        "SUCCESS", "INVALID_SESSION_ID", "INVALID_FEATURE_ID", "INVALID_COMMAND_ID", "INVALID_REQUEST_ID",
        "INVALID_PARAMETER", "TIMEOUT", "BUSY", "NOT_AUTHORIZED", "NOT_ACTIVE", "INVALID_STATE", "INVALID_SIZE",
        "INSUFFICIENT_RESOURCES", "NOT_SUPPORTED",
    ];

    public static string StatusName(byte s) => s < StatusNames.Length ? StatusNames[s] : $"0x{s:X2}";

    /// <summary>Sends a request (split into continuation frames if needed) and returns the response data.</summary>
    public byte[] Send(byte feat, byte cmd, ReadOnlySpan<byte> data = default, int timeoutMs = 3000)
    {
        _stream.ReadTimeout = timeoutMs;
        if (!Allowed.Contains((feat, cmd)))
            throw new InvalidOperationException($"Command {feat}/{cmd} is not on the safety allowlist.");

        _reqId = (byte)(_reqId == 255 ? 1 : _reqId + 1);

        const int first = PacketSize - 9, cont = PacketSize - 5;
        int total = data.Length <= first ? 1 : 1 + (data.Length - first + cont - 1) / cont;
        int off = 0;
        for (int seq = 0; seq < total; seq++)
        {
            int len = Math.Min(seq == 0 ? first : cont, data.Length - off);
            var frame = BuildFrame(feat, cmd, data.Slice(off, len), seq, total);
            if (Verbose) Console.WriteLine($"  -> {Convert.ToHexString(frame, 0, 20)}...");
            _stream.Write(frame);
            off += len;
        }

        while (true)
        {
            var f = ReadFrame();
            if (f.IsNotification) { OnNotification?.Invoke(f); continue; }
            // Skip late replies to earlier (timed-out) requests.
            if (f.Feature != feat || f.Command != cmd || f.ReqId != _reqId) continue;
            if (f.Status != 0)
                throw new QLinkException(f.Status, $"{feat}/{cmd} failed: {StatusName(f.Status)}");
            var result = f.Data;
            while (f.HasMore)
            {
                f = ReadFrame();
                result = [.. result, .. f.Data];
            }
            return result;
        }
    }

    /// <summary>Stalls observed by <see cref="SendReliable"/>: (when, how long in ms).</summary>
    public List<(long atMs, long durationMs)> Stalls { get; } = [];
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// Like <see cref="Send"/>, but if the device goes quiet (it stalls while redrawing), waits until it
    /// answers again and re-sends the same (idempotent) request.
    /// </summary>
    public byte[] SendReliable(byte feat, byte cmd, ReadOnlySpan<byte> data = default, int attempts = 5)
    {
        for (int i = 1; ; i++)
        {
            try { return Send(feat, cmd, data, timeoutMs: 1500); }
            catch (TimeoutException) when (i < attempts)
            {
                long start = _clock.ElapsedMilliseconds - 1500;
                WaitReady();
                long dur = _clock.ElapsedMilliseconds - start;
                Stalls.Add((start, dur));
                Console.WriteLine($"    [stall ~{dur} ms on {feat}/{cmd}, retrying]");
            }
        }
    }

    /// <summary>Polls a cheap read (media dock GetState) until the device answers; returns elapsed ms.</summary>
    public long WaitReady(int maxMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxMs)
        {
            try { Send(FeatMediaDock, 1, timeoutMs: 150); return sw.ElapsedMilliseconds; }
            catch (TimeoutException) { }
        }
        throw new TimeoutException($"Device did not answer for {maxMs} ms");
    }

    /// <summary>Waits for incoming frames (notifications) for up to <paramref name="ms"/>.</summary>
    public void Pump(int ms)
    {
        _stream.ReadTimeout = Math.Max(1, ms);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            try { var f = ReadFrame(); if (f.IsNotification) OnNotification?.Invoke(f); }
            catch (TimeoutException) { }
        }
    }

    // ---------------------------------------------------------------- session

    public const byte ClientWeb = 2;

    public void OpenSession()
    {
        Sid = 0;
        var conn = BitConverter.GetBytes(Random.Shared.Next(10000, 99999));
        var d = Send(FeatRoot, 1, [.. conn, ClientWeb]);
        Sid = d[4];
        SessionState = d.Length > 5 ? d[5] : (byte)0;
        SessionTimeout = d.Length > 6 ? d[6] : (byte)0;
    }

    public void KeepAlive() => Send(FeatRoot, 3);

    public void CloseSession()
    {
        try { Send(FeatRoot, 2, [Sid]); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        CloseSession();
        _stream.Dispose();
    }
}
