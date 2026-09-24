using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>Smooth line / filled-area graphs of a value history.</summary>
public static class WaveGraph
{
    /// <summary>
    /// Draws <paramref name="values"/> (oldest → newest) into <paramref name="rect"/>, newest at the right edge.
    /// Nulls break the curve. <paramref name="fill"/> draws a translucent gradient area, otherwise a 2 px line
    /// with a soft glow and a dot on the newest value. <paramref name="capacity"/> sets the horizontal spacing
    /// (a history that is not full yet grows in from the right).
    /// </summary>
    public static void Draw(SKCanvas canvas, SKRect rect, IReadOnlyList<double?> values, double min, double max,
        SKColor color, bool fill, int capacity = 60)
    {
        if (values.Count == 0 || max <= min) return;
        int slots = Math.Max(Math.Max(capacity, values.Count), 2);
        float step = rect.Width / (slots - 1);

        canvas.Save();
        canvas.ClipRect(rect);

        var segments = Segments(rect, values, min, max, step);
        foreach (var pts in segments)
        {
            if (pts.Count == 1)
            {
                if (!fill)
                {
                    using var dot = Theme.Fill(color);
                    canvas.DrawCircle(pts[0], 1.5f, dot);
                }
                continue;
            }

            using var path = SmoothPath(pts);
            if (fill)
            {
                using var areaBuilder = new SKPathBuilder();
                areaBuilder.AddPath(path);
                areaBuilder.LineTo(pts[^1].X, rect.Bottom);
                areaBuilder.LineTo(pts[0].X, rect.Bottom);
                areaBuilder.Close();
                using var area = areaBuilder.Detach();
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Shader = SKShader.CreateLinearGradient(
                        new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                        [color.WithAlpha(0x66), color.WithAlpha(0x0C)], SKShaderTileMode.Clamp),
                };
                canvas.DrawPath(area, paint);
                using var edge = Theme.Stroke(color.WithAlpha(0x70), 1f);
                canvas.DrawPath(path, edge);
            }
            else
            {
                using var glow = Theme.Stroke(color.WithAlpha(0x50), 4.5f);
                glow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f);
                canvas.DrawPath(path, glow);
                using var line = Theme.Stroke(color, 2f);
                canvas.DrawPath(path, line);
            }
        }

        canvas.Restore();

        // Newest value marker (drawn unclipped so it is not cut in half at the right edge).
        if (!fill && segments.Count > 0 && values[^1] is not null)
        {
            var last = segments[^1][^1];
            last.X = Math.Min(last.X, rect.Right - 3);
            using var halo = Theme.Fill(color.WithAlpha(0x55));
            canvas.DrawCircle(last, 4.5f, halo);
            using var dot = Theme.Fill(color);
            canvas.DrawCircle(last, 2.5f, dot);
        }
    }

    /// <summary>Faint horizontal grid lines dividing <paramref name="rect"/> into <paramref name="divisions"/> bands.</summary>
    public static void DrawGrid(SKCanvas canvas, SKRect rect, int divisions = 4)
    {
        using var paint = Theme.Stroke(Theme.Grid, 1f);
        paint.StrokeCap = SKStrokeCap.Butt;
        paint.PathEffect = SKPathEffect.CreateDash([2f, 3f], 0);
        for (int i = 1; i < divisions; i++)
        {
            float y = MathF.Round(rect.Top + rect.Height * i / divisions) + 0.5f;
            canvas.DrawLine(rect.Left + 4, y, rect.Right - 4, y, paint);
        }
    }

    /// <summary>A dashed horizontal reference line at <paramref name="value"/>.</summary>
    public static void DrawDashedLevel(SKCanvas canvas, SKRect rect, double value, double min, double max, SKColor color)
    {
        if (max <= min) return;
        float y = Map(value, min, max, rect);
        using var paint = Theme.Stroke(color, 1.5f);
        paint.StrokeCap = SKStrokeCap.Butt;
        paint.PathEffect = SKPathEffect.CreateDash([5f, 4f], 0);
        canvas.DrawLine(rect.Left + 2, y, rect.Right - 2, y, paint);
    }

    internal static float Map(double value, double min, double max, SKRect rect)
    {
        double t = Math.Clamp((value - min) / (max - min), 0, 1);
        // Keep a 2 px margin so a 2 px line at the extremes stays fully visible.
        return (float)(rect.Bottom - 2 - t * (rect.Height - 4));
    }

    private static List<List<SKPoint>> Segments(SKRect rect, IReadOnlyList<double?> values, double min, double max, float step)
    {
        var result = new List<List<SKPoint>>();
        List<SKPoint>? current = null;
        int n = values.Count;
        for (int i = 0; i < n; i++)
        {
            if (values[i] is not double v || double.IsNaN(v))
            {
                current = null;
                continue;
            }
            if (current == null)
            {
                current = [];
                result.Add(current);
            }
            float x = rect.Right - (n - 1 - i) * step;
            current.Add(new SKPoint(x, Map(v, min, max, rect)));
        }
        return result;
    }

    /// <summary>Monotone-ish smoothing: cubic segments with horizontal tangents at the midpoint x.</summary>
    private static SKPath SmoothPath(List<SKPoint> pts)
    {
        using var path = new SKPathBuilder();
        path.MoveTo(pts[0]);
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            float mx = (a.X + b.X) / 2;
            path.CubicTo(mx, a.Y, mx, b.Y, b.X, b.Y);
        }
        return path.Detach();
    }
}
