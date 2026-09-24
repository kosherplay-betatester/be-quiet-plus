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
/// clean, complete upload. The dock is slow and easily overwhelmed, so the uploader never re-sends while a
/// request is outstanding: it waits generously for each reply and, if one never comes, abandons the frame
/// and lets the caller back off before trying again from the header.
/// </summary>
public sealed class FrameUploader(QLinkClient q, MediaDock dock)
{
    public const int Width = 320, Height = 240;
    public const int FrameBytes = Width * Height * 2;
    public const int DefaultChunkSize = 4000;

    readonly object _gate = new();

    /// <summary>Pixel bytes per SetImage request (sent as one multi-frame message).</summary>
    public int ChunkSize { get; init; } = DefaultChunkSize;
    readonly byte[] _header = MediaDock.ImageHeader(Width, Height, FrameBytes);

    /// <summary>How long to wait for the header reply (the dock prepares its buffer; can take seconds).</summary>
    public int HeaderTimeoutMs { get; init; } = 8000;

    /// <summary>How long to wait for each pixel chunk reply.</summary>
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
                for (offset = 0; offset < FrameBytes; offset += ChunkSize)
                {
                    int len = Math.Min(ChunkSize, FrameBytes - offset);
                    dock.SetImage(MediaDock.SlotScreensaver, (uint)(MediaDock.HeaderSize + offset), rgb565.AsSpan(offset, len), ChunkTimeoutMs);
                }
                LastDuration = sw.Elapsed;
                return UploadResult.Done;
            }
            catch (TimeoutException)
            {
                Log?.Invoke(offset < 0
                    ? $"Dock did not accept a new image within {HeaderTimeoutMs} ms (asleep or busy)"
                    : $"Dock stopped answering at byte {offset} of the frame");
                return UploadResult.Stalled;
            }
        }
    }
}
