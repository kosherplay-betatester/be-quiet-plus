using Darkmount.QLink;

namespace Darkmount.Dock;

/// <summary>
/// Remembers the user's real dock settings in a file so they are always restored correctly — never a
/// value this app (or a crashed run) left on the keyboard.
/// </summary>
public sealed class DockConfigGuard(string backupPath)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkmountHub", "dock-config-backup.hex");

    DockConfig? _original;

    /// <summary>Returns the user's original config: the saved backup, else <paramref name="current"/> if sane (and saves it).</summary>
    public DockConfig Resolve(DockConfig current)
    {
        if (_original is not null) return _original;
        if (TryLoad() is { } saved) return _original = saved;

        _original = LooksLikeAppConfig(current) ? DockConfig.UserOriginal : current;
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, Convert.ToHexString(_original.ToBytes()));
        return _original;
    }

    DockConfig? TryLoad()
    {
        try
        {
            return File.Exists(backupPath) ? DockConfig.FromBytes(Convert.FromHexString(File.ReadAllText(backupPath).Trim())) : null;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>This app (and the test tools) use a short idle delay and a never-off screen; users never do.</summary>
    public static bool LooksLikeAppConfig(DockConfig c) => c.IdleSeconds < 5 || c.ScreenOffSeconds == 0;

    /// <summary>Config while the app drives the dock: show our image after the idle delay, keep the screen on.</summary>
    public static DockConfig Running(DockConfig original, int idleSeconds) =>
        original with { Screensaver = ScreensaverMode.Image, IdleSeconds = Math.Clamp(idleSeconds, 1, 4), ScreenOffSeconds = 0 };
}
