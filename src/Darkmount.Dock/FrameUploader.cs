using Darkmount.QLink;

namespace Darkmount.Dock;

/// <summary>
/// Sends complete 320×240 RGB565 frames to the dock's screensaver slot. The dock only redraws after a
/// clean, complete upload, so a stall anywhere restarts the frame from the header (spec §3).
/// </summary>
public sealed class FrameUploader(QLinkClient q, MediaDock dock)
{
    public const int Width = 320, Height = 240;
    public const int FrameBytes = Width * Height * 2;
    public const int ChunkSize = 4000;

    readonly object _gate = new();
    readonly byte[] _header = MediaDock.ImageHeader(Width, Height, FrameBytes);

    public int MaxRestarts { get; init; } = 3;

    public event Action<string>? Log;

    /// <summary>Uploads one frame; returns false if it could not be completed after <see cref="MaxRestarts"/> restarts.</summary>
    /// <exception cref="TimeoutException">The keyboard stopped answering entirely.</exception>
    public bool Upload(byte[] rgb565)
    {
        if (rgb565.Length != FrameBytes)
            throw new ArgumentException($"Frame must be {FrameBytes} bytes of RGB565", nameof(rgb565));

        lock (_gate)
        {
            for (int attempt = 0; attempt <= MaxRestarts; attempt++)
            {
                try
                {
                    dock.SetImage(MediaDock.SlotScreensaver, 0, _header);
                    for (int offset = 0; offset < FrameBytes; offset += ChunkSize)
                    {
                        int len = Math.Min(ChunkSize, FrameBytes - offset);
                        dock.SetImage(MediaDock.SlotScreensaver, (uint)(MediaDock.HeaderSize + offset), rgb565.AsSpan(offset, len));
                    }
                    return true;
                }
                catch (TimeoutException)
                {
                    long waited = q.WaitReady();
                    Log?.Invoke($"Keyboard stalled during upload (attempt {attempt + 1}); ready again after {waited} ms, restarting frame");
                }
            }
            return false;
        }
    }
}
