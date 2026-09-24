using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Darkmount.App.Macros;

/// <summary>
/// How a macro plays when its trigger fires. RegisterHotKey only reports presses (never releases),
/// so "repeat while held" is not possible.
/// </summary>
public enum PlaybackMode
{
    /// <summary>Play the steps once.</summary>
    Once,

    /// <summary>Play the steps <see cref="Macro.RepeatCount"/> times.</summary>
    RepeatCount,

    /// <summary>The first press starts looping the steps; the next press stops.</summary>
    Toggle,
}

/// <summary>Keys the keyboard can send as macro triggers. Values are the Windows virtual-key codes.</summary>
public enum TriggerKey
{
    F13 = 0x7C, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
}

[Flags]
public enum MacroModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

public enum MouseButton { Left, Right, Middle, X1, X2 }

public enum MediaKey { PlayPause, Next, Previous, Stop, VolumeUp, VolumeDown, Mute }

/// <summary>The key combination the keyboard sends for a macro, e.g. "Ctrl+F13".</summary>
public sealed record MacroTrigger(TriggerKey Key = TriggerKey.F13, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    /// <summary>True when <see cref="Key"/> is one of F13–F24.</summary>
    [JsonIgnore]
    public bool IsValid => Enum.IsDefined(Key);

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        return sb.Append(Key).ToString();
    }

    /// <summary>Parses "F13", "Ctrl+Shift+F20", … (case and order insensitive). Only F13–F24 with Ctrl/Shift/Alt.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MacroTrigger? trigger)
    {
        trigger = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, shift = false, alt = false;
        TriggerKey? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    if (ctrl) return false;
                    ctrl = true;
                    break;
                case "shift":
                    if (shift) return false;
                    shift = true;
                    break;
                case "alt":
                    if (alt) return false;
                    alt = true;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var k)) return false;
                    key = k;
                    break;
            }
        }
        if (key is null) return false;
        trigger = new MacroTrigger(key.Value, ctrl, shift, alt);
        return true;
    }

    static bool TryParseKey(string text, out TriggerKey key)
    {
        key = default;
        if (text.Length != 3 || char.ToUpperInvariant(text[0]) != 'F') return false;
        if (!int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n is < 13 or > 24) return false;
        key = TriggerKey.F13 + (n - 13);
        return true;
    }
}

/// <summary>A host-side macro: a sequence of steps played when the keyboard sends <see cref="Trigger"/>.</summary>
public sealed class Macro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public MacroTrigger Trigger { get; set; } = new();
    public List<MacroStep> Steps { get; set; } = [];
    public PlaybackMode Mode { get; set; } = PlaybackMode.Once;

    /// <summary>Number of plays in <see cref="PlaybackMode.RepeatCount"/> mode (clamped to 1–<see cref="MacroPlayer.MaxRepeatCount"/>).</summary>
    public int RepeatCount { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>Copy with its own step list (steps themselves are immutable records). Keeps the same <see cref="Id"/>.</summary>
    public Macro Clone() => new()
    {
        Id = Id,
        Name = Name,
        Trigger = Trigger,
        Steps = [.. Steps],
        Mode = Mode,
        RepeatCount = RepeatCount,
        Enabled = Enabled,
    };
}
