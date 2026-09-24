using System.Buffers.Binary;

namespace Darkmount.Sensors;

/// <summary>HWiNFO reading type (subset).</summary>
public enum HwInfoReadingType : uint
{
    None = 0,
    Temperature = 1,
    Voltage = 2,
    Fan = 3,
    Current = 4,
    Power = 5,
    Clock = 6,
    Usage = 7,
    Other = 8,
}

/// <summary>One HWiNFO reading with the name of the sensor it belongs to.</summary>
public sealed record HwInfoReading(
    HwInfoReadingType Type, string SensorName, string LabelOrig, string LabelUser, string Unit, double Value);

/// <summary>Parser for HWiNFO's "Global\HWiNFO_SENS_SM2" shared memory.</summary>
public static class HwInfoReader
{
    public const string MappingName = @"Global\HWiNFO_SENS_SM2";

    private const uint Signature = 0x53695748; // 'HWiS'
    private const uint DeadSignature = 0x44414544; // 'DEAD' (HWiNFO shutting down)

    /// <summary>Parses all readings, or returns null when the blob is invalid or HWiNFO is shutting down.</summary>
    public static IReadOnlyList<HwInfoReading>? ParseReadings(byte[] blob)
    {
        try
        {
            if (blob.Length < 44) return null;
            var span = blob.AsSpan();
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(span);
            if (sig == DeadSignature || sig != Signature) return null;

            long sensorOffset = U32(span, 20), sensorSize = U32(span, 24), sensorCount = U32(span, 28);
            long readingOffset = U32(span, 32), readingSize = U32(span, 36), readingCount = U32(span, 40);
            if (readingSize < 292 || readingCount > 100_000 || sensorCount > 10_000) return null;

            var sensors = new List<string>();
            if (sensorSize >= 264)
            {
                for (long i = 0; i < sensorCount; i++)
                {
                    long o = sensorOffset + i * sensorSize;
                    if (o + sensorSize > blob.Length) break;
                    string user = SharedMemory.ReadString(span, (int)o + 136, 128);
                    string orig = SharedMemory.ReadString(span, (int)o + 8, 128);
                    sensors.Add(user.Length > 0 ? user : orig);
                }
            }

            var readings = new List<HwInfoReading>();
            for (long i = 0; i < readingCount; i++)
            {
                long lo = readingOffset + i * readingSize;
                if (lo + readingSize > blob.Length) break;
                int o = (int)lo;
                var type = (HwInfoReadingType)U32(span, o);
                int sensorIndex = (int)U32(span, o + 4);
                string sensorName = sensorIndex >= 0 && sensorIndex < sensors.Count ? sensors[sensorIndex] : "";
                double value = BinaryPrimitives.ReadDoubleLittleEndian(span[(o + 284)..]);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                readings.Add(new HwInfoReading(
                    type,
                    sensorName,
                    SharedMemory.ReadString(span, o + 12, 128),
                    SharedMemory.ReadString(span, o + 140, 128),
                    SharedMemory.ReadString(span, o + 268, 16),
                    value));
            }
            return readings;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Maps HWiNFO readings to a partial snapshot, or returns null when the blob is invalid.</summary>
    public static Snapshot? Parse(byte[] blob, SensorOptions options)
    {
        var readings = ParseReadings(blob);
        if (readings is null) return null;

        return new Snapshot
        {
            CpuTemp = Find(readings, options.CpuTempLabels, HwInfoReadingType.Temperature, gpu: false),
            CpuPower = Find(readings, options.CpuPowerLabels, HwInfoReadingType.Power, gpu: false),
            CpuLoad = Find(readings, options.CpuLoadLabels, HwInfoReadingType.Usage, gpu: false),
            GpuTemp = Find(readings, options.GpuTempLabels, HwInfoReadingType.Temperature, gpu: true),
            GpuPower = Find(readings, options.GpuPowerLabels, HwInfoReadingType.Power, gpu: true),
            GpuLoad = Find(readings, options.GpuLoadLabels, HwInfoReadingType.Usage, gpu: true),
            RamUsedMb = Megabytes(FindReading(readings, options.RamUsedLabels, null, gpu: false)),
            VramUsedMb = Megabytes(FindReading(readings, options.VramUsedLabels, null, gpu: true)),
        };
    }

    private static double? Find(IReadOnlyList<HwInfoReading> readings, string[] labels, HwInfoReadingType type, bool gpu) =>
        FindReading(readings, labels, type, gpu)?.Value;

    private static double? Megabytes(HwInfoReading? r) => r is null
        ? null
        : r.Unit.Equals("GB", StringComparison.OrdinalIgnoreCase) ? r.Value * 1024 : r.Value;

    /// <summary>
    /// Exact label match (orig or user label, any configured label) first, then "contains".
    /// Within a pass, earlier labels win; for GPU readings dedicated GPUs are preferred over integrated ones.
    /// </summary>
    private static HwInfoReading? FindReading(
        IReadOnlyList<HwInfoReading> readings, string[] labels, HwInfoReadingType? type, bool gpu)
    {
        var candidates = type is null ? readings : readings.Where(r => r.Type == type).ToList();
        if (gpu)
            candidates = candidates.OrderBy(r => IsIntegratedGpu(r.SensorName) ? 1 : 0).ToList(); // stable

        foreach (bool exact in new[] { true, false })
        {
            foreach (string label in labels)
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                var hit = candidates.FirstOrDefault(r => Matches(r.LabelOrig, label, exact) || Matches(r.LabelUser, label, exact));
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    private static bool Matches(string text, string label, bool exact) => exact
        ? text.Equals(label, StringComparison.OrdinalIgnoreCase)
        : text.Contains(label, StringComparison.OrdinalIgnoreCase);

    internal static bool IsIntegratedGpu(string sensorName) =>
        sensorName.Contains("(TM) Graphics", StringComparison.OrdinalIgnoreCase)
        || sensorName.Contains("UHD", StringComparison.OrdinalIgnoreCase)
        || sensorName.Contains("Iris", StringComparison.OrdinalIgnoreCase);

    private static uint U32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
}
