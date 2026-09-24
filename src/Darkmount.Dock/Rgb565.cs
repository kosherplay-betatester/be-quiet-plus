using SkiaSharp;

namespace Darkmount.Dock;

public static class Rgb565
{
    /// <summary>Converts a 320×240 bitmap to little-endian RGB565 (the dock's only pixel format).</summary>
    public static byte[] FromBitmap(SKBitmap bitmap)
    {
        if (bitmap.Width != FrameUploader.Width || bitmap.Height != FrameUploader.Height)
            throw new ArgumentException("Dock frames are 320×240", nameof(bitmap));

        using var rgba = bitmap.ColorType == SKColorType.Rgba8888 ? null : bitmap.Copy(SKColorType.Rgba8888);
        var src = (rgba ?? bitmap).GetPixelSpan();
        var dst = new byte[FrameUploader.FrameBytes];
        for (int i = 0, o = 0; i < src.Length; i += 4, o += 2)
        {
            int v = (src[i] >> 3) << 11 | (src[i + 1] >> 2) << 5 | (src[i + 2] >> 3);
            dst[o] = (byte)v;
            dst[o + 1] = (byte)(v >> 8);
        }
        return dst;
    }
}
