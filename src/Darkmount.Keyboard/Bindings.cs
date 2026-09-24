using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.QLink;

namespace Darkmount.Keyboard;

/// <summary>Binding layer; the value is the wire layer byte (docs/QLINK_KEYBOARD.md §3.2).</summary>
public enum Layer : byte { Common = 0x00, Fn = 0x80 }

/// <summary>Wire binding types (§3.3). Type 0 means "key disabled"; restoring the default is ClearBinding.</summary>
public enum BindingType : byte
{
    Disabled = 0, StandardKey = 1, Media = 2, Mouse = 3, OpenFolder = 4, OpenFile = 5, OpenBrowser = 6,
    WindowsShortcut = 7, Profile = 8, Backlight = 9, Macro = 10, AltCode = 11,
}

/// <summary>Standard HID modifier byte.</summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0, LeftCtrl = 0x01, LeftShift = 0x02, LeftAlt = 0x04, LeftWin = 0x08,
    RightCtrl = 0x10, RightShift = 0x20, RightAlt = 0x40, RightWin = 0x80,
}

public enum MediaAction : byte
{
    None = 0, VolumeUp = 1, VolumeDown = 2, Mute = 3, MicMute = 4, NextTrack = 5, PrevTrack = 6, PlayPause = 7,
    Stop = 8, VolumeMixer = 9, SpecificSound = 10,
}

public enum MouseButtonKind : byte { Left = 1, Right = 2, Middle = 3, Forward = 4, Backward = 5 }

public enum MouseScrollDirection : byte { Up = 1, Down = 2, Left = 3, Right = 4 }

public enum WindowsShortcutAction : byte
{
    None = 0, FileExplorer = 1, Calculator = 2, TaskManager = 3, LockPC = 4, ShutDownPC = 5, SleepPC = 6,
    HibernatePC = 7, SnippingTool = 8, Notepad = 9, Paint = 10, AirplaneMode = 11, XboxGameBar = 12,
    SystemSettings = 13, Refresh = 14, TabbingApp = 15, CloseApp = 16, Copy = 17, Paste = 18, Cut = 19,
    InternetBrowser = 20, EmailReader = 21,
}

public enum BacklightAction : byte
{
    None = 0, SelectEffect = 1, NextEffect = 2, PrevEffect = 3, IncreaseBrightness = 4, DecreaseBrightness = 5,
}

/// <summary>
/// What a key does. <see cref="IsReadOnly"/> marks desktop-app actions this app can keep and write back
/// unchanged but should not offer for editing (profile, macro, specific sound, unknown payloads).
/// </summary>
public abstract record BindingAction
{
    public abstract BindingType Type { get; }
    public virtual bool IsReadOnly => false;

    /// <summary>The key sends nothing (wire type 0).</summary>
    public sealed record Disabled : BindingAction
    {
        public override BindingType Type => BindingType.Disabled;
    }

    /// <summary>Modifiers + any HID keyboard usage 0x04–0xE7: <c>[mods][usage]</c>.</summary>
    public sealed record StandardKey(KeyModifiers Modifiers, byte Usage) : BindingAction
    {
        public override BindingType Type => BindingType.StandardKey;
    }

    /// <summary>F1–F24 (incl. F13–F24 for host-side triggers): <c>[0x00][usage]</c>.</summary>
    public sealed record FKey(byte Usage) : BindingAction
    {
        public override BindingType Type => BindingType.StandardKey;
        public int Number => HidUsage.FKeyNumber(Usage);
        public static FKey Of(int number) => new(HidUsage.FKey(number));
    }

    /// <summary><c>[action]</c>; SpecificSound read from the desktop app: <c>[0x0A][len][ASCII device id]</c>.</summary>
    public sealed record Media(MediaAction Action, string? AudioDeviceId = null) : BindingAction
    {
        public override BindingType Type => BindingType.Media;
        public override bool IsReadOnly => Action == MediaAction.SpecificSound;
    }

    /// <summary><c>[0x01][flags][autoFire]</c>; AutoFire = clicks/s 1–50, 0 = off.</summary>
    public sealed record MouseButton(MouseButtonKind Button, bool WhilePressed = false, bool DoubleClick = false,
        byte AutoFire = 0) : BindingAction
    {
        public const byte MaxAutoFire = 50;
        public override BindingType Type => BindingType.Mouse;
    }

    /// <summary><c>[0x02][direction − 1]</c>.</summary>
    public sealed record MouseScroll(MouseScrollDirection Direction) : BindingAction
    {
        public override BindingType Type => BindingType.Mouse;
    }

    /// <summary><c>[len][Latin-1 path]</c> (a desktop-app action).</summary>
    public sealed record OpenFolder(string Path) : BindingAction
    {
        public override BindingType Type => BindingType.OpenFolder;
    }

    /// <summary><c>[len][Latin-1 path]</c> ("Open file / start application", a desktop-app action).</summary>
    public sealed record OpenFile(string Path) : BindingAction
    {
        public override BindingType Type => BindingType.OpenFile;
    }

    /// <summary><c>[len][Latin-1 URL]</c> (factory default of display key 1).</summary>
    public sealed record OpenBrowser(string Url) : BindingAction
    {
        public override BindingType Type => BindingType.OpenBrowser;
    }

    public sealed record WindowsShortcut(WindowsShortcutAction Action) : BindingAction
    {
        public override BindingType Type => BindingType.WindowsShortcut;
    }

    /// <summary>Desktop profile switch, kept as raw payload (<c>[action]</c> or <c>[0x01][16-byte UUID]</c>).</summary>
    public sealed record Profile(byte[] Payload) : BindingAction
    {
        public override BindingType Type => BindingType.Profile;
        public override bool IsReadOnly => true;
        public bool Equals(Profile? other) => other is not null && Payload.AsSpan().SequenceEqual(other.Payload);
        public override int GetHashCode() => Payload.Length;
    }

    /// <summary><c>[action]</c>, or <c>[0x01][effect]</c> for SelectEffect ("on-the-fly lighting").</summary>
    public sealed record Backlight(BacklightAction Action, Effect? Effect = null) : BindingAction
    {
        public override BindingType Type => BindingType.Backlight;
    }

    /// <summary>Desktop macro trigger, kept as raw payload (<c>[action][macroId]</c>; meaning unknown).</summary>
    public sealed record Macro(byte[] Payload) : BindingAction
    {
        public override BindingType Type => BindingType.Macro;
        public override bool IsReadOnly => true;
        public bool Equals(Macro? other) => other is not null && Payload.AsSpan().SequenceEqual(other.Payload);
        public override int GetHashCode() => Payload.Length;
    }

    /// <summary>Types a Unicode character via Alt code ("Special" tab): <c>[0x02][code point u16 LE]</c>.</summary>
    public sealed record AltCode(ushort CodePoint) : BindingAction
    {
        public override BindingType Type => BindingType.AltCode;
        public char Char => (char)CodePoint;
    }

    /// <summary>A record this library does not understand, kept byte for byte (payload = bytes after the type).</summary>
    public sealed record Raw(byte TypeByte, byte[] Payload) : BindingAction
    {
        public override BindingType Type => (BindingType)TypeByte;
        public override bool IsReadOnly => true;
        public bool Equals(Raw? other) =>
            other is not null && TypeByte == other.TypeByte && Payload.AsSpan().SequenceEqual(other.Payload);
        public override int GetHashCode() => HashCode.Combine(TypeByte, Payload.Length);
    }
}

/// <summary>One key's binding on one layer. JSON form: the encoded record as a hex string.</summary>
[JsonConverter(typeof(KeyBindingJsonConverter))]
public sealed record KeyBinding(byte KeyId, Layer Layer, BindingAction Action)
{
    /// <summary>The SetBinding record <c>[keyId][layer][type][payload…]</c>; throws <see cref="ArgumentException"/> if invalid.</summary>
    public byte[] Encode() => BindingCodec.Encode(this);

    /// <summary>Decodes exactly one record (unknown or odd-length records become <see cref="BindingAction.Raw"/>).</summary>
    public static KeyBinding Decode(ReadOnlySpan<byte> record) => BindingCodec.Decode(record);
}

sealed class KeyBindingJsonConverter : JsonConverter<KeyBinding>
{
    public override KeyBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        KeyBinding.Decode(Convert.FromHexString(reader.GetString() ?? throw new JsonException("Binding hex expected")));

    public override void Write(Utf8JsonWriter writer, KeyBinding value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Convert.ToHexString(value.Encode()));
}

/// <summary>Binding record encoder/decoder (§3.3). Parsing never throws on garbled device data.</summary>
public static class BindingCodec
{
    public const int HeaderSize = 3;
    const int MaxString = 255;

    public static byte[] Encode(KeyBinding b)
    {
        if (b.Layer is not (Layer.Common or Layer.Fn)) throw new ArgumentException($"Unknown layer {b.Layer}");
        var r = new List<byte>(8) { b.KeyId, (byte)b.Layer, (byte)b.Action.Type };
        switch (b.Action)
        {
            case BindingAction.Disabled:
                break;
            case BindingAction.StandardKey k:
                if (!HidUsage.IsValid(k.Usage)) throw new ArgumentException($"HID usage 0x{k.Usage:X2} is outside 0x04–0xE7");
                r.AddRange([(byte)k.Modifiers, k.Usage]);
                break;
            case BindingAction.FKey f:
                if (!HidUsage.IsFKey(f.Usage)) throw new ArgumentException($"HID usage 0x{f.Usage:X2} is not F1–F24");
                r.AddRange([0, f.Usage]);
                break;
            case BindingAction.Media m:
                r.Add((byte)m.Action);
                if (m.Action == MediaAction.SpecificSound && m.AudioDeviceId is { } id) AddString(r, id);
                break;
            case BindingAction.MouseButton mb:
                if ((byte)mb.Button is < 1 or > 32) throw new ArgumentException($"Unknown mouse button {mb.Button}");
                if (mb.AutoFire > BindingAction.MouseButton.MaxAutoFire)
                    throw new ArgumentOutOfRangeException(nameof(b), mb.AutoFire, "Auto fire is 1–50 clicks/s (0 = off)");
                r.AddRange([1, (byte)(((byte)mb.Button - 1) | (mb.WhilePressed ? 0x40 : 0) | (mb.DoubleClick ? 0x80 : 0)), mb.AutoFire]);
                break;
            case BindingAction.MouseScroll s:
                if (s.Direction == 0) throw new ArgumentException("Scroll direction 0 is not valid");
                r.AddRange([2, (byte)(s.Direction - 1)]);
                break;
            case BindingAction.OpenFolder f:
                AddString(r, f.Path);
                break;
            case BindingAction.OpenFile f:
                AddString(r, f.Path);
                break;
            case BindingAction.OpenBrowser u:
                AddString(r, u.Url);
                break;
            case BindingAction.WindowsShortcut w:
                r.Add((byte)w.Action);
                break;
            case BindingAction.Profile p:
                if (p.Payload.Length == 0) throw new ArgumentException("Profile payload is empty");
                r.AddRange(p.Payload);
                break;
            case BindingAction.Backlight bl:
                r.Add((byte)bl.Action);
                if (bl.Action == BacklightAction.SelectEffect)
                    r.Add((byte)(bl.Effect ?? throw new ArgumentException("SelectEffect needs an effect")));
                break;
            case BindingAction.Macro m:
                if (m.Payload.Length == 0) throw new ArgumentException("Macro payload is empty");
                r.AddRange(m.Payload);
                break;
            case BindingAction.AltCode a:
                r.AddRange([2, (byte)a.CodePoint, (byte)(a.CodePoint >> 8)]);
                break;
            case BindingAction.Raw raw:
                r.AddRange(raw.Payload);
                break;
            default:
                throw new ArgumentException($"Unsupported binding action {b.Action.GetType().Name}");
        }
        return [.. r];
    }

    /// <summary>
    /// Length of the record starting at <paramref name="d"/>[0] (from the §3.3 length table), or −1 if the type or
    /// sub-type is unknown or the record is truncated.
    /// </summary>
    public static int RecordLength(ReadOnlySpan<byte> d)
    {
        if (d.Length < HeaderSize) return -1;
        int? sub = d.Length > 3 ? d[3] : null, next = d.Length > 4 ? d[4] : null;
        int len = (BindingType)d[2] switch
        {
            BindingType.Disabled => 3,
            BindingType.StandardKey => 5,
            BindingType.Media when sub == (int)MediaAction.SpecificSound => next is { } n ? 5 + n : -1,
            BindingType.Media => 4,
            BindingType.Mouse => sub switch { 1 => 6, 2 => 5, _ => -1 },
            BindingType.OpenFolder or BindingType.OpenFile or BindingType.OpenBrowser => sub is { } n ? 4 + n : -1,
            BindingType.WindowsShortcut => 4,
            BindingType.Profile => sub is null ? -1 : sub == 1 ? 20 : 4,
            BindingType.Backlight => sub is null ? -1 : sub == (int)BacklightAction.SelectEffect ? 5 : 4,
            BindingType.Macro => 5,
            BindingType.AltCode => 6,
            _ => -1,
        };
        return len > 0 && len <= d.Length ? len : -1;
    }

    /// <summary>Decodes exactly one record; if its length does not match the table, it is kept as Raw.</summary>
    public static KeyBinding Decode(ReadOnlySpan<byte> record)
    {
        if (record.Length < HeaderSize) throw new FormatException("A binding record has at least 3 bytes");
        return RecordLength(record) == record.Length ? DecodeKnown(record) : RawBinding(record);
    }

    /// <summary>
    /// Appends up to <paramref name="max"/> consecutive records from <paramref name="data"/> to <paramref name="into"/>.
    /// Stops at the end of the data or at the first record of unknown length. Returns how many were read.
    /// </summary>
    public static int ParseRecords(ReadOnlySpan<byte> data, int max, List<KeyBinding> into)
    {
        int n = 0, p = 0;
        while (n < max && p < data.Length)
        {
            int len = RecordLength(data[p..]);
            if (len < 0) break;
            into.Add(DecodeKnown(data.Slice(p, len)));
            p += len;
            n++;
        }
        return n;
    }

    static KeyBinding DecodeKnown(ReadOnlySpan<byte> r)
    {
        var p = r[HeaderSize..];
        BindingAction? a = (BindingType)r[2] switch
        {
            BindingType.Disabled => new BindingAction.Disabled(),
            BindingType.StandardKey when !HidUsage.IsValid(p[1]) => null,
            BindingType.StandardKey => p[0] == 0 && HidUsage.IsFKey(p[1])
                ? new BindingAction.FKey(p[1])
                : new BindingAction.StandardKey((KeyModifiers)p[0], p[1]),
            BindingType.Media => p[0] == (byte)MediaAction.SpecificSound
                ? new BindingAction.Media(MediaAction.SpecificSound, Latin1(p.Slice(2, p[1])))
                : new BindingAction.Media((MediaAction)p[0]),
            BindingType.Mouse when p[0] == 1 && ((p[1] & 0x20) != 0 || p[2] > BindingAction.MouseButton.MaxAutoFire) => null,
            BindingType.Mouse => p[0] == 1
                ? new BindingAction.MouseButton((MouseButtonKind)((p[1] & 0x1F) + 1), (p[1] & 0x40) != 0, (p[1] & 0x80) != 0, p[2])
                : new BindingAction.MouseScroll((MouseScrollDirection)(p[1] + 1)),
            BindingType.OpenFolder => new BindingAction.OpenFolder(Latin1(p.Slice(1, p[0]))),
            BindingType.OpenFile => new BindingAction.OpenFile(Latin1(p.Slice(1, p[0]))),
            BindingType.OpenBrowser => new BindingAction.OpenBrowser(Latin1(p.Slice(1, p[0]))),
            BindingType.WindowsShortcut => new BindingAction.WindowsShortcut((WindowsShortcutAction)p[0]),
            BindingType.Profile => new BindingAction.Profile(p.ToArray()),
            BindingType.Backlight => p[0] == (byte)BacklightAction.SelectEffect
                ? new BindingAction.Backlight(BacklightAction.SelectEffect, (Effect)p[1])
                : new BindingAction.Backlight((BacklightAction)p[0]),
            BindingType.Macro => new BindingAction.Macro(p.ToArray()),
            BindingType.AltCode when p[0] == 2 => new BindingAction.AltCode(BinaryPrimitives.ReadUInt16LittleEndian(p[1..])),
            _ => null,
        };
        return a is null ? RawBinding(r) : new KeyBinding(r[0], LayerOf(r[1]), a);
    }

    static KeyBinding RawBinding(ReadOnlySpan<byte> r) =>
        new(r[0], LayerOf(r[1]), new BindingAction.Raw(r[2], r[HeaderSize..].ToArray()));

    static Layer LayerOf(byte b) => (b & 0x80) != 0 ? Layer.Fn : Layer.Common;

    static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    static void AddString(List<byte> r, string s)
    {
        if (s.Length > MaxString) throw new ArgumentException($"Text is longer than {MaxString} characters");
        foreach (char c in s)
            if (c > 0xFF) throw new ArgumentException($"'{c}' cannot be sent (only ASCII/Latin-1 characters)");
        r.Add((byte)s.Length);
        r.AddRange(Encoding.Latin1.GetBytes(s));
    }
}

/// <summary>The Dark Mount's factory bindings (§3.4), for "reset to defaults": clear every custom entry, then set these.</summary>
public static class BindingDefaults
{
    public const string DisplayKey1Url = "https://www.bequiet.com/en";

    public static IReadOnlyList<KeyBinding> Factory { get; } = Build();

    public static IReadOnlyList<KeyBinding> For(Layer layer) => Factory.Where(b => b.Layer == layer).ToArray();

    static KeyBinding[] Build()
    {
        var dock = new (byte Key, MediaAction Action)[]
        {
            (KeyIds.DockPlayPause, MediaAction.PlayPause), (KeyIds.DockMute, MediaAction.Mute),
            (KeyIds.DockPrevious, MediaAction.PrevTrack), (KeyIds.DockNext, MediaAction.NextTrack),
        };
        WindowsShortcutAction[] displayKeys =
        [
            WindowsShortcutAction.InternetBrowser, WindowsShortcutAction.FileExplorer, WindowsShortcutAction.EmailReader,
            WindowsShortcutAction.SnippingTool, WindowsShortcutAction.TaskManager, WindowsShortcutAction.LockPC,
            WindowsShortcutAction.SleepPC,
        ];
        var list = new List<KeyBinding>();
        list.AddRange(dock.Select(d => new KeyBinding(d.Key, Layer.Common, new BindingAction.Media(d.Action))));
        list.Add(new(KeyIds.DisplayKey1, Layer.Common, new BindingAction.OpenBrowser(DisplayKey1Url)));
        list.AddRange(displayKeys.Select((a, i) =>
            new KeyBinding((byte)(KeyIds.DisplayKey1 + 1 + i), Layer.Common, new BindingAction.WindowsShortcut(a))));
        list.AddRange(dock.Select(d => new KeyBinding(d.Key, Layer.Fn, new BindingAction.Media(d.Action))));
        list.Add(new(KeyIds.Up, Layer.Fn, new BindingAction.Backlight(BacklightAction.IncreaseBrightness)));
        list.Add(new(KeyIds.Left, Layer.Fn, new BindingAction.Backlight(BacklightAction.PrevEffect)));
        list.Add(new(KeyIds.Down, Layer.Fn, new BindingAction.Backlight(BacklightAction.DecreaseBrightness)));
        list.Add(new(KeyIds.Right, Layer.Fn, new BindingAction.Backlight(BacklightAction.NextEffect)));
        return [.. list];
    }
}

/// <summary>
/// BINDINGS feature (17): key remapping on the Common and Fn layers. SetBinding/Clear/SetEnabled are persistent
/// writes; call them only on a user action. Keys the firmware locks (Fn; Fn-layer R and Pause) are refused.
/// </summary>
public sealed class Bindings(QLinkClient q)
{
    /// <summary>
    /// Reads every custom binding, paging with the start index while fewer than Total = a + b records were read.
    /// Stops early if a page yields no records (garbled or short data never throws).
    /// </summary>
    public IReadOnlyList<KeyBinding> GetAll()
    {
        var all = new List<KeyBinding>();
        int total = -1;
        while (true)
        {
            var d = q.Send(Features.Bindings, BindingCommands.GetBindings, [(byte)all.Count, (byte)(all.Count >> 8)]);
            if (d.Length < 2) break;
            if (total < 0) total = d[0] + d[1];
            if (all.Count >= total || BindingCodec.ParseRecords(d.AsSpan(2), total - all.Count, all) == 0
                || all.Count >= total) break;
        }
        return all;
    }

    /// <summary>Writes one binding (validated before anything is sent).</summary>
    public void SetBinding(KeyBinding binding)
    {
        EnsureRebindable(binding.KeyId, binding.Layer);
        q.Send(Features.Bindings, BindingCommands.SetBinding, binding.Encode());
    }

    /// <summary>Restores a key's default on one layer (ClearBinding).</summary>
    public void Clear(byte keyId, Layer layer)
    {
        EnsureRebindable(keyId, layer);
        q.Send(Features.Bindings, BindingCommands.ClearBinding, [keyId, (byte)layer]);
    }

    /// <summary>The master "key bindings on/off" switch.</summary>
    public bool GetEnabled()
    {
        var d = q.Send(Features.Bindings, BindingCommands.GetConfig);
        return d.Length > 0 ? d[0] != 0 : throw new QLinkException(QLinkStatus.InvalidSize, "Empty Bindings GetConfig reply");
    }

    public void SetEnabled(bool enabled) => q.Send(Features.Bindings, BindingCommands.SetConfig, [(byte)(enabled ? 1 : 0)]);

    static void EnsureRebindable(byte keyId, Layer layer)
    {
        var key = KeyIds.Find(keyId) ?? throw new ArgumentOutOfRangeException(nameof(keyId), keyId, "Not a Dark Mount key id");
        if (layer is not (Layer.Common or Layer.Fn)) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown layer");
        if (!KeyIds.IsRebindable(keyId, layer))
            throw new InvalidOperationException($"{key.Name} cannot be rebound on the {layer} layer");
    }
}
