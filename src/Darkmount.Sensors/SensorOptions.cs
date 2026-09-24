namespace Darkmount.Sensors;

/// <summary>
/// User-tunable sensor settings. Label lists are matched against HWiNFO reading labels
/// (case-insensitive, exact match first, then "contains").
/// </summary>
public sealed class SensorOptions
{
    public string[] CpuTempLabels { get; set; } = ["CPU Package", "CPU (Tctl/Tdie)", "CPU Die (average)"];
    public string[] CpuPowerLabels { get; set; } = ["CPU Package Power", "CPU PPT"];
    public string[] CpuLoadLabels { get; set; } = ["Total CPU Usage"];

    public string[] GpuTempLabels { get; set; } = ["GPU Temperature"];
    public string[] GpuPowerLabels { get; set; } = ["GPU Power", "Total Board Power", "GPU Package Power"];
    public string[] GpuLoadLabels { get; set; } = ["GPU Core Load", "GPU Utilization"];

    /// <summary>RAM used in MB.</summary>
    public string[] RamUsedLabels { get; set; } = ["Physical Memory Used"];

    /// <summary>VRAM used in MB.</summary>
    public string[] VramUsedLabels { get; set; } = ["GPU Memory Allocated", "GPU D3D Memory Dedicated"];

    /// <summary>Afterburner GPU index (the n in "GPUn ..."); 0 = automatic.</summary>
    public int GpuIndex { get; set; }

    /// <summary>Executables that are never treated as games (case-insensitive).</summary>
    public string[] GameExcludes { get; set; } =
    [
        "explorer.exe", "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe",
        "Discord.exe", "Code.exe", "Darkmount.App.exe", "OverMount.exe", "dwm.exe",
    ];

    /// <summary>Minimum RTSS frame rate for an app to count as a running game.</summary>
    public double GameMinFps { get; set; } = 1;

    /// <summary>An RTSS entry older than this is considered stale.</summary>
    public int GameStaleMs { get; set; } = 2000;
}
