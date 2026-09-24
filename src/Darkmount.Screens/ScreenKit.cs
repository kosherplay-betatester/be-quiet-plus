using System.Text;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// Shared helpers for the media/clock/network/timer screens: text that falls back to other fonts for missing glyphs
/// (CJK, emoji) and is reordered for right-to-left scripts, word wrapping, small path-drawn glyphs and image cropping.
/// </summary>
internal static class ScreenKit
{
    // ================================================================ text

    private static readonly object FallbackGate = new();
    private static readonly Dictionary<(SKTypeface, int), SKTypeface> FallbackCache = [];

    /// <summary>Width of <paramref name="text"/>, including glyphs drawn from fallback fonts.</summary>
    public static float Measure(string text, SKFont font)
    {
        float w = 0;
        foreach (var (run, tf) in Runs(text, font.Typeface ?? SKTypeface.Default))
            w += WithTypeface(font, tf, f => f.MeasureText(run));
        return w;
    }

    /// <summary>
    /// Draws <paramref name="text"/> (logical order) with font fallback and right-to-left reordering.
    /// Returns the drawn width.
    /// </summary>
    public static float DrawText(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint,
        SKTextAlign align = SKTextAlign.Left)
    {
        var runs = Runs(Visual(text), font.Typeface ?? SKTypeface.Default);
        float width = 0;
        foreach (var (run, tf) in runs) width += WithTypeface(font, tf, f => f.MeasureText(run));
        float cx = align switch
        {
            SKTextAlign.Center => x - width / 2,
            SKTextAlign.Right => x - width,
            _ => x,
        };
        foreach (var (run, tf) in runs)
        {
            float start = cx;
            cx += WithTypeface(font, tf, f =>
            {
                canvas.DrawText(run, start, baseline, SKTextAlign.Left, f, paint);
                return f.MeasureText(run);
            });
        }
        return width;
    }

    /// <summary>Truncates with an ellipsis so the text fits (never splits a surrogate pair).</summary>
    public static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        if (Measure(text, font) <= maxWidth) return text;
        const string ell = "…";
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[..SafeCut(text, mid)].TrimEnd() + ell, font) <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        int cut = SafeCut(text, lo);
        return cut == 0 ? ell : text[..cut].TrimEnd() + ell;
    }

    /// <summary>
    /// Greedy word wrap into at most <paramref name="maxLines"/> lines; the last line is ellipsized. Words wider than a
    /// line (or text without spaces, e.g. CJK) are broken between characters.
    /// </summary>
    public static List<string> Wrap(string text, SKFont font, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        string rest = (text ?? "").Trim();
        while (rest.Length > 0 && lines.Count < maxLines)
        {
            if (Measure(rest, font) <= maxWidth)
            {
                lines.Add(rest);
                break;
            }
            if (lines.Count == maxLines - 1)
            {
                lines.Add(Ellipsize(rest, font, maxWidth));
                break;
            }
            int fit = FittingPrefix(rest, font, maxWidth);
            int space = rest.LastIndexOf(' ', Math.Max(0, fit - 1), fit);
            int cut = space > 0 ? space : fit > 0 ? fit : char.IsHighSurrogate(rest[0]) && rest.Length > 1 ? 2 : 1;
            lines.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        return lines;
    }

    /// <summary>Longest prefix length (in chars, surrogate-safe) that fits <paramref name="maxWidth"/>.</summary>
    private static int FittingPrefix(string text, SKFont font, float maxWidth)
    {
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[..SafeCut(text, mid)], font) <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return SafeCut(text, lo);
    }

    private static int SafeCut(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index > 0 && index < text.Length && char.IsLowSurrogate(text[index])) index--;
        return index;
    }

    private static float WithTypeface(SKFont font, SKTypeface tf, Func<SKFont, float> action)
    {
        if (ReferenceEquals(tf, font.Typeface)) return action(font);
        using var f = Theme.Font(tf, font.Size);
        return action(f);
    }

    /// <summary>Splits text into runs that share a typeface: the preferred one, or a system fallback for missing glyphs.</summary>
    internal static List<(string Text, SKTypeface Typeface)> Runs(string text, SKTypeface preferred)
    {
        var runs = new List<(string, SKTypeface)>();
        if (string.IsNullOrEmpty(text)) return runs;
        var sb = new StringBuilder();
        SKTypeface? current = null;
        foreach (var rune in text.EnumerateRunes())
        {
            int cp = rune.Value;
            SKTypeface tf;
            bool joiner = cp is 0x200D or (>= 0xFE00 and <= 0xFE0F) or (>= 0x1F3FB and <= 0x1F3FF);
            if (current != null && (joiner || (Rune.IsWhiteSpace(rune) && current.ContainsGlyph(cp)))) tf = current;
            else if (preferred.ContainsGlyph(cp)) tf = preferred;
            else if (current != null && !ReferenceEquals(current, preferred) && current.ContainsGlyph(cp)) tf = current;
            else tf = Fallback(preferred, cp);

            if (current != null && !ReferenceEquals(tf, current))
            {
                runs.Add((sb.ToString(), current));
                sb.Clear();
            }
            current = tf;
            sb.Append(rune.ToString());
        }
        if (current != null && sb.Length > 0) runs.Add((sb.ToString(), current));
        return runs;
    }

    private static SKTypeface Fallback(SKTypeface preferred, int cp)
    {
        lock (FallbackGate)
        {
            if (FallbackCache.TryGetValue((preferred, cp), out var cached)) return cached;
            SKTypeface tf;
            try
            {
                tf = SKFontManager.Default.MatchCharacter(preferred.FamilyName, preferred.FontStyle, null, cp) ?? preferred;
            }
            catch (Exception)
            {
                tf = preferred;
            }
            // Keep one instance per family/style so runs of the same fallback font merge.
            foreach (var existing in FallbackCache.Values)
            {
                if (existing.FamilyName == tf.FamilyName && existing.FontStyle.Weight == tf.FontStyle.Weight &&
                    existing.FontStyle.Slant == tf.FontStyle.Slant)
                {
                    if (!ReferenceEquals(existing, tf) && !ReferenceEquals(tf, preferred)) tf.Dispose();
                    tf = existing;
                    break;
                }
            }
            if (FallbackCache.Count < 4096) FallbackCache[(preferred, cp)] = tf;
            return tf;
        }
    }

    // ---------------------------------------------------------------- bidi (minimal)

    /// <summary>
    /// Reorders a logical string into visual (left-to-right drawing) order for Hebrew/Arabic text: right-to-left runs are
    /// reversed and brackets mirrored, digits and Latin keep their order. Arabic letters are not shaped.
    /// Strings without right-to-left characters are returned unchanged.
    /// </summary>
    public static string Visual(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        bool any = false;
        foreach (var r in text.EnumerateRunes())
            if (IsRtl(r.Value)) { any = true; break; }
        if (!any) return text;

        var runes = text.EnumerateRunes().ToArray();
        int n = runes.Length;
        var dir = new int[n]; // +1 RTL, -1 LTR, 0 neutral
        for (int i = 0; i < n; i++)
            dir[i] = IsRtl(runes[i].Value) ? 1 : Rune.IsLetterOrDigit(runes[i]) ? -1 : 0;

        int para = 1;
        foreach (int d in dir)
            if (d != 0) { para = d; break; }

        // Neutrals take the direction of their strong neighbours when both agree, otherwise the paragraph direction.
        var resolved = (int[])dir.Clone();
        for (int i = 0; i < n; i++)
        {
            if (dir[i] != 0) continue;
            int prev = para, next = para;
            for (int j = i - 1; j >= 0; j--) if (dir[j] != 0) { prev = dir[j]; break; }
            for (int j = i + 1; j < n; j++) if (dir[j] != 0) { next = dir[j]; break; }
            resolved[i] = prev == next ? prev : para;
        }

        var runs = new List<(int Dir, List<Rune> Chars)>();
        for (int i = 0; i < n; i++)
        {
            if (runs.Count == 0 || runs[^1].Dir != resolved[i]) runs.Add((resolved[i], []));
            runs[^1].Chars.Add(runes[i]);
        }
        if (para == 1) runs.Reverse();

        var sb = new StringBuilder(text.Length);
        foreach (var (d, chars) in runs)
        {
            if (d == 1)
                for (int i = chars.Count - 1; i >= 0; i--) sb.Append(Mirror(chars[i]).ToString());
            else
                foreach (var c in chars) sb.Append(c.ToString());
        }
        return sb.ToString();
    }

    private static bool IsRtl(int cp) => cp is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);

    private static Rune Mirror(Rune r) => r.Value switch
    {
        '(' => new Rune(')'), ')' => new Rune('('),
        '[' => new Rune(']'), ']' => new Rune('['),
        '{' => new Rune('}'), '}' => new Rune('{'),
        '<' => new Rune('>'), '>' => new Rune('<'),
        '«' => new Rune('»'), '»' => new Rune('«'),
        _ => r,
    };

    // ================================================================ colours

    public static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t), a.Alpha);

    public static SKColor Lighten(SKColor c, float t) => Blend(c, SKColors.White, t);

    public static SKColor Darken(SKColor c, float t) => Blend(c, SKColors.Black, t);

    // ================================================================ panels

    /// <summary>A short accent strip on a panel's left edge (same as the stats rows).</summary>
    public static void DrawAccentTab(SKCanvas canvas, SKRect panel, SKColor accent)
    {
        var tab = new SKRect(panel.Left, panel.Top + 10, panel.Left + 3, panel.Bottom - 10);
        using var paint = Theme.Fill(accent);
        canvas.DrawRoundRect(tab, 1.5f, 1.5f, paint);
    }

    /// <summary>Graph background with the dashed grid, as on the stats screen.</summary>
    public static void DrawGraphFrame(SKCanvas canvas, SKRect graph)
    {
        using var bg = Theme.Fill(Theme.GraphBackground);
        canvas.DrawRoundRect(graph, 5, 5, bg);
        WaveGraph.DrawGrid(canvas, graph, 4);
    }

    // ================================================================ images

    /// <summary>Draws <paramref name="image"/> scaled to cover <paramref name="dest"/> (centre crop, no letterboxing).</summary>
    public static void DrawCover(SKCanvas canvas, SKImage image, SKRect dest, SKPaint? paint = null)
    {
        float scale = Math.Max(dest.Width / image.Width, dest.Height / image.Height);
        float sw = dest.Width / scale, sh = dest.Height / scale;
        var src = new SKRect((image.Width - sw) / 2, (image.Height - sh) / 2, (image.Width + sw) / 2, (image.Height + sh) / 2);
        canvas.DrawImage(image, src, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    // ================================================================ glyphs (paths, no icon font needed)

    /// <summary>Two beamed eighth notes (♫) filling the square <paramref name="r"/>.</summary>
    public static void DrawMusicNote(SKCanvas canvas, SKRect r, SKColor color)
    {
        float w = r.Width, h = r.Height;
        using var fill = Theme.Fill(color);
        var heads = new[] { new SKPoint(r.Left + w * 0.30f, r.Top + h * 0.76f), new SKPoint(r.Left + w * 0.74f, r.Top + h * 0.66f) };
        float rx = w * 0.14f, ry = h * 0.105f, stem = w * 0.075f;
        foreach (var c in heads)
        {
            canvas.Save();
            canvas.RotateDegrees(-22, c.X, c.Y);
            canvas.DrawOval(c, new SKSize(rx, ry), fill);
            canvas.Restore();
        }
        float s1 = heads[0].X + rx * 0.86f, s2 = heads[1].X + rx * 0.86f;
        float top1 = r.Top + h * 0.26f, top2 = r.Top + h * 0.16f;
        canvas.DrawRect(new SKRect(s1 - stem, top1, s1, heads[0].Y - ry * 0.2f), fill);
        canvas.DrawRect(new SKRect(s2 - stem, top2, s2, heads[1].Y - ry * 0.2f), fill);
        using var beam = new SKPathBuilder();
        float bh = h * 0.13f;
        beam.MoveTo(s1 - stem, top1);
        beam.LineTo(s2, top2);
        beam.LineTo(s2, top2 + bh);
        beam.LineTo(s1 - stem, top1 + bh);
        beam.Close();
        using var path = beam.Detach();
        canvas.DrawPath(path, fill);
    }

    /// <summary>A right-pointing play triangle with softly rounded corners, optically centred on <paramref name="c"/>.</summary>
    public static void DrawPlay(SKCanvas canvas, SKPoint c, float size, SKColor color)
    {
        float h = size, w = size * 0.88f;
        float left = c.X - w * 0.40f; // optical centre sits left of the geometric one
        using var b = new SKPathBuilder();
        b.MoveTo(left, c.Y - h / 2);
        b.LineTo(left + w, c.Y);
        b.LineTo(left, c.Y + h / 2);
        b.Close();
        using var path = b.Detach();
        using var fill = Theme.Fill(color);
        using var round = Theme.Stroke(color, size * 0.14f);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, round);
    }

    /// <summary>Two pause bars centred on <paramref name="c"/>.</summary>
    public static void DrawPause(SKCanvas canvas, SKPoint c, float size, SKColor color)
    {
        float bw = size * 0.30f, gap = size * 0.26f, h = size;
        using var fill = Theme.Fill(color);
        canvas.DrawRoundRect(new SKRect(c.X - gap / 2 - bw, c.Y - h / 2, c.X - gap / 2, c.Y + h / 2), bw * 0.3f, bw * 0.3f, fill);
        canvas.DrawRoundRect(new SKRect(c.X + gap / 2, c.Y - h / 2, c.X + gap / 2 + bw, c.Y + h / 2), bw * 0.3f, bw * 0.3f, fill);
    }

    /// <summary>Static equalizer bars (a "playing" hint) in <paramref name="r"/>.</summary>
    public static void DrawEqualizer(SKCanvas canvas, SKRect r, SKColor color)
    {
        float[] heights = [0.55f, 1f, 0.7f, 0.85f];
        float bw = r.Width / (heights.Length * 1.6f - 0.6f);
        using var fill = Theme.Fill(color);
        for (int i = 0; i < heights.Length; i++)
        {
            float x = r.Left + i * bw * 1.6f;
            canvas.DrawRoundRect(new SKRect(x, r.Bottom - r.Height * heights[i], x + bw, r.Bottom), bw / 2, bw / 2, fill);
        }
    }

    /// <summary>A tomato: red body with a highlight and a green leaf crown. <paramref name="filled"/> false draws an outline.</summary>
    public static void DrawTomato(SKCanvas canvas, SKPoint c, float radius, bool filled)
    {
        var red = new SKColor(0xF0, 0x44, 0x3A);
        var green = new SKColor(0x4C, 0xC9, 0x5A);
        var body = new SKRect(c.X - radius, c.Y - radius * 0.86f, c.X + radius, c.Y + radius * 0.94f);
        if (filled)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(new SKPoint(c.X - radius * 0.35f, c.Y - radius * 0.35f), radius * 1.5f,
                    [Lighten(red, 0.25f), red, Darken(red, 0.3f)], [0f, 0.45f, 1f], SKShaderTileMode.Clamp),
            };
            canvas.DrawOval(body, paint);
        }
        else
        {
            using var outline = Theme.Stroke(Theme.TextDim.WithAlpha(0xB0), Math.Max(1.2f, radius * 0.13f));
            canvas.DrawOval(body, outline);
        }

        // Five-pointed leaf crown on top.
        float cy = body.Top + radius * 0.10f, outer = radius * 0.55f, inner = radius * 0.2f;
        using var b = new SKPathBuilder();
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float rr = i % 2 == 0 ? outer : inner;
            var p = new SKPoint(c.X + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr * 0.62f);
            if (i == 0) b.MoveTo(p); else b.LineTo(p);
        }
        b.Close();
        using var leaf = b.Detach();
        using var leafPaint = Theme.Fill(filled ? green : Theme.TextDim.WithAlpha(0xB0));
        canvas.DrawPath(leaf, leafPaint);
    }

    /// <summary>A down (download) or up (upload) arrow in a tinted circle.</summary>
    public static void DrawArrowBadge(SKCanvas canvas, SKPoint c, float radius, bool up, SKColor color)
    {
        using var bg = Theme.Fill(color.WithAlpha(0x38));
        canvas.DrawCircle(c, radius, bg);
        float len = radius * 0.95f, head = radius * 0.48f, dir = up ? -1 : 1;
        using var stroke = Theme.Stroke(color, Math.Max(1.6f, radius * 0.24f));
        canvas.DrawLine(c.X, c.Y - dir * len / 2, c.X, c.Y + dir * len / 2, stroke);
        float tipY = c.Y + dir * len / 2;
        canvas.DrawLine(c.X - head, tipY - dir * head, c.X, tipY, stroke);
        canvas.DrawLine(c.X + head, tipY - dir * head, c.X, tipY, stroke);
    }
}
