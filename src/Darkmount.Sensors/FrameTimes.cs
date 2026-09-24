using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Darkmount.Sensors;

/// <summary>
/// Rolling window of frame times (µs) → "1% low" and "0.1% low" FPS: the FPS at the 99th / 99.9th percentile
/// frame time over the last <see cref="WindowMs"/>. Pure and thread-safe.
/// </summary>
public sealed class FrameTimeStats
{
    public const int WindowMs = 10_000;
    const int Capacity = 20_000; // enough for 10 s at 2000 fps

    readonly (long AtMs, int FrameUs)[] _ring = new (long, int)[Capacity];
    readonly object _gate = new();
    int _next, _count;

    public void Add(int frameTimeUs, long atMs)
    {
        if (frameTimeUs is <= 0 or > 5_000_000) return; // ignore pauses/loading screens > 5 s
        lock (_gate)
        {
            _ring[_next] = (atMs, frameTimeUs);
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }
    }

    public void Clear()
    {
        lock (_gate) { _count = 0; _next = 0; }
    }

    /// <summary>1% low needs ≥ 100 frames in the window, 0.1% low ≥ 1000; otherwise null.</summary>
    public (double? Low1, double? Low01, int Frames) Lows(long nowMs)
    {
        int[] times;
        lock (_gate)
        {
            var list = new List<int>(_count);
            for (int i = 0; i < _count; i++)
            {
                var (at, us) = _ring[(_next - 1 - i + Capacity) % Capacity];
                if (nowMs - at > WindowMs) break;
                list.Add(us);
            }
            times = [.. list];
        }
        if (times.Length < 100) return (null, null, times.Length);
        Array.Sort(times);
        double Percentile(double p) => 1_000_000.0 / times[Math.Min(times.Length - 1, (int)Math.Ceiling(p * times.Length) - 1)];
        return (Percentile(0.99), times.Length >= 1000 ? Percentile(0.999) : null, times.Length);
    }
}

/// <summary>
/// Reads RivaTuner's per-frame timing for one process about a thousand times per second and feeds
/// <see cref="FrameTimeStats"/>. Only two numbers of one shared-memory entry are read per poll, so it is cheap.
/// Works whether or not Afterburner is set up to compute its own lows.
/// </summary>
public sealed class FrameTimeSampler : IDisposable
{
    const int OffTime0 = 268, OffFrames = 276, OffFrameTime = 280;

    readonly FrameTimeStats _stats = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Thread _thread;
    volatile int _targetPid;
    volatile bool _stop;

    public FrameTimeSampler()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Frame-time sampler", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>The game to measure (0 = none; the window is cleared when it changes).</summary>
    public int TargetPid
    {
        get => _targetPid;
        set
        {
            if (_targetPid == value) return;
            _targetPid = value;
            _stats.Clear();
        }
    }

    public (double? Low1, double? Low01, int Frames) Lows() => _stats.Lows(_clock.ElapsedMilliseconds);

    void Run()
    {
        timeBeginPeriod(1);
        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;
        long entry = -1, lastScan = -10_000;
        int scannedPid = 0;
        uint prevTime0 = 0, prevFrames = 0;
        try
        {
            while (!_stop)
            {
                int pid = _targetPid;
                if (pid == 0) { Thread.Sleep(200); continue; }
                try
                {
                    if (view is null)
                    {
                        mmf = MemoryMappedFile.OpenExisting(RtssReader.MappingName, MemoryMappedFileRights.Read);
                        view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    }
                    long now = _clock.ElapsedMilliseconds;
                    if (pid != scannedPid || entry < 0 && now - lastScan > 1000)
                    {
                        entry = FindEntry(view, pid);
                        scannedPid = pid;
                        lastScan = now;
                        prevFrames = 0;
                        prevTime0 = 0;
                    }
                    if (entry < 0) { Thread.Sleep(50); continue; }
                    if (view.ReadInt32(entry) != pid) { entry = -1; continue; } // slot reused

                    uint time0 = view.ReadUInt32(entry + OffTime0), frames = view.ReadUInt32(entry + OffFrames);
                    int frameUs = view.ReadInt32(entry + OffFrameTime);
                    // RTSS restarts its frame counter each ~second (new time0); count the frames since the last poll.
                    uint fresh = time0 != prevTime0 ? frames : frames >= prevFrames ? frames - prevFrames : frames;
                    for (uint i = 0; i < Math.Min(fresh, 8u); i++) _stats.Add(frameUs, now);
                    (prevTime0, prevFrames) = (time0, frames);
                    Thread.Sleep(1);
                }
                catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    view?.Dispose(); mmf?.Dispose(); view = null; mmf = null; entry = -1;
                    Thread.Sleep(1000); // RivaTuner not running
                }
            }
        }
        finally
        {
            view?.Dispose();
            mmf?.Dispose();
            timeEndPeriod(1);
        }
    }

    /// <summary>Offset of the RTSS app entry for <paramref name="pid"/>, or -1.</summary>
    static long FindEntry(MemoryMappedViewAccessor view, int pid)
    {
        if (view.ReadUInt32(0) != 0x52545353) return -1; // 'RTSS'
        uint size = view.ReadUInt32(8), offset = view.ReadUInt32(12), count = view.ReadUInt32(16);
        if (size < OffFrameTime + 4 || count > 4096) return -1;
        for (uint i = 0; i < count; i++)
        {
            long at = offset + (long)i * size;
            if (at + size > view.Capacity) break;
            if (view.ReadInt32(at) == pid) return at;
        }
        return -1;
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
}
