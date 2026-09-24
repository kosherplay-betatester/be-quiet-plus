using System.Buffers.Binary;
using Darkmount.QLink;
using SkiaSharp;

namespace Darkmount.Keyboard;

/// <summary>
/// The eight LCD keys on the numpad (feature 0x20). Images are 120×120 JPEG, stored rotated 90° clockwise,
/// in a blob: [total u32][width u16][height u16][format u8 = 3][jpeg…]. Stored on the keyboard (persistent),
/// so writes should only happen on a user action.
/// </summary>
public sealed class DisplayKeys(QLinkClient q)
{
    public const int Count = 8;
    public const int ImageSize = 120;
    public const ushort FirstKeyId = 109; // KEY_ID_NUMPAD_B1..B8 = 109..116
    public const byte FormatJpeg = 3;
    const int HeaderSize = 9;
    const int ReadChunk = 54;
    const int WriteChunk = QLinkClient.MaxSingleFramePayload - 6; // key id + offset + 49 bytes

    public static ushort KeyId(int index) =>
        index is >= 0 and < Count ? (ushort)(FirstKeyId + index) : throw new ArgumentOutOfRangeException(nameof(index));

    public bool IsConnected() => q.Send(Features.Numpad, NumpadCommands.GetState) is [1, ..];

    /// <summary>Reads the stored JPEG of a key exactly as stored (rotated), or null if the key has no image.</summary>
    public byte[]? ReadStoredJpeg(int index)
    {
        var header = Read(index, 0, HeaderSize);
        if (header.Length < HeaderSize) return null;
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (total <= HeaderSize || total > 256 * 1024 || header[8] != FormatJpeg) return null;

        var jpeg = new byte[total - HeaderSize];
        for (int off = 0; off < jpeg.Length; off += ReadChunk)
        {
            int len = Math.Min(ReadChunk, jpeg.Length - off);
            var part = Read(index, HeaderSize + off, len);
            part.AsSpan(0, Math.Min(len, part.Length)).CopyTo(jpeg.AsSpan(off));
        }
        return jpeg;
    }

    /// <summary>Reads a key image upright (as the user sees it), or null.</summary>
    public SKBitmap? ReadImage(int index) => ReadStoredJpeg(index) is { } jpeg ? DecodeStored(jpeg) : null;

    /// <summary>Writes an image to a key (cover-scaled to 120×120, rotated for the panel, JPEG).</summary>
    public void WriteImage(int index, SKBitmap image) => WriteStoredJpeg(index, EncodeForKey(image));

    /// <summary>Writes an already-encoded stored JPEG (e.g. from a backup) back unchanged.</summary>
    public void WriteStoredJpeg(int index, byte[] jpeg)
    {
        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(jpeg.Length + HeaderSize));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), ImageSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), ImageSize);
        header[8] = FormatJpeg;
        Write(index, 0, header, 8000);
        // Single-frame writes, pipelined and never repeated (large writes make the firmware hold replies;
        // a repeated chunk corrupts the image).
        var payloads = new List<byte[]>();
        for (int off = 0; off < jpeg.Length; off += WriteChunk)
            payloads.Add(Payload(index, HeaderSize + off, jpeg.AsSpan(off, Math.Min(WriteChunk, jpeg.Length - off))));
        q.SendWindowed(Features.Numpad, NumpadCommands.SetImage, payloads);
    }

    static byte[] Payload(int index, int offset, ReadOnlySpan<byte> data)
    {
        var payload = new byte[6 + data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, KeyId(index));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), (uint)offset);
        data.CopyTo(payload.AsSpan(6));
        return payload;
    }

    byte[] Read(int index, int offset, int size)
    {
        var req = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(req, KeyId(index));
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(2), (uint)offset);
        BinaryPrimitives.WriteUInt32LittleEndian(req.AsSpan(6), (uint)size);
        return q.Send(Features.Numpad, NumpadCommands.GetImage, req);
    }

    void Write(int index, int offset, ReadOnlySpan<byte> data, int timeoutMs) =>
        q.Send(Features.Numpad, NumpadCommands.SetImage, Payload(index, offset, data), timeoutMs);

    /// <summary>Cover-scales to 120×120, rotates 90° clockwise (the panel's orientation) and encodes JPEG.</summary>
    public static byte[] EncodeForKey(SKBitmap source, int quality = 92)
    {
        using var square = new SKBitmap(ImageSize, ImageSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var c = new SKCanvas(square))
        {
            c.Clear(SKColors.Black);
            float scale = Math.Max((float)ImageSize / source.Width, (float)ImageSize / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            using var img = SKImage.FromBitmap(source);
            c.DrawImage(img, SKRect.Create((ImageSize - w) / 2, (ImageSize - h) / 2, w, h),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        using var rotated = Rotate(square, 90);
        using var data = rotated.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    /// <summary>Decodes a stored JPEG and rotates it back upright.</summary>
    public static SKBitmap? DecodeStored(byte[] jpeg)
    {
        using var stored = SKBitmap.Decode(jpeg);
        return stored is null ? null : Rotate(stored, -90);
    }

    static SKBitmap Rotate(SKBitmap src, float degrees)
    {
        var dst = new SKBitmap(src.Height, src.Width, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var c = new SKCanvas(dst);
        c.Translate(dst.Width / 2f, dst.Height / 2f);
        c.RotateDegrees(degrees);
        c.Translate(-src.Width / 2f, -src.Height / 2f);
        using var image = SKImage.FromBitmap(src);
        c.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return dst;
    }
}

/// <summary>Backs up and restores the eight display-key images (raw stored JPEGs) in a folder.</summary>
public static class DisplayKeyBackup
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkmountHub", "display-keys-backup");

    /// <summary>Saves every key that has an image (key1.jpg … key8.jpg, as stored) unless a backup exists already.</summary>
    public static int BackupOnce(DisplayKeys keys, string? folder = null)
    {
        folder ??= DefaultFolder;
        if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "key*.jpg").Any()) return 0;
        Directory.CreateDirectory(folder);
        int saved = 0;
        for (int i = 0; i < DisplayKeys.Count; i++)
        {
            if (keys.ReadStoredJpeg(i) is not { } jpeg) continue;
            File.WriteAllBytes(Path.Combine(folder, $"key{i + 1}.jpg"), jpeg);
            saved++;
        }
        return saved;
    }

    public static int Restore(DisplayKeys keys, string? folder = null)
    {
        folder ??= DefaultFolder;
        int restored = 0;
        for (int i = 0; i < DisplayKeys.Count; i++)
        {
            var path = Path.Combine(folder, $"key{i + 1}.jpg");
            if (!File.Exists(path)) continue;
            keys.WriteStoredJpeg(i, File.ReadAllBytes(path));
            restored++;
        }
        return restored;
    }
}
