using System.Runtime.InteropServices;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.App.Input;

/// <summary>
/// Samples the primary monitor into a small grid of average colours for the "Screen sync" (ambilight) effect.
/// Uses GDI StretchBlt with HALFTONE averaging into a 24×8 bitmap, so it costs about a millisecond per sample.
/// Exclusive-fullscreen games may come back black; borderless/windowed games and video work.
/// </summary>
public sealed class ScreenSampler : IDisposable
{
    public const int GridWidth = 24, GridHeight = 8;
    const int Halftone = 4, SrcCopy = 0x00CC0020, SmCxScreen = 0, SmCyScreen = 1;

    readonly LampColor[] _grid = new LampColor[GridWidth * GridHeight];
    readonly LampColor[] _smoothed = new LampColor[GridWidth * GridHeight];

    /// <summary>Captures the screen and returns the grid (row-major, top-left first), lightly smoothed over time.</summary>
    public IReadOnlyList<LampColor> Sample()
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, GridWidth, GridHeight);
            old = SelectObject(memory, bitmap);
            SetStretchBltMode(memory, Halftone);
            SetBrushOrgEx(memory, 0, 0, IntPtr.Zero);
            StretchBlt(memory, 0, 0, GridWidth, GridHeight, screen, 0, 0, GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen), SrcCopy);
            SelectObject(memory, old);
            old = IntPtr.Zero;

            var info = new BitmapInfo
            {
                Size = Marshal.SizeOf<BitmapInfo>(), Width = GridWidth, Height = -GridHeight, Planes = 1, BitCount = 32,
            };
            var pixels = new byte[GridWidth * GridHeight * 4];
            if (GetDIBits(memory, bitmap, 0, GridHeight, pixels, ref info, 0) == 0) return _smoothed;
            for (int i = 0; i < _grid.Length; i++)
            {
                var c = new LampColor(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);
                var p = _smoothed[i];
                _smoothed[i] = new LampColor((byte)((p.R + c.R * 2) / 3), (byte)((p.G + c.G * 2) / 3), (byte)((p.B + c.B * 2) / 3));
            }
            return (LampColor[])_smoothed.Clone();
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose() { }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        public int Colors0;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr prev);
    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr dst, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr dc, IntPtr bitmap, int start, int lines, byte[] bits, ref BitmapInfo info, int usage);
}
