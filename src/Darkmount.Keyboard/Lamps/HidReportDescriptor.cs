namespace Darkmount.Keyboard.Lamps;

public enum HidReportKind : byte { Input, Output, Feature }

/// <summary>
/// One report element: a single value of an Input/Output/Feature main item after its Report Count has been expanded.
/// </summary>
/// <param name="BitOffset">Bit offset inside the report payload, i.e. after the report-ID byte.</param>
/// <param name="Usage">Full 32-bit usage (page &lt;&lt; 16 | id); 0 when the element has no usage (array items).</param>
/// <param name="CollectionUsage">Usage of the innermost enclosing collection (0 outside any collection).</param>
/// <param name="ApplicationUsage">Usage of the innermost enclosing Application collection.</param>
public readonly record struct HidField(
    HidReportKind Kind, byte ReportId, int BitOffset, int BitSize, uint Usage,
    bool IsConstant, bool IsVariable, long LogicalMinimum, long LogicalMaximum,
    uint CollectionUsage, uint ApplicationUsage)
{
    public ushort UsagePage => (ushort)(Usage >> 16);
    public ushort UsageId => (ushort)Usage;
}

/// <summary>
/// Pure HID report-descriptor parser (HID 1.11 §6.2.2). Tracks global state (with Push/Pop), local usages
/// (including Usage Minimum/Maximum ranges) and collections, and lays out every report per (kind, report id).
/// Constant items without a usage (padding) are not listed; they only advance the bit offsets.
/// </summary>
public sealed class HidReportDescriptor
{
    const int MaxReportBits = 8 * 65536;
    const int MaxUsageRange = 4096;

    readonly Dictionary<(HidReportKind, byte), int> _reportBits;

    HidReportDescriptor(List<HidField> fields, Dictionary<(HidReportKind, byte), int> reportBits, bool usesReportIds)
    {
        Fields = fields;
        _reportBits = reportBits;
        UsesReportIds = usesReportIds;
    }

    public IReadOnlyList<HidField> Fields { get; }

    /// <summary>True when the descriptor declares Report ID items (then every report starts with its id byte).</summary>
    public bool UsesReportIds { get; }

    /// <summary>All (kind, report id) pairs that have at least one main item.</summary>
    public IEnumerable<(HidReportKind Kind, byte ReportId)> Reports => _reportBits.Keys;

    /// <summary>Payload length in bytes (excluding the report-ID byte), rounded up to whole bytes.</summary>
    public int GetPayloadLength(HidReportKind kind, byte reportId) =>
        _reportBits.TryGetValue((kind, reportId), out int bits) ? (bits + 7) / 8 : 0;

    /// <summary>Length of the report buffer as Windows/HidSharp exchange it: 1 report-ID byte (0 when unused) + payload.</summary>
    public int GetReportLength(HidReportKind kind, byte reportId) => 1 + GetPayloadLength(kind, reportId);

    /// <exception cref="FormatException">Truncated item, unbalanced collections, invalid report id or oversized report.</exception>
    public static HidReportDescriptor Parse(ReadOnlySpan<byte> descriptor)
    {
        var fields = new List<HidField>();
        var reportBits = new Dictionary<(HidReportKind, byte), int>();
        var global = new GlobalState();
        var globalStack = new Stack<GlobalState>();
        var usages = new List<uint>();
        uint? usageMin = null, usageMax = null;
        var collections = new List<(byte Type, uint Usage)>();
        bool usesReportIds = false;

        int i = 0;
        while (i < descriptor.Length)
        {
            int itemStart = i;
            byte prefix = descriptor[i++];
            if (prefix == 0xFE) // long item: [0xFE][bDataSize][bLongItemTag][data…] — no defined long items, skip
            {
                if (i + 2 > descriptor.Length) throw new FormatException($"Truncated long item at offset {itemStart}.");
                i += 2 + descriptor[i];
                if (i > descriptor.Length) throw new FormatException($"Truncated long item at offset {itemStart}.");
                continue;
            }

            int size = (prefix & 3) == 3 ? 4 : prefix & 3;
            if (i + size > descriptor.Length) throw new FormatException($"Truncated item 0x{prefix:X2} at offset {itemStart}.");
            uint raw = 0;
            for (int k = 0; k < size; k++) raw |= (uint)descriptor[i + k] << (8 * k);
            long signed = size switch { 1 => (sbyte)raw, 2 => (short)raw, 4 => (int)raw, _ => 0 };
            i += size;

            int type = (prefix >> 2) & 3, tag = prefix >> 4;
            switch (type)
            {
                case 0: // Main
                    switch (tag)
                    {
                        case 0x8: AddFields(HidReportKind.Input, raw); break;
                        case 0x9: AddFields(HidReportKind.Output, raw); break;
                        case 0xB: AddFields(HidReportKind.Feature, raw); break;
                        case 0xA:
                            collections.Add(((byte)raw, usages.Count > 0 ? usages[0] : usageMin ?? 0));
                            break;
                        case 0xC:
                            if (collections.Count == 0) throw new FormatException($"End Collection without Collection at offset {itemStart}.");
                            collections.RemoveAt(collections.Count - 1);
                            break;
                    }
                    usages.Clear();
                    usageMin = usageMax = null;
                    break;

                case 1: // Global
                    switch (tag)
                    {
                        case 0x0: global.UsagePage = raw & 0xFFFF; break;
                        case 0x1: global.LogicalMin = signed; break;
                        case 0x2: global.LogicalMaxSigned = signed; global.LogicalMaxUnsigned = raw; break;
                        case 0x7: global.ReportSize = (int)Math.Min(raw, 64); break;
                        case 0x8:
                            if (raw is 0 or > 255) throw new FormatException($"Invalid Report ID {raw} at offset {itemStart}.");
                            global.ReportId = (byte)raw;
                            usesReportIds = true;
                            break;
                        case 0x9: global.ReportCount = (int)Math.Min(raw, MaxReportBits); break;
                        case 0xA: globalStack.Push(global); break;
                        case 0xB:
                            if (globalStack.Count == 0) throw new FormatException($"Pop without Push at offset {itemStart}.");
                            global = globalStack.Pop();
                            break;
                    }
                    break;

                case 2: // Local
                    uint full = size == 4 ? raw : (global.UsagePage << 16) | (raw & 0xFFFF);
                    switch (tag)
                    {
                        case 0x0: usages.Add(full); break;
                        case 0x1: usageMin = full; break;
                        case 0x2: usageMax = full; break;
                    }
                    if (usageMin is { } lo && usageMax is { } hi)
                    {
                        for (uint u = lo; u <= hi && u - lo < MaxUsageRange; u++) usages.Add(u);
                        usageMin = usageMax = null;
                    }
                    break;
            }
        }

        if (collections.Count != 0) throw new FormatException($"{collections.Count} collection(s) not closed.");
        return new HidReportDescriptor(fields, reportBits, usesReportIds);

        void AddFields(HidReportKind kind, uint mainFlags)
        {
            var key = (kind, global.ReportId);
            int offset = reportBits.GetValueOrDefault(key);
            long bits = (long)global.ReportSize * global.ReportCount;
            if (offset + bits > MaxReportBits) throw new FormatException($"Report {global.ReportId} ({kind}) is too large.");
            reportBits[key] = offset + (int)bits;

            // Main item data: bit 0 = Constant, bit 1 = Variable.
            bool isConstant = (mainFlags & 1) != 0, isVariable = (mainFlags & 2) != 0;
            long min = global.LogicalMin;
            long max = min >= 0 && global.LogicalMaxSigned < 0 ? global.LogicalMaxUnsigned : global.LogicalMaxSigned;
            uint collection = collections.Count > 0 ? collections[^1].Usage : 0;
            uint application = 0;
            for (int c = collections.Count - 1; c >= 0; c--)
                if (collections[c].Type == 1) { application = collections[c].Usage; break; }

            for (int e = 0; e < global.ReportCount; e++)
            {
                uint usage = isVariable && usages.Count > 0 ? usages[Math.Min(e, usages.Count - 1)] : 0;
                if (isConstant && usage == 0) continue; // padding
                fields.Add(new HidField(kind, global.ReportId, offset + e * global.ReportSize, global.ReportSize, usage,
                    isConstant, isVariable, min, max, collection, application));
            }
        }
    }

    struct GlobalState
    {
        public uint UsagePage;
        public long LogicalMin, LogicalMaxSigned, LogicalMaxUnsigned;
        public int ReportSize, ReportCount;
        public byte ReportId;
    }
}
