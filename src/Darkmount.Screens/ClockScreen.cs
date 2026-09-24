using System.Globalization;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// A desk clock: huge HH:mm (no seconds — the dock refreshes about every 5 s), the weekday and date, a month calendar
/// with today highlighted, and CPU/GPU temperatures when the snapshot has them.
/// </summary>
public sealed class ClockScreen : IDockScreen
{
    private static readonly SKColor Accent = Theme.Mem;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const float TimeBandBottom = 116;
    private static readonly SKRect DatePanel = new(3, 119, 161, 237);
    private static readonly SKRect CalendarPanel = new(164, 119, 317, 237);

    private readonly bool _use24Hour;
    private readonly DayOfWeek _firstDay;

    /// <param name="use24Hour">24-hour "21:47" (default) or 12-hour "9:47 PM".</param>
    /// <param name="firstDayOfWeek">First calendar column; defaults to the current culture's.</param>
    public ClockScreen(bool use24Hour = true, DayOfWeek? firstDayOfWeek = null)
    {
        _use24Hour = use24Hour;
        _firstDay = firstDayOfWeek ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
    }

    public string Name => "Clock";

    public void Render(SKCanvas canvas, ScreenContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var now = ctx.Now;
        DrawTime(canvas, now);
        DrawDate(canvas, now, ctx);
        DrawCalendar(canvas, now);
    }

    // ---------------------------------------------------------------- time

    private void DrawTime(SKCanvas canvas, DateTime now)
    {
        string hh = _use24Hour ? now.ToString("HH", Inv) : (now.Hour % 12 == 0 ? 12 : now.Hour % 12).ToString(Inv);
        string mm = now.ToString("mm", Inv);
        string? ampm = _use24Hour ? null : (now.Hour < 12 ? "AM" : "PM");

        float size = 104;
        using var font = Theme.Font(Theme.Bold, size);
        using var ampmFont = Theme.Font(Theme.Bold, 22);
        float Width()
        {
            float gap = font.Size * 0.035f;
            return font.MeasureText(hh) + gap + font.MeasureText(":") + gap + font.MeasureText(mm)
                   + (ampm is null ? 0 : 8 + ampmFont.MeasureText(ampm));
        }
        while (Width() > 286 && font.Size > 60) font.Size -= 2;

        var m = font.Metrics;
        float capH = m.CapHeight;
        float baseline = (TimeBandBottom - capH) / 2 + capH + 2;
        float x = (DockRenderer.Width - Width()) / 2;
        float g = font.Size * 0.035f;

        // Soft accent glow behind the digits.
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(160, baseline - capH / 2), 170,
                [Accent.WithAlpha(0x22), Accent.WithAlpha(0x00)], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, DockRenderer.Width, TimeBandBottom + 10, glow);
        }

        using var digits = Theme.Fill(Theme.Text);
        canvas.DrawText(hh, x, baseline, SKTextAlign.Left, font, digits);
        x += font.MeasureText(hh) + g;

        // The colon sits at x-height in Segoe UI: lift it to the optical middle of the digits.
        float lift = (capH - m.XHeight) / 2;
        using var colon = Theme.Fill(Accent);
        canvas.DrawText(":", x, baseline - lift, SKTextAlign.Left, font, colon);
        x += font.MeasureText(":") + g;

        canvas.DrawText(mm, x, baseline, SKTextAlign.Left, font, digits);
        x += font.MeasureText(mm);

        if (ampm is not null)
        {
            using var p = Theme.Fill(Theme.TextSecondary);
            canvas.DrawText(ampm, x + 8, baseline - capH + ampmFont.Metrics.CapHeight, SKTextAlign.Left, ampmFont, p);
        }
    }

    // ---------------------------------------------------------------- date + stats

    private static void DrawDate(SKCanvas canvas, DateTime now, ScreenContext ctx)
    {
        var panel = DatePanel;
        Theme.DrawPanel(canvas, panel);
        ScreenKit.DrawAccentTab(canvas, panel, Accent);
        float left = panel.Left + 12;
        var s = ctx.Snapshot;
        bool stats = s.CpuTemp is not null || s.GpuTemp is not null;
        float top = panel.Top + (stats ? 0 : 15); // without the stats line the date block is centred

        using var dayFont = Theme.Font(Theme.Bold, 24);
        using var dayPaint = Theme.Fill(Accent);
        canvas.DrawText(now.ToString("dddd", Inv), left, top + 31, SKTextAlign.Left, dayFont, dayPaint);

        TextRuns.Draw(canvas, left, top + 56,
            now.Day.ToString(Inv), Theme.Font(Theme.Bold, 18), Theme.Text,
            " " + now.ToString("MMMM", Inv), Theme.Font(Theme.SemiBold, 17), Theme.Text);

        TextRuns.Draw(canvas, left, top + 75,
            now.Year.ToString(Inv), Theme.Font(Theme.SemiBold, 12.5f), Theme.TextSecondary,
            "  ·  ", Theme.Font(Theme.Bold, 12), Theme.TextDim,
            "Week " + ISOWeek.GetWeekOfYear(now).ToString(Inv), Theme.Font(Theme.SemiBold, 12.5f), Theme.TextSecondary);

        // Tiny stats line: coloured dot, label, temperature.
        if (!stats) return;
        float baseline = panel.Bottom - 11;
        using var sep = Theme.Stroke(Theme.PanelBorder, 1);
        canvas.DrawLine(left, baseline - 19, panel.Right - 10, baseline - 19, sep);
        float x = left;
        x = DrawTemp(canvas, x, baseline, "CPU", Theme.Cpu, s.CpuTemp);
        DrawTemp(canvas, x + 12, baseline, "GPU", Theme.Gpu, s.GpuTemp);
    }

    private static float DrawTemp(SKCanvas canvas, float x, float baseline, string label, SKColor accent, double? temp)
    {
        using (var dot = Theme.Fill(accent))
            canvas.DrawCircle(x + 3.5f, baseline - 4.5f, 3.5f, dot);
        x += 11;
        x += TextRuns.Draw(canvas, x, baseline,
            label + " ", Theme.Font(Theme.Bold, 11.5f), accent,
            temp is double t ? t.ToString("0", Inv) : "--", Theme.Font(Theme.Bold, 15), temp is null ? Theme.TextDim : Theme.TempColor(temp),
            "°", Theme.Font(Theme.SemiBold, 13), Theme.TextSecondary);
        return x;
    }

    // ---------------------------------------------------------------- calendar

    /// <summary>Column of the 1st (0-based from <paramref name="first"/>) and number of week rows in the month.</summary>
    internal static (int Offset, int Rows) MonthGrid(int year, int month, DayOfWeek first)
    {
        int offset = ((int)new DateTime(year, month, 1).DayOfWeek - (int)first + 7) % 7;
        int days = DateTime.DaysInMonth(year, month);
        return (offset, (offset + days + 6) / 7);
    }

    private void DrawCalendar(SKCanvas canvas, DateTime now)
    {
        var panel = CalendarPanel;
        Theme.DrawPanel(canvas, panel);

        float left = panel.Left + 6, right = panel.Right - 6;
        float cellW = (right - left) / 7;
        var (offset, rows) = MonthGrid(now.Year, now.Month, _firstDay);
        bool sixRows = rows > 5;

        using var headFont = Theme.Font(Theme.Bold, 11.5f);
        using var headPaint = Theme.Fill(Accent);
        canvas.DrawText(now.ToString("MMMM", Inv).ToUpperInvariant(), left + 4, panel.Top + (sixRows ? 15 : 16),
            SKTextAlign.Left, headFont, headPaint);

        using var dowFont = Theme.Font(Theme.Bold, 9.5f);
        float dowBaseline = panel.Top + (sixRows ? 28 : 31);
        for (int i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)_firstDay + i) % 7);
            bool weekend = dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
            using var p = Theme.Fill(weekend ? ScreenKit.Blend(Theme.TextDim, Accent, 0.35f) : Theme.TextDim);
            canvas.DrawText(Inv.DateTimeFormat.GetShortestDayName(dow)[..1], left + cellW * (i + 0.5f), dowBaseline,
                SKTextAlign.Center, dowFont, p);
        }

        float gridTop = dowBaseline + (sixRows ? 3 : 4), gridBottom = panel.Bottom - (sixRows ? 3 : 4);
        float rowH = (gridBottom - gridTop) / rows;
        using var dayFont = Theme.Font(Theme.SemiBold, sixRows ? 10.5f : 11.5f);
        using var todayFont = Theme.Font(Theme.Bold, sixRows ? 10.5f : 11.5f);
        float capH = dayFont.Metrics.CapHeight;
        int days = DateTime.DaysInMonth(now.Year, now.Month);

        for (int day = 1; day <= days; day++)
        {
            int cell = offset + day - 1;
            float cx = left + cellW * (cell % 7 + 0.5f);
            float cy = gridTop + rowH * (cell / 7 + 0.5f);
            string text = day.ToString(Inv);
            if (day == now.Day)
            {
                float r = sixRows ? rowH / 2 : Math.Min(cellW, rowH) / 2 + 1.5f; // never overlap the rows around it
                using var disc = Theme.Fill(Accent);
                canvas.DrawRoundRect(new SKRect(cx - cellW / 2 + 1, cy - r, cx + cellW / 2 - 1, cy + r), r, r, disc);
                using var p = Theme.Fill(SKColors.White);
                canvas.DrawText(text, cx, cy + capH / 2, SKTextAlign.Center, todayFont, p);
            }
            else
            {
                using var p = Theme.Fill(day < now.Day ? Theme.TextDim : Theme.TextSecondary);
                canvas.DrawText(text, cx, cy + capH / 2, SKTextAlign.Center, dayFont, p);
            }
        }
    }
}
