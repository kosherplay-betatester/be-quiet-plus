using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace Darkmount.Sensors;

/// <summary>One MSI Afterburner monitoring entry. <see cref="Value"/> is null when Afterburner has no data.</summary>
public sealed record MahmEntry(string Name, string Units, double? Value, uint Gpu);

/// <summary>Parsed content of Afterburner's "MAHMSharedMemory".</summary>
public sealed partial class MahmData
{
    private readonly Dictionary<string, MahmEntry> _byName = new(StringComparer.OrdinalIgnoreCase);

    public MahmData(IReadOnlyList<MahmEntry> entries)
    {
        Entries = entries;
        foreach (var e in entries)
            _byName.TryAdd(e.Name, e);
    }

    public IReadOnlyList<MahmEntry> Entries { get; }

    /// <summary>Entry by name (case-insensitive), or null.</summary>
    public MahmEntry? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Value of the named entry (case-insensitive), or null when missing or without data.</summary>
    public double? Value(string name) => Find(name)?.Value;

    public bool HasFpsLowEntries => Find(PointOneLow) is not null || Find(OneLow) is not null;

    private const string PointOneLow = "Framerate 0.1% Low";
    private const string OneLow = "Framerate 1% Low";

    /// <summary>
    /// GPU index to use for GPU values: <paramref name="gpuIndex"/> when non-zero, otherwise the index with the
    /// highest power reading, else the highest index with a temperature, else the highest index seen (0 if none).
    /// An unindexed "GPU xxx" entry counts as index 1.
    /// </summary>
    public int SelectGpu(int gpuIndex)
    {
        if (gpuIndex > 0) return gpuIndex;

        var gpus = Entries
            .Select(e => (Parsed: ParseGpuName(e.Name), e.Value))
            .Where(x => x.Parsed is not null)
            .Select(x => (Index: x.Parsed!.Value.Index, Metric: x.Parsed.Value.Metric, x.Value))
            .ToList();
        if (gpus.Count == 0) return 0;

        var power = gpus.Where(g => g.Metric == "power" && g.Value is not null).ToList();
        if (power.Count > 0) return power.MaxBy(g => g.Value!.Value).Index;

        var temp = gpus.Where(g => g.Metric == "temperature" && g.Value is not null).ToList();
        if (temp.Count > 0) return temp.Max(g => g.Index);

        return gpus.Max(g => g.Index);
    }

    /// <summary>Maps the entries to a partial snapshot (FPS values are always filled; the hub decides if a game runs).</summary>
    public Snapshot ToSnapshot(SensorOptions options)
    {
        int gpu = SelectGpu(options.GpuIndex);
        double? pointOne = Value(PointOneLow);
        double? one = Value(OneLow);

        return new Snapshot
        {
            CpuTemp = Value("CPU temperature"),
            CpuLoad = Value("CPU usage"),
            CpuPower = Value("CPU power"),
            RamUsedMb = Value("RAM usage"),
            GpuTemp = GpuValue(gpu, "temperature"),
            GpuLoad = GpuValue(gpu, "usage"),
            GpuPower = GpuValue(gpu, "power"),
            VramUsedMb = GpuValue(gpu, "memory usage"),
            Fps = Value("Framerate"),
            FpsLow = pointOne ?? one,
            FpsLowLabel = pointOne is not null || one is null ? "0.1% low" : "1% low",
        };
    }

    private double? GpuValue(int gpu, string metric)
    {
        if (gpu <= 0) return null;
        return Value($"GPU{gpu} {metric}") ?? (gpu == 1 ? Value($"GPU {metric}") : null);
    }

    internal static (int Index, string Metric)? ParseGpuName(string name)
    {
        var m = GpuNameRegex().Match(name);
        if (!m.Success) return null;
        int index = m.Groups[1].Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
        return (index, m.Groups[2].Value.ToLowerInvariant());
    }

    [GeneratedRegex(@"^GPU(\d*)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex GpuNameRegex();
}

/// <summary>Parser for MSI Afterburner's "MAHMSharedMemory" (v2.0 layout).</summary>
public static class MahmReader
{
    public const string MappingName = "MAHMSharedMemory";

    private const uint Signature = 0x4D41484D; // 'MAHM'
    private const int NameOffset = 0, NameLength = 260;
    private const int UnitsOffset = 260, UnitsLength = 260;
    private const int DataOffset = 1300;
    private const int GpuOffset = 1316;

    /// <summary>Parses the blob, or returns null when it is not a valid MAHM block.</summary>
    public static MahmData? Parse(byte[] blob)
    {
        try
        {
            if (blob.Length < 20) return null;
            var span = blob.AsSpan();
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != Signature) return null;

            long headerSize = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            long count = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
            long entrySize = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
            if (entrySize < DataOffset + 4 || count > 10_000) return null;

            var entries = new List<MahmEntry>((int)count);
            for (long i = 0; i < count; i++)
            {
                long o = headerSize + i * entrySize;
                if (o + entrySize > blob.Length) break;
                int off = (int)o;

                string name = SharedMemory.ReadString(span, off + NameOffset, NameLength);
                string units = SharedMemory.ReadString(span, off + UnitsOffset, UnitsLength);
                float raw = BinaryPrimitives.ReadSingleLittleEndian(span[(off + DataOffset)..]);
                double? value = float.IsNaN(raw) || float.IsInfinity(raw) || raw == float.MaxValue ? null : raw;
                uint gpu = entrySize >= GpuOffset + 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(span[(off + GpuOffset)..])
                    : 0;
                entries.Add(new MahmEntry(name, units, value, gpu));
            }
            return new MahmData(entries);
        }
        catch
        {
            return null;
        }
    }
}
