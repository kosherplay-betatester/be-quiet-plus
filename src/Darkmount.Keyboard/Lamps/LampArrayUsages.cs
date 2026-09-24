namespace Darkmount.Keyboard.Lamps;

/// <summary>HID Usage Tables 1.4, Lighting And Illumination page (0x59) — the HID LampArray usages.</summary>
public static class LampArrayUsages
{
    public const ushort Page = 0x59;

    public const ushort LampArray = 0x01;                       // CA
    public const ushort LampArrayAttributesReport = 0x02;       // CL
    public const ushort LampCount = 0x03;
    public const ushort BoundingBoxWidthInMicrometers = 0x04;
    public const ushort BoundingBoxHeightInMicrometers = 0x05;
    public const ushort BoundingBoxDepthInMicrometers = 0x06;
    public const ushort LampArrayKindUsage = 0x07;       // LampArrayKind (named apart from the enum)
    public const ushort MinUpdateIntervalInMicroseconds = 0x08;

    public const ushort LampAttributesRequestReport = 0x20;     // CL
    public const ushort LampId = 0x21;
    public const ushort LampAttributesResponseReport = 0x22;    // CL
    public const ushort PositionXInMicrometers = 0x23;
    public const ushort PositionYInMicrometers = 0x24;
    public const ushort PositionZInMicrometers = 0x25;
    public const ushort LampPurposesUsage = 0x26;        // LampPurposes (named apart from the enum)
    public const ushort UpdateLatencyInMicroseconds = 0x27;
    public const ushort RedLevelCount = 0x28;
    public const ushort GreenLevelCount = 0x29;
    public const ushort BlueLevelCount = 0x2A;
    public const ushort IntensityLevelCount = 0x2B;
    public const ushort IsProgrammable = 0x2C;
    public const ushort InputBinding = 0x2D;

    public const ushort LampMultiUpdateReport = 0x50;           // CL
    public const ushort RedUpdateChannel = 0x51;
    public const ushort GreenUpdateChannel = 0x52;
    public const ushort BlueUpdateChannel = 0x53;
    public const ushort IntensityUpdateChannel = 0x54;
    public const ushort LampUpdateFlags = 0x55;

    public const ushort LampRangeUpdateReport = 0x60;           // CL
    public const ushort LampIdStart = 0x61;
    public const ushort LampIdEnd = 0x62;

    public const ushort LampArrayControlReport = 0x70;          // CL
    public const ushort AutonomousMode = 0x71;

    /// <summary>LampUpdateFlags bit 0: the device applies all colours buffered since the last complete update.</summary>
    public const int LampUpdateComplete = 0x01;
}

/// <summary>LampArrayKind values (HUT 1.4 §0x59, Windows.Devices.Lights.LampArrayKind).</summary>
public enum LampArrayKind
{
    Undefined = 0, Keyboard = 1, Mouse = 2, GameController = 3, Peripheral = 4, Scene = 5,
    Notification = 6, Chassis = 7, Wearable = 8, Furniture = 9, Art = 10, Headset = 11,
}

[Flags]
public enum LampPurposes
{
    None = 0, Control = 0x01, Accent = 0x02, Branding = 0x04, Status = 0x08, Illumination = 0x10, Presentation = 0x20,
}

/// <summary>Colour for one lamp. <see cref="I"/> is the intensity channel; devices without one get RGB scaled by I/255.</summary>
public readonly record struct LampColor(byte R, byte G, byte B, byte I = 255)
{
    public static LampColor Off => new(0, 0, 0, 0);

    public static implicit operator LampColor((byte R, byte G, byte B, byte I) c) => new(c.R, c.G, c.B, c.I);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}/{I}";
}

/// <summary>Decoded LampArrayAttributesReport. Lengths in micrometres, interval in microseconds.</summary>
public sealed record LampArrayAttributes(
    int LampCount, int BoundingBoxWidth, int BoundingBoxHeight, int BoundingBoxDepth,
    LampArrayKind Kind, int MinUpdateIntervalMicroseconds);

/// <summary>
/// Decoded LampAttributesResponseReport. Positions in micrometres from the bounding-box origin.
/// <see cref="IntensityLevelCount"/> is null when the device has no intensity field. <see cref="InputBinding"/> is 0 when
/// the lamp has no binding — the HID spec says it is a Keyboard-page usage, but see <see cref="LampBindingKind"/>.
/// </summary>
public sealed record LampInfo(
    int Id, int PositionX, int PositionY, int PositionZ, int UpdateLatencyMicroseconds, LampPurposes Purposes,
    int RedLevelCount, int GreenLevelCount, int BlueLevelCount, int? IntensityLevelCount,
    bool IsProgrammable, int InputBinding);
