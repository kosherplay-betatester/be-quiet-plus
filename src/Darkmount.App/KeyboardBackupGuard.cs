using Darkmount.Keyboard;
using Darkmount.QLink;

namespace Darkmount.App;

/// <summary>
/// Saves the keyboard's original settings (lighting, bindings, Game Mode locks, display-key images) once,
/// before the app changes anything, so the user can always get back to where they started.
/// </summary>
public static class KeyboardBackupGuard
{
    static readonly object Gate = new();
    static bool _ensured;

    /// <summary>Call on the keyboard thread (inside <see cref="KeyboardService.Run"/>) before any keyboard write.</summary>
    public static void EnsureBackup(QLinkClient q)
    {
        lock (Gate)
        {
            if (_ensured) return;
            if (KeyboardBackup.Load() is null)
            {
                var snapshot = KeyboardBackup.Read(q, includeDisplayKeys: true);
                KeyboardBackup.SaveOnce(snapshot);
                Log.Write("Saved the original keyboard settings backup");
            }
            _ensured = true;
        }
    }

    /// <summary>The original settings saved before the first change, or null.</summary>
    public static KeyboardSnapshot? Original => KeyboardBackup.Load();
}
