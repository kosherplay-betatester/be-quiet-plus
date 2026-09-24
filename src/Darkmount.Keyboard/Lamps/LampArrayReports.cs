using static Darkmount.Keyboard.Lamps.LampArrayUsages;

namespace Darkmount.Keyboard.Lamps;

/// <summary>
/// Pure encoders/decoders for the LampArray feature reports, driven by a <see cref="LampArrayLayout"/>.
/// <c>bufferLength</c> pads a report to the length the transport needs (Windows wants every feature report at the
/// device's maximum feature-report length).
/// </summary>
public static class LampArrayReports
{
    public static LampArrayAttributes DecodeAttributes(LampArrayLayout layout, ReadOnlySpan<byte> report)
    {
        var r = layout.Attributes;
        Check(r, report);
        return new LampArrayAttributes(
            Int(r, report, LampCount),
            Int(r, report, BoundingBoxWidthInMicrometers),
            Int(r, report, BoundingBoxHeightInMicrometers),
            Int(r, report, BoundingBoxDepthInMicrometers),
            (LampArrayKind)Int(r, report, LampArrayKindUsage),
            Int(r, report, MinUpdateIntervalInMicroseconds));
    }

    /// <summary>LampAttributesRequestReport: selects the lamp the next LampAttributesResponseReport describes.</summary>
    public static byte[] EncodeAttributesRequest(LampArrayLayout layout, int lampId, int bufferLength = 0)
    {
        var r = layout.AttributesRequest;
        var buffer = r.NewBuffer(bufferLength);
        r.Get(LampId).Write(buffer, lampId);
        return buffer;
    }

    public static LampInfo DecodeLampAttributes(LampArrayLayout layout, ReadOnlySpan<byte> report)
    {
        var r = layout.AttributesResponse;
        Check(r, report);
        return new LampInfo(
            Int(r, report, LampId),
            Int(r, report, PositionXInMicrometers),
            Int(r, report, PositionYInMicrometers),
            Int(r, report, PositionZInMicrometers),
            Int(r, report, UpdateLatencyInMicroseconds),
            (LampPurposes)Int(r, report, LampPurposesUsage),
            Int(r, report, RedLevelCount),
            Int(r, report, GreenLevelCount),
            Int(r, report, BlueLevelCount),
            r.Find(IntensityLevelCount) is { } intensity ? (int)intensity.Read(report) : null,
            Int(r, report, IsProgrammable) != 0,
            Int(r, report, InputBinding));
    }

    /// <summary>
    /// Splits per-lamp colours into LampMultiUpdateReports of at most <see cref="LampArrayLayout.MaxLampsPerMultiUpdate"/>
    /// lamps (ascending lamp id). Only the last report carries LampUpdateComplete, so the device shows the whole set at once.
    /// </summary>
    public static IReadOnlyList<byte[]> EncodeMultiUpdates(
        LampArrayLayout layout, IEnumerable<KeyValuePair<int, LampColor>> colors, int bufferLength = 0)
    {
        var items = colors.OrderBy(kv => kv.Key).ToList();
        for (int k = 1; k < items.Count; k++)
            if (items[k].Key == items[k - 1].Key) throw new ArgumentException($"Lamp {items[k].Key} appears twice.", nameof(colors));

        var r = layout.MultiUpdate;
        int perReport = layout.MaxLampsPerMultiUpdate;
        var reports = new List<byte[]>((items.Count + perReport - 1) / perReport);
        for (int start = 0; start < items.Count; start += perReport)
        {
            int count = Math.Min(perReport, items.Count - start);
            var buffer = r.NewBuffer(bufferLength);
            r.Get(LampCount).Write(buffer, count);
            r.Find(LampUpdateFlags)?.Write(buffer, start + count == items.Count ? LampUpdateComplete : 0);
            for (int slot = 0; slot < count; slot++)
            {
                var (lampId, color) = items[start + slot];
                if (lampId < 0) throw new ArgumentOutOfRangeException(nameof(colors), lampId, "Lamp ids are non-negative.");
                r.Get(LampId, slot).Write(buffer, lampId);
                WriteColor(r, buffer, slot, color);
            }
            reports.Add(buffer);
        }
        return reports;
    }

    /// <summary>LampRangeUpdateReport: one colour for lamps <paramref name="firstLampId"/>..<paramref name="lastLampId"/> inclusive.</summary>
    public static byte[] EncodeRangeUpdate(
        LampArrayLayout layout, int firstLampId, int lastLampId, LampColor color, bool updateComplete = true, int bufferLength = 0)
    {
        if (firstLampId < 0 || lastLampId < firstLampId)
            throw new ArgumentOutOfRangeException(nameof(lastLampId), $"Invalid lamp range {firstLampId}..{lastLampId}.");
        var r = layout.RangeUpdate;
        var buffer = r.NewBuffer(bufferLength);
        r.Find(LampUpdateFlags)?.Write(buffer, updateComplete ? LampUpdateComplete : 0);
        r.Get(LampIdStart).Write(buffer, firstLampId);
        r.Get(LampIdEnd).Write(buffer, lastLampId);
        WriteColor(r, buffer, 0, color);
        return buffer;
    }

    /// <summary>LampArrayControlReport: true = device runs its own (firmware) effects, false = host controls the lamps.</summary>
    public static byte[] EncodeControl(LampArrayLayout layout, bool autonomousMode, int bufferLength = 0)
    {
        var r = layout.Control;
        var buffer = r.NewBuffer(bufferLength);
        r.Get(AutonomousMode).Write(buffer, autonomousMode ? 1 : 0);
        return buffer;
    }

    /// <summary>Writes colour slot <paramref name="slot"/>; without an intensity field, RGB is pre-scaled by I/255.</summary>
    static void WriteColor(LampReportLayout r, byte[] buffer, int slot, LampColor color)
    {
        var intensity = r.Find(IntensityUpdateChannel, slot);
        var (red, green, blue) = intensity is null && color.I != 255
            ? (Scale(color.R, color.I), Scale(color.G, color.I), Scale(color.B, color.I))
            : (color.R, color.G, color.B);
        r.Get(RedUpdateChannel, slot).Write(buffer, red);
        r.Get(GreenUpdateChannel, slot).Write(buffer, green);
        r.Get(BlueUpdateChannel, slot).Write(buffer, blue);
        intensity?.Write(buffer, color.I);
    }

    static byte Scale(byte value, byte intensity) => (byte)((value * intensity + 127) / 255);

    static int Int(LampReportLayout r, ReadOnlySpan<byte> report, ushort usage) =>
        r.Find(usage) is { } f ? (int)Math.Min(f.Read(report), int.MaxValue) : 0;

    static void Check(LampReportLayout r, ReadOnlySpan<byte> report)
    {
        if (report.Length < r.Length)
            throw new InvalidDataException($"Report {r.ReportId} is {report.Length} bytes, expected {r.Length}.");
        if (r.ReportId != 0 && report[0] != r.ReportId)
            throw new InvalidDataException($"Expected report id {r.ReportId}, got {report[0]}.");
    }
}
