using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.Screens;
using Darkmount.Sensors;

namespace Darkmount.App;

/// <summary>What the dock shows. <see cref="DockDefault"/> hands the screen back to the keyboard's own be quiet! menu.</summary>
public enum ScreenMode { Auto, Stats, Animation, DockDefault }
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

    /// <summary>Kept for old settings files; the dock idle delay is always 1 s while the app drives the dock.</summary>
    public int DockIdleSeconds { get; set; } = 1;

    /// <summary>Target time between dock images. Each upload takes ~2.2 s and is followed by a 3 s rest.</summary>
    public int RefreshMs { get; set; } = 5000;
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+D";
    public bool StartWithWindows { get; set; } = true;
    public AlertSettings Alerts { get; set; } = new();

    /// <summary>Host RGB animations on the keyboard's LEDs (off = the keyboard's own lighting effect).</summary>
    public bool RgbEnabled { get; set; }
    public Darkmount.Keyboard.Lamps.RgbEffectSettings Rgb { get; set; } = new();

    /// <summary>Flash the keyboard red while a dock alert is active.</summary>
    public bool RgbAlertFlash { get; set; } = true;
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
