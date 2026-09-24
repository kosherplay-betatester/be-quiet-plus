namespace Darkmount.Keyboard;

public enum KeyZone : byte { Keyboard, Numpad, DisplayKey, DockButton }

/// <summary>
/// One Dark Mount key id (docs/QLINK_KEYBOARD.md §3.6). <see cref="HidUsage"/> is the standard usage the key
/// normally sends (derived from its name, not read from the device); null for Fn, display keys and dock buttons.
/// </summary>
public sealed record KeyInfo(byte Id, string Name, KeyZone Zone, string Label, byte? HidUsage)
{
    public string LabelUk { get; init; } = Label;
    public string LabelDe { get; init; } = Label;
    public string LabelFr { get; init; } = Label;

    /// <summary>Consumer-page usage of a dock button's factory function (Mute 0xE2, Play/Pause 0xCD, …).</summary>
    public ushort? ConsumerUsage { get; init; }

    /// <summary>Only present on ISO boards (the key next to Left Shift).</summary>
    public bool IsoOnly { get; init; }

    /// <summary>Cannot be rebound on the Common layer (the Fn key).</summary>
    public bool LockedCommon { get; init; }

    /// <summary>Cannot be rebound on the Fn layer (Fn, R = factory reset, Pause = Game Mode).</summary>
    public bool LockedFn { get; init; }

    /// <summary>The key cap label for a visual layout (NO falls back to UK).</summary>
    public string LabelFor(VisualLayout layout) => layout switch
    {
        VisualLayout.UK or VisualLayout.NO => LabelUk,
        VisualLayout.DE => LabelDe,
        VisualLayout.FR => LabelFr,
        _ => Label,
    };
}

/// <summary>The Dark Mount key-id table (enum <c>x</c> of the web app). Ids 0 and 106–108 are not keys.</summary>
public static class KeyIds
{
    public const byte R = 19, CapsLock = 29, Fn = 55, Left = 62, Up = 65, Down = 66, Right = 69, Esc = 87, F1 = 88,
        Pause = 102, LeftWin = 103, RightWin = 104, Iso = 105;
    public const byte DisplayKey1 = 109, DisplayKey8 = 116;
    public const byte DockMute = 117, DockPlayPause = 118, DockPrevious = 119, DockNext = 120;

    static KeyInfo K(byte id, string name, string us, byte? usage, string? uk = null, string? de = null, string? fr = null) =>
        new(id, name, KeyZone.Keyboard, us, usage) { LabelUk = uk ?? us, LabelDe = de ?? uk ?? us, LabelFr = fr ?? de ?? uk ?? us };

    static KeyInfo Pad(byte id, string name, string label, byte usage) => new(id, name, KeyZone.Numpad, label, usage);

    static KeyInfo Display(int n) => new((byte)(DisplayKey1 + n - 1), $"KEY_ID_NUMPAD_B{n}", KeyZone.DisplayKey, $"Numpad Image Button {n}", null);

    static KeyInfo Dock(byte id, string name, string label, ushort consumer) =>
        new(id, name, KeyZone.DockButton, label, null) { ConsumerUsage = consumer };

    static KeyInfo Same(byte id, string name, string label, byte usage) => K(id, name, label, usage);

    /// <summary>Every key id, sorted by id.</summary>
    public static IReadOnlyList<KeyInfo> All { get; } = new KeyInfo[]
    {
        K(1, "KEY_ID_TIL", "`", 0x35, "`", "^", "²"),
        Same(2, "KEY_ID_1", "1", 0x1E), Same(3, "KEY_ID_2", "2", 0x1F), Same(4, "KEY_ID_3", "3", 0x20),
        Same(5, "KEY_ID_4", "4", 0x21), Same(6, "KEY_ID_5", "5", 0x22), Same(7, "KEY_ID_6", "6", 0x23),
        Same(8, "KEY_ID_7", "7", 0x24), Same(9, "KEY_ID_8", "8", 0x25), Same(10, "KEY_ID_9", "9", 0x26),
        Same(11, "KEY_ID_0", "0", 0x27),
        K(12, "KEY_ID_MIS", "-", 0x2D, "-", "ẞ", "°"),
        K(13, "KEY_ID_EQU", "=", 0x2E, "=", "`", "+"),
        Same(14, "KEY_ID_BSP", "Backspace", 0x2A), Same(15, "KEY_ID_TAB", "Tab", 0x2B),
        K(16, "KEY_ID_q", "Q", 0x14, "Q", "Q", "A"),
        K(17, "KEY_ID_w", "W", 0x1A, "W", "W", "Z"),
        Same(18, "KEY_ID_e", "E", 0x08), Same(19, "KEY_ID_r", "R", 0x15) with { LockedFn = true }, Same(20, "KEY_ID_t", "T", 0x17),
        K(21, "KEY_ID_y", "Y", 0x1C, "Y", "Z", "Y"),
        Same(22, "KEY_ID_u", "U", 0x18), Same(23, "KEY_ID_i", "I", 0x0C), Same(24, "KEY_ID_o", "O", 0x12),
        Same(25, "KEY_ID_p", "P", 0x13),
        K(26, "KEY_ID_OQO", "[", 0x2F, "[", "ü", "¨"),
        K(27, "KEY_ID_EQO", "]", 0x30, "]", "+", "£"),
        K(28, "KEY_ID_BSL", "\\", 0x31, "#", "#", "μ"),
        Same(29, "KEY_ID_CAP", "Caps Lock", 0x39),
        K(30, "KEY_ID_a", "A", 0x04, "A", "A", "Q"),
        Same(31, "KEY_ID_s", "S", 0x16), Same(32, "KEY_ID_d", "D", 0x07), Same(33, "KEY_ID_f", "F", 0x09),
        Same(34, "KEY_ID_g", "G", 0x0A), Same(35, "KEY_ID_h", "H", 0x0B), Same(36, "KEY_ID_j", "J", 0x0D),
        Same(37, "KEY_ID_k", "K", 0x0E), Same(38, "KEY_ID_l", "L", 0x0F),
        K(39, "KEY_ID_COL", ";", 0x33, ";", "ö", "M"),
        K(40, "KEY_ID_CC", "'", 0x34, "'", "ä", "%"),
        Same(41, "KEY_ID_RTN", "Enter", 0x28), Same(42, "KEY_ID_LSHFT", "Left Shift", 0xE1),
        K(43, "KEY_ID_z", "Z", 0x1D, "Z", "Y", "W"),
        Same(44, "KEY_ID_x", "X", 0x1B), Same(45, "KEY_ID_c", "C", 0x06), Same(46, "KEY_ID_v", "V", 0x19),
        Same(47, "KEY_ID_b", "B", 0x05), Same(48, "KEY_ID_n", "N", 0x11),
        K(49, "KEY_ID_m", "M", 0x10, "M", "M", "?"),
        K(50, "KEY_ID_CMA", ",", 0x36, ",", ",", "."),
        K(51, "KEY_ID_DOT", ".", 0x37, ".", ".", "/"),
        K(52, "KEY_ID_SL", "/", 0x38, "/", "-", "§"),
        Same(53, "KEY_ID_RSHFT", "Right Shift", 0xE5), Same(54, "KEY_ID_LCTRL", "Left Ctrl", 0xE0),
        K(55, "KEY_ID_FN", "Fn", null) with { LockedCommon = true, LockedFn = true },
        Same(56, "KEY_ID_LALT", "Left Alt", 0xE2), Same(57, "KEY_ID_SPC", "Space", 0x2C),
        Same(58, "KEY_ID_RALT", "Alt Gr", 0xE6), Same(59, "KEY_ID_RCTRL", "Right Ctrl", 0xE4),
        Same(60, "KEY_ID_INS", "Insert", 0x49), Same(61, "KEY_ID_DEL", "Delete", 0x4C),
        Same(62, "KEY_ID_LA", "←", 0x50), Same(63, "KEY_ID_HOME", "Home", 0x4A), Same(64, "KEY_ID_END", "End", 0x4D),
        Same(65, "KEY_ID_UA", "↑", 0x52), Same(66, "KEY_ID_DA", "↓", 0x51), Same(67, "KEY_ID_PUP", "Page Up", 0x4B),
        Same(68, "KEY_ID_PDN", "Page Down", 0x4E), Same(69, "KEY_ID_RA", "→", 0x4F),
        Pad(70, "PAD_ID_NUM", "Num Lock", 0x53), Pad(71, "PAD_ID_7", "Numpad 7", 0x5F), Pad(72, "PAD_ID_4", "Numpad 4", 0x5C),
        Pad(73, "PAD_ID_1", "Numpad 1", 0x59), Pad(74, "PAD_ID_SL", "/", 0x54), Pad(75, "PAD_ID_8", "Numpad 8", 0x60),
        Pad(76, "PAD_ID_5", "Numpad 5", 0x5D), Pad(77, "PAD_ID_2", "Numpad 2", 0x5A), Pad(78, "PAD_ID_0", "Numpad 0", 0x62),
        Pad(79, "PAD_ID_MUL", "*", 0x55), Pad(80, "PAD_ID_9", "Numpad 9", 0x61), Pad(81, "PAD_ID_6", "Numpad 6", 0x5E),
        Pad(82, "PAD_ID_3", "Numpad 3", 0x5B), Pad(83, "PAD_ID_DEL", ".", 0x63), Pad(84, "PAD_ID_SUB", "-", 0x56),
        Pad(85, "PAD_ID_PLUS", "+", 0x57), Pad(86, "PAD_ID_ENT", "Enter", 0x58),
        Same(87, "KEY_ID_ESC", "Escape", 0x29),
        Same(88, "KEY_ID_F1", "F1", 0x3A), Same(89, "KEY_ID_F2", "F2", 0x3B), Same(90, "KEY_ID_F3", "F3", 0x3C),
        Same(91, "KEY_ID_F4", "F4", 0x3D), Same(92, "KEY_ID_F5", "F5", 0x3E), Same(93, "KEY_ID_F6", "F6", 0x3F),
        Same(94, "KEY_ID_F7", "F7", 0x40), Same(95, "KEY_ID_F8", "F8", 0x41), Same(96, "KEY_ID_F9", "F9", 0x42),
        Same(97, "KEY_ID_F10", "F10", 0x43), Same(98, "KEY_ID_F11", "F11", 0x44), Same(99, "KEY_ID_F12", "F12", 0x45),
        Same(100, "KEY_ID_PSC", "Print Screen", 0x46), Same(101, "KEY_ID_SLK", "Scroll Lock", 0x47),
        Same(102, "KEY_ID_PSE", "Pause", 0x48) with { LockedFn = true },
        Same(103, "KEY_ID_LWIN", "Left WIN", 0xE3),
        Same(104, "KEY_ID_APP", "Right Win", 0xE7),
        K(105, "KEY_ID_ISO", "\\", 0x64, "\\", "<", "<") with { IsoOnly = true },
        Display(1), Display(2), Display(3), Display(4), Display(5), Display(6), Display(7), Display(8),
        Dock(117, "KEY_ID_MUTE", "Mute", 0xE2),
        Dock(118, "KEY_ID_PLAY_PAUSE", "Play/Pause", 0xCD),
        Dock(119, "KEY_ID_PREVIOUS_TRACK", "Previous Track", 0xB6),
        Dock(120, "KEY_ID_NEXT_TRACK", "Next Track", 0xB5),
    };

    static readonly Dictionary<byte, KeyInfo> ById = All.ToDictionary(k => k.Id);
    static readonly Dictionary<string, KeyInfo> ByName = All.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);

    public static KeyInfo? Find(byte id) => ById.GetValueOrDefault(id);

    /// <summary>Looks up a key by its constant name, e.g. "KEY_ID_ESC" (case-insensitive).</summary>
    public static KeyInfo? FindByName(string name) => ByName.GetValueOrDefault(name);

    public static KeyInfo Get(byte id) =>
        Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Not a Dark Mount key id");

    /// <summary>False for unknown ids and for the keys the firmware locks on that layer.</summary>
    public static bool IsRebindable(byte id, Layer layer) =>
        Find(id) is { } k && !(layer == Layer.Fn ? k.LockedFn : k.LockedCommon);
}
