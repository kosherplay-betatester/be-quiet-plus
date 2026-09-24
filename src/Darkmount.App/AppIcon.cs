using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Darkmount.App;

/// <summary>What the tray icon shows: full colour when working, greyed when idle, with a status dot for attention.</summary>
public enum TrayState { Active, Idle, IoCenter, Alert }

/// <summary>The app icon (Assets/OverMount.ico, embedded) for windows, plus status variants for the tray.</summary>
public static class AppIcon
{
    static readonly Dictionary<(TrayState, int), Icon> TrayCache = [];
    static Icon? _window;

    static Stream Resource() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream("OverMount.ico") ?? throw new InvalidOperationException("App icon resource missing");

    /// <summary>The multi-size icon for window title bars and the taskbar.</summary>
    public static Icon Window
    {
        get
        {
            if (_window is not null) return _window;
            using var s = Resource();
            return _window = new Icon(s);
        }
    }

    /// <summary>The tray icon for a state, at the system's small-icon size (cached; don't dispose).</summary>
    public static Icon Tray(TrayState state)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        if (TrayCache.TryGetValue((state, size), out var cached)) return cached;

        using var s = Resource();
        using var sized = new Icon(s, size, size);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (state == TrayState.Idle)
            {
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(new ColorMatrix(
                [
                    [0.30f, 0.30f, 0.30f, 0, 0], [0.45f, 0.45f, 0.45f, 0, 0], [0.12f, 0.12f, 0.12f, 0, 0], [0, 0, 0, 0.85f, 0], [0, 0, 0, 0, 1],
                ]));
                using var colour = sized.ToBitmap();
                g.DrawImage(colour, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attrs);
            }
            else g.DrawIcon(sized, new Rectangle(0, 0, size, size));

            Color? dot = state switch { TrayState.IoCenter => Color.FromArgb(255, 176, 32), TrayState.Alert => Color.FromArgb(255, 48, 48), _ => null };
            if (dot is { } c)
            {
                float d = size * 0.42f;
                var r = new RectangleF(size - d - 0.5f, size - d - 0.5f, d, d);
                using var ring = new SolidBrush(Color.FromArgb(12, 14, 18));
                g.FillEllipse(ring, RectangleF.Inflate(r, size * 0.06f, size * 0.06f));
                using var fill = new SolidBrush(c);
                g.FillEllipse(fill, r);
            }
        }
        var handle = bmp.GetHicon();
        try { return TrayCache[(state, size)] = (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
