using HidSharp;

namespace Darkmount.QLink;

/// <summary>Talks to the Dark Mount vendor interface (VID 0x373F, PID 0x0001, MI_02, usage page 0xFF00).</summary>
public sealed class HidSharpTransport : IHidTransport
{
    public const int VendorId = 0x373F;
    public const int ProductId = 0x0001;
    public const int BootloaderProductId = 0x0009;

    readonly HidStream _stream;
    readonly byte[] _rx = new byte[Frame.Size + 1];
    readonly byte[] _tx = new byte[Frame.Size + 1];

    HidSharpTransport(HidStream stream) => _stream = stream;

    /// <summary>Opens the keyboard, or returns null when it is not connected.</summary>
    /// <exception cref="BootloaderModeException">The keyboard is in bootloader mode.</exception>
    public static HidSharpTransport? TryOpen()
    {
        var devices = DeviceList.Local.GetHidDevices(VendorId).ToList();
        if (devices.Any(d => d.ProductID == BootloaderProductId)) throw new BootloaderModeException();

        var device = devices
            .Where(d => d.ProductID == ProductId && MaxOutput(d) == Frame.Size + 1)
            .OrderByDescending(d => d.DevicePath.Contains("mi_02", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (device is null || !device.TryOpen(out HidStream stream)) return null;
        stream.WriteTimeout = 1000;
        return new HidSharpTransport(stream);
    }

    public static bool IsPresent() =>
        DeviceList.Local.GetHidDevices(VendorId).Any(d => d.ProductID is ProductId or BootloaderProductId);

    static int MaxOutput(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength(); }
        catch (Exception) { return 0; }
    }

    public void Write(ReadOnlySpan<byte> packet)
    {
        _tx[0] = 0; // report id
        packet.CopyTo(_tx.AsSpan(1));
        _stream.Write(_tx, 0, _tx.Length);
    }

    public byte[] Read(int timeoutMs)
    {
        _stream.ReadTimeout = Math.Max(1, timeoutMs);
        int n = _stream.Read(_rx, 0, _rx.Length);
        return n == Frame.Size + 1 ? _rx[1..] : _rx[..Frame.Size];
    }

    public void Dispose() => _stream.Dispose();
}
