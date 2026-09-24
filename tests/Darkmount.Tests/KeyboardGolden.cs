using Darkmount.QLink;

namespace Darkmount.Tests;

/// <summary>Parses the example packets of docs/QLINK_KEYBOARD.md Appendix A ("bytes | 00 xN | crc lo hi").</summary>
static class KeyboardGolden
{
    public static byte[] Packet(string line)
    {
        var parts = line.Split('|');
        var head = Convert.FromHexString(parts[0].Replace(" ", ""));
        int padding = int.Parse(parts[1].Trim().Split('x')[1]);
        var crc = Convert.FromHexString(parts[2].Replace("crc", "").Replace(" ", ""));
        if (head.Length + padding != Frame.Size - 2) throw new FormatException($"Bad golden line: {line}");
        var p = new byte[Frame.Size];
        head.CopyTo(p, 0);
        p[62] = crc[0];
        p[63] = crc[1];
        return p;
    }

    /// <summary>Re-frames a captured request with the golden packet's SID and request id.</summary>
    public static byte[] Reframe(byte[] golden, Frame request) =>
        Frame.Build(golden[2], golden[4], request.Feature, request.Command, request.Data).Single();
}
