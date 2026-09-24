using System.Text.Json;
using System.Text.Json.Serialization;
using Darkmount.QLink;

namespace Darkmount.Keyboard;

/// <summary>
/// Everything the app can change on the keyboard. Used for the one-time backup and for profiles: a null
/// member means "not part of this snapshot" and is left alone by <see cref="KeyboardBackup.Apply"/>.
/// </summary>
public sealed class KeyboardSnapshot
{
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public GameModeLocks? LockMask { get; set; }

    /// <summary>State byte as read (informational; Apply never toggles Game Mode). Null on firmware without GetState.</summary>
    public KeyboardStateFlags? GameModeState { get; set; }

    public LightingMode? LightingMode { get; set; }

    /// <summary>Layer 0 configuration.</summary>
    public LayerConfig? LayerConfig { get; set; }

    public bool? BindingsEnabled { get; set; }

    /// <summary>All custom bindings of both layers (JSON: one hex record per binding).</summary>
    public List<KeyBinding>? Bindings { get; set; }

    /// <summary>Stored display-key JPEGs (as on the device, rotated) by display-key index 0–7.</summary>
    public Dictionary<int, byte[]>? DisplayKeyJpegs { get; set; }
}

/// <summary>Reads, saves and re-applies complete keyboard snapshots.</summary>
public static class KeyboardBackup
{
    public const string FileName = "keyboard-backup.json";

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkmountHub", "keyboard-backup");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Reads every setting from the keyboard (read-only). Display-key images are slow to read (≈150 requests
    /// per key), so they are only included on request and only when the numpad is connected.
    /// </summary>
    public static KeyboardSnapshot Read(QLinkClient q, bool includeDisplayKeys)
    {
        var settings = new KeyboardSettings(q);
        var lighting = new Lighting(q);
        var bindings = new Bindings(q);
        var s = new KeyboardSnapshot
        {
            CreatedUtc = DateTime.UtcNow,
            LockMask = settings.GetLocks(),
            GameModeState = TryGetState(settings),
            LightingMode = lighting.GetMode(),
            LayerConfig = lighting.GetLayerConfig(Lighting.TopLayer),
            BindingsEnabled = bindings.GetEnabled(),
            Bindings = [.. bindings.GetAll()],
        };
        if (includeDisplayKeys) s.DisplayKeyJpegs = ReadDisplayKeys(new DisplayKeys(q));
        return s;
    }

    static KeyboardStateFlags? TryGetState(KeyboardSettings settings)
    {
        try { return settings.GetState(); }
        catch (QLinkException) { return null; } // keyboard MCU firmware < 1.2.0
    }

    static Dictionary<int, byte[]> ReadDisplayKeys(DisplayKeys keys)
    {
        var images = new Dictionary<int, byte[]>();
        if (!keys.IsConnected()) return images;
        for (int i = 0; i < DisplayKeys.Count; i++)
            if (keys.ReadStoredJpeg(i) is { } jpeg) images[i] = jpeg;
        return images;
    }

    public static string ToJson(KeyboardSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static KeyboardSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<KeyboardSnapshot>(json, Options) ?? throw new JsonException("Empty keyboard snapshot");

    /// <summary>Saves the snapshot unless a backup already exists (never overwrites). Returns true if written.</summary>
    public static bool SaveOnce(KeyboardSnapshot snapshot, string? folder = null)
    {
        folder ??= DefaultFolder;
        var path = Path.Combine(folder, FileName);
        if (File.Exists(path)) return false;
        Directory.CreateDirectory(folder);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(snapshot));
        try
        {
            File.Move(tmp, path, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(tmp);
            return false;
        }
    }

    /// <summary>The saved backup, or null if there is none.</summary>
    public static KeyboardSnapshot? Load(string? folder = null)
    {
        var path = Path.Combine(folder ?? DefaultFolder, FileName);
        return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// Writes only what differs between <paramref name="target"/> and <paramref name="current"/>, in this order:
    /// lock mask → lighting mode → layer config → bindings switch → bindings (clear entries missing from the
    /// target, then set new/changed ones) → display-key images. Null target members are skipped; locked keys are
    /// never written. If <paramref name="current"/> is null the device is read first (images excluded, so every
    /// target image is written). Everything is validated before the first write. Returns the number of writes.
    /// </summary>
    public static int Apply(QLinkClient q, KeyboardSnapshot target, KeyboardSnapshot? current)
    {
        var targetConfig = target.LayerConfig?.Encode(Lighting.TopLayer);
        var wanted = ByKey(target.Bindings ?? []);
        foreach (var b in wanted.Values) b.Encode();
        foreach (var index in target.DisplayKeyJpegs?.Keys ?? Enumerable.Empty<int>()) DisplayKeys.KeyId(index);

        current ??= Read(q, includeDisplayKeys: false);
        var settings = new KeyboardSettings(q);
        var lighting = new Lighting(q);
        var bindings = new Bindings(q);
        int writes = 0;

        if (target.LockMask is { } locks && locks != current.LockMask)
        {
            settings.SetLocks(locks);
            writes++;
        }
        if (target.LightingMode is { } mode && mode != current.LightingMode)
        {
            lighting.SetMode(mode);
            writes++;
        }
        if (target.LayerConfig is { } config && !Same(targetConfig!, TryEncode(current.LayerConfig)))
        {
            lighting.SetLayerConfig(Lighting.TopLayer, config);
            writes++;
        }
        if (target.BindingsEnabled is { } enabled && enabled != current.BindingsEnabled)
        {
            bindings.SetEnabled(enabled);
            writes++;
        }
        if (target.Bindings is not null)
        {
            var have = ByKey(current.Bindings ?? []);
            foreach (var (key, b) in have)
            {
                if (wanted.ContainsKey(key) || !KeyIds.IsRebindable(b.KeyId, b.Layer)) continue;
                bindings.Clear(b.KeyId, b.Layer);
                writes++;
            }
            foreach (var (key, b) in wanted)
            {
                if (!KeyIds.IsRebindable(b.KeyId, b.Layer)) continue;
                if (have.TryGetValue(key, out var old) && Same(b.Encode(), TryEncode(old))) continue;
                bindings.SetBinding(b);
                writes++;
            }
        }
        if (target.DisplayKeyJpegs is { Count: > 0 } images)
        {
            var keys = new DisplayKeys(q);
            foreach (var (index, jpeg) in images.OrderBy(kv => kv.Key))
            {
                if (current.DisplayKeyJpegs?.TryGetValue(index, out var old) == true && Same(jpeg, old)) continue;
                keys.WriteStoredJpeg(index, jpeg);
                writes++;
            }
        }
        return writes;
    }

    /// <summary>Last entry wins for duplicate (key, layer) pairs; iteration keeps first-seen order.</summary>
    static Dictionary<(byte, Layer), KeyBinding> ByKey(IEnumerable<KeyBinding> list)
    {
        var d = new Dictionary<(byte, Layer), KeyBinding>();
        foreach (var b in list) d[(b.KeyId, b.Layer)] = b;
        return d;
    }

    static byte[]? TryEncode(LayerConfig? c)
    {
        try { return c?.Encode(Lighting.TopLayer); }
        catch (ArgumentException) { return null; }
    }

    static byte[]? TryEncode(KeyBinding b)
    {
        try { return b.Encode(); }
        catch (ArgumentException) { return null; }
    }

    static bool Same(byte[] a, byte[]? b) => b is not null && a.AsSpan().SequenceEqual(b);
}
