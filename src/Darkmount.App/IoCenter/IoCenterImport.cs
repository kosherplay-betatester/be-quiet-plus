using System.IO.Compression;
using System.Text.Json.Nodes;
using Darkmount.App.Macros;
using Darkmount.Keyboard;
using Darkmount.Keyboard.Lamps;
using Darkmount.QLink;
using SkiaSharp;

namespace Darkmount.App.IoCenter;

/// <summary>An IO Center profile found on this PC.</summary>
public sealed record IoCenterProfileFile(string Name, string Path, DateTime Modified);

/// <summary>A key that opens a program, folder or website in IO Center; OverMount does this with a macro.</summary>
public sealed record IoCenterLauncher(byte KeyId, Layer Layer, string KeyLabel, MacroStep Step);

/// <summary>A converted profile plus what could not be taken over, in plain words for the user.</summary>
public sealed record IoCenterImportResult(Profile Profile, IReadOnlyList<string> Notes, IReadOnlyList<IoCenterLauncher> Launchers);

/// <summary>
/// Reads IO Center profiles — the JSON files in %APPDATA%\be quiet!\IO Center\profiles and exported .ioprofile files (a zip
/// of the same JSON plus the display-key pictures) — and converts them to OverMount profiles: custom illumination layers
/// become a Lighting-studio scene, the general effect the keyboard's own effect, plus key bindings, display-key pictures and
/// the linked game. Read-only: IO Center's files are never changed.
/// </summary>
/// <remarks>
/// The files are cereal JSON. Effects and bindings are std::variant values stored as <c>impl.index</c>; the orders below come
/// from the type table in IO_Center.exe and match every sample (6 Reactive, 10 Static, 11 Tornado; 4 media, 7 open file,
/// 10 Windows shortcut, 12 backlight).
/// </remarks>
public static class IoCenterImport
{
    public static string ProfilesFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "be quiet!", "IO Center", "profiles");

    /// <summary>IO Center's picture store; installed profiles point into it with file:/// URLs.</summary>
    public static string AssetsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "be quiet!", "IO Center", "assets");

    const int MaxFileBytes = 64 * 1024 * 1024, MaxAssetBytes = 8 * 1024 * 1024;

    enum IoEffect { Breathing, ColorWave, Gif, Image, Matrix, MulticolorStatic, Reactive, ScreenCapture, ScreenSync, Sensor, Static, Tornado, Video, Off, Ripple }

    enum IoBinding { Default, Disabled, StandardKey, SpecialKey, Media, MouseButton, MouseScroll, OpenFile, OpenFolder, OpenBrowser, WindowsShortcut, Profile, Backlight, Macro }

    /// <summary>The profiles IO Center has stored on this PC (unreadable files are skipped).</summary>
    public static IReadOnlyList<IoCenterProfileFile> ListInstalled(string? folder = null)
    {
        folder ??= ProfilesFolder;
        if (!Directory.Exists(folder)) return [];
        var list = new List<IoCenterProfileFile>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.ioprofile"))
        {
            try
            {
                var (json, _) = ReadFile(path);
                var name = JsonNode.Parse(json)?["data"]?["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
                list.Add(new(name, path, File.GetLastWriteTime(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                          or InvalidDataException or InvalidOperationException) { }
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Loads an installed or exported profile.</summary>
    /// <param name="lampOfEdgeLight">Edge-light number (1..96) → LampArray lamp id, from the connected keyboard; null skips
    /// edge-LED selections that don't cover every edge LED.</param>
    public static IoCenterImportResult Load(string path, KeyboardModel model, Func<int, int?>? lampOfEdgeLight = null)
    {
        var (json, zipAssets) = ReadFile(path);
        return Parse(json, model, url => zipAssets is not null ? FromZip(zipAssets, url) : FromAssetsFolder(url), lampOfEdgeLight);
    }

    /// <summary>JSON text and, for an exported (zip) profile, its pictures by file name.</summary>
    static (string Json, Dictionary<string, byte[]>? Assets) ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("The file is too large to be an IO Center profile.");
        if (bytes is not [(byte)'P', (byte)'K', ..]) return (System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), null);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var data = zip.GetEntry("data") ?? throw new InvalidDataException("This .ioprofile has no profile data.");
        string json = System.Text.Encoding.UTF8.GetString(ReadBounded(data, MaxFileBytes)).TrimStart('\uFEFF');
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && e.Length is > 0 and < MaxAssetBytes))
        {
            var picture = ReadBounded(e, MaxAssetBytes);
            if ((total += picture.Length) > MaxFileBytes) throw new InvalidDataException("This .ioprofile has far too many pictures.");
            assets[Path.GetFileNameWithoutExtension(e.Name)] = picture;
        }
        return (json, assets);
    }

    /// <summary>Unpacks an entry, counting what actually comes out (a declared size can lie: zip bombs).</summary>
    static byte[] ReadBounded(ZipArchiveEntry entry, int max)
    {
        using var s = entry.Open();
        using var m = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            m.Write(buffer, 0, read);
            if (m.Length > max) throw new InvalidDataException("This .ioprofile is too large to be an IO Center profile.");
        }
        return m.ToArray();
    }

    static byte[]? FromZip(Dictionary<string, byte[]> assets, string url) =>
        assets.GetValueOrDefault(Path.GetFileNameWithoutExtension(LocalPath(url) ?? url));

    /// <summary>Only files inside IO Center's own picture store are read.</summary>
    static byte[]? FromAssetsFolder(string url)
    {
        if (LocalPath(url) is not { } path) return null;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(AssetsFolder) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return new FileInfo(full).Length < 8 << 20 ? File.ReadAllBytes(full) : null;
    }

    static string? LocalPath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;

    /// <summary>Converts profile JSON; <paramref name="asset"/> returns a picture's bytes for its source URL.</summary>
    public static IoCenterImportResult Parse(string json, KeyboardModel model, Func<string, byte[]?> asset, Func<int, int?>? lampOfEdgeLight = null)
    {
        var root = JsonNode.Parse(json)?["data"] ?? throw new InvalidDataException("This is not an IO Center profile.");
        var notes = new List<string>();
        var profile = new Profile { Name = Str(root["name"]) is { Length: > 0 } n ? n.Trim() : "IO Center profile" };

        if (Str(root["linkedAppPath"]) is { Length: > 0 } app)
        {
            var exe = Path.GetFileName(LocalPath(app) ?? app);
            if (exe.Length > 0) profile.Games.Add(exe);
        }

        var properties = root["properties"]?.AsArray() ?? [];
        var launchers = new List<IoCenterLauncher>();
        var keyboard = profile.Keyboard;
        keyboard.CreatedUtc = DateTime.UtcNow;
        foreach (var property in properties)
        {
            string? kind = Str(property?["polymorphic_name"]);
            var sub = DeviceSub(property, model);
            if (sub is null) continue;
            switch (kind)
            {
                case "LightingsProperty": ImportLighting(sub, profile, lampOfEdgeLight, notes); break;
                case "KeyBindingsProperty": ImportBindings(sub, model, keyboard, launchers, notes); break;
                case "NumpadProperty" when model.HasDisplayKeys: ImportImages(sub, asset, keyboard, notes); break;
                case "MacrosProperty" when sub["macros"] is JsonArray { Count: > 0 } macros:
                    notes.Add($"{macros.Count} IO Center macro(s) were not imported — recreate them on the Macros page (it records them for you).");
                    break;
                case "MediaDockProperty":
                    if (model.HasMediaDock) notes.Add("The dock's screensaver settings stay as they are (Dock settings page).");
                    break;
            }
        }
        return new(profile, notes, launchers);
    }

    /// <summary>The property's settings for this keyboard model (IO Center keys them by device name).</summary>
    static JsonNode? DeviceSub(JsonNode? property, KeyboardModel model)
    {
        if (property?["ptr_wrapper"]?["data"]?["subs"] is not JsonArray subs || subs.Count == 0) return null;
        var match = subs.FirstOrDefault(s => string.Equals(Str(s?["key"]), model.Name, StringComparison.OrdinalIgnoreCase)) ?? subs[0];
        return match?["value"]?["ptr_wrapper"]?["data"];
    }

    // ------------------------------------------------------------------ lighting

    static void ImportLighting(JsonNode sub, Profile profile, Func<int, int?>? lampOfEdgeLight, List<string> notes)
    {
        bool enabled = sub["enabled"]?.GetValue<bool>() ?? true;
        string mode = Str(sub["mode"]) ?? "General";

        // The general effect is the keyboard's own effect: shown in General mode and whenever OverMount isn't drawing.
        if (sub["generalModeData"]?["data"] is JsonArray { Count: > 0 } general && Variant(general[0]) is var (index, data) && data is not null)
        {
            var config = FirmwareConfig((IoEffect)index, data);
            if (config is not null) profile.Keyboard.LayerConfig = config;
            else notes.Add($"The general effect \"{(IoEffect)index}\" has no keyboard equivalent; the keyboard keeps its current effect.");
        }
        profile.Keyboard.LightingMode = enabled ? LightingMode.General : LightingMode.Off;

        var layers = new List<LightLayer>();
        foreach (var layerNode in sub["customModeData"]?["layers"]?.AsArray() ?? [])
        {
            if (layerNode is null) continue;
            string name = Str(layerNode["name"]) ?? "Layer";
            if (layerNode["effectData"] is not { } effectData || effectData["nullopt"]?.GetValue<bool>() == true
                || Variant(effectData["data"]) is not var (effectIndex, fx) || fx is null)
                continue; // a layer without an effect draws nothing
            var layer = SceneLayer((IoEffect)effectIndex, fx, name, notes);
            if (layer is null) continue;
            Assign(layer, layerNode["assignments"]?.AsArray() ?? [], lampOfEdgeLight, notes);
            if (!layer.AllKeys && layer.Keys.Count == 0 && !layer.AllEdges && layer.EdgeLamps.Count == 0) continue;
            layers.Add(layer);
        }
        if (layers.Count > LightingScene.MaxLayers)
        {
            notes.Add($"The profile has {layers.Count} lighting layers; the top {LightingScene.MaxLayers} were kept.");
            layers = layers.Take(LightingScene.MaxLayers).ToList();
        }
        if (layers.Count > 0)
            profile.Scene = new LightingScene { Name = profile.Name, Description = "Imported from IO Center", Layers = layers };

        profile.RgbEnabled = enabled && mode.Equals("Custom", StringComparison.OrdinalIgnoreCase) && layers.Count > 0;
        if (!enabled) notes.Add("Lighting is switched off in this profile.");
        else if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase) && layers.Count == 0)
            notes.Add("The custom illumination has no layers OverMount can draw; the general effect is used instead.");
    }

    static LayerConfig? FirmwareConfig(IoEffect effect, JsonNode d)
    {
        Effect? fw = effect switch
        {
            IoEffect.Static or IoEffect.MulticolorStatic => Effect.Static,
            IoEffect.ColorWave => Effect.ColorWave,
            IoEffect.Tornado => Effect.Tornado,
            IoEffect.Breathing => Effect.Breathing,
            IoEffect.Reactive => Effect.Reactive,
            IoEffect.Matrix => Effect.Matrix,
            IoEffect.Off => Effect.Off,
            IoEffect.Ripple => Effect.Ripple,
            _ => null,
        };
        if (fw is not { } e) return null;
        var (mode, colors) = Colours(d);
        if (colors.Count == 0) colors = ["FF2800"];
        var fwMode = mode switch
        {
            SceneColorMode.Dual when colors.Count >= 2 => ColorMode.Dual,
            SceneColorMode.Gradient when colors.Count >= LightingEffects.MinGradientStops => ColorMode.Gradient,
            _ => ColorMode.Single,
        };
        var used = fwMode switch
        {
            ColorMode.Single => colors.Take(1).ToList(),
            ColorMode.Dual => colors.Take(2).ToList(),
            _ => colors.Take(LightingEffects.MaxGradientStops).ToList(),
        };
        var stops = used.Select((c, i) => new GradientStop(Rgb.Parse(c), used.Count == 1 ? 0 : i * 100 / (used.Count - 1))).ToList();
        return new LayerConfig(e, FirmwareDirection(Str(d["direction"])), Int(d["brightness"], 100), Int(d["speed"], 50), fwMode, stops);
    }

    static LightLayer? SceneLayer(IoEffect effect, JsonNode d, string name, List<string> notes)
    {
        SceneEffect? scene = effect switch
        {
            IoEffect.Static or IoEffect.MulticolorStatic or IoEffect.Off => SceneEffect.Static,
            IoEffect.ColorWave => SceneEffect.ColorWave,
            IoEffect.Tornado => SceneEffect.Tornado,
            IoEffect.Breathing => SceneEffect.Breathing,
            IoEffect.Reactive => SceneEffect.Reactive,
            IoEffect.Matrix => SceneEffect.Matrix,
            IoEffect.Ripple => SceneEffect.Ripple,
            IoEffect.ScreenSync or IoEffect.ScreenCapture => SceneEffect.ScreenSync,
            IoEffect.Sensor => SceneEffect.CpuTemperature,
            _ => null,
        };
        if (scene is not { } fx)
        {
            notes.Add($"Layer \"{name}\" uses IO Center's {effect} effect, which isn't available for keys; it was left out.");
            return null;
        }
        var (mode, colors) = Colours(d);
        if (effect == IoEffect.Off) (mode, colors) = (SceneColorMode.Single, ["000000"]);
        if (effect == IoEffect.Sensor) notes.Add($"Layer \"{name}\" (sensor colours) now follows the CPU temperature.");
        return new LightLayer
        {
            Name = name,
            Effect = fx,
            ColorMode = colors.Count == 0 ? SceneColorMode.Single : mode,
            Colors = colors.Count == 0 ? ["FF2800"] : colors,
            Direction = SceneDirectionOf(Str(d["direction"])),
            Speed = Math.Clamp((int)Math.Round(Int(d["speed"], 50) / 10.0), 1, 10),
            Brightness = effect == IoEffect.Off ? 100 : Math.Clamp(Int(d["brightness"], 100), 0, 100),
            AllKeys = false,
            AllEdges = false,
        };
    }

    /// <summary>Colour mode and "RRGGBB" colours from IO Center's colorMode/singleColor/dualColors/gradient/color fields.</summary>
    static (SceneColorMode Mode, List<string> Colors) Colours(JsonNode d)
    {
        if (Hex(d["color"]) is { } single && d["colorMode"] is null) return (SceneColorMode.Single, [single]);
        string mode = Str(d["colorMode"]) ?? "Single";
        if (mode.Contains("Dual", StringComparison.OrdinalIgnoreCase)
            && Hex(d["dualColors"]?["first"]) is { } a && Hex(d["dualColors"]?["second"]) is { } b)
            return (SceneColorMode.Dual, [a, b]);
        if (mode.Contains("Gradient", StringComparison.OrdinalIgnoreCase) && d["gradient"] is JsonArray stops)
        {
            var colors = stops.Where(s => s is not null)
                .Select(s => (Color: Hex(s!["first"]), Pos: s["second"]?.GetValue<double>() ?? 0))
                .Where(s => s.Color is not null).OrderBy(s => s.Pos).Select(s => s.Color!).ToList();
            if (colors.Count > 7) colors = Enumerable.Range(0, 7).Select(i => colors[i * (colors.Count - 1) / 6]).ToList();
            if (colors.Count >= 2) return (SceneColorMode.Gradient, colors);
            if (colors.Count == 1) return (SceneColorMode.Single, colors);
        }
        var first = Hex(d["singleColor"]) ?? Hex(d["color"]) ?? Hex(d["colors"]?.AsArray().FirstOrDefault());
        return (SceneColorMode.Single, first is null ? [] : [first]);
    }

    static void Assign(LightLayer layer, JsonArray assignments, Func<int, int?>? lampOfEdgeLight, List<string> notes)
    {
        int unknown = 0, edgesWanted = 0;
        var keys = new SortedSet<int>();
        var edges = new SortedSet<int>();
        foreach (var node in assignments)
        {
            if (Str(node) is not { } name) continue;
            if (IoCenterNames.EdgeLightOf(name) is { } edge)
            {
                edgesWanted++;
                if (lampOfEdgeLight?.Invoke(edge) is { } lamp) edges.Add(lamp);
            }
            else if (IoCenterNames.KeyIdOf(name) is { } key && key < KeyIds.DisplayKey1) keys.Add(key);
            else unknown++;
        }
        layer.Keys = [.. keys];
        layer.EdgeLamps = [.. edges];
        if (edgesWanted > 0 && edges.Count == 0)
        {
            // Without the keyboard's lamp list, a layer on (nearly) the whole ring still gets every edge LED.
            if (edgesWanted >= 64) layer.AllEdges = true;
            else notes.Add($"Layer \"{layer.Name}\": its edge-LED selection needs the keyboard connected; import again with it plugged in.");
        }
        if (unknown > 0) notes.Add($"Layer \"{layer.Name}\": {unknown} LED name(s) weren't recognised and were skipped.");
    }

    static SceneDirection SceneDirectionOf(string? d) => (d ?? "").ToLowerInvariant() switch
    {
        "left" => SceneDirection.Left,
        "up" => SceneDirection.Up,
        "down" or "vertical" => SceneDirection.Down,
        "clockwise" => SceneDirection.Clockwise,
        "counterclockwise" => SceneDirection.CounterClockwise,
        "omnidirectional" or "cross" or "outward" => SceneDirection.Outward,
        "inward" => SceneDirection.Inward,
        _ => SceneDirection.Right,
    };

    static Direction FirmwareDirection(string? d) =>
        Enum.TryParse<Direction>(d, ignoreCase: true, out var dir) ? dir : Direction.Right;

    // ------------------------------------------------------------------ key bindings

    static void ImportBindings(JsonNode sub, KeyboardModel model, KeyboardSnapshot keyboard, List<IoCenterLauncher> launchers,
        List<string> notes)
    {
        keyboard.BindingsEnabled = sub["enabled"]?.GetValue<bool>() ?? true;
        var list = new List<KeyBinding>();
        int skipped = 0;
        foreach (var layerNode in sub["layerBindingsData"]?.AsArray() ?? [])
        {
            var layer = (Str(layerNode?["key"]) ?? "").StartsWith("Fn", StringComparison.OrdinalIgnoreCase) ? Layer.Fn : Layer.Common;
            foreach (var entry in layerNode?["value"]?.AsArray() ?? [])
            {
                string keyName = Str(entry?["key"]) ?? "";
                if (IoCenterNames.KeyIdOf(keyName) is not { } keyId) { skipped++; continue; }
                if (keyId is >= KeyIds.DisplayKey1 and <= KeyIds.DisplayKey8 && !model.HasDisplayKeys) continue;
                if (keyId >= KeyIds.DockMute && !model.HasMediaDock) continue;
                if (Variant(entry?["value"]) is not var (index, data)) { skipped++; continue; }
                if (!KeyIds.IsRebindable(keyId, layer)) continue;
                if ((IoBinding)index is IoBinding.OpenFile or IoBinding.OpenFolder or IoBinding.OpenBrowser)
                {
                    // An empty path is an unset slot in IO Center: nothing to take over.
                    if (Launch((IoBinding)index, data ?? new JsonObject()) is { } step) launchers.Add(new(keyId, layer, Label(keyId, layer), step));
                    continue;
                }
                var action = Binding((IoBinding)index, data ?? new JsonObject());
                if (action is null)
                {
                    if ((IoBinding)index != IoBinding.Default) skipped++;
                    continue;
                }
                var binding = new KeyBinding(keyId, layer, action);
                try { binding.Encode(); }
                catch (ArgumentException) { skipped++; continue; }
                list.Add(binding);
            }
        }
        keyboard.Bindings = list;
        if (skipped > 0) notes.Add($"{skipped} key binding(s) use IO Center-only functions (macros, profile switching, …) and were skipped.");
    }

    /// <summary>IO Center's open-file / folder / website bindings as the equivalent macro step.</summary>
    static MacroStep? Launch(IoBinding kind, JsonNode d)
    {
        string url = (Str(d["url"]) ?? Str(d["path"]) ?? "").Trim();
        if (url.Length == 0) return null;
        return kind switch
        {
            IoBinding.OpenFile => new LaunchProgramStep(LocalPath(url) ?? url),
            IoBinding.OpenFolder => new OpenFolderStep(LocalPath(url) ?? url),
            _ => new OpenUrlStep(url),
        };
    }

    static string Label(byte keyId, Layer layer) =>
        (layer == Layer.Fn ? "Fn + " : "") + (keyId is >= KeyIds.DisplayKey1 and <= KeyIds.DisplayKey8
            ? $"display key {keyId - KeyIds.DisplayKey1 + 1}" : KeyIds.Find(keyId)?.Label ?? $"key {keyId}");

    static BindingAction? Binding(IoBinding kind, JsonNode d)
    {
        string action = Str(d["action"]) ?? "";
        switch (kind)
        {
            case IoBinding.Disabled: return new BindingAction.Disabled();
            case IoBinding.Media:
                MediaAction? media = action.ToLowerInvariant() switch
                {
                    "increasevolume" or "volumeup" => MediaAction.VolumeUp,
                    "decreasevolume" or "volumedown" => MediaAction.VolumeDown,
                    "launchvolume" or "volumemixer" => MediaAction.VolumeMixer,
                    _ => Enum.TryParse<MediaAction>(action, true, out var m) && m is not (MediaAction.None or MediaAction.SpecificSound) ? m : null,
                };
                return media is { } ma ? new BindingAction.Media(ma) : null;
            case IoBinding.WindowsShortcut:
                return Enum.TryParse<WindowsShortcutAction>(action, true, out var w) && w != WindowsShortcutAction.None
                    ? new BindingAction.WindowsShortcut(w) : null;
            case IoBinding.Backlight:
                if (!Enum.TryParse<BacklightAction>(action, true, out var b) || b == BacklightAction.None) return null;
                Effect? effect = b == BacklightAction.SelectEffect && Enum.TryParse<Effect>(Str(d["effect"]), true, out var e) ? e : null;
                return new BindingAction.Backlight(b, effect);
            case IoBinding.StandardKey or IoBinding.SpecialKey:
                var keyName = d.AsObject().Select(kv => Str(kv.Value)).FirstOrDefault(v => v?.StartsWith("Key_", StringComparison.OrdinalIgnoreCase) == true);
                if (keyName is null || IoCenterNames.UsageOf(keyName) is not { } usage) return null;
                if (usage is >= 0x68 and <= 0x73) return new BindingAction.FKey(usage);
                return new BindingAction.StandardKey(Modifiers(d), usage);
            case IoBinding.MouseButton:
                return Enum.TryParse<MouseButtonKind>(Str(d["button"]) ?? action, true, out var mb) ? new BindingAction.MouseButton(mb) : null;
            case IoBinding.MouseScroll:
                return Enum.TryParse<MouseScrollDirection>(Str(d["direction"]) ?? action, true, out var sd) ? new BindingAction.MouseScroll(sd) : null;
            default:
                return null; // Default (factory binding), Profile and Macro; open file/folder/website become macros
        }
    }

    static KeyModifiers Modifiers(JsonNode d)
    {
        var mods = KeyModifiers.None;
        foreach (var (name, value) in d.AsObject())
        {
            if (!name.Contains("modifier", StringComparison.OrdinalIgnoreCase)) continue;
            if (value is JsonValue v && v.TryGetValue(out int flags)) return (KeyModifiers)(byte)flags;
            foreach (var item in value as JsonArray ?? [])
                if (Str(item) is { } s && Enum.TryParse<KeyModifiers>(s.Replace("Key_", "").Replace("Gui", "Win"), true, out var m)) mods |= m;
        }
        return mods;
    }

    // ------------------------------------------------------------------ display keys

    static void ImportImages(JsonNode sub, Func<string, byte[]?> asset, KeyboardSnapshot keyboard, List<string> notes)
    {
        int missing = 0;
        foreach (var entry in sub["images"]?.AsArray() ?? [])
        {
            if (IoCenterNames.KeyIdOf(Str(entry?["key"]) ?? "") is not { } keyId || keyId is < KeyIds.DisplayKey1 or > KeyIds.DisplayKey8) continue;
            if (Variant(entry?["value"]) is not var (_, data) || data is null || Str(data["source"]) is not { } source) continue;
            if (source.StartsWith("qrc:", StringComparison.OrdinalIgnoreCase)) continue; // IO Center's built-in picture
            var bytes = asset(source);
            using var bitmap = bytes is null ? null : SKBitmap.Decode(bytes);
            if (bitmap is null) { missing++; continue; }
            using var cropped = Crop(bitmap, data["cropRect"]);
            (keyboard.DisplayKeyJpegs ??= [])[keyId - KeyIds.DisplayKey1] = DisplayKeys.EncodeForKey(cropped);
        }
        if (missing > 0) notes.Add($"{missing} display-key picture(s) could not be found and were left unchanged.");
    }

    static SKBitmap Crop(SKBitmap source, JsonNode? rect)
    {
        double x = Num(rect?["x"], 0), y = Num(rect?["y"], 0), w = Num(rect?["width"], 1), h = Num(rect?["height"], 1);
        var r = SKRectI.Round(new SKRect((float)(x * source.Width), (float)(y * source.Height),
            (float)((x + w) * source.Width), (float)((y + h) * source.Height)));
        r.Intersect(new SKRectI(0, 0, source.Width, source.Height));
        if (r.Width <= 0 || r.Height <= 0 || r == new SKRectI(0, 0, source.Width, source.Height)) return source.Copy();
        var cropped = new SKBitmap(r.Width, r.Height);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(source, r, new SKRect(0, 0, r.Width, r.Height));
        return cropped;
    }

    // ------------------------------------------------------------------ JSON helpers

    /// <summary>A cereal std::variant: (impl.index, impl.data).</summary>
    static (int Index, JsonNode? Data)? Variant(JsonNode? node) =>
        node?["impl"] is { } impl && impl["index"] is JsonValue v && v.TryGetValue(out int index) ? (index, impl["data"]) : null;

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    static int Int(JsonNode? n, int fallback) =>
        n is JsonValue v ? v.TryGetValue(out int i) ? i : v.TryGetValue(out double d) ? (int)Math.Round(d) : fallback : fallback;

    static double Num(JsonNode? n, double fallback) =>
        n is JsonValue v ? v.TryGetValue(out double d) ? d : v.TryGetValue(out int i) ? i : fallback : fallback;

    /// <summary>"#AARRGGBB" or "#RRGGBB" → "RRGGBB".</summary>
    static string? Hex(JsonNode? n)
    {
        var s = Str(n)?.Trim().TrimStart('#');
        if (s is null || !s.All(Uri.IsHexDigit)) return null;
        return s.Length switch { 8 => s[2..].ToUpperInvariant(), 6 => s.ToUpperInvariant(), _ => null };
    }
}
