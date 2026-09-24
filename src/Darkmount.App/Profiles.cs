using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.Keyboard;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.App;

/// <summary>A complete setup: the keyboard's stored settings plus what OverMount shows and animates.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Profile";

    /// <summary>Lighting, bindings, Game Mode locks (and optionally display-key images) as stored on the keyboard.</summary>
    public KeyboardSnapshot Keyboard { get; set; } = new();

    public ScreenMode Mode { get; set; } = ScreenMode.Auto;
    public ScreenKind DefaultScreen { get; set; } = ScreenKind.Stats;
    public bool RgbEnabled { get; set; }
    public LightingScene? Scene { get; set; }

    /// <summary>Game executables (e.g. "cs2.exe") that switch to this profile automatically.</summary>
    public List<string> Games { get; set; } = [];
}

/// <summary>Profiles in %APPDATA%\OverMount\profiles.json plus the "no game" default.</summary>
public sealed class ProfileSet
{
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Profile applied when a game with its own profile closes (null = leave settings as they are).</summary>
    public string? DefaultProfile { get; set; }

    public Profile? ForGame(string? exe) =>
        exe is null ? null : Profiles.FirstOrDefault(p => p.Games.Any(g => g.Equals(exe, StringComparison.OrdinalIgnoreCase)));
}

public static class ProfileStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverMount", "profiles.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static ProfileSet Load(string? path = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ProfileSet>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or NotSupportedException)
        {
            Log.Write($"Profiles unreadable: {e.Message}");
            return new();
        }
    }

    public static void Save(ProfileSet set, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(set, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

/// <summary>
/// Applies profiles: keyboard settings (only what differs, via <see cref="KeyboardBackup.Apply"/>) and app settings.
/// Also switches automatically when a game with its own profile starts or stops.
/// </summary>
public sealed class ProfileManager(KeyboardService keyboard, Func<AppSettings> getSettings, Action<AppSettings> applySettings)
{
    string? _lastGame;
    string? _autoApplied;

    public ProfileSet Set { get; private set; } = ProfileStore.Load();

    public string? ActiveProfile { get; private set; }

    public event Action<string>? Status;

    public void Save(ProfileSet set)
    {
        Set = set;
        ProfileStore.Save(set);
    }

    /// <summary>Captures the current keyboard and app setup as a profile.</summary>
    public async Task<Profile> Capture(string name, bool includeDisplayKeys)
    {
        var snapshot = await keyboard.Run(q => KeyboardBackup.Read(q, includeDisplayKeys));
        snapshot.GameModeState = null; // Game Mode itself is toggled with Fn+Pause, not by profiles
        var s = getSettings();
        return new Profile
        {
            Name = name, Keyboard = snapshot, Mode = s.Mode, DefaultScreen = s.DefaultScreen, RgbEnabled = s.RgbEnabled, Scene = s.Scene?.Clone(),
        };
    }

    /// <summary>Edge-light number (1..96) → LampArray lamp id of the connected keyboard (empty when unknown).</summary>
    public Func<IReadOnlyDictionary<int, int>>? EdgeLights { get; set; }

    /// <summary>
    /// Converts an IO Center profile. "Open program/folder/website" keys become macros in <paramref name="macros"/>
    /// (saved by the caller) bound to F13–F24 triggers. Nothing is written to the keyboard until the profile is applied.
    /// </summary>
    public async Task<IoCenter.IoCenterImportResult> ImportIoCenter(string path, List<Macros.Macro>? macros)
    {
        var edges = await Task.Run(() => EdgeLights?.Invoke() ?? new Dictionary<int, int>());
        var result = await Task.Run(() => IoCenter.IoCenterImport.Load(path, keyboard.Model,
            edges.Count > 0 ? n => edges.TryGetValue(n, out int lamp) ? lamp : null : null));
        var s = getSettings();
        result.Profile.Mode = s.Mode;
        result.Profile.DefaultScreen = s.DefaultScreen;
        var notes = result.Notes.ToList();
        if (macros is not null) notes.AddRange(IoCenter.IoCenterMacros.Attach(result, macros));
        return result with { Notes = notes };
    }

    public async Task<int> Apply(Profile profile)
    {
        int writes = await keyboard.Run(q =>
        {
            KeyboardBackupGuard.EnsureBackup(q);
            return KeyboardBackup.Apply(q, profile.Keyboard, current: null);
        });
        var s = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(getSettings()))!;
        s.Mode = profile.Mode;
        s.DefaultScreen = profile.DefaultScreen;
        s.RgbEnabled = profile.RgbEnabled;
        if (profile.Scene is not null) s.Scene = profile.Scene.Clone();
        applySettings(s);
        ActiveProfile = profile.Name;
        Log.Write($"Applied profile '{profile.Name}' ({writes} keyboard write(s))");
        Status?.Invoke($"Profile \"{profile.Name}\" applied.");
        return writes;
    }

    /// <summary>Call periodically with the detected game (null = none). Applies game profiles on start/stop.</summary>
    public async Task OnGame(string? game)
    {
        if (string.Equals(game, _lastGame, StringComparison.OrdinalIgnoreCase)) return;
        _lastGame = game;
        try
        {
            if (Set.ForGame(game) is { } profile)
            {
                _autoApplied = profile.Name;
                await Apply(profile);
            }
            else if (game is null && _autoApplied is not null)
            {
                _autoApplied = null;
                if (Set.Profiles.FirstOrDefault(p => p.Name == Set.DefaultProfile) is { } fallback) await Apply(fallback);
            }
        }
        catch (Exception e) when (e is KeyboardUnavailableException or IOException or TimeoutException)
        {
            Log.Write($"Automatic profile switch failed: {e.Message}");
        }
    }
}
