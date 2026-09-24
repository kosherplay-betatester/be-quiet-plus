namespace Darkmount.QLink;

/// <summary>
/// One 64-byte QLink packet. First frame: [len][seq][sid][status][req][feature][command][data..55][crc lo][crc hi];
/// continuation frames: [len][seq][sid][data..59][crc]. seq bit 7 = more frames follow. See docs/QLINK_PROTOCOL.md §1.
/// </summary>
public readonly record struct Frame(byte Length, byte Seq, byte Sid, byte Status, byte ReqId, byte Feature, byte Command, byte[] Data)
{
    public const int Size = 64;
    public const int FirstFrameData = Size - 9;
    public const int ContinuationData = Size - 5;

    public int Index => Seq & 0x7F;
    public bool HasMore => (Seq & 0x80) != 0;
    public bool IsContinuation => Index > 0;

    /// <summary>Unsolicited device notification (request id 0 on a single/first frame).</summary>
    public bool IsNotification => ReqId == 0 && !IsContinuation;

    /// <summary>Builds the packets for one message, splitting the payload into continuation frames when needed.</summary>
    public static List<byte[]> Build(byte sid, byte reqId, byte feature, byte command, ReadOnlySpan<byte> data, byte status = 0)
    {
        int total = data.Length <= FirstFrameData ? 1 : 1 + (data.Length - FirstFrameData + ContinuationData - 1) / ContinuationData;
        var packets = new List<byte[]>(total);
        int offset = 0;
        for (int seq = 0; seq < total; seq++)
        {
            int len = Math.Min(seq == 0 ? FirstFrameData : ContinuationData, data.Length - offset);
            var p = new byte[Size];
            p[1] = total > 1 ? (byte)(seq | (seq == total - 1 ? 0 : 0x80)) : (byte)0;
            p[2] = sid;
            if (seq == 0)
            {
                p[0] = (byte)(len + 6);
                p[3] = status;
                p[4] = reqId;
                p[5] = feature;
                p[6] = command;
                data.Slice(offset, len).CopyTo(p.AsSpan(7));
            }
            else
            {
                p[0] = (byte)(len + 2);
                data.Slice(offset, len).CopyTo(p.AsSpan(3));
            }
            var crc = Crc16.Modbus(p.AsSpan(0, Size - 2));
            p[Size - 2] = (byte)crc;
            p[Size - 1] = (byte)(crc >> 8);
            packets.Add(p);
            offset += len;
        }
        return packets;
    }

    public static Frame Parse(ReadOnlySpan<byte> p)
    {
        if (p.Length < Size) throw new ArgumentException($"QLink packets are {Size} bytes", nameof(p));
        bool continuation = (p[1] & 0x7F) > 0;
        int start = continuation ? 3 : 7;
        int end = Math.Clamp(p[0] + 1, start, Size - 2);
        return continuation
            ? new Frame(p[0], p[1], p[2], 0, 0, 0, 0, p[start..end].ToArray())
            : new Frame(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[start..end].ToArray());
    }
}
