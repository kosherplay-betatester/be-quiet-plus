namespace Darkmount.App.Macros;

/// <summary>Windows virtual-key codes used by macros, plus naming and extended-key helpers.</summary>
public static class VirtualKeys
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12, Pause = 0x13,
        CapsLock = 0x14, Escape = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24,
        Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E,
        A = 0x41, C = 0x43, S = 0x53, V = 0x56, X = 0x58, Z = 0x5A,
        LWin = 0x5B, RWin = 0x5C, Apps = 0x5D, Divide = 0x6F, F1 = 0x70, F13 = 0x7C, F24 = 0x87, NumLock = 0x90,
        LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5,
        VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF,
        MediaNext = 0xB0, MediaPrev = 0xB1, MediaStop = 0xB2, MediaPlayPause = 0xB3;

    static readonly Dictionary<int, string> Names = new()
    {
        [Back] = "Backspace", [Tab] = "Tab", [Return] = "Enter", [Shift] = "Shift", [Control] = "Ctrl", [Menu] = "Alt",
        [Pause] = "Pause", [CapsLock] = "Caps Lock", [Escape] = "Esc", [Space] = "Space", [PageUp] = "Page Up",
        [PageDown] = "Page Down", [End] = "End", [Home] = "Home", [Left] = "Left", [Up] = "Up", [Right] = "Right",
        [Down] = "Down", [PrintScreen] = "Print Screen", [Insert] = "Insert", [Delete] = "Delete", [LWin] = "Win",
        [RWin] = "Right Win", [Apps] = "Menu key", [Divide] = "Num /", [NumLock] = "Num Lock",
        [LShift] = "Left Shift", [RShift] = "Right Shift", [LControl] = "Left Ctrl", [RControl] = "Right Ctrl",
        [LMenu] = "Left Alt", [RMenu] = "Right Alt", [VolumeMute] = "Mute", [VolumeDown] = "Volume Down",
        [VolumeUp] = "Volume Up", [MediaNext] = "Next Track", [MediaPrev] = "Previous Track", [MediaStop] = "Stop",
        [MediaPlayPause] = "Play/Pause", [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0x91] = "Scroll Lock",
    };

    /// <summary>
    /// Keys whose scan code has the E0 prefix and therefore need KEYEVENTF_EXTENDEDKEY: the navigation cluster,
    /// arrows, right Ctrl/Alt, Win/Menu keys, Num / and Num Lock, Print Screen and the media/browser keys.
    /// Right Shift is <b>not</b> extended.
    /// </summary>
    public static bool IsExtended(int vk) => vk switch
    {
        0x03 => true, // Ctrl+Break
        >= PageUp and <= Down => true,
        PrintScreen or Insert or Delete => true,
        LWin or RWin or Apps => true,
        Divide or NumLock => true,
        RControl or RMenu => true,
        >= 0xA6 and <= 0xB7 => true, // browser, volume, media and launch keys
        _ => false,
    };

    public static int ForMediaKey(MediaKey key) => key switch
    {
        MediaKey.PlayPause => MediaPlayPause,
        MediaKey.Next => MediaNext,
        MediaKey.Previous => MediaPrev,
        MediaKey.Stop => MediaStop,
        MediaKey.VolumeUp => VolumeUp,
        MediaKey.VolumeDown => VolumeDown,
        MediaKey.Mute => VolumeMute,
        _ => throw new MacroStepException($"Unknown media key {key}"),
    };

    /// <summary>Left-side modifier keys to hold for <paramref name="modifiers"/>, in press order (Win, Ctrl, Alt, Shift).</summary>
    public static IReadOnlyList<int> ForModifiers(MacroModifiers modifiers)
    {
        var keys = new List<int>(4);
        if (modifiers.HasFlag(MacroModifiers.Win)) keys.Add(LWin);
        if (modifiers.HasFlag(MacroModifiers.Ctrl)) keys.Add(LControl);
        if (modifiers.HasFlag(MacroModifiers.Alt)) keys.Add(LMenu);
        if (modifiers.HasFlag(MacroModifiers.Shift)) keys.Add(LShift);
        return keys;
    }

    /// <summary>Display name of a key, e.g. "A", "Esc", "Left Ctrl", "F13".</summary>
    public static string Name(int vk)
    {
        if (Names.TryGetValue(vk, out var name)) return name;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= F1 and <= F24) return $"F{vk - F1 + 1}";
        if (vk is >= 0x60 and <= 0x69) return $"Num {vk - 0x60}";
        return $"Key 0x{vk:X2}";
    }

    /// <summary>"Ctrl+Shift+Esc" style text for a key with modifiers.</summary>
    public static string Combo(int vk, MacroModifiers modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(MacroModifiers.Win)) parts.Add("Win");
        if (modifiers.HasFlag(MacroModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(MacroModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(MacroModifiers.Shift)) parts.Add("Shift");
        parts.Add(Name(vk));
        return string.Join('+', parts);
    }
}
