namespace Darkmount.Sensors;

/// <summary>
/// One sample of everything the dock can show. Any value is null when no source provides it.
/// Temperatures in °C, power in W, loads in %, memory in MB, frame rates in FPS.
/// </summary>
public sealed record Snapshot
{
    public double? CpuTemp { get; init; }
    public double? CpuPower { get; init; }
    public double? CpuLoad { get; init; }

    public double? GpuTemp { get; init; }
    public double? GpuPower { get; init; }
    public double? GpuLoad { get; init; }

    public double? RamUsedMb { get; init; }
    public double? RamTotalMb { get; init; }
    public double? VramUsedMb { get; init; }
    public double? VramTotalMb { get; init; }

    /// <summary>Average frame rate of the detected game.</summary>
    public double? Fps { get; init; }

    /// <summary>Low frame rate: OverMount's own 1 % low from RivaTuner frame times, else Afterburner's low.</summary>
    public double? FpsLow { get; init; }

    /// <summary>Label for <see cref="FpsLow"/>, e.g. "0.1% low" or "1% low".</summary>
    public string FpsLowLabel { get; init; } = "1% low";

    /// <summary>Executable name of the running game, or null when no game is detected.</summary>
    public string? GameName { get; init; }

    public bool InGame => GameName is not null;

    /// <summary>Human-readable hints about missing data sources (for the tray tooltip).</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];

    public DateTime Time { get; init; } = DateTime.Now;
}
