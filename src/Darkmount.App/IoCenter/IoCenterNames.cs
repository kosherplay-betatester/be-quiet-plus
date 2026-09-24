using Darkmount.Keyboard;

namespace Darkmount.App.IoCenter;

/// <summary>
/// IO Center's names for keys ("Key_W", "Key_NumpadImage0") and edge LEDs ("Led_KeyboardTop5"), mapped to Dark Mount key
/// ids and edge-light numbers (1..96, i.e. LampArray InputBinding − 105).
/// </summary>
public static class IoCenterNames
{
    /// <summary>USB HID usage names as IO Center writes them (keyboard page).</summary>
    static readonly Dictionary<string, byte> Usages = BuildUsages();

    static readonly Dictionary<string, byte> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Key_Fn"] = 55,
        ["Key_NonUsHash"] = 28, ["Key_NonUsHashAndTilde"] = 28,
        ["Key_Mute"] = KeyIds.DockMute, ["Key_PlayPause"] = KeyIds.DockPlayPause,
        ["Key_ScanPreviousTrack"] = KeyIds.DockPrevious, ["Key_ScanNextTrack"] = KeyIds.DockNext,
    };

    static Dictionary<string, byte> BuildUsages()
    {
        var u = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) u[$"Key_{c}"] = (byte)(0x04 + c - 'A');
        for (int n = 1; n <= 9; n++) u[$"Key_{n}"] = (byte)(0x1E + n - 1);
        u["Key_0"] = 0x27;
        for (int n = 1; n <= 12; n++) u[$"Key_F{n}"] = (byte)(0x3A + n - 1);
        for (int n = 13; n <= 24; n++) u[$"Key_F{n}"] = (byte)(0x68 + n - 13);
        for (int n = 1; n <= 9; n++) u[$"Key_Numpad{n}"] = (byte)(0x59 + n - 1);
        u["Key_Numpad0"] = 0x62;
        foreach (var (name, usage) in new (string, byte)[]
                 {
                     ("Enter", 0x28), ("Escape", 0x29), ("Backspace", 0x2A), ("Tab", 0x2B), ("Space", 0x2C),
                     ("MinusAndUnderscore", 0x2D), ("EqualsAndPlus", 0x2E), ("BracketLeft", 0x2F), ("BracketRight", 0x30),
                     ("Backslash", 0x31), ("SemicolonAndColon", 0x33), ("ApostropheAndDoubleQuote", 0x34),
                     ("GraveAccentAndTilde", 0x35), ("CommaAndLessThan", 0x36), ("PeriodAndBiggerThan", 0x37),
                     ("SlashAndQuestionMark", 0x38), ("CapsLock", 0x39), ("PrintScreen", 0x46), ("ScrollLock", 0x47),
                     ("PauseBreak", 0x48), ("Insert", 0x49), ("Home", 0x4A), ("PageUp", 0x4B), ("Delete", 0x4C), ("End", 0x4D),
                     ("PageDown", 0x4E), ("RightArrow", 0x4F), ("LeftArrow", 0x50), ("DownArrow", 0x51), ("UpArrow", 0x52),
                     ("NumLock", 0x53), ("NumpadSlash", 0x54), ("NumpadAsterisk", 0x55), ("NumpadMinus", 0x56),
                     ("NumpadPlus", 0x57), ("NumpadEnter", 0x58), ("NumpadPeriodAndDelete", 0x63), ("NonUsBackslash", 0x64),
                     ("NonUsBackslashAndPipe", 0x64), ("Application", 0x65), ("Menu", 0x65),
                     ("LeftCtrl", 0xE0), ("LeftShift", 0xE1), ("LeftAlt", 0xE2), ("LeftGui", 0xE3),
                     ("RightCtrl", 0xE4), ("RightShift", 0xE5), ("RightAlt", 0xE6), ("RightGui", 0xE7),
                 })
            u["Key_" + name] = usage;
        return u;
    }

    static readonly Dictionary<byte, byte> KeyByUsage = KeyIds.All
        .Where(k => k.HidUsage is not null && k.Zone is KeyZone.Keyboard or KeyZone.Numpad)
        .GroupBy(k => k.HidUsage!.Value).ToDictionary(g => g.Key, g => g.First().Id);

    /// <summary>HID usage of an IO Center key name ("Key_A" → 0x04), null if unknown.</summary>
    public static byte? UsageOf(string name) => Usages.TryGetValue(name, out var u) ? u : null;

    /// <summary>Dark Mount key id (<see cref="KeyIds"/>) of an IO Center key name, null if unknown.</summary>
    public static byte? KeyIdOf(string name)
    {
        if (SpecialKeys.TryGetValue(name, out var special)) return special;
        if (name.StartsWith("Key_NumpadImage", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan("Key_NumpadImage".Length), out int image) && image is >= 0 and < DisplayKeys.Count)
            return (byte)(KeyIds.DisplayKey1 + image);
        return UsageOf(name) is { } usage && KeyByUsage.TryGetValue(usage, out var id) ? id : null;
    }

    /// <summary>
    /// Edge-light number 1..96 of an IO Center LED name. The firmware numbers each ring clockwise from its top-left corner
    /// (keyboard 1..64, numpad 65..96); IO Center's sides hold the LEDs between the corners on top and bottom (21 / 5) and
    /// include both corners on the left and right (11 each), which matches those counts exactly.
    /// </summary>
    public static int? EdgeLightOf(string name)
    {
        if (!name.StartsWith("Led_", StringComparison.OrdinalIgnoreCase)) return null;
        var s = name[4..];
        bool numpad = s.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase);
        if (!numpad && !s.StartsWith("Keyboard", StringComparison.OrdinalIgnoreCase)) return null;
        s = s[(numpad ? 6 : 8)..];
        int digits = s.Length;
        while (digits > 0 && char.IsDigit(s[digits - 1])) digits--;
        if (digits == s.Length || !int.TryParse(s[digits..], out int n) || n < 1) return null;
        string side = s[..digits];

        int across = numpad ? 5 : 21, first = numpad ? 65 : 1; // LEDs between the corners on top/bottom; ring start
        int? offset = side.ToLowerInvariant() switch
        {
            "top" when n <= across => n,                                      // after the top-left corner
            "right" when n <= 11 => across + n,                                // top-right corner … bottom-right corner
            "bottom" when n <= across => across + 11 + n,                      // right → left
            "left" when n <= 10 => 2 * across + 11 + n,                        // bottom-left corner … upwards
            "left" when n == 11 => 0,                                          // top-left corner (ring start)
            _ => null,
        };
        return offset is { } o ? first + o : null;
    }
}
