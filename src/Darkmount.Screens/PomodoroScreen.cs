using System.Globalization;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// Focus (Pomodoro) timer: a countdown ring in the phase colour (Focus orange, Break green, Long break teal) with a
/// big mm:ss inside, and the tomatoes completed today on the right. Paused and Ready states are drawn dimmed.
/// </summary>
public sealed class PomodoroScreen : IDockScreen
{
    internal static readonly SKColor FocusColor = Theme.Cpu;
    internal static readonly SKColor BreakColor = Theme.Gpu;
    internal static readonly SKColor LongBreakColor = new(0x2D, 0xD4, 0xBF);

    private static readonly SKPoint Center = new(112, 120);
    private const float RingRadius = 94, RingWidth = 13;
    private const float ColumnLeft = 228;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Name => "Focus timer";

    public void Render(SKCanvas canvas, ScreenContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Pomodoro is not { } p)
        {
            DrawOff(canvas);
            return;
        }
        var (color, active, label) = Style(p.Phase);

        double fraction = p.Total.Ticks > 0 ? Math.Clamp(p.Remaining.Ticks / (double)p.Total.Ticks, 0, 1) : 0;
        DrawRing(canvas, Center, fraction, color, active, hasTimer: true);
        DrawCenter(canvas, p, color, active, label);
        DrawToday(canvas, p.CompletedToday);
    }

    /// <summary>Colour, whether the timer is counting, and the label for a phase.</summary>
    private static (SKColor Color, bool Active, string Label) Style(string phase) => phase switch
    {
        PomodoroInfo.Focus => (FocusColor, true, "FOCUS"),
        PomodoroInfo.Break => (BreakColor, true, "BREAK"),
        PomodoroInfo.LongBreak => (LongBreakColor, true, "LONG BREAK"),
        PomodoroInfo.Paused => (Theme.TextDim, false, "PAUSED"),
        PomodoroInfo.Ready => (Theme.TextDim, false, "READY"),
        _ => (Theme.TextSecondary, true, (phase ?? "").ToUpperInvariant()),
    };

    /// <summary>No timer: an empty dial centred on the screen with a tomato and "not running".</summary>
    private static void DrawOff(SKCanvas canvas)
    {
        var c = new SKPoint(DockRenderer.Width / 2f, 120);
        DrawRing(canvas, c, 0, Theme.TextDim, active: false, hasTimer: false);
        using var labelFont = Theme.Font(Theme.Bold, 14);
        using var labelPaint = Theme.Fill(Theme.TextSecondary);
        canvas.DrawText("FOCUS TIMER", c.X, c.Y - 38, SKTextAlign.Center, labelFont, labelPaint);
        ScreenKit.DrawTomato(canvas, new SKPoint(c.X, c.Y + 2), 22, filled: true);
        using var capFont = Theme.Font(Theme.SemiBold, 13);
        using var capPaint = Theme.Fill(Theme.TextDim);
        canvas.DrawText("not running", c.X, c.Y + 50, SKTextAlign.Center, capFont, capPaint);
    }

    // ---------------------------------------------------------------- ring

    private static void DrawRing(SKCanvas canvas, SKPoint center, double fraction, SKColor color, bool active, bool hasTimer)
    {
        var oval = new SKRect(center.X - RingRadius, center.Y - RingRadius, center.X + RingRadius, center.Y + RingRadius);

        // Soft inner disc so the ring reads as a dial.
        using (var disc = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(center, RingRadius,
                [ScreenKit.Blend(Theme.Panel, color, active ? 0.10f : 0.02f), Theme.Background], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(center, RingRadius - RingWidth / 2, disc);
        }

        using (var track = Theme.Stroke(Theme.Track, RingWidth))
            canvas.DrawOval(oval, track);

        // Minute ticks just inside the ring.
        using (var tick = Theme.Stroke(Theme.Grid, 1.5f))
        {
            for (int i = 0; i < 60; i += 5)
            {
                double a = i / 60.0 * 2 * Math.PI;
                float r1 = RingRadius - RingWidth / 2 - 5, r2 = r1 - (i % 15 == 0 ? 6 : 3.5f);
                float sx = (float)Math.Sin(a), cy = -(float)Math.Cos(a);
                canvas.DrawLine(center.X + sx * r1, center.Y + cy * r1, center.X + sx * r2, center.Y + cy * r2, tick);
            }
        }

        if (!hasTimer || fraction <= 0) return;
        float sweep = (float)(fraction * 360);
        var arcColor = active ? color : ScreenKit.Blend(Theme.TextDim, Theme.Track, 0.15f);
        using var arc = new SKPathBuilder();
        arc.AddArc(oval, -90, Math.Min(sweep, 359.9f));
        using var path = arc.Detach();

        if (active)
        {
            using var glow = Theme.Stroke(color.WithAlpha(0x60), RingWidth + 6);
            glow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5);
            canvas.DrawPath(path, glow);
        }
        using (var stroke = Theme.Stroke(arcColor, RingWidth))
        {
            if (active)
            {
                // Darker at the start of the arc, full colour at the moving end.
                using var sweepShader = SKShader.CreateSweepGradient(center,
                    [ScreenKit.Darken(color, 0.35f), color], [0f, Math.Max(0.02f, sweep / 360f)]);
                stroke.Shader = sweepShader.WithLocalMatrix(SKMatrix.CreateRotationDegrees(-90, center.X, center.Y));
            }
            canvas.DrawPath(path, stroke);
        }

        // Bright cap at the moving end of the arc.
        double end = (-90 + sweep) * Math.PI / 180;
        var tip = new SKPoint(center.X + (float)Math.Cos(end) * RingRadius, center.Y + (float)Math.Sin(end) * RingRadius);
        using var cap = Theme.Fill(active ? ScreenKit.Lighten(color, 0.55f) : Theme.TextSecondary);
        canvas.DrawCircle(tip, RingWidth / 2 - 2.5f, cap);
    }

    // ---------------------------------------------------------------- centre

    private static void DrawCenter(SKCanvas canvas, PomodoroInfo p, SKColor color, bool active, string label)
    {
        // Phase label (with a pause glyph when paused).
        using var labelFont = Theme.Font(Theme.Bold, 14);
        using var labelPaint = Theme.Fill(active ? color : Theme.TextSecondary);
        float labelBaseline = Center.Y - 33;
        if (p.Phase == PomodoroInfo.Paused)
        {
            float w = labelFont.MeasureText(label);
            float x0 = Center.X - (w + 14) / 2;
            ScreenKit.DrawPause(canvas, new SKPoint(x0 + 4, labelBaseline - 5), 10, Theme.TextSecondary);
            canvas.DrawText(label, x0 + 14, labelBaseline, SKTextAlign.Left, labelFont, labelPaint);
        }
        else
        {
            canvas.DrawText(label, Center.X, labelBaseline, SKTextAlign.Center, labelFont, labelPaint);
        }

        // Big remaining time.
        string time = FormatRemaining(p.Remaining);
        using var big = Theme.Font(Theme.Bold, 54);
        while (big.MeasureText(time) > 2 * (RingRadius - RingWidth) - 14 && big.Size > 30) big.Size -= 2;
        using var timePaint = Theme.Fill(active ? Theme.Text : Theme.TextSecondary);
        canvas.DrawText(time, Center.X, Center.Y + big.Metrics.CapHeight / 2 + 2, SKTextAlign.Center, big, timePaint);

        // Caption: phase length.
        using var capFont = Theme.Font(Theme.SemiBold, 12.5f);
        using var capPaint = Theme.Fill(Theme.TextDim);
        string caption = "of " + FormatMinutes(p.Total);
        canvas.DrawText(caption, Center.X, Center.Y + 44, SKTextAlign.Center, capFont, capPaint);
    }

    // ---------------------------------------------------------------- today

    private static void DrawToday(SKCanvas canvas, int completed)
    {
        float x = ColumnLeft, right = DockRenderer.Width - 8;
        int count = Math.Max(0, completed);

        using (var labelFont = Theme.Font(Theme.Bold, 11.5f))
        using (var labelPaint = Theme.Fill(Theme.TextDim))
            canvas.DrawText("TODAY", x, 38, SKTextAlign.Left, labelFont, labelPaint);

        using (var big = Theme.Font(Theme.Bold, 46))
        using (var bigPaint = Theme.Fill(Theme.Text))
            canvas.DrawText(count.ToString(Inv), x - 2, 86, SKTextAlign.Left, big, bigPaint);

        using (var capFont = Theme.Font(Theme.SemiBold, 12.5f))
        using (var capPaint = Theme.Fill(Theme.TextSecondary))
            canvas.DrawText(count == 1 ? "pomodoro" : "pomodoros", x, 104, SKTextAlign.Left, capFont, capPaint);

        // Tomato grid: 3 per row, at most 3 rows; empty outlines fill the current row. Overflow shows "+N".
        const int perRow = 3, maxIcons = 9;
        const float r = 10.5f;
        float step = (right - x) / perRow;
        int slots = Math.Clamp((count + perRow - 1) / perRow * perRow, perRow, maxIcons);
        bool overflow = count > maxIcons;
        for (int i = 0; i < slots; i++)
        {
            var c = new SKPoint(x + step * (i % perRow) + step / 2 - 2, 132 + (i / perRow) * 28);
            if (overflow && i == maxIcons - 1)
            {
                using var f = Theme.Font(Theme.Bold, 13);
                using var paint = Theme.Fill(Theme.TextSecondary);
                canvas.DrawText("+" + (count - (maxIcons - 1)).ToString(Inv), c.X, c.Y + 5, SKTextAlign.Center, f, paint);
                continue;
            }
            ScreenKit.DrawTomato(canvas, c, r, filled: i < count);
        }
    }

    // ---------------------------------------------------------------- formatting

    /// <summary>mm:ss, rounded up so the display reads 25:00 at the start and 0:01 in the last second.</summary>
    internal static string FormatRemaining(TimeSpan t)
    {
        long seconds = Math.Max(0, (long)Math.Ceiling(t.TotalSeconds - 1e-6));
        return string.Create(Inv, $"{seconds / 60:00}:{seconds % 60:00}");
    }

    private static string FormatMinutes(TimeSpan t)
    {
        int minutes = (int)Math.Round(t.TotalMinutes);
        return minutes >= 60 && minutes % 60 == 0
            ? string.Create(Inv, $"{minutes / 60} h")
            : string.Create(Inv, $"{minutes} min");
    }
}
