namespace Darkmount.Screens;

/// <summary>
/// The media session Windows reports as current (Spotify, a browser tab, Media Player, ...).
/// <paramref name="Duration"/> is zero for live streams. <paramref name="ArtworkPng"/> is a PNG of at most 256 px, or null.
/// </summary>
public sealed record MediaInfo(string Title, string Artist, string? Album, string? AppName, TimeSpan Position,
    TimeSpan Duration, bool Playing, byte[]? ArtworkPng);

/// <summary>Current network throughput summed over the active adapters.</summary>
public sealed record NetworkInfo(double DownloadBytesPerSec, double UploadBytesPerSec, string? AdapterName);

/// <summary>
/// State of the focus (Pomodoro) timer. <paramref name="Phase"/> is one of <see cref="Focus"/>, <see cref="Break"/>,
/// <see cref="LongBreak"/>, <see cref="Paused"/> or <see cref="Ready"/> (not started / waiting for the next focus).
/// </summary>
public sealed record PomodoroInfo(string Phase, TimeSpan Remaining, TimeSpan Total, int CompletedToday)
{
    public const string Focus = "Focus";
    public const string Break = "Break";
    public const string LongBreak = "Long break";
    public const string Paused = "Paused";
    public const string Ready = "Ready";
}
