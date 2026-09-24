namespace Darkmount.Keyboard.Lamps;

/// <summary>
/// Renders a <see cref="LightingScene"/> to one colour per lamp. Pure and stateless: the same scene and context always give
/// the same colours, so it can drive the keyboard and an on-screen preview alike.
/// </summary>
/// <remarks>
/// Compositing: every lamp starts as <see cref="LightingScene.Background"/>; layers are applied from the last (bottom) to
/// the first (top), each only on its target lamps (<see cref="TargetLamps"/>). An opaque effect replaces the lamp's colour;
/// a transparent one (Reactive, Ripple, Matrix, …) blends by its coverage so lower layers show where it is dark. Disabled
/// layers, brightness 0 layers and layers beyond <see cref="LightingScene.MaxLayers"/> are skipped.
/// </remarks>
public static class SceneRenderer
{
    public static Dictionary<int, LampColor> Render(LightingScene scene, SceneContext ctx)
    {
        var pixels = Normalise(ctx.Lamps);
        var background = Rgbf.TryParse(scene.Background, out var bg) ? bg : Rgbf.Black;
        var colors = new Rgbf[pixels.Length];
        Array.Fill(colors, background);

        if (pixels.Length > 0 && scene.Layers is { } layers)
        {
            var frame = new SceneFrame(ctx, pixels, background);
            for (int li = Math.Min(layers.Count, LightingScene.MaxLayers) - 1; li >= 0; li--)
            {
                var layer = layers[li];
                if (layer is null || !layer.Enabled || layer.Brightness <= 0) continue;
                var target = Targeting.For(layer);
                if (target.IsEmpty) continue;

                var shader = SceneShaders.Create(new LayerFrame(frame, layer));
                double gain = Math.Min(layer.Brightness, 100) / 100.0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    ref readonly var p = ref pixels[i];
                    if (!target.Includes(p.LampId, p.IsKey, p.KeyId)) continue;
                    var s = shader(in p);
                    if (s.A <= 0) continue;
                    var c = s.C * gain;
                    colors[i] = s.A >= 1 ? c : Rgbf.Lerp(colors[i], c, s.A);
                }
            }
        }

        var result = new Dictionary<int, LampColor>(pixels.Length);
        for (int i = 0; i < pixels.Length; i++) result[pixels[i].LampId] = colors[i].ToLamp();
        return result;
    }

    /// <summary>
    /// The lamp ids a layer draws on, in <paramref name="lamps"/> order: keys (all, or those whose key id is in
    /// <see cref="LightLayer.Keys"/>) plus edge LEDs (all non-key lamps, or those listed in <see cref="LightLayer.EdgeLamps"/>).
    /// </summary>
    public static IReadOnlyList<int> TargetLamps(LightLayer layer, IReadOnlyList<LampPoint> lamps)
    {
        var target = Targeting.For(layer);
        var ids = new List<int>();
        if (target.IsEmpty) return ids;
        foreach (var l in lamps)
            if (target.Includes(l.LampId, l.IsKey, l.KeyId)) ids.Add(l.LampId);
        return ids;
    }

    readonly struct Targeting
    {
        readonly bool _allKeys, _allEdges;
        readonly HashSet<int>? _keys, _edges;

        Targeting(bool allKeys, HashSet<int>? keys, bool allEdges, HashSet<int>? edges)
        {
            _allKeys = allKeys;
            _keys = keys;
            _allEdges = allEdges;
            _edges = edges;
        }

        public static Targeting For(LightLayer layer) => layer.Effect == SceneEffect.PerKey
            ? new(true, null, true, null) // painted lamps choose themselves
            : new(
            layer.AllKeys, layer.AllKeys || layer.Keys is not { Count: > 0 } ? null : [.. layer.Keys],
            layer.AllEdges, layer.AllEdges || layer.EdgeLamps is not { Count: > 0 } ? null : [.. layer.EdgeLamps]);

        public bool IsEmpty => !_allKeys && !_allEdges && _keys is null && _edges is null;

        public bool Includes(int lampId, bool isKey, int keyId) => isKey
            ? _allKeys || (keyId != 0 && _keys is not null && _keys.Contains(keyId))
            : _allEdges || (_edges is not null && _edges.Contains(lampId));
    }

    /// <summary>Stretches the keys and the edge LEDs each to fill 0..1 (an axis with no extent keeps its values).</summary>
    static Pixel[] Normalise(IReadOnlyList<LampPoint> lamps)
    {
        var keys = Bounds.Empty;
        var edges = Bounds.Empty;
        foreach (var l in lamps)
        {
            if (l.IsKey) keys = keys.Add(l.X, l.Y);
            else edges = edges.Add(l.X, l.Y);
        }
        var pixels = new Pixel[lamps.Count];
        for (int i = 0; i < pixels.Length; i++)
        {
            var l = lamps[i];
            var b = l.IsKey ? keys : edges;
            pixels[i] = new Pixel(l.LampId, b.MapX(l.X), b.MapY(l.Y), l.IsKey, l.IsKey ? l.KeyId : 0);
        }
        return pixels;
    }

    readonly record struct Bounds(double MinX, double MaxX, double MinY, double MaxY)
    {
        public static Bounds Empty => new(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);

        public Bounds Add(double x, double y) => new(Math.Min(MinX, x), Math.Max(MaxX, x), Math.Min(MinY, y), Math.Max(MaxY, y));

        public double MapX(double x) => MaxX - MinX > 1e-6 ? (x - MinX) / (MaxX - MinX) : x;
        public double MapY(double y) => MaxY - MinY > 1e-6 ? (y - MinY) / (MaxY - MinY) : y;
    }
}
