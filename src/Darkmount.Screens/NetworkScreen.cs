using System.Globalization;
using SkiaSharp;

namespace Darkmount.Screens;

/// <summary>
/// Download and upload throughput: two rows styled like the stats screen (big number with auto units, peak, and a
/// wave graph of <see cref="MetricHistory.NetDown"/>/<see cref="MetricHistory.NetUp"/>), plus the adapter name.
/// </summary>
public sealed class NetworkScreen : IDockScreen
{
    internal static readonly SKColor DownColor = Theme.Mem;
    internal static readonly SKColor UpColor = Theme.Cpu;
    private const float LeftColumn = 146;
    private const double MinScale = 64 * 1024; // graphs never zoom in further than 64 KB/s full scale
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Units = ["KB/s", "MB/s", "GB/s"];

    public string Name => "Network";

    public void Render(SKCanvas canvas, ScreenContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var n = ctx.Network;
        var h = ctx.History;
        DrawRow(canvas, new SKRect(3, 3, 317, 103), "DOWNLOAD", DownColor, up: false, n?.DownloadBytesPerSec, h.NetDown, h.Capacity);
        DrawRow(canvas, new SKRect(3, 106, 317, 206), "UPLOAD", UpColor, up: true, n?.UploadBytesPerSec, h.NetUp, h.Capacity);
        DrawFooter(canvas, new SKRect(3, 209, 317, 237), n);
    }

    /// <summary>
    /// Splits a rate into a number and unit with 1024-based units: "0.3" KB/s, "845" KB/s, "0.98" MB/s, "11.8" MB/s,
    /// "2.50" GB/s. Values never show 4 integer digits (≥ 1000 moves to the next unit).
    /// </summary>
    public static (string Value, string Unit) FormatRate(double bytesPerSec)
    {
        if (double.IsNaN(bytesPerSec) || double.IsInfinity(bytesPerSec)) return ("--", Units[0]);
        double v = Math.Max(0, bytesPerSec) / 1024;
        int u = 0;
        while (u < Units.Length - 1 && v >= 999.5)
        {
            v /= 1024;
            u++;
        }
        string format = u == 0
            ? (v < 99.95 ? "0.0" : "0")
            : (v < 9.995 ? "0.00" : v < 99.95 ? "0.0" : "0");
        return (v.ToString(format, Inv), Units[u]);
    }

    // ---------------------------------------------------------------- rows

    private static void DrawRow(SKCanvas canvas, SKRect panel, string label, SKColor accent, bool up, double? rate,
        IReadOnlyList<double?> history, int capacity)
    {
        Theme.DrawPanel(canvas, panel);
        ScreenKit.DrawAccentTab(canvas, panel, accent);
        float left = panel.Left + 11, colRight = panel.Left + LeftColumn;

        // Header: arrow badge + label.
        ScreenKit.DrawArrowBadge(canvas, new SKPoint(left + 8, panel.Top + 15), 8, up, accent);
        using (var labelFont = Theme.Font(Theme.Bold, 12.5f))
        using (var labelPaint = Theme.Fill(accent))
            canvas.DrawText(label, left + 22, panel.Top + 19.5f, SKTextAlign.Left, labelFont, labelPaint);

        // Big value + unit, shrunk to fit the column.
        bool known = rate is double r && !double.IsNaN(r);
        var (value, unit) = known ? FormatRate(rate!.Value) : ("--", "");
        using var big = Theme.Font(Theme.Bold, 46);
        using var unitFont = Theme.Font(Theme.SemiBold, 15);
        float maxW = colRight - 8 - left;
        while (big.MeasureText(value) + 5 + unitFont.MeasureText(unit) > maxW && big.Size > 28) big.Size -= 1;
        float baseline = panel.Top + 67;
        using (var valuePaint = Theme.Fill(known ? Theme.Text : Theme.TextDim))
            canvas.DrawText(value, left, baseline, SKTextAlign.Left, big, valuePaint);
        using (var unitPaint = Theme.Fill(known ? Theme.TextSecondary : Theme.TextDim))
            canvas.DrawText(unit, left + big.MeasureText(value) + 5, baseline, SKTextAlign.Left, unitFont, unitPaint);

        // Peak over the visible history.
        double peak = rate is double cur && !double.IsNaN(cur) ? cur : 0;
        foreach (var v in history) if (v is double d && d > peak) peak = d;
        if (history.Count > 0 || known)
        {
            var (pv, pu) = FormatRate(peak);
            TextRuns.Draw(canvas, left, panel.Top + 88,
                "PEAK  ", Theme.Font(Theme.Bold, 10), Theme.TextDim,
                pv, Theme.Font(Theme.SemiBold, 13), Theme.TextSecondary,
                " " + pu, Theme.Font(Theme.SemiBold, 10.5f), Theme.TextDim);
        }

        // Graph with an auto "nice" full scale.
        var graph = new SKRect(colRight, panel.Top + 5, panel.Right - 5, panel.Bottom - 5);
        ScreenKit.DrawGraphFrame(canvas, graph);
        double max = NiceCeiling(Math.Max(peak * 1.1, MinScale));
        var inner = new SKRect(graph.Left + 1, graph.Top + 2, graph.Right - 1, graph.Bottom - 1);
        WaveGraph.Draw(canvas, inner, history, 0, max, accent, fill: true, capacity);
        WaveGraph.Draw(canvas, inner, history, 0, max, accent, fill: false, capacity);

        if (history.Count == 0) return; // no scale on an empty graph
        var (sv, su) = FormatRate(max);
        using var scaleFont = Theme.Font(Theme.SemiBold, 9.5f);
        using var scalePaint = Theme.Fill(Theme.TextDim);
        canvas.DrawText(TrimZeros(sv) + " " + su, graph.Right - 6, graph.Top + 12, SKTextAlign.Right, scaleFont, scalePaint);
    }

    private static void DrawFooter(SKCanvas canvas, SKRect panel, NetworkInfo? n)
    {
        Theme.DrawPanel(canvas, panel);
        float left = panel.Left + 11, baseline = panel.MidY + 4.5f;
        bool online = n is not null;
        using (var dot = Theme.Fill(online ? Theme.Gpu : Theme.TextDim))
            canvas.DrawCircle(left + 3.5f, panel.MidY, 3.5f, dot);

        using var nameFont = Theme.Font(Theme.SemiBold, 12.5f);
        using var namePaint = Theme.Fill(online ? Theme.TextSecondary : Theme.TextDim);
        string text = !online ? "No network data" : n!.AdapterName is { Length: > 0 } a ? a : "Network";

        float x = left + 13;
        if (n is not null)
        {
            // Combined throughput on the right, adapter name ellipsized in the remaining space.
            var (tv, tu) = FormatRate(n.DownloadBytesPerSec + n.UploadBytesPerSec);
            float totalW = TextRuns.DrawRight(canvas, panel.Right - 10, baseline,
                "TOTAL  ", Theme.Font(Theme.Bold, 10), Theme.TextDim,
                tv, Theme.Font(Theme.SemiBold, 13), Theme.Text,
                " " + tu, Theme.Font(Theme.SemiBold, 10.5f), Theme.TextDim);
            text = ScreenKit.Ellipsize(text, nameFont, panel.Right - 10 - totalW - 12 - x);
        }
        ScreenKit.DrawText(canvas, text, x, baseline, nameFont, namePaint);
    }

    /// <summary>Rounds up to 1, 2 or 5 × 10ⁿ in the display unit (e.g. 12.3 MB/s → 20 MB/s).</summary>
    internal static double NiceCeiling(double bytesPerSec)
    {
        double unit = 1024;
        while (bytesPerSec / unit >= 1000 && unit < 1024.0 * 1024 * 1024) unit *= 1024;
        double v = bytesPerSec / unit;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(v, 1e-9))));
        foreach (double step in (double[])[1, 2, 5, 10])
            if (v <= step * pow + 1e-9) return step * pow * unit;
        return 10 * pow * unit;
    }

    private static string TrimZeros(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
}
