namespace Darkmount.Keyboard.Lamps;

/// <summary>Deterministic hashing and smooth value noise for the scene effects (same input → same output, no state).</summary>
internal static class Noise
{
    /// <summary>Integer hash → 0..1.</summary>
    public static double Hash(int a, int b = 0, int c = 0)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>1-D value noise, 0..1, smooth.</summary>
    public static double Value(double x)
    {
        double fx = Math.Floor(x);
        int ix = (int)fx;
        double t = x - fx;
        t = t * t * (3 - 2 * t);
        double a = Hash(ix, 7), b = Hash(ix + 1, 7);
        return a + (b - a) * t;
    }

    /// <summary>2-D value noise, 0..1, smooth.</summary>
    public static double Value(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        int ix = (int)fx, iy = (int)fy;
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        double top = a + (b - a) * tx, bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    /// <summary>Fractal (3-octave) value noise, 0..1.</summary>
    public static double Fbm(double x, double y)
    {
        double sum = Value(x, y) * 0.5714;                  // weights 4/7, 2/7, 1/7
        sum += Value(x * 2.03 + 17.1, y * 2.03 - 5.3) * 0.2857;
        sum += Value(x * 4.01 - 9.7, y * 4.01 + 31.9) * 0.1429;
        return sum;
    }
}
