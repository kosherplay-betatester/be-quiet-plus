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

    /// <summary>Current media session for <see cref="NowPlayingScreen"/>; null when nothing is playing.</summary>
    public MediaInfo? Media { get; init; }

    /// <summary>Current network throughput for <see cref="NetworkScreen"/>; null when unavailable.</summary>
    public NetworkInfo? Network { get; init; }

    /// <summary>Focus timer state for <see cref="PomodoroScreen"/>; null when the timer is not in use.</summary>
    public PomodoroInfo? Pomodoro { get; init; }
}
