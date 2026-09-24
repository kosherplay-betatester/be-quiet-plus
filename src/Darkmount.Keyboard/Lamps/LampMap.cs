namespace Darkmount.Keyboard.Lamps;

/// <summary>How to interpret LampAttributes.InputBinding.</summary>
public enum LampBindingKind
{
    /// <summary>Decide from the data (<see cref="LampMap.DetectBindingKind"/>).</summary>
    Auto,

    /// <summary>No lamp has a binding: map by position against a reference layout.</summary>
    None,

    /// <summary>Standard HID: the binding is a Keyboard/Keypad-page (0x07) usage.</summary>
    HidKeyboardUsage,

    /// <summary>Dark Mount firmware: the binding is the QLink key id (<see cref="DarkmountKeys"/>); ≥ 106 = edge lights.</summary>
    DarkmountKeyId,
}

public enum LampKeySource { InputBinding, Position }

/// <summary>A lamp identified as a key. <see cref="HidUsage"/> is 0 for keys without one (Fn); <see cref="KeyId"/> is the
/// Dark Mount key id when known, else 0.</summary>
public sealed record LampKey(int LampId, string Name, int HidUsage, int KeyId, LampKeySource Source);

/// <summary>Maps lamps to keys: HID keyboard usage → lamp id, plus per-lamp key names.</summary>
public sealed class LampMap
{
    LampMap(LampBindingKind kind, List<LampKey> keys, IReadOnlyList<LampInfo> lamps)
    {
        BindingKind = kind;
        Keys = keys.ToDictionary(k => k.LampId);
        var byUsage = new Dictionary<int, int>();
        var byKeyId = new Dictionary<int, int>();
        foreach (var k in keys)
        {
            if (k.HidUsage != 0) byUsage.TryAdd(k.HidUsage, k.LampId);
            if (k.KeyId != 0) byKeyId.TryAdd(k.KeyId, k.LampId);
        }
        UsageToLamp = byUsage;
        KeyIdToLamp = byKeyId;
        UnmappedLamps = lamps.Where(l => !Keys.ContainsKey(l.Id)).Select(l => l.Id).ToList();
        _bindings = lamps.ToDictionary(l => l.Id, l => l.InputBinding);
    }

    readonly Dictionary<int, int> _bindings;

    public LampBindingKind BindingKind { get; }

    /// <summary>HID Keyboard-page usage → lamp id (first lamp wins if a usage repeats).</summary>
    public IReadOnlyDictionary<int, int> UsageToLamp { get; }

    /// <summary>Dark Mount key id → lamp id (only for <see cref="LampBindingKind.DarkmountKeyId"/>).</summary>
    public IReadOnlyDictionary<int, int> KeyIdToLamp { get; }

    /// <summary>Lamp id → key, for every lamp identified as a key.</summary>
    public IReadOnlyDictionary<int, LampKey> Keys { get; }

    /// <summary>Lamps that are not keys (edge lights, unbound or unmatched lamps).</summary>
    public IReadOnlyList<int> UnmappedLamps { get; }

    public bool TryGetLamp(int hidUsage, out int lampId) => UsageToLamp.TryGetValue(hidUsage, out lampId);

    /// <summary>Key name, "Edge light n" for Dark Mount edge LEDs, else "Lamp n".</summary>
    public string Describe(int lampId)
    {
        if (Keys.TryGetValue(lampId, out var key)) return key.Name;
        if (BindingKind == LampBindingKind.DarkmountKeyId && _bindings.TryGetValue(lampId, out int b) && b >= DarkmountKeys.FirstEdgeLightBinding)
            return $"Edge light {b - DarkmountKeys.FirstEdgeLightBinding + 1}";
        return $"Lamp {lampId}";
    }

    /// <summary>
    /// None when no lamp is bound; DarkmountKeyId when bindings use the reserved Keyboard-page codes 1–3
    /// (ErrorRollOver/POSTFail/ErrorUndefined — never real keys, but Dark Mount key ids for `, 1 and 2); else HidKeyboardUsage.
    /// </summary>
    public static LampBindingKind DetectBindingKind(IReadOnlyList<LampInfo> lamps)
    {
        if (lamps.All(l => l.InputBinding == 0)) return LampBindingKind.None;
        return lamps.Any(l => l.InputBinding is >= 1 and <= 3) ? LampBindingKind.DarkmountKeyId : LampBindingKind.HidKeyboardUsage;
    }

    /// <param name="referenceLayout">Key positions for the position fallback (default: <see cref="DarkmountKeys.AnsiMainBlock"/>).</param>
    /// <param name="tolerance">Max distance for a position match, as a fraction of the normalised (0..1) layout.</param>
    public static LampMap Build(
        IReadOnlyList<LampInfo> lamps, LampBindingKind kind = LampBindingKind.Auto,
        IReadOnlyList<KeyPosition>? referenceLayout = null, double tolerance = 0.03)
    {
        if (kind == LampBindingKind.Auto) kind = DetectBindingKind(lamps);
        var keys = new List<LampKey>();
        switch (kind)
        {
            case LampBindingKind.DarkmountKeyId:
                foreach (var lamp in lamps)
                    if (DarkmountKeys.TryGet(lamp.InputBinding, out var key))
                        keys.Add(new LampKey(lamp.Id, key.Name, key.HidUsage, key.Id, LampKeySource.InputBinding));
                break;
            case LampBindingKind.HidKeyboardUsage:
                foreach (var lamp in lamps.Where(l => l.InputBinding != 0))
                {
                    int keyId = DarkmountKeys.ByUsage.TryGetValue(lamp.InputBinding, out var key) ? key.Id : 0;
                    keys.Add(new LampKey(lamp.Id, DarkmountKeys.UsageName(lamp.InputBinding), lamp.InputBinding, keyId, LampKeySource.InputBinding));
                }
                break;
            default:
                keys.AddRange(MatchByPosition(lamps, referenceLayout ?? DarkmountKeys.AnsiMainBlock, tolerance));
                break;
        }
        return new LampMap(kind, keys, lamps);
    }

    /// <summary>
    /// Normalises lamp and key positions to their own bounding boxes and pairs them greedily, closest first, within
    /// <paramref name="tolerance"/>. Best effort: extra non-key lamps (edge strips) stretch the lamp bounding box.
    /// </summary>
    static IEnumerable<LampKey> MatchByPosition(IReadOnlyList<LampInfo> lamps, IReadOnlyList<KeyPosition> layout, double tolerance)
    {
        if (lamps.Count == 0 || layout.Count == 0) yield break;
        var lampPoints = Normalise(lamps.Select(l => ((double)l.PositionX, (double)l.PositionY)).ToList());
        var keyPoints = Normalise(layout.Select(k => (k.X, k.Y)).ToList());

        var pairs = new List<(double Distance, int Lamp, int Key)>();
        for (int l = 0; l < lampPoints.Count; l++)
            for (int k = 0; k < keyPoints.Count; k++)
            {
                double dx = lampPoints[l].X - keyPoints[k].X, dy = lampPoints[l].Y - keyPoints[k].Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= tolerance) pairs.Add((d, l, k));
            }
        pairs.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var usedLamps = new HashSet<int>();
        var usedKeys = new HashSet<int>();
        foreach (var (_, l, k) in pairs)
        {
            if (!usedLamps.Add(l)) continue;
            if (!usedKeys.Add(k)) { usedLamps.Remove(l); continue; }
            var key = layout[k];
            int keyId = key.HidUsage != 0 && DarkmountKeys.ByUsage.TryGetValue(key.HidUsage, out var dk) ? dk.Id : 0;
            yield return new LampKey(lamps[l].Id, key.Name, key.HidUsage, keyId, LampKeySource.Position);
        }
    }

    static List<(double X, double Y)> Normalise(List<(double X, double Y)> points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double w = Math.Max(maxX - minX, 1e-9), h = Math.Max(maxY - minY, 1e-9);
        return points.Select(p => ((p.X - minX) / w, (p.Y - minY) / h)).ToList();
    }
}
