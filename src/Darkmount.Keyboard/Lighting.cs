using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.QLink;

namespace Darkmount.Keyboard;

/// <summary>LIGHTINGS lighting mode (docs/QLINK_KEYBOARD.md §2.2). The web app only uses Off and General.</summary>
public enum LightingMode : byte { Off = 0, General = 1, Custom = 2, Realtime = 3 }

/// <summary>On-board effects (§2.4). The Dark Mount UI offers Static…Matrix; Off and Ripple are K3 effects.</summary>
public enum Effect : byte { Static = 0, ColorWave = 1, Tornado = 2, Breathing = 3, Reactive = 4, Matrix = 5, Off = 6, Ripple = 7 }

public enum Direction : byte
{
    Up = 0, Down = 1, Left = 2, Right = 3, Clockwise = 4, CounterClockwise = 5, Omnidirectional = 6, Horizontal = 7,
    Vertical = 8, Cross = 9,
}

public enum ColorMode : byte { Single = 0, Dual = 1, Gradient = 2, OrientedGradient = 3 }

public enum GradientType : byte { Linear = 0, Radial = 1, Conic = 2 }

/// <summary>An RGB colour; JSON and <see cref="ToString"/> use "RRGGBB".</summary>
[JsonConverter(typeof(RgbJsonConverter))]
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Parses "RRGGBB" or "#RRGGBB".</summary>
    public static Rgb Parse(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6) throw new FormatException($"'{hex}' is not an RRGGBB colour");
        var b = Convert.FromHexString(s);
        return new(b[0], b[1], b[2]);
    }

    public override string ToString() => $"{R:X2}{G:X2}{B:X2}";
}

sealed class RgbJsonConverter : JsonConverter<Rgb>
{
    public override Rgb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Rgb.Parse(reader.GetString() ?? throw new JsonException("Colour expected"));

    public override void Write(Utf8JsonWriter writer, Rgb value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>A colour at a gradient position (0..100 %). Single/dual colours ignore the position on the wire.</summary>
public readonly record struct GradientStop(Rgb Color, int Position);

/// <summary>
/// One lighting layer's configuration (SetLayerConfig / GetLayerConfig payload, §2.3). Colours: Single = 1,
/// Dual = 2, Gradient/OrientedGradient = 2..7 stops. Brightness, speed and positions are clamped to 0..100 on encode.
/// </summary>
public sealed record LayerConfig(Effect Effect, Direction Direction, int Brightness, int Speed, ColorMode ColorMode,
    IReadOnlyList<GradientStop> Colors)
{
    /// <summary>Only used by <see cref="ColorMode.OrientedGradient"/>.</summary>
    public GradientType GradientType { get; init; }

    /// <summary>Degrees 0..359, only used by <see cref="ColorMode.OrientedGradient"/>.</summary>
    public int Angle { get; init; }

    /// <summary>Ripple extras, only sent for <see cref="Effect.Ripple"/>.</summary>
    public int RippleWidth { get; init; } = 5;
    public int RippleRange { get; init; } = 7;
    public int RippleFadeOut { get; init; }

    /// <summary>SetLayerConfig data: [layerId][effect][direction][brightness][speed][colorMode][colours…][ripple tail].</summary>
    public byte[] Encode(byte layerId)
    {
        var b = new List<byte>(48) { layerId, (byte)Effect, (byte)Direction, Pct(Brightness), Pct(Speed), (byte)ColorMode };
        switch (ColorMode)
        {
            case ColorMode.Single:
                RequireColors(1, 1);
                AddRgb(b, Colors[0].Color);
                break;
            case ColorMode.Dual:
                RequireColors(2, 2);
                AddRgb(b, Colors[0].Color);
                AddRgb(b, Colors[1].Color);
                break;
            case ColorMode.Gradient:
                RequireColors(LightingEffects.MinGradientStops, LightingEffects.MaxGradientStops);
                AddStops(b);
                break;
            case ColorMode.OrientedGradient:
                RequireColors(LightingEffects.MinGradientStops, LightingEffects.MaxGradientStops);
                b.Add((byte)GradientType);
                ushort angle = (ushort)(((Angle % 360) + 360) % 360);
                b.Add((byte)angle);
                b.Add((byte)(angle >> 8));
                AddStops(b);
                break;
            default:
                throw new ArgumentException($"Unknown colour mode {ColorMode}");
        }
        if (Effect == Effect.Ripple) b.AddRange([Byte(RippleWidth), Byte(RippleRange), Byte(RippleFadeOut)]);
        return [.. b];
    }

    /// <summary>
    /// Decodes a GetLayerConfig reply (no layer id). Effect values above 100 mean Off (web rule); a short colour
    /// block yields only the complete colours. Throws <see cref="FormatException"/> for replies under 5 bytes.
    /// </summary>
    public static LayerConfig Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 5) throw new FormatException($"LayerConfig needs at least 5 bytes, got {d.Length}");
        var effect = d[0] > 100 ? Effect.Off : (Effect)d[0];
        var mode = (ColorMode)d[4];
        var colors = new List<GradientStop>();
        var type = GradientType.Linear;
        int angle = 0, p = 5;
        switch (mode)
        {
            case ColorMode.Single:
            case ColorMode.Dual:
                for (int i = 0; i < (mode == ColorMode.Single ? 1 : 2) && p + 3 <= d.Length; i++, p += 3)
                    colors.Add(new(new Rgb(d[p], d[p + 1], d[p + 2]), i * 100));
                break;
            case ColorMode.Gradient:
                p = ReadStops(d, p, colors);
                break;
            case ColorMode.OrientedGradient when d.Length >= p + 3:
                type = (GradientType)d[p];
                angle = BinaryPrimitives.ReadUInt16LittleEndian(d[(p + 1)..]);
                p = ReadStops(d, p + 3, colors);
                break;
        }
        var cfg = new LayerConfig(effect, (Direction)d[1], d[2], d[3], mode, colors) { GradientType = type, Angle = angle };
        if (effect == Effect.Ripple && d.Length >= p + 3)
            cfg = cfg with { RippleWidth = d[^3], RippleRange = d[^2], RippleFadeOut = d[^1] };
        return cfg;
    }

    static int ReadStops(ReadOnlySpan<byte> d, int p, List<GradientStop> into)
    {
        if (p >= d.Length) return p;
        int n = d[p++];
        for (int i = 0; i < n && p + 4 <= d.Length; i++, p += 4)
            into.Add(new(new Rgb(d[p], d[p + 1], d[p + 2]), d[p + 3]));
        return p;
    }

    void RequireColors(int min, int max)
    {
        if (Colors.Count < min || Colors.Count > max)
            throw new ArgumentException(min == max
                ? $"{ColorMode} needs exactly {min} colour(s), got {Colors.Count}"
                : $"{ColorMode} needs {min}–{max} colours, got {Colors.Count}");
    }

    void AddStops(List<byte> b)
    {
        b.Add((byte)Colors.Count);
        foreach (var s in Colors)
        {
            AddRgb(b, s.Color);
            b.Add(Pct(s.Position));
        }
    }

    static void AddRgb(List<byte> b, Rgb c) => b.AddRange([c.R, c.G, c.B]);
    static byte Pct(int v) => (byte)Math.Clamp(v, 0, 100);
    static byte Byte(int v) => (byte)Math.Clamp(v, 0, 255);

    public bool Equals(LayerConfig? other) =>
        other is not null && Effect == other.Effect && Direction == other.Direction && Brightness == other.Brightness
        && Speed == other.Speed && ColorMode == other.ColorMode && Colors.SequenceEqual(other.Colors)
        && GradientType == other.GradientType && Angle == other.Angle && RippleWidth == other.RippleWidth
        && RippleRange == other.RippleRange && RippleFadeOut == other.RippleFadeOut;

    public override int GetHashCode() =>
        HashCode.Combine(Effect, Direction, Brightness, Speed, ColorMode, Colors.Count, GradientType, Angle);
}

/// <summary>What the UI offers for one effect: colour modes, direction buttons, speed slider and defaults (§2.4).</summary>
public sealed record EffectInfo(Effect Effect, string Name, IReadOnlyList<ColorMode> ColorModes,
    IReadOnlyList<Direction> Directions, bool HasSpeed, LayerConfig Default,
    IReadOnlyDictionary<ColorMode, IReadOnlyList<GradientStop>> DefaultColors)
{
    public bool HasDirection => Directions.Count > 0;
}

/// <summary>Effect metadata and defaults for the Dark Mount, taken from the IO Center web app (§2.4).</summary>
public static class LightingEffects
{
    public const int MinBrightness = 10, MaxBrightness = 100, SliderStep = 10;
    public const int MinGradientStops = 2, MaxGradientStops = 7;

    /// <summary>be quiet! orange FF2800, the Dark Mount's default colour.</summary>
    public static Rgb Orange { get; } = new(0xFF, 0x28, 0x00);

    /// <summary>Default gradient: red, yellow, green, cyan, blue, magenta, red (wire positions).</summary>
    public static IReadOnlyList<GradientStop> Rainbow { get; } =
    [
        new(new(0xFF, 0, 0), 0), new(new(0xFF, 0xFF, 0), 17), new(new(0, 0xFF, 0), 33), new(new(0, 0xFF, 0xFF), 50),
        new(new(0, 0, 0xFF), 67), new(new(0xFF, 0, 0xFF), 83), new(new(0xFF, 0, 0), 100),
    ];

    static readonly IReadOnlyList<GradientStop> OrangeOnly = [new(Orange, 0)];
    static readonly IReadOnlyList<GradientStop> OrangeWhite = [new(Orange, 0), new(new(0xFF, 0xFF, 0xFF), 100)];
    static readonly IReadOnlyList<GradientStop> MatrixDual = [new(new(0, 0xFF, 0), 0), new(new(0, 0, 0), 100)];
    static readonly IReadOnlyList<GradientStop> MatrixGradient =
        [new(new(0x0D, 0x02, 0x08), 0), new(new(0, 0x3B, 0), 33), new(new(0, 0x8F, 0x11), 67), new(new(0, 0xFF, 0x41), 100)];

    /// <summary>The six effects the Dark Mount offers, in UI order.</summary>
    public static IReadOnlyList<EffectInfo> DarkMount { get; } =
    [
        Info(Effect.Static, "Static", [ColorMode.Single], [], false, ColorMode.Single, Direction.Up),
        Info(Effect.ColorWave, "Color wave", [ColorMode.Single, ColorMode.Dual, ColorMode.Gradient],
            [Direction.Left, Direction.Up, Direction.Down, Direction.Right], true, ColorMode.Single, Direction.Right),
        Info(Effect.Tornado, "Tornado", [ColorMode.Single, ColorMode.Dual, ColorMode.Gradient],
            [Direction.Clockwise, Direction.CounterClockwise], true, ColorMode.Gradient, Direction.Clockwise),
        Info(Effect.Breathing, "Breathing", [ColorMode.Single, ColorMode.Dual, ColorMode.Gradient], [], true,
            ColorMode.Gradient, Direction.Up),
        Info(Effect.Reactive, "Reactive", [ColorMode.Single, ColorMode.Dual, ColorMode.Gradient], [], true,
            ColorMode.Dual, Direction.Up),
        Info(Effect.Matrix, "Matrix", [ColorMode.Dual, ColorMode.Gradient],
            [Direction.Left, Direction.Up, Direction.Down, Direction.Right], true, ColorMode.Gradient, Direction.Down,
            new Dictionary<ColorMode, IReadOnlyList<GradientStop>> { [ColorMode.Dual] = MatrixDual, [ColorMode.Gradient] = MatrixGradient }),
    ];

    /// <summary>The keyboard's factory lighting: Static orange, direction Right, speed 50, brightness 50.</summary>
    public static LayerConfig FactoryDefault { get; } = new(Effect.Static, Direction.Right, 50, 50, ColorMode.Single, OrangeOnly);

    /// <summary>Metadata for an effect the Dark Mount offers, or null (Off, Ripple, unknown).</summary>
    public static EffectInfo? Find(Effect effect) => DarkMount.FirstOrDefault(e => e.Effect == effect);

    static EffectInfo Info(Effect effect, string name, ColorMode[] modes, Direction[] directions, bool speed,
        ColorMode defaultMode, Direction defaultDirection, Dictionary<ColorMode, IReadOnlyList<GradientStop>>? colors = null)
    {
        colors ??= modes.ToDictionary(m => m, m => m switch
        {
            ColorMode.Single => OrangeOnly,
            ColorMode.Dual => OrangeWhite,
            _ => Rainbow,
        });
        var def = new LayerConfig(effect, defaultDirection, 100, 50, defaultMode, colors[defaultMode]);
        return new(effect, name, modes, directions, speed, def, colors);
    }
}

/// <summary>
/// LIGHTINGS feature (16) for the Dark Mount's single layer 0 ("TOP"). SetMode/SetLayerConfig are persistent
/// writes; call them only on a user action. Calibration and the desktop Custom/Realtime commands are not offered.
/// </summary>
public sealed class Lighting(QLinkClient q)
{
    /// <summary>The Dark Mount's only lighting layer.</summary>
    public const byte TopLayer = 0;

    public LightingMode GetMode()
    {
        var d = q.Send(Features.Lightings, LightingCommands.GetLightingMode);
        return d.Length > 0 ? (LightingMode)d[0] : throw new QLinkException(QLinkStatus.InvalidSize, "Empty GetLightingMode reply");
    }

    /// <summary>Off switches the LEDs off; General runs the on-board effect from the layer config.</summary>
    public void SetMode(LightingMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lighting mode");
        q.Send(Features.Lightings, LightingCommands.SetLightingMode, [(byte)mode]);
    }

    public LayerConfig GetLayerConfig(byte layerId = TopLayer) =>
        LayerConfig.Decode(q.Send(Features.Lightings, LightingCommands.GetLayerConfig, [layerId]));

    /// <summary>Validates and writes a layer config (throws <see cref="ArgumentException"/> before sending if invalid).</summary>
    public void SetLayerConfig(byte layerId, LayerConfig config) =>
        q.Send(Features.Lightings, LightingCommands.SetLayerConfig, config.Encode(layerId));

    /// <summary>Layer ids the firmware reports as global (expected [0] on the Dark Mount).</summary>
    public IReadOnlyList<byte> GetGlobalLayers() => IdList(q.Send(Features.Lightings, LightingCommands.GetGlobalLayers));

    /// <summary>Desktop "Custom" layer order ([count][ids…]); read-only here.</summary>
    public IReadOnlyList<byte> GetLayersLayout() => IdList(q.Send(Features.Lightings, LightingCommands.GetLayersLayout));

    static byte[] IdList(byte[] d) => d.Length == 0 ? [] : d.AsSpan(1, Math.Min(d[0], d.Length - 1)).ToArray();
}
