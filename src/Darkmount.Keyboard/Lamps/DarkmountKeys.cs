namespace Darkmount.Keyboard.Lamps;

/// <summary>A key position in some reference coordinate system (only relative positions matter).</summary>
public readonly record struct KeyPosition(int HidUsage, string Name, double X, double Y);

/// <summary>
/// Dark Mount key ids (QLink <c>KEY_ID_*</c>, docs/QLINK_KEYBOARD.md §3.6) with their ANSI-US names and the standard HID
/// Keyboard-page usage each key sends. The keyboard's LampArray firmware reports these ids — not HID usages — in
/// LampAttributes.InputBinding; bindings ≥ <see cref="FirstEdgeLightBinding"/> are the edge light strips around the
/// keyboard and numpad (106–169 main block, 170–201 numpad on the probed unit).
/// </summary>
public static class DarkmountKeys
{
    public readonly record struct Key(int Id, string Name, int HidUsage);

    /// <summary>First InputBinding value that is an edge-light LED rather than a key.</summary>
    public const int FirstEdgeLightBinding = 106;

    static readonly Key[] All =
    [
        new(1, "`", 0x35), new(2, "1", 0x1E), new(3, "2", 0x1F), new(4, "3", 0x20), new(5, "4", 0x21),
        new(6, "5", 0x22), new(7, "6", 0x23), new(8, "7", 0x24), new(9, "8", 0x25), new(10, "9", 0x26),
        new(11, "0", 0x27), new(12, "-", 0x2D), new(13, "=", 0x2E), new(14, "Backspace", 0x2A), new(15, "Tab", 0x2B),
        new(16, "Q", 0x14), new(17, "W", 0x1A), new(18, "E", 0x08), new(19, "R", 0x15), new(20, "T", 0x17),
        new(21, "Y", 0x1C), new(22, "U", 0x18), new(23, "I", 0x0C), new(24, "O", 0x12), new(25, "P", 0x13),
        new(26, "[", 0x2F), new(27, "]", 0x30), new(28, "\\", 0x31), new(29, "Caps Lock", 0x39), new(30, "A", 0x04),
        new(31, "S", 0x16), new(32, "D", 0x07), new(33, "F", 0x09), new(34, "G", 0x0A), new(35, "H", 0x0B),
        new(36, "J", 0x0D), new(37, "K", 0x0E), new(38, "L", 0x0F), new(39, ";", 0x33), new(40, "'", 0x34),
        new(41, "Enter", 0x28), new(42, "Left Shift", 0xE1), new(43, "Z", 0x1D), new(44, "X", 0x1B), new(45, "C", 0x06),
        new(46, "V", 0x19), new(47, "B", 0x05), new(48, "N", 0x11), new(49, "M", 0x10), new(50, ",", 0x36),
        new(51, ".", 0x37), new(52, "/", 0x38), new(53, "Right Shift", 0xE5), new(54, "Left Ctrl", 0xE0), new(55, "Fn", 0),
        new(56, "Left Alt", 0xE2), new(57, "Space", 0x2C), new(58, "Right Alt", 0xE6), new(59, "Right Ctrl", 0xE4),
        new(60, "Insert", 0x49), new(61, "Delete", 0x4C), new(62, "Left", 0x50), new(63, "Home", 0x4A), new(64, "End", 0x4D),
        new(65, "Up", 0x52), new(66, "Down", 0x51), new(67, "Page Up", 0x4B), new(68, "Page Down", 0x4E), new(69, "Right", 0x4F),
        new(70, "Num Lock", 0x53), new(71, "Numpad 7", 0x5F), new(72, "Numpad 4", 0x5C), new(73, "Numpad 1", 0x59),
        new(74, "Numpad /", 0x54), new(75, "Numpad 8", 0x60), new(76, "Numpad 5", 0x5D), new(77, "Numpad 2", 0x5A),
        new(78, "Numpad 0", 0x62), new(79, "Numpad *", 0x55), new(80, "Numpad 9", 0x61), new(81, "Numpad 6", 0x5E),
        new(82, "Numpad 3", 0x5B), new(83, "Numpad .", 0x63), new(84, "Numpad -", 0x56), new(85, "Numpad +", 0x57),
        new(86, "Numpad Enter", 0x58), new(87, "Esc", 0x29), new(88, "F1", 0x3A), new(89, "F2", 0x3B), new(90, "F3", 0x3C),
        new(91, "F4", 0x3D), new(92, "F5", 0x3E), new(93, "F6", 0x3F), new(94, "F7", 0x40), new(95, "F8", 0x41),
        new(96, "F9", 0x42), new(97, "F10", 0x43), new(98, "F11", 0x44), new(99, "F12", 0x45), new(100, "Print Screen", 0x46),
        new(101, "Scroll Lock", 0x47), new(102, "Pause", 0x48), new(103, "Left Win", 0xE3), new(104, "Right Win", 0xE7),
        new(105, "ISO \\", 0x64),
    ];

    public static IReadOnlyDictionary<int, Key> ById { get; } = All.ToDictionary(k => k.Id);

    /// <summary>HID usage → key (Fn has no usage and is absent).</summary>
    public static IReadOnlyDictionary<int, Key> ByUsage { get; } = All.Where(k => k.HidUsage != 0).ToDictionary(k => k.HidUsage);

    public static bool TryGet(int keyId, out Key key) => ById.TryGetValue(keyId, out key);

    /// <summary>Human-readable name of a Keyboard-page usage ("Usage 0x68" when not on the Dark Mount).</summary>
    public static string UsageName(int hidUsage) => ByUsage.TryGetValue(hidUsage, out var k) ? k.Name : $"Usage 0x{hidUsage:X2}";

    /// <summary>
    /// Main-block key centres (ANSI US) in the IO Center SVG layout units (docs/QLINK_KEYBOARD.md §8) — the reference for
    /// mapping lamps by position when a device gives no InputBinding.
    /// </summary>
    public static IReadOnlyList<KeyPosition> AnsiMainBlock { get; } = BuildAnsiMainBlock();

    static KeyPosition[] BuildAnsiMainBlock()
    {
        // (key id, x, y, w, h) — bounding boxes of the SVG key paths.
        (int Id, int X, int Y, int W, int H)[] boxes =
        [
            (87, 607, 188, 72, 78), (88, 830, 188, 72, 78), (89, 941, 188, 70, 78), (90, 1051, 188, 72, 78),
            (91, 1162, 188, 70, 78), (92, 1328, 188, 72, 78), (93, 1438, 188, 72, 78), (94, 1549, 187, 72, 78),
            (95, 1660, 187, 72, 78), (96, 1825, 187, 70, 78), (97, 1933, 187, 74, 78), (98, 2048, 187, 72, 78),
            (99, 2157, 187, 72, 78), (100, 2292, 186, 72, 78), (101, 2403, 186, 70, 78), (102, 2514, 186, 72, 78),
            (1, 607, 321, 72, 78), (2, 718, 321, 72, 78), (3, 829, 321, 70, 78), (4, 940, 321, 70, 78),
            (5, 1049, 321, 74, 78), (6, 1162, 321, 70, 78), (7, 1273, 321, 70, 78), (8, 1384, 321, 70, 78),
            (9, 1493, 321, 72, 78), (10, 1604, 321, 72, 78), (11, 1715, 321, 72, 78), (12, 1824, 321, 74, 78),
            (13, 1935, 321, 73, 78), (14, 2047, 321, 182, 78), (60, 2293, 320, 72, 78), (63, 2402, 320, 72, 78),
            (67, 2515, 320, 72, 78), (15, 608, 428, 128, 78), (16, 774, 428, 74, 78), (17, 884, 428, 74, 78),
            (18, 994, 428, 74, 78), (19, 1104, 428, 74, 78), (20, 1214, 428, 74, 78), (21, 1327, 428, 72, 78),
            (22, 1437, 428, 74, 78), (23, 1547, 428, 74, 78), (24, 1658, 428, 74, 78), (25, 1768, 428, 74, 78),
            (26, 1878, 428, 74, 78), (27, 1990, 428, 74, 78), (28, 2101, 428, 128, 78), (61, 2293, 427, 72, 80),
            (64, 2402, 427, 72, 80), (68, 2514, 427, 72, 78), (29, 608, 539, 156, 78), (30, 802, 539, 74, 78),
            (31, 912, 539, 74, 78), (32, 1023, 539, 73, 78), (33, 1134, 539, 72, 78), (34, 1243, 539, 74, 78),
            (35, 1356, 539, 71, 78), (36, 1465, 539, 72, 78), (37, 1575, 539, 74, 78), (38, 1685, 539, 74, 78),
            (39, 1796, 539, 74, 78), (40, 1907, 539, 74, 78), (41, 2016, 539, 213, 78), (42, 609, 648, 213, 78),
            (43, 858, 648, 72, 78), (44, 968, 648, 74, 78), (45, 1080, 648, 72, 78), (46, 1190, 648, 72, 78),
            (47, 1301, 648, 72, 78), (48, 1411, 648, 72, 78), (49, 1522, 648, 72, 78), (50, 1632, 648, 72, 78),
            (51, 1742, 648, 72, 78), (52, 1853, 648, 72, 78), (53, 1965, 645, 266, 78), (65, 2403, 647, 72, 78),
            (54, 608, 758, 102, 78), (103, 747, 758, 102, 78), (56, 885, 758, 102, 78), (57, 1023, 756, 652, 78),
            (58, 1715, 757, 102, 78), (104, 1853, 757, 102, 78), (55, 1992, 757, 102, 78), (59, 2129, 756, 102, 78),
            (62, 2293, 757, 72, 78), (66, 2403, 757, 72, 78), (69, 2514, 757, 72, 78),
        ];
        return boxes.Select(b => new KeyPosition(ById[b.Id].HidUsage, ById[b.Id].Name, b.X + b.W / 2.0, b.Y + b.H / 2.0)).ToArray();
    }
}
