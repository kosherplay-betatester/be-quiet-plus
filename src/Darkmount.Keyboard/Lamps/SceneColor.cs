namespace Darkmount.Keyboard.Lamps;

/// <summary>A colour with 0..1 channels (values may briefly leave the range while mixing; <see cref="ToLamp"/> clamps).</summary>
internal readonly record struct Rgbf(double R, double G, double B)
{
    public static readonly Rgbf Black = new(0, 0, 0);
    public static readonly Rgbf White = new(1, 1, 1);

    public static Rgbf operator *(Rgbf c, double k) => new(c.R * k, c.G * k, c.B * k);
    public static Rgbf operator +(Rgbf a, Rgbf b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static Rgbf Lerp(Rgbf a, Rgbf b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    public double Max => Math.Max(R, Math.Max(G, B));

    public static Rgbf From(LampColor c) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    public LampColor ToLamp() => new(ToByte(R), ToByte(G), ToByte(B));

    static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);

    /// <summary>Parses "RRGGBB" or "#RRGGBB" (either case).</summary>
    public static bool TryParse(string? hex, out Rgbf color)
    {
        color = Black;
        var s = hex.AsSpan().Trim();
        if (s.StartsWith("#")) s = s[1..];
        if (s.Length != 6) return false;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.AllowHexSpecifier, null, out uint v)) return false;
        color = new(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
        return true;
    }

    public static Rgbf Parse(string hex) => TryParse(hex, out var c) ? c : Black;

    /// <summary>Hue 0..1 (wraps), saturation and value 0..1.</summary>
    public static Rgbf Hsv(double h, double s = 1, double v = 1)
    {
        h = (h - Math.Floor(h)) * 6;
        int i = (int)h;
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        return (i % 6) switch
        {
            0 => new(v, t, p), 1 => new(q, v, p), 2 => new(p, v, t), 3 => new(p, q, v), 4 => new(t, p, v), _ => new(v, p, q),
        };
    }
}

/// <summary>A colour plus coverage: A = 0 leaves the lamp to the layers below, 1 replaces it.</summary>
internal readonly record struct Rgba(Rgbf C, double A)
{
    public static readonly Rgba Clear = new(Rgbf.Black, 0);

    public static Rgba Opaque(Rgbf c) => new(c, 1);
}

/// <summary>A layer's colours, resolved from its <see cref="SceneColorMode"/> (evenly spaced stops).</summary>
internal sealed class Palette
{
    readonly Rgbf[] _stops;

    public Palette(IReadOnlyList<Rgbf> stops)
    {
        _stops = stops.Count > 0 ? [.. stops] : [Rgbf.White];
    }

    public int Count => _stops.Length;
    public Rgbf this[int i] => _stops[((i % _stops.Length) + _stops.Length) % _stops.Length];
    public Rgbf First => _stops[0];
    public Rgbf Last => _stops[^1];

    /// <summary>Linear gradient: 0 = first stop, 1 = last (clamped).</summary>
    public Rgbf Sample(double u)
    {
        if (_stops.Length == 1) return _stops[0];
        u = Math.Clamp(u, 0, 1) * (_stops.Length - 1);
        int i = Math.Min((int)u, _stops.Length - 2);
        return Rgbf.Lerp(_stops[i], _stops[i + 1], u - i);
    }

    /// <summary>Repeating gradient that wraps from the last stop back to the first (seamless for moving waves).</summary>
    public Rgbf SampleCyclic(double u)
    {
        if (_stops.Length == 1) return _stops[0];
        u = (u - Math.Floor(u)) * _stops.Length;
        int i = Math.Min((int)u, _stops.Length - 1);
        return Rgbf.Lerp(_stops[i], _stops[(i + 1) % _stops.Length], u - i);
    }

    /// <summary>
    /// A palette for intensity-mapped effects (fire, plasma, …): a single colour becomes dark → colour → pale colour so the
    /// effect keeps its shape.
    /// </summary>
    public Palette AsRamp() => _stops.Length > 1 ? this : new([_stops[0] * 0.08, _stops[0], Rgbf.Lerp(_stops[0], Rgbf.White, 0.55)]);

    /// <summary>The layer's colours for its mode; missing or invalid ones come from <paramref name="defaults"/>.</summary>
    public static Palette For(LightLayer layer, IReadOnlyList<string> defaults)
    {
        var mine = Valid(layer.Colors);
        var fallback = Valid(defaults);
        if (fallback.Count == 0) fallback.Add(Rgbf.White);
        if (mine.Count == 0) mine = fallback;
        switch (layer.ColorMode)
        {
            case SceneColorMode.Single:
                return new([mine[0]]);
            case SceneColorMode.Dual:
                var second = mine.Count > 1 ? mine[1] : fallback.Count > 1 ? fallback[1] : Rgbf.Black;
                return new([mine[0], second]);
            default:
                return new(mine.Take(7).ToArray());
        }
    }

    static List<Rgbf> Valid(IEnumerable<string>? colors)
    {
        var list = new List<Rgbf>();
        if (colors is null) return list;
        foreach (var s in colors)
            if (Rgbf.TryParse(s, out var c)) list.Add(c);
        return list;
    }
}

internal static class SceneMath
{
    public static double Frac(double x) => x - Math.Floor(x);

    public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    public static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }

    /// <summary>Signed distance from a to b on a unit circle, in -0.5..0.5.</summary>
    public static double WrapDelta(double a, double b)
    {
        double d = Frac(a - b + 0.5) - 0.5;
        return d;
    }
}
