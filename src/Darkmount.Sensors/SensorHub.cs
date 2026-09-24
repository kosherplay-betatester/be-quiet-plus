namespace Darkmount.Sensors;

/// <summary>
/// Combines HWiNFO, MSI Afterburner, RTSS and Windows into one <see cref="Snapshot"/> per call.
/// Per field the first source with a value wins: HWiNFO, then Afterburner, then Windows.
/// </summary>
public sealed class SensorHub : IDisposable
{
    public const string HintHwInfo = "HWiNFO: enable 'Shared Memory Support' in HWiNFO settings (optional)";
    public const string HintAfterburner = "Start MSI Afterburner for CPU/GPU/FPS data";
    public const string HintRtss = "Start RivaTuner Statistics Server for game detection";
    public const string HintFpsLow = "Afterburner: enable 'Framerate 0.1% low' in Monitoring settings";

    /// <summary>How long a game stays detected while it is not in the foreground (alt-tab debounce).</summary>
    private const uint GameDebounceMs = 5000;

    private readonly SensorOptions _options;
    private readonly Func<double?> _ramTotalMb;
    private readonly Func<double?> _ramUsedMb;
    private readonly Func<double?> _vramTotalMb;
    private readonly Lock _lock = new();

    private int _lastGamePid;
    private uint _lastGameTicks;

    public SensorHub(SensorOptions options)
        : this(options, () => SystemInfo.RamTotalMb, () => SystemInfo.RamUsedMb, () => SystemInfo.VramTotalMb)
    {
    }

    internal SensorHub(SensorOptions options, Func<double?> ramTotalMb, Func<double?> ramUsedMb, Func<double?> vramTotalMb)
    {
        _options = options;
        _ramTotalMb = ramTotalMb;
        _ramUsedMb = ramUsedMb;
        _vramTotalMb = vramTotalMb;
    }

    /// <summary>Reads all sources now. Never throws.</summary>
    public Snapshot Sample() => Sample(
        SharedMemory.TryRead(HwInfoReader.MappingName),
        SharedMemory.TryRead(MahmReader.MappingName),
        SharedMemory.TryRead(RtssReader.MappingName),
        SystemInfo.ForegroundProcessId(),
        (uint)Environment.TickCount);

    internal Snapshot Sample(byte[]? hw, byte[]? mahm, byte[]? rtss, int foregroundPid, uint nowTicks)
    {
        var hints = new List<string>();

        Snapshot? h = hw is null ? null : HwInfoReader.Parse(hw, _options);
        if (h is null) hints.Add(HintHwInfo);

        MahmData? mahmData = mahm is null ? null : MahmReader.Parse(mahm);
        Snapshot? m = mahmData?.ToSnapshot(_options);
        if (m is null) hints.Add(HintAfterburner);

        IReadOnlyList<RtssApp>? apps = rtss is null ? null : RtssReader.Parse(rtss, nowTicks);
        if (apps is null) hints.Add(HintRtss);

        RtssApp? game;
        lock (_lock)
            game = DetectGame(apps ?? [], foregroundPid, nowTicks);

        double? fps = null, fpsLow = null;
        string fpsLowLabel = "0.1% low";
        if (game is not null)
        {
            fps = m?.Fps ?? (game.Fps > 0 ? game.Fps : null);
            fpsLow = m?.FpsLow;
            if (m is not null) fpsLowLabel = m.FpsLowLabel;
            if (mahmData is not null && !mahmData.HasFpsLowEntries) hints.Add(HintFpsLow);
        }

        return new Snapshot
        {
            CpuTemp = h?.CpuTemp ?? m?.CpuTemp,
            CpuPower = h?.CpuPower ?? m?.CpuPower,
            CpuLoad = h?.CpuLoad ?? m?.CpuLoad,
            GpuTemp = h?.GpuTemp ?? m?.GpuTemp,
            GpuPower = h?.GpuPower ?? m?.GpuPower,
            GpuLoad = h?.GpuLoad ?? m?.GpuLoad,
            RamUsedMb = h?.RamUsedMb ?? m?.RamUsedMb ?? _ramUsedMb(),
            RamTotalMb = _ramTotalMb(),
            VramUsedMb = h?.VramUsedMb ?? m?.VramUsedMb,
            VramTotalMb = _vramTotalMb(),
            Fps = fps,
            FpsLow = fpsLow,
            FpsLowLabel = fpsLowLabel,
            GameName = game?.ExeName,
            Hints = hints,
        };
    }

    private RtssApp? DetectGame(IReadOnlyList<RtssApp> apps, int foregroundPid, uint nowTicks)
    {
        if (foregroundPid != 0)
        {
            var fg = apps.FirstOrDefault(a => a.Pid == foregroundPid && IsRunningGame(a));
            if (fg is not null)
            {
                _lastGamePid = fg.Pid;
                _lastGameTicks = nowTicks;
                return fg;
            }
        }

        // Debounce alt-tab: keep the last foreground game while it still renders, for a few seconds.
        if (_lastGamePid != 0 && unchecked(nowTicks - _lastGameTicks) < GameDebounceMs)
        {
            var last = apps.FirstOrDefault(a => a.Pid == _lastGamePid && IsRunningGame(a));
            if (last is not null) return last;
        }

        _lastGamePid = 0;
        return null;
    }

    private bool IsRunningGame(RtssApp app) =>
        app.Fps >= _options.GameMinFps
        && app.AgeMs < (uint)Math.Max(0, _options.GameStaleMs)
        && !_options.GameExcludes.Contains(app.ExeName, StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        // Shared memory is opened and closed per sample; nothing is held between calls.
    }
}
