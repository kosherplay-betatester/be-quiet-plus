using System.Buffers.Binary;

namespace Darkmount.Sensors;

/// <summary>One application hooked by RivaTuner Statistics Server.</summary>
/// <param name="AgeMs">Milliseconds since RTSS last updated the frame-rate period of this app.</param>
public sealed record RtssApp(int Pid, string ExeName, double Fps, double FrameTimeMs, uint AgeMs);

/// <summary>Parser for RivaTuner's "RTSSSharedMemoryV2".</summary>
public static class RtssReader
{
    public const string MappingName = "RTSSSharedMemoryV2";

    private const uint Signature = 0x52545353; // 'RTSS'

    /// <summary>
    /// Parses the app array. <paramref name="nowTicks"/> is the current <c>GetTickCount</c> value
    /// (<c>(uint)Environment.TickCount</c>). Returns an empty list for invalid blobs.
    /// </summary>
    public static IReadOnlyList<RtssApp> Parse(byte[] blob, uint nowTicks)
    {
        var apps = new List<RtssApp>();
        try
        {
            if (blob.Length < 20) return apps;
            var span = blob.AsSpan();
            if (U32(span, 0) != Signature) return apps;

            long entrySize = U32(span, 8);
            long arrOffset = U32(span, 12);
            long arrSize = U32(span, 16);
            if (entrySize < 284 || arrSize > 4096) return apps;

            for (long i = 0; i < arrSize; i++)
            {
                long lo = arrOffset + i * entrySize;
                if (lo + entrySize > blob.Length) break;
                int o = (int)lo;

                uint pid = U32(span, o);
                if (pid == 0) continue;

                string path = SharedMemory.ReadString(span, o + 4, 260);
                uint time0 = U32(span, o + 268);
                uint time1 = U32(span, o + 272);
                uint frames = U32(span, o + 276);
                uint frameTimeUs = U32(span, o + 280);

                uint period = unchecked(time1 - time0);
                double fps = time1 != time0 && period < int.MaxValue ? frames * 1000.0 / period : 0;

                apps.Add(new RtssApp(
                    (int)pid,
                    ExeName(path),
                    fps,
                    frameTimeUs / 1000.0,
                    unchecked(nowTicks - time1)));
            }
        }
        catch
        {
            // never throw on bad shared memory; return what was parsed
        }
        return apps;
    }

    private static string ExeName(string path)
    {
        int slash = path.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static uint U32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
}
