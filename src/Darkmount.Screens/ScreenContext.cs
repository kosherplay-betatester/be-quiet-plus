using Darkmount.Sensors;

namespace Darkmount.Screens;

/// <summary>An active alert shown in the red banner, e.g. ("gpu-temp", "GPU 91°C").</summary>
public sealed record Alert(string Key, string Text);

/// <summary>Everything a screen needs to render one frame.</summary>
public sealed class ScreenContext
{
    public required Snapshot Snapshot { get; init; }
    public required MetricHistory History { get; init; }
    public IReadOnlyList<Alert> Alerts { get; init; } = [];
    public DateTime Now { get; init; } = DateTime.Now;
}
