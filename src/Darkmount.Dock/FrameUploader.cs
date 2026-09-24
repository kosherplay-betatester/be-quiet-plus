using System.Diagnostics;
using Darkmount.QLink;

namespace Darkmount.Dock;

public enum UploadResult
{
    Done,
    /// <summary>The dock stopped answering mid-frame (busy, asleep or stuck). Nothing was re-sent.</summary>
    Stalled,
}

/// <summary>
/// Sends complete 320×240 RGB565 frames to the dock's screensaver slot. The dock only redraws after a
/// clean, complete upload in which no byte arrives twice, so pixels go out as small single-frame writes, a few in
/// flight, never repeated (measured ~2.2 s per frame including the ~0.75 s header). If a reply never comes the frame
/// is abandoned and the caller backs off before trying again from the header.
/// </summary>
public sealed class FrameUploader(MediaDock dock)
{
    public const int Width = 320, Height = 240;
    public const int FrameBytes = Width * Height * 2;
    readonly object _gate = new();

    /// <summary>Single-frame image writes kept in flight (4 measured as fast as 8 or 16 on the Dark Mount).</summary>
    public int Window { get; init; } = 4;

    /// <summary>
    /// Large multi-frame writes. Off by default: on hardware the firmware held every reply and the dock rejected the
    /// images (12.7 s per frame, be quiet! logo shown). Kept only for experiments.
    /// </summary>
    public bool LargeWrites { get; init; }
    readonly byte[] _header = MediaDock.ImageHeader(Width, Height, FrameBytes);

    /// <summary>How long to wait for the header reply (the dock prepares its buffer; can take seconds).</summary>
    public int HeaderTimeoutMs { get; init; } = 8000;

    /// <summary>How long to wait for each pixel write reply.</summary>
    public int ChunkTimeoutMs { get; init; } = 5000;

    public event Action<string>? Log;

    /// <summary>Time taken by the last completed upload.</summary>
    public TimeSpan LastDuration { get; private set; }

    public UploadResult Upload(byte[] rgb565)
    {
        if (rgb565.Length != FrameBytes)
            throw new ArgumentException($"Frame must be {FrameBytes} bytes of RGB565", nameof(rgb565));

        lock (_gate)
        {
            var sw = Stopwatch.StartNew();
            int offset = -1;
            try
            {
                dock.SetImage(MediaDock.SlotScreensaver, 0, _header, HeaderTimeoutMs);
                offset = 0;
                // Pipelined and never repeated: the dock rejects an image if any chunk arrives twice.
                if (LargeWrites) dock.SetImageDataLarge(MediaDock.SlotScreensaver, rgb565, timeoutMs: ChunkTimeoutMs);
                else dock.SetImageData(MediaDock.SlotScreensaver, rgb565, Window, ChunkTimeoutMs);
                LastDuration = sw.Elapsed;
                return UploadResult.Done;
            }
            catch (TimeoutException)
            {
                Log?.Invoke(offset < 0
                    ? $"Dock did not accept a new image within {HeaderTimeoutMs} ms (asleep or busy)"
                    : "Dock stopped answering while receiving the frame's pixels");
                return UploadResult.Stalled;
            }
        }
    }
}
