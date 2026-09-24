using System.Buffers.Binary;

namespace Darkmount.QLink;

public enum ScreensaverMode : byte { Off = 0, Clock = 1, Image = 2 }

/// <summary>The media dock's 9-byte configuration (menu colour, clock format, screensaver, screen-off).</summary>
public sealed record DockConfig(byte MenuR, byte MenuG, byte MenuB, bool Clock24h, ScreensaverMode Screensaver,
    int IdleSeconds, int ScreenOffSeconds)
{
    /// <summary>Settings read from the user's keyboard before any testing (#DC4D00, 24 h, image after 30 s, off after 60 s).</summary>
    public static DockConfig UserOriginal { get; } = FromBytes(Convert.FromHexString("DC4D0001021E003C00"));

    public static DockConfig FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < 9) throw new ArgumentException("Dock config is 9 bytes", nameof(b));
        return new(b[0], b[1], b[2], b[3] == 1, (ScreensaverMode)b[4],
            BinaryPrimitives.ReadUInt16LittleEndian(b[5..]), BinaryPrimitives.ReadUInt16LittleEndian(b[7..]));
    }

    public byte[] ToBytes()
    {
        var b = new byte[9];
        b[0] = MenuR; b[1] = MenuG; b[2] = MenuB;
        b[3] = (byte)(Clock24h ? 1 : 0);
        b[4] = (byte)Screensaver;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), (ushort)IdleSeconds);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(7), (ushort)ScreenOffSeconds);
        return b;
    }
}

/// <summary>Typed media dock commands (feature 0x21).</summary>
public sealed class MediaDock(QLinkClient q)
{
    public const byte SlotScreensaver = 0, SlotArtwork = 1;
    public const byte FormatRgb565 = 1;
    public const int HeaderSize = 9;

    public bool IsConnected() => q.Send(Features.MediaDock, MediaDockCommands.GetState) is [1, ..];

    public DockConfig GetConfig() => DockConfig.FromBytes(q.Send(Features.MediaDock, MediaDockCommands.GetConfig));

    public void SetConfig(DockConfig config, int timeoutMs = 3000) =>
        q.Send(Features.MediaDock, MediaDockCommands.SetConfig, config.ToBytes(), timeoutMs);

    public void SetDateTime(DateTime local) => q.Send(Features.MediaDock, MediaDockCommands.SetDateTime, EncodeDateTime(local));

    /// <summary>Local wall-clock time as seconds since 1970-01-01, little-endian u32 (what IO Center sends).</summary>
    public static byte[] EncodeDateTime(DateTime local)
    {
        var seconds = (uint)(DateTime.SpecifyKind(local, DateTimeKind.Unspecified) - DateTime.UnixEpoch).TotalSeconds;
        return BitConverter.GetBytes(seconds);
    }

    /// <summary>Writes part of an image blob: offset 0 is the 9-byte header, pixels start at offset 9.</summary>
    public void SetImage(byte slot, uint offset, ReadOnlySpan<byte> data, int timeoutMs = 1500)
    {
        var payload = new byte[5 + data.Length];
        payload[0] = slot;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), offset);
        data.CopyTo(payload.AsSpan(5));
        q.Send(Features.MediaDock, MediaDockCommands.SetImage, payload, timeoutMs);
    }

    /// <summary>Pixel bytes per single-frame image write (55-byte payload = slot + offset + 50 pixels).</summary>
    public const int PixelsPerWrite = QLinkClient.MaxSingleFramePayload - 5;

    /// <summary>
    /// Writes image data (pixels start at offset 9) as single-frame writes, pipelined and never repeated
    /// (see <see cref="QLinkClient.SendWindowed"/>).
    /// </summary>
    public void SetImageData(byte slot, ReadOnlySpan<byte> pixels, int window = 4, int replyTimeoutMs = 3000)
    {
        var payloads = new List<byte[]>(pixels.Length / PixelsPerWrite + 1);
        for (int offset = 0; offset < pixels.Length; offset += PixelsPerWrite)
        {
            int len = Math.Min(PixelsPerWrite, pixels.Length - offset);
            var payload = new byte[5 + len];
            payload[0] = slot;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), (uint)(HeaderSize + offset));
            pixels.Slice(offset, len).CopyTo(payload.AsSpan(5));
            payloads.Add(payload);
        }
        q.SendWindowed(Features.MediaDock, MediaDockCommands.SetImage, payloads, window, replyTimeoutMs);
    }

    public static byte[] ImageHeader(int width, int height, int payloadLength, byte format = FormatRgb565)
    {
        var h = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(h, (uint)(payloadLength + HeaderSize));
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), (ushort)height);
        h[8] = format;
        return h;
    }
}
