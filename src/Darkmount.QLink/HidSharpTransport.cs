using HidSharp;

namespace Darkmount.QLink;

/// <summary>
/// Talks to a be quiet! keyboard's vendor interface (VID 0x373F, usage page 0xFF00, 64-byte reports): the Dark Mount
/// (PID 0x0001, interface MI_02) or a Light Mount (see <see cref="KeyboardModel"/>).
/// </summary>
public sealed class HidSharpTransport : IHidTransport
{
    public const int VendorId = KeyboardModel.VendorId;

    readonly HidStream _stream;
    readonly byte[] _rx = new byte[Frame.Size + 1];
    readonly byte[] _tx = new byte[Frame.Size + 1];

    HidSharpTransport(HidStream stream, KeyboardModel model)
    {
        _stream = stream;
        Model = model;
    }

    /// <summary>Which keyboard this transport is connected to.</summary>
    public KeyboardModel Model { get; }

    /// <summary>Opens the first supported keyboard, or returns null when none is connected.</summary>
    /// <exception cref="BootloaderModeException">A keyboard is in firmware-update (bootloader) mode.</exception>
    public static HidSharpTransport? TryOpen()
    {
        var devices = DeviceList.Local.GetHidDevices(VendorId).ToList();
        if (devices.Any(d => KeyboardModel.BootloaderProductIds.Contains(d.ProductID))) throw new BootloaderModeException();

        foreach (var model in KeyboardModel.All)
        {
            var device = devices
                .Where(d => d.ProductID == model.ProductId && MaxOutput(d) == Frame.Size + 1)
                .OrderByDescending(d => d.DevicePath.Contains("mi_02", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (device is not null && device.TryOpen(out HidStream stream))
            {
                stream.WriteTimeout = 1000;
                return new HidSharpTransport(stream, model);
            }
        }
        return null;
    }

    public static bool IsPresent() =>
        DeviceList.Local.GetHidDevices(VendorId).Any(d =>
            KeyboardModel.ForProductId(d.ProductID) is not null || KeyboardModel.BootloaderProductIds.Contains(d.ProductID));

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
