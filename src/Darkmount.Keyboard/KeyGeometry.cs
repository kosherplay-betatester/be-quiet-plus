namespace Darkmount.Keyboard;

/// <summary>Where the numpad module is attached (NUMPAD/MEDIA_DOCK state "position": 1 Left, 2 Right).</summary>
public enum NumpadSide : byte { None = 0, Left = 1, Right = 2 }

/// <summary>A key's bounding box in layout units (1u key ≈ 72 × 78, pitch ≈ 110).</summary>
public readonly record struct KeyRect(byte KeyId, int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// Key positions for a per-key editor, from the web app's Dark Mount SVG overlay (docs/QLINK_KEYBOARD.md §8).
/// Coordinates are already translated like the SVG (numpad Left: as-is; numpad Right or absent: x − 540), so they
/// fit the documented canvas. JIS boards use the ANSI geometry. The dock grid is not in the SVG (see <see cref="Layout"/>).
/// </summary>
public static class KeyGeometry
{
    public const int CanvasHeight = 886;
    const int CanvasWidthWithNumpad = 2649, CanvasWidthWithoutNumpad = 2108, RightShift = 540;
    const int DockButtonSize = 84, DockPitchX = 102, DockPitchY = 118, DockTop = 60;

    // (id, x, y, w, h) of the main block, ANSI.
    static readonly (byte Id, int X, int Y, int W, int H)[] Main =
    [
        (87, 607, 188, 72, 78), (88, 830, 188, 72, 78), (89, 941, 188, 70, 78), (90, 1051, 188, 72, 78),
        (91, 1162, 188, 70, 78), (92, 1328, 188, 72, 78), (93, 1438, 188, 72, 78), (94, 1549, 187, 72, 78),
        (95, 1660, 187, 72, 78), (96, 1825, 187, 70, 78), (97, 1933, 187, 74, 78), (98, 2048, 187, 72, 78),
        (99, 2157, 187, 72, 78), (100, 2292, 186, 72, 78), (101, 2403, 186, 70, 78), (102, 2514, 186, 72, 78),
        (1, 607, 321, 72, 78), (2, 718, 321, 72, 78), (3, 829, 321, 70, 78), (4, 940, 321, 70, 78),
        (5, 1049, 321, 74, 78), (6, 1162, 321, 70, 78), (7, 1273, 321, 70, 78), (8, 1384, 321, 70, 78),
        (9, 1493, 321, 72, 78), (10, 1604, 321, 72, 78), (11, 1715, 321, 72, 78), (12, 1824, 321, 74, 78),
        (13, 1935, 321, 73, 78), (14, 2047, 321, 182, 78), (60, 2293, 320, 72, 78), (63, 2402, 320, 72, 78),
        (67, 2515, 320, 72, 78),
        (15, 608, 428, 128, 78), (16, 774, 428, 74, 78), (17, 884, 428, 74, 78), (18, 994, 428, 74, 78),
        (19, 1104, 428, 74, 78), (20, 1214, 428, 74, 78), (21, 1327, 428, 72, 78), (22, 1437, 428, 74, 78),
        (23, 1547, 428, 74, 78), (24, 1658, 428, 74, 78), (25, 1768, 428, 74, 78), (26, 1878, 428, 74, 78),
        (27, 1990, 428, 74, 78), (28, 2101, 428, 128, 78), (61, 2293, 427, 72, 80), (64, 2402, 427, 72, 80),
        (68, 2514, 427, 72, 78),
        (29, 608, 539, 156, 78), (30, 802, 539, 74, 78), (31, 912, 539, 74, 78), (32, 1023, 539, 73, 78),
        (33, 1134, 539, 72, 78), (34, 1243, 539, 74, 78), (35, 1356, 539, 71, 78), (36, 1465, 539, 72, 78),
        (37, 1575, 539, 74, 78), (38, 1685, 539, 74, 78), (39, 1796, 539, 74, 78), (40, 1907, 539, 74, 78),
        (41, 2016, 539, 213, 78),
        (42, 609, 648, 213, 78), (43, 858, 648, 72, 78), (44, 968, 648, 74, 78), (45, 1080, 648, 72, 78),
        (46, 1190, 648, 72, 78), (47, 1301, 648, 72, 78), (48, 1411, 648, 72, 78), (49, 1522, 648, 72, 78),
        (50, 1632, 648, 72, 78), (51, 1742, 648, 72, 78), (52, 1853, 648, 72, 78), (53, 1965, 645, 266, 78),
        (65, 2403, 647, 72, 78),
        (54, 608, 758, 102, 78), (103, 747, 758, 102, 78), (56, 885, 758, 102, 78), (57, 1023, 756, 652, 78),
        (58, 1715, 757, 102, 78), (104, 1853, 757, 102, 78), (55, 1992, 757, 102, 78), (59, 2129, 756, 102, 78),
        (62, 2293, 757, 72, 78), (66, 2403, 757, 72, 78), (69, 2514, 757, 72, 78),
    ];

    // ISO differences (Enter 2 rows, "#" on the home row, short Left Shift, extra key).
    static readonly (byte Id, int X, int Y, int W, int H)[] IsoChanges =
        [(41, 2102, 430, 126, 190), (28, 2018, 540, 74, 78), (42, 607, 647, 102, 78), (105, 747, 648, 72, 78)];

    // (id, x with numpad Left, x with numpad Right before the −540 shift, y, w, h): display keys then keypad.
    static readonly (byte Id, int XLeft, int XRight, int Y, int W, int H)[] Numpad =
    [
        (109, 76, 2724, 60, 84, 84), (110, 178, 2827, 60, 84, 84), (111, 280, 2930, 60, 84, 84), (112, 382, 3032, 60, 84, 84),
        (113, 76, 2724, 178, 84, 84), (114, 178, 2827, 178, 84, 84), (115, 280, 2930, 178, 84, 84), (116, 382, 3032, 178, 84, 84),
        (70, 69, 2718, 321, 72, 78), (74, 179, 2828, 321, 75, 78), (79, 292, 2941, 321, 72, 78), (84, 403, 3051, 321, 72, 78),
        (71, 69, 2718, 429, 72, 80), (75, 179, 2828, 429, 74, 80), (80, 291, 2940, 429, 74, 80), (85, 403, 3051, 429, 72, 191),
        (72, 69, 2718, 538, 72, 82), (76, 180, 2829, 538, 72, 82), (81, 292, 2940, 540, 73, 80),
        (73, 69, 2718, 649, 72, 78), (77, 179, 2828, 649, 74, 78), (82, 292, 2941, 649, 72, 78), (86, 401, 3051, 649, 72, 188),
        (78, 70, 2718, 759, 182, 78), (83, 292, 2941, 759, 72, 78),
    ];

    /// <summary>Canvas size of the web app's drawing (2649 × 886 with the numpad, 2108 × 886 without).</summary>
    public static (int Width, int Height) CanvasSize(NumpadSide numpad) =>
        (numpad == NumpadSide.None ? CanvasWidthWithoutNumpad : CanvasWidthWithNumpad, CanvasHeight);

    /// <summary>Keyboard, numpad and display keys (no dock buttons).</summary>
    public static IReadOnlyList<KeyRect> Keys(PhysicalLayout layout = PhysicalLayout.Ansi, NumpadSide numpad = NumpadSide.Right)
    {
        int shift = numpad == NumpadSide.Left ? 0 : -RightShift;
        var main = Main.ToDictionary(k => k.Id);
        if (layout == PhysicalLayout.Iso)
            foreach (var k in IsoChanges) main[k.Id] = k;

        var keys = main.Values.Select(k => new KeyRect(k.Id, k.X + shift, k.Y, k.W, k.H)).ToList();
        if (numpad != NumpadSide.None)
            keys.AddRange(Numpad.Select(k =>
                new KeyRect(k.Id, (numpad == NumpadSide.Left ? k.XLeft : k.XRight) + shift, k.Y, k.W, k.H)));
        return keys;
    }

    /// <summary>The media dock's four buttons as a 2 × 2 grid at (x, y): Mute, Play/Pause / Previous, Next.</summary>
    public static IReadOnlyList<KeyRect> DockButtons(int x, int y) =>
    [
        new(KeyIds.DockMute, x, y, DockButtonSize, DockButtonSize),
        new(KeyIds.DockPlayPause, x + DockPitchX, y, DockButtonSize, DockButtonSize),
        new(KeyIds.DockPrevious, x, y + DockPitchY, DockButtonSize, DockButtonSize),
        new(KeyIds.DockNext, x + DockPitchX, y + DockPitchY, DockButtonSize, DockButtonSize),
    ];

    /// <summary>
    /// <see cref="Keys"/> plus, optionally, the dock buttons. The web app draws the dock grid outside the SVG,
    /// so its place here is a UI choice: right of the canvas, level with the display keys.
    /// </summary>
    public static IReadOnlyList<KeyRect> Layout(PhysicalLayout layout = PhysicalLayout.Ansi,
        NumpadSide numpad = NumpadSide.Right, bool includeDock = true)
    {
        var keys = Keys(layout, numpad);
        return includeDock ? [.. keys, .. DockButtons(CanvasSize(numpad).Width, DockTop)] : keys;
    }

    /// <summary>Smallest size (from the origin) that contains every rect.</summary>
    public static (int Width, int Height) BoundingSize(IEnumerable<KeyRect> keys)
    {
        int w = 0, h = 0;
        foreach (var k in keys)
        {
            w = Math.Max(w, k.Right);
            h = Math.Max(h, k.Bottom);
        }
        return (w, h);
    }

    /// <summary>The key under a point, or null.</summary>
    public static KeyRect? HitTest(IEnumerable<KeyRect> keys, double x, double y)
    {
        foreach (var k in keys)
            if (k.Contains(x, y)) return k;
        return null;
    }
}
