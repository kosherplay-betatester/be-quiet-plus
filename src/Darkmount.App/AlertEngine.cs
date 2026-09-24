using Darkmount.Screens;
using Darkmount.Sensors;

namespace Darkmount.App;

/// <summary>
/// Turns snapshots into alerts. An alert stays visible while its condition holds plus
/// <see cref="AlertSettings.HoldSeconds"/>; low FPS must last <see cref="AlertSettings.FpsSeconds"/> first.
/// </summary>
public sealed class AlertEngine(AlertSettings settings)
{
    readonly Dictionary<string, (Alert Alert, DateTime LastTrue)> _active = [];
    DateTime? _fpsLowSince;

    /// <summary>True when the last <see cref="Evaluate"/> produced an alert that was not showing before.</summary>
    public bool HasNewAlert { get; private set; }

    public AlertSettings Settings { get; set; } = settings;

    public IReadOnlyList<Alert> Evaluate(Snapshot s, DateTime now)
    {
        HasNewAlert = false;
        var a = Settings;

        Check("cpu-temp", a.CpuTempEnabled && s.CpuTemp >= a.CpuTempMax, () => $"CPU {s.CpuTemp:F0}°C");
        Check("gpu-temp", a.GpuTempEnabled && s.GpuTemp >= a.GpuTempMax, () => $"GPU {s.GpuTemp:F0}°C");

        double? ram = Percent(s.RamUsedMb, s.RamTotalMb), vram = Percent(s.VramUsedMb, s.VramTotalMb);
        Check("ram", a.RamEnabled && ram >= a.RamMaxPercent, () => $"RAM {ram:F0}%");
        Check("vram", a.VramEnabled && vram >= a.VramMaxPercent, () => $"VRAM {vram:F0}%");

        bool fpsLow = a.FpsEnabled && s.InGame && s.Fps < a.FpsMin;
        _fpsLowSince = fpsLow ? _fpsLowSince ?? now : null;
        Check("fps", fpsLow && now - _fpsLowSince >= TimeSpan.FromSeconds(a.FpsSeconds), () => $"FPS {s.Fps:F0}");

        foreach (var key in _active.Where(kv => now - kv.Value.LastTrue > TimeSpan.FromSeconds(a.HoldSeconds)).Select(kv => kv.Key).ToList())
            _active.Remove(key);

        return _active.Values.Select(v => v.Alert).ToList();

        void Check(string key, bool condition, Func<string> text)
        {
            if (!condition) return;
            if (!_active.ContainsKey(key)) HasNewAlert = true;
            _active[key] = (new Alert(key, text()), now);
        }
    }

    static double? Percent(double? used, double? total) => used is { } u && total is > 0 ? u / total * 100 : null;
}
