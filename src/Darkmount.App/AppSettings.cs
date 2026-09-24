using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.Screens;
using Darkmount.Sensors;

namespace Darkmount.App;

public enum ScreenMode { Auto, Stats, Animation }
public enum ScreenKind { Stats, Animation }

public sealed class AlertSettings
{
    public bool CpuTempEnabled { get; set; } = true;
    public double CpuTempMax { get; set; } = 90;
    public bool GpuTempEnabled { get; set; } = true;
    public double GpuTempMax { get; set; } = 85;
    public bool RamEnabled { get; set; } = true;
    public double RamMaxPercent { get; set; } = 90;
    public bool VramEnabled { get; set; } = true;
    public double VramMaxPercent { get; set; } = 95;
    public bool FpsEnabled { get; set; } = true;
    public double FpsMin { get; set; } = 30;
    public double FpsSeconds { get; set; } = 4;
    public double HoldSeconds { get; set; } = 10;
}

public sealed class AppSettings
{
    public ScreenMode Mode { get; set; } = ScreenMode.Auto;

    /// <summary>Screen shown in Auto mode when no game runs.</summary>
    public ScreenKind DefaultScreen { get; set; } = ScreenKind.Stats;

    public AnimationKind AnimationKind { get; set; } = AnimationKind.Plasma;
    public string? AnimationPath { get; set; }

    /// <summary>
    /// Idle delay the dock uses while the app drives it. Must stay 1 s: every new image counts as activity for
    /// the dock, so with a longer delay and continuous refreshes the dock would never show the dashboard.
    /// </summary>
    public int DockIdleSeconds { get; set; } = 1;

    public int RefreshMs { get; set; } = 2000;
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+D";
    public bool StartWithWindows { get; set; } = true;
    public AlertSettings Alerts { get; set; } = new();
    public SensorOptions Sensors { get; set; } = new();
}

public static class SettingsStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkmountHub", "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
        {
            Log.Write($"Settings file unreadable, using defaults: {e.Message}");
            return new();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
