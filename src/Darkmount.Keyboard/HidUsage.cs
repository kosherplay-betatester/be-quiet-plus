using System.Globalization;

namespace Darkmount.Keyboard;

public enum HidUsageGroup : byte { Letter, Digit, Basic, Punctuation, Function, Navigation, Numpad, Modifier, International, Other }

/// <summary>A HID keyboard-page usage with a display name (US layout).</summary>
public sealed record HidUsageInfo(byte Usage, string Name, HidUsageGroup Group);

/// <summary>
/// HID keyboard/keypad page (0x07) usages 0x04–0xE7, which a StandardKey binding may send
/// (docs/QLINK_KEYBOARD.md §3.7; the web app only offers 65 of them, the firmware accepts any).
/// </summary>
public static class HidUsage
{
    public const byte Min = 0x04, Max = 0xE7;
    const byte F1 = 0x3A, F12 = 0x45, F13 = 0x68, F24 = 0x73;

    /// <summary>Every usage 0x04–0xE7 (reserved ones get a generic name).</summary>
    public static IReadOnlyList<HidUsageInfo> All { get; } = Build();

    /// <summary>Usages worth offering in a key picker (no Power, no legacy/reserved codes).</summary>
    public static IReadOnlyList<HidUsageInfo> Offered { get; } = All.Where(IsOffered).ToArray();

    public static bool IsValid(byte usage) => usage is >= Min and <= Max;

    public static bool IsFKey(byte usage) => usage is >= F1 and <= F12 or >= F13 and <= F24;

    /// <summary>Usage of F1…F24.</summary>
    public static byte FKey(int number) => number switch
    {
        >= 1 and <= 12 => (byte)(F1 + number - 1),
        >= 13 and <= 24 => (byte)(F13 + number - 13),
        _ => throw new ArgumentOutOfRangeException(nameof(number), number, "F-keys are F1–F24"),
    };

    /// <summary>1–24 for an F-key usage, else 0.</summary>
    public static int FKeyNumber(byte usage) => usage switch
    {
        >= F1 and <= F12 => usage - F1 + 1,
        >= F13 and <= F24 => usage - F13 + 13,
        _ => 0,
    };

    public static HidUsageInfo? Find(byte usage) => IsValid(usage) ? All[usage - Min] : null;

    /// <summary>Display name for any byte; values outside 0x04–0xE7 give "Usage 0xNN".</summary>
    public static string NameOf(byte usage) => Find(usage)?.Name ?? $"Usage 0x{usage:X2}";

    /// <summary>Parses a display name ("F13", "Esc", case-insensitive) or a hex number ("0x9A") in 0x04–0xE7.</summary>
    public static bool TryParse(string text, out byte usage)
    {
        usage = 0;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
                || value is < Min or > Max) return false;
            usage = (byte)value;
            return true;
        }
        var match = All.FirstOrDefault(u => string.Equals(u.Name, s, StringComparison.OrdinalIgnoreCase));
        if (match is not null) usage = match.Usage;
        else if (!Aliases.TryGetValue(s, out usage)) return false;
        return true;
    }

    static readonly Dictionary<string, byte> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Escape"] = 0x29, ["Del"] = 0x4C, ["PgUp"] = 0x4B, ["PgDn"] = 0x4E, ["Context"] = 0x65, ["Win"] = 0xE3,
        ["Ctrl"] = 0xE0, ["Shift"] = 0xE1, ["Alt"] = 0xE2, ["AltGr"] = 0xE6,
    };

    static bool IsOffered(HidUsageInfo u) =>
        u.Usage is <= 0x65 or >= 0xE0 || IsFKey(u.Usage) || u.Usage is >= 0x87 and <= 0x91;

    static HidUsageInfo[] Build()
    {
        var names = new Dictionary<int, (string Name, HidUsageGroup Group)>();
        for (int i = 0; i < 26; i++) names[0x04 + i] = (((char)('A' + i)).ToString(), HidUsageGroup.Letter);
        for (int i = 0; i < 9; i++) names[0x1E + i] = ((i + 1).ToString(CultureInfo.InvariantCulture), HidUsageGroup.Digit);
        names[0x27] = ("0", HidUsageGroup.Digit);
        Add(names, HidUsageGroup.Basic, 0x28, "Enter", "Esc", "Backspace", "Tab", "Space");
        Add(names, HidUsageGroup.Punctuation, 0x2D, "-", "=", "[", "]", "\\", "Non-US #", ";", "'", "`", ",", ".", "/");
        names[0x39] = ("Caps Lock", HidUsageGroup.Basic);
        for (int n = 1; n <= 24; n++) names[FKey(n)] = ($"F{n}", HidUsageGroup.Function);
        Add(names, HidUsageGroup.Navigation, 0x46, "Print Screen", "Scroll Lock", "Pause", "Insert", "Home", "Page Up",
            "Delete", "End", "Page Down", "Right Arrow", "Left Arrow", "Down Arrow", "Up Arrow");
        Add(names, HidUsageGroup.Numpad, 0x53, "Num Lock", "Numpad /", "Numpad *", "Numpad -", "Numpad +", "Numpad Enter",
            "Numpad 1", "Numpad 2", "Numpad 3", "Numpad 4", "Numpad 5", "Numpad 6", "Numpad 7", "Numpad 8", "Numpad 9",
            "Numpad 0", "Numpad .");
        Add(names, HidUsageGroup.Punctuation, 0x64, "Non-US \\");
        Add(names, HidUsageGroup.Basic, 0x65, "Menu/Context");
        Add(names, HidUsageGroup.Other, 0x66, "Power");
        Add(names, HidUsageGroup.Numpad, 0x67, "Numpad =");
        Add(names, HidUsageGroup.Other, 0x74, "Execute", "Help", "Menu", "Select", "Stop", "Again", "Undo", "Cut", "Copy",
            "Paste", "Find", "Mute", "Volume Up", "Volume Down", "Locking Caps Lock", "Locking Num Lock",
            "Locking Scroll Lock");
        Add(names, HidUsageGroup.Numpad, 0x85, "Numpad ,", "Numpad = (AS/400)");
        Add(names, HidUsageGroup.International, 0x87, "Intl 1 (Ro)", "Intl 2 (Katakana/Hiragana)", "Intl 3 (Yen)",
            "Intl 4 (Henkan)", "Intl 5 (Muhenkan)", "Intl 6", "Intl 7", "Intl 8", "Intl 9", "Lang 1 (Hangul/English)",
            "Lang 2 (Hanja)", "Lang 3 (Katakana)", "Lang 4 (Hiragana)", "Lang 5 (Zenkaku/Hankaku)", "Lang 6", "Lang 7",
            "Lang 8", "Lang 9");
        Add(names, HidUsageGroup.Other, 0x99, "Alternate Erase", "SysReq", "Cancel", "Clear", "Prior", "Return",
            "Separator", "Out", "Oper", "Clear/Again", "CrSel", "ExSel");
        Add(names, HidUsageGroup.Numpad, 0xB0, "Numpad 00", "Numpad 000", "Thousands Separator", "Decimal Separator",
            "Currency Unit", "Currency Sub-unit", "Numpad (", "Numpad )", "Numpad {", "Numpad }", "Numpad Tab",
            "Numpad Backspace", "Numpad A", "Numpad B", "Numpad C", "Numpad D", "Numpad E", "Numpad F", "Numpad XOR",
            "Numpad ^", "Numpad %", "Numpad <", "Numpad >", "Numpad &", "Numpad &&", "Numpad |", "Numpad ||",
            "Numpad :", "Numpad #", "Numpad Space", "Numpad @", "Numpad !", "Numpad Memory Store",
            "Numpad Memory Recall", "Numpad Memory Clear", "Numpad Memory Add", "Numpad Memory Subtract",
            "Numpad Memory Multiply", "Numpad Memory Divide", "Numpad +/-", "Numpad Clear", "Numpad Clear Entry",
            "Numpad Binary", "Numpad Octal", "Numpad Decimal", "Numpad Hexadecimal");
        Add(names, HidUsageGroup.Modifier, 0xE0, "Left Ctrl", "Left Shift", "Left Alt", "Left Win", "Right Ctrl",
            "Right Shift", "Right Alt", "Right Win");

        var all = new HidUsageInfo[Max - Min + 1];
        for (int u = Min; u <= Max; u++)
        {
            var (name, group) = names.TryGetValue(u, out var n) ? n : ($"Usage 0x{u:X2}", HidUsageGroup.Other);
            all[u - Min] = new((byte)u, name, group);
        }
        return all;
    }

    static void Add(Dictionary<int, (string, HidUsageGroup)> names, HidUsageGroup group, int first, params string[] list)
    {
        for (int i = 0; i < list.Length; i++) names[first + i] = (list[i], group);
    }
}
