using System.Diagnostics.CodeAnalysis;
using static Darkmount.Keyboard.Lamps.LampArrayUsages;

namespace Darkmount.Keyboard.Lamps;

/// <summary>
/// One field of a LampArray feature report. <see cref="BitOffset"/> counts from the start of the report buffer as
/// HidSharp/Windows exchange it, i.e. byte 0 is the report id (0 when the descriptor uses none).
/// Values are little-endian, LSB-first bit packing (HID 1.11 §5.8).
/// </summary>
public readonly record struct LampReportField(ushort Usage, int BitOffset, int BitSize, long LogicalMaximum)
{
    /// <summary>Largest value the field accepts: the logical maximum when declared, else what fits the bit size.</summary>
    public long MaxValue
    {
        get
        {
            long bitMax = BitSize >= 63 ? long.MaxValue : (1L << BitSize) - 1;
            return LogicalMaximum > 0 ? Math.Min(LogicalMaximum, bitMax) : bitMax;
        }
    }

    public long Read(ReadOnlySpan<byte> report)
    {
        if ((BitOffset + BitSize + 7) / 8 > report.Length)
            throw new InvalidDataException($"Report too short ({report.Length} bytes) for field 0x{Usage:X2} at bit {BitOffset}.");
        long value = 0;
        for (int k = 0; k < BitSize; k++)
        {
            int bit = BitOffset + k;
            value |= (long)((report[bit >> 3] >> (bit & 7)) & 1) << k;
        }
        return value;
    }

    public void Write(Span<byte> report, long value)
    {
        if (value < 0 || value > MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Field 0x{Usage:X2} accepts 0..{MaxValue}.");
        if ((BitOffset + BitSize + 7) / 8 > report.Length)
            throw new ArgumentException($"Report buffer too short ({report.Length} bytes) for field 0x{Usage:X2}.", nameof(report));
        for (int k = 0; k < BitSize; k++)
        {
            int bit = BitOffset + k;
            if (((value >> k) & 1) != 0) report[bit >> 3] |= (byte)(1 << (bit & 7));
            else report[bit >> 3] &= (byte)~(1 << (bit & 7));
        }
    }

    public override string ToString() =>
        BitOffset % 8 == 0 && BitSize % 8 == 0 ? $"0x{Usage:X2}@{BitOffset / 8}:{BitSize}" : $"0x{Usage:X2}@bit{BitOffset}:{BitSize}";
}

/// <summary>Layout of one LampArray feature report, derived from the report descriptor.</summary>
public sealed class LampReportLayout(ushort usage, byte reportId, int length, IReadOnlyList<LampReportField> fields)
{
    /// <summary>The report's logical-collection usage (e.g. <see cref="LampArrayUsages.LampMultiUpdateReport"/>).</summary>
    public ushort Usage { get; } = usage;
    public byte ReportId { get; } = reportId;

    /// <summary>Report length in bytes including the report-id byte, as declared by the descriptor.</summary>
    public int Length { get; } = length;

    /// <summary>Fields in report order (constant padding omitted).</summary>
    public IReadOnlyList<LampReportField> Fields { get; } = fields;

    /// <summary>The <paramref name="index"/>-th field carrying <paramref name="usage"/> (e.g. LampId slot 3), or null.</summary>
    public LampReportField? Find(ushort usage, int index = 0)
    {
        foreach (var f in Fields)
            if (f.Usage == usage && index-- == 0) return f;
        return null;
    }

    public LampReportField Get(ushort usage, int index = 0) =>
        Find(usage, index) ?? throw new InvalidOperationException($"Report {ReportId} has no field 0x{usage:X2} #{index}.");

    public int CountOf(ushort usage) => Fields.Count(f => f.Usage == usage);

    /// <summary>A zeroed buffer with the report id in byte 0, at least <paramref name="minLength"/> bytes long.</summary>
    public byte[] NewBuffer(int minLength = 0)
    {
        var buffer = new byte[Math.Max(Length, minLength)];
        buffer[0] = ReportId;
        return buffer;
    }

    public override string ToString() => $"report {ReportId} (usage 0x{Usage:X2}), {Length} bytes: {string.Join(' ', Fields)}";
}

/// <summary>
/// Report ids and field layouts of the six LampArray feature reports, parsed from a HID report descriptor
/// (so 8/16-bit lamp ids, lamps per multi-update and presence of the intensity channel follow the device).
/// </summary>
public sealed class LampArrayLayout
{
    static readonly ushort[] ReportUsages =
    [
        LampArrayAttributesReport, LampAttributesRequestReport, LampAttributesResponseReport,
        LampMultiUpdateReport, LampRangeUpdateReport, LampArrayControlReport,
    ];

    LampArrayLayout(IReadOnlyDictionary<ushort, LampReportLayout> reports, bool isReference)
    {
        Attributes = reports[LampArrayAttributesReport];
        AttributesRequest = reports[LampAttributesRequestReport];
        AttributesResponse = reports[LampAttributesResponseReport];
        MultiUpdate = reports[LampMultiUpdateReport];
        RangeUpdate = reports[LampRangeUpdateReport];
        Control = reports[LampArrayControlReport];
        IsMicrosoftReference = isReference;

        Require(Attributes, LampCount);
        Require(AttributesRequest, LampId);
        Require(AttributesResponse, LampId);
        Require(MultiUpdate, LampCount);
        Require(MultiUpdate, LampId);
        Require(RangeUpdate, LampIdStart);
        Require(RangeUpdate, LampIdEnd);
        Require(Control, AutonomousMode);
        foreach (var channel in new[] { RedUpdateChannel, GreenUpdateChannel, BlueUpdateChannel })
        {
            Require(MultiUpdate, channel);
            Require(RangeUpdate, channel);
        }

        long slots = new[] { LampId, RedUpdateChannel, GreenUpdateChannel, BlueUpdateChannel }.Min(MultiUpdate.CountOf);
        long countMax = MultiUpdate.Get(LampCount).MaxValue;
        MaxLampsPerMultiUpdate = (int)Math.Min(slots, countMax);
        HasIntensityChannel = MultiUpdate.CountOf(IntensityUpdateChannel) >= MaxLampsPerMultiUpdate
                              && RangeUpdate.Find(IntensityUpdateChannel) is not null;
    }

    public LampReportLayout Attributes { get; }
    public LampReportLayout AttributesRequest { get; }
    public LampReportLayout AttributesResponse { get; }
    public LampReportLayout MultiUpdate { get; }
    public LampReportLayout RangeUpdate { get; }
    public LampReportLayout Control { get; }

    public IEnumerable<LampReportLayout> Reports => [Attributes, AttributesRequest, AttributesResponse, MultiUpdate, RangeUpdate, Control];

    /// <summary>True when this is the Microsoft reference layout (used only because the descriptor could not be parsed).</summary>
    public bool IsMicrosoftReference { get; }

    /// <summary>Lamps per LampMultiUpdateReport: min(LampId slots, colour slots, LampCount logical maximum).</summary>
    public int MaxLampsPerMultiUpdate { get; }

    /// <summary>Whether updates carry an IntensityUpdateChannel; without it, <see cref="LampColor.I"/> pre-scales RGB.</summary>
    public bool HasIntensityChannel { get; }

    public int MaxReportLength => Reports.Max(r => r.Length);

    /// <exception cref="FormatException">The descriptor is malformed or lacks a complete LampArray collection.</exception>
    public static LampArrayLayout Parse(ReadOnlySpan<byte> descriptor) => Build(descriptor, isReference: false);

    public static bool TryParse(ReadOnlySpan<byte> descriptor, [NotNullWhen(true)] out LampArrayLayout? layout, out string? error)
    {
        try
        {
            layout = Parse(descriptor);
            error = null;
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            layout = null;
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses the device descriptor; only if that fails returns <see cref="MicrosoftReference"/> and sets
    /// <paramref name="fallbackReason"/>. Callers should treat a fallback as a warning (the device may differ).
    /// </summary>
    public static LampArrayLayout ParseOrReference(ReadOnlySpan<byte> descriptor, out string? fallbackReason)
    {
        if (TryParse(descriptor, out var layout, out var error))
        {
            fallbackReason = null;
            return layout;
        }
        fallbackReason = $"Descriptor not usable ({error}); using the Microsoft reference LampArray layout.";
        return MicrosoftReference;
    }

    static LampArrayLayout? _reference;

    /// <summary>The layout of Microsoft's sample LampArray descriptor (report ids 1–6, 16-bit ids, 8 RGBI lamps per update).</summary>
    public static LampArrayLayout MicrosoftReference => _reference ??= Build(MicrosoftReferenceDescriptor, isReference: true);

    /// <summary>
    /// Microsoft's reference LampArray descriptor (as shipped in TinyUSB's <c>TUD_HID_REPORT_DESC_LIGHTING(1)</c> and the
    /// Windows LampArray firmware samples).
    /// </summary>
    public static ReadOnlySpan<byte> MicrosoftReferenceDescriptor =>
    [
        0x05, 0x59, 0x09, 0x01, 0xA1, 0x01,
        // 1: LampArrayAttributesReport
        0x85, 0x01, 0x09, 0x02, 0xA1, 0x02,
        0x09, 0x03, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x75, 0x10, 0x95, 0x01, 0xB1, 0x03,
        0x09, 0x04, 0x09, 0x05, 0x09, 0x06, 0x09, 0x07, 0x09, 0x08,
        0x15, 0x00, 0x27, 0xFF, 0xFF, 0xFF, 0x7F, 0x75, 0x20, 0x95, 0x05, 0xB1, 0x03,
        0xC0,
        // 2: LampAttributesRequestReport
        0x85, 0x02, 0x09, 0x20, 0xA1, 0x02,
        0x09, 0x21, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x75, 0x10, 0x95, 0x01, 0xB1, 0x02,
        0xC0,
        // 3: LampAttributesResponseReport
        0x85, 0x03, 0x09, 0x22, 0xA1, 0x02,
        0x09, 0x21, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x75, 0x10, 0x95, 0x01, 0xB1, 0x02,
        0x09, 0x23, 0x09, 0x24, 0x09, 0x25, 0x09, 0x27, 0x09, 0x26,
        0x15, 0x00, 0x27, 0xFF, 0xFF, 0xFF, 0x7F, 0x75, 0x20, 0x95, 0x05, 0xB1, 0x02,
        0x09, 0x28, 0x09, 0x29, 0x09, 0x2A, 0x09, 0x2B, 0x09, 0x2C, 0x09, 0x2D,
        0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x06, 0xB1, 0x02,
        0xC0,
        // 4: LampMultiUpdateReport
        0x85, 0x04, 0x09, 0x50, 0xA1, 0x02,
        0x09, 0x03, 0x09, 0x55, 0x15, 0x00, 0x25, 0x08, 0x75, 0x08, 0x95, 0x02, 0xB1, 0x02,
        0x09, 0x21, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x75, 0x10, 0x95, 0x08, 0xB1, 0x02,
        0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54, 0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54,
        0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54, 0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54,
        0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54, 0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54,
        0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54, 0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54,
        0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x20, 0xB1, 0x02,
        0xC0,
        // 5: LampRangeUpdateReport
        0x85, 0x05, 0x09, 0x60, 0xA1, 0x02,
        0x09, 0x55, 0x15, 0x00, 0x25, 0x08, 0x75, 0x08, 0x95, 0x01, 0xB1, 0x02,
        0x09, 0x61, 0x09, 0x62, 0x15, 0x00, 0x27, 0xFF, 0xFF, 0x00, 0x00, 0x75, 0x10, 0x95, 0x02, 0xB1, 0x02,
        0x09, 0x51, 0x09, 0x52, 0x09, 0x53, 0x09, 0x54, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x04, 0xB1, 0x02,
        0xC0,
        // 6: LampArrayControlReport
        0x85, 0x06, 0x09, 0x70, 0xA1, 0x02,
        0x09, 0x71, 0x15, 0x00, 0x25, 0x01, 0x75, 0x08, 0x95, 0x01, 0xB1, 0x02,
        0xC0,
        0xC0,
    ];

    static LampArrayLayout Build(ReadOnlySpan<byte> descriptor, bool isReference)
    {
        var parsed = HidReportDescriptor.Parse(descriptor);
        const uint application = ((uint)Page << 16) | LampArray;
        var lampFields = parsed.Fields
            .Where(f => f.Kind == HidReportKind.Feature && f.ApplicationUsage == application && f.UsagePage == Page)
            .ToList();
        if (lampFields.Count == 0)
            throw new FormatException("No LampArray application collection (usage page 0x59, usage 0x01) with feature reports.");

        var reports = new Dictionary<ushort, LampReportLayout>();
        foreach (var group in lampFields.GroupBy(f => f.ReportId))
        {
            if (Classify(group.ToList()) is not { } usage) continue;
            if (reports.ContainsKey(usage))
                throw new FormatException($"More than one feature report looks like LampArray report 0x{usage:X2}.");
            var fields = group.Select(f => new LampReportField(f.UsageId, 8 + f.BitOffset, f.BitSize, f.LogicalMaximum)).ToList();
            reports[usage] = new LampReportLayout(usage, group.Key, parsed.GetReportLength(HidReportKind.Feature, group.Key), fields);
        }

        var missing = ReportUsages.Where(u => !reports.ContainsKey(u)).Select(u => $"0x{u:X2}").ToList();
        if (missing.Count > 0) throw new FormatException($"LampArray report(s) missing: {string.Join(", ", missing)}.");
        try { return new LampArrayLayout(reports, isReference); }
        catch (InvalidOperationException e) { throw new FormatException(e.Message, e); }
    }

    /// <summary>Identifies a report by its logical collection usage, or (collection-less descriptors) by telltale usages.</summary>
    static ushort? Classify(List<HidField> fields)
    {
        var collections = fields.Select(f => f.CollectionUsage).Distinct().ToList();
        if (collections is [var c] && (c >> 16) == Page && ReportUsages.Contains((ushort)c)) return (ushort)c;

        bool Has(ushort usage) => fields.Any(f => f.UsageId == usage);
        if (Has(AutonomousMode)) return LampArrayControlReport;
        if (Has(LampIdStart)) return LampRangeUpdateReport;
        if (Has(PositionXInMicrometers)) return LampAttributesResponseReport;
        if (Has(RedUpdateChannel) && Has(LampId)) return LampMultiUpdateReport;
        if (Has(LampArrayKindUsage) || Has(BoundingBoxWidthInMicrometers)) return LampArrayAttributesReport;
        if (Has(LampId) && fields.All(f => f.UsageId == LampId)) return LampAttributesRequestReport;
        return null;
    }

    static void Require(LampReportLayout report, ushort usage)
    {
        if (report.Find(usage) is null)
            throw new InvalidOperationException($"LampArray report 0x{report.Usage:X2} (id {report.ReportId}) lacks usage 0x{usage:X2}.");
    }
}
