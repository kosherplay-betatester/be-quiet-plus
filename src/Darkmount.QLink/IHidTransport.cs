namespace Darkmount.QLink;

/// <summary>Raw 64-byte packet transport to the keyboard's vendor HID interface.</summary>
public interface IHidTransport : IDisposable
{
    void Write(ReadOnlySpan<byte> packet);

    /// <summary>Reads one 64-byte packet; throws <see cref="TimeoutException"/> when nothing arrives in time.</summary>
    byte[] Read(int timeoutMs);
}

public sealed class QLinkException(QLinkStatus status, string message) : Exception(message)
{
    public QLinkStatus Status { get; } = status;
}

/// <summary>The keyboard is in firmware-update (bootloader) mode; this app never talks to it then.</summary>
public sealed class BootloaderModeException() : Exception("The keyboard is in bootloader mode (PID 0x0009).");
