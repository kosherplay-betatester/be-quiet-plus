using System.Diagnostics;

namespace Darkmount.QLink;

/// <summary>
/// Request/response QLink client. Thread-safe: one request at a time. Replies are matched by
/// feature, command and request id, so late replies to timed-out requests are ignored.
/// Only commands on <see cref="CommandAllowlist"/> can be sent.
/// </summary>
public sealed class QLinkClient(IHidTransport transport) : IDisposable
{
    public const byte ClientTypeWeb = 2;

    readonly object _gate = new();
    byte _reqId;

    public byte Sid { get; private set; }
    public bool IsActive { get; private set; }
    public int SessionTimeoutSeconds { get; private set; }

    /// <summary>SID of another client that asked to take over the Active state, or null.</summary>
    public byte? PendingTakeoverSid { get; private set; }

    /// <summary>Raised for every unsolicited device notification (on the thread that is reading).</summary>
    public event Action<Frame>? Notification;

    /// <summary>
    /// The firmware sometimes holds a finished reply until it receives more traffic. When no reply has
    /// arrived after this many ms, a harmless media-dock GetState is sent to flush it (0 = never).
    /// </summary>
    public int NudgeAfterMs { get; set; } = 0;

    /// <summary>Number of nudges sent so far (diagnostics).</summary>
    public int Nudges { get; private set; }

    /// <summary>What to send as a nudge. Only use <see cref="NudgeMode.RepeatRequest"/> for idempotent requests.</summary>
    public NudgeMode NudgeMode { get; set; } = NudgeMode.RepeatRequest;

    byte NextRequestId() => _reqId = (byte)(_reqId == 255 ? 1 : _reqId + 1);

    public byte[] Send(byte feature, byte command, ReadOnlySpan<byte> data = default, int timeoutMs = 3000) =>
        Send(feature, command, data, timeoutMs, allowNudge: true);

    /// <param name="allowNudge">False for writes that must never be repeated (e.g. an image header).</param>
    public byte[] Send(byte feature, byte command, ReadOnlySpan<byte> data, int timeoutMs, bool allowNudge)
    {
        if (!CommandAllowlist.IsAllowed(feature, command))
            throw new InvalidOperationException($"Command {feature}/{command} is not on the safety allowlist.");

        lock (_gate)
        {
            byte expected = NextRequestId();
            Span<byte> accepted = stackalloc byte[16];
            int acceptedCount = 0;
            accepted[acceptedCount++] = expected;
            foreach (var packet in Frame.Build(Sid, expected, feature, command, data))
                transport.Write(packet);

            var sw = Stopwatch.StartNew();
            // Only image writes show the held-reply quirk, and only they are safe to repeat.
            bool canNudge = allowNudge && NudgeAfterMs > 0 && ImageWriteHeaderLength(feature, command) > 0;
            long nextNudge = canNudge ? NudgeAfterMs : long.MaxValue;
            while (true)
            {
                Frame f;
                try
                {
                    int left = Remaining(sw, timeoutMs);
                    f = ReadFrame((int)Math.Clamp(nextNudge - sw.ElapsedMilliseconds, 1, left));
                }
                catch (TimeoutException) when (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (sw.ElapsedMilliseconds >= nextNudge)
                    {
                        byte? repeatId = Nudge(feature, command, data);
                        if (repeatId is { } id && acceptedCount < accepted.Length) accepted[acceptedCount++] = id;
                        nextNudge = sw.ElapsedMilliseconds + NudgeAfterMs;
                    }
                    continue;
                }
                if (f.IsNotification || f.IsContinuation) continue;
                if (f.Feature != feature || f.Command != command || !accepted[..acceptedCount].Contains(f.ReqId)) continue;
                if (f.Status != 0)
                    throw new QLinkException((QLinkStatus)f.Status, $"{feature}/{command} failed: {(QLinkStatus)f.Status}");

                var result = f.Data;
                while (f.HasMore)
                {
                    f = ReadFrame(Remaining(sw, timeoutMs));
                    if (!f.IsContinuation) continue;
                    result = [.. result, .. f.Data];
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Sends several requests of the same command in order, <b>never repeating one</b>. The firmware sometimes
    /// holds a reply back until the next write arrives; when a reply is late by <paramref name="heldAfterMs"/>, the
    /// next request is sent (at most two outstanding), which releases it. The dock counts image bytes, so a repeated
    /// chunk would spoil the image — this is the only safe way to keep an image upload moving.
    /// If only the very last reply stays held for <paramref name="finalGraceMs"/>, the sequence counts as delivered
    /// (the next request of any kind releases it; its late reply is ignored by request id).
    /// </summary>
    /// <exception cref="TimeoutException">No progress for <paramref name="timeoutMs"/>.</exception>
    /// <exception cref="QLinkException">The device rejected one of the requests.</exception>
    public void SendSequence(byte feature, byte command, IReadOnlyList<byte[]> payloads,
        int heldAfterMs = 250, int timeoutMs = 5000, int finalGraceMs = 1500)
    {
        if (!CommandAllowlist.IsAllowed(feature, command))
            throw new InvalidOperationException($"Command {feature}/{command} is not on the safety allowlist.");

        lock (_gate)
        {
            var outstanding = new List<byte>(2);
            int next = 0;
            var sinceProgress = Stopwatch.StartNew();

            void SendNext()
            {
                byte id = NextRequestId();
                foreach (var packet in Frame.Build(Sid, id, feature, command, payloads[next++])) transport.Write(packet);
                outstanding.Add(id);
                sinceProgress.Restart();
            }

            while (next < payloads.Count || outstanding.Count > 0)
            {
                if (outstanding.Count == 0) { SendNext(); continue; }

                bool moreToSend = next < payloads.Count;
                long waitLimit = moreToSend && outstanding.Count < 2 ? heldAfterMs
                    : moreToSend ? timeoutMs
                    : outstanding.Count == 1 ? finalGraceMs : timeoutMs;
                long left = waitLimit - sinceProgress.ElapsedMilliseconds;

                Frame f;
                try
                {
                    if (left <= 0) throw new TimeoutException();
                    f = ReadFrame((int)left);
                }
                catch (TimeoutException)
                {
                    if (moreToSend && outstanding.Count < 2) { SendNext(); continue; } // releases the held reply
                    if (!moreToSend && outstanding.Count == 1) { HeldFinalReplies++; return; } // last reply held: delivered
                    throw new TimeoutException($"No reply for {timeoutMs} ms during a {payloads.Count}-part transfer (part {next}).");
                }

                if (f.IsNotification || f.IsContinuation) continue;
                if (f.Feature != feature || f.Command != command || !outstanding.Contains(f.ReqId)) continue;
                if (f.Status != 0)
                    throw new QLinkException((QLinkStatus)f.Status, $"{feature}/{command} failed: {(QLinkStatus)f.Status}");
                outstanding.Remove(f.ReqId);
                sinceProgress.Restart();
            }
        }
    }

    /// <summary>How often the last reply of a <see cref="SendSequence"/> stayed held (diagnostics).</summary>
    public int HeldFinalReplies { get; private set; }

    /// <summary>
    /// Sends traffic whose only purpose is to flush a held reply. Returns the request id of a repeated
    /// request (whose reply also counts as the answer), or null.
    /// </summary>
    byte? Nudge(byte feature, byte command, ReadOnlySpan<byte> data)
    {
        Nudges++;
        byte id = NextRequestId();
        var packets = NudgeMode switch
        {
            NudgeMode.RepeatRequest => Frame.Build(Sid, id, feature, command, data),
            NudgeMode.TruncatedRepeat => Frame.Build(Sid, id, feature, command,
                data[..Math.Min(ImageWriteHeaderLength(feature, command), data.Length)]),
            _ => Frame.Build(Sid, id, Features.MediaDock, MediaDockCommands.GetState, []),
        };
        foreach (var packet in packets) transport.Write(packet);
        return NudgeMode == NudgeMode.GetState ? null : id;
    }

    /// <summary>
    /// Length of the addressing prefix of an image write (media dock: slot + offset = 5; numpad: key id + offset = 6),
    /// or 0 for requests that must never be nudged or repeated.
    /// </summary>
    static int ImageWriteHeaderLength(byte feature, byte command) => (feature, command) switch
    {
        (Features.MediaDock, MediaDockCommands.SetImage) => 5,
        (Features.Numpad, NumpadCommands.SetImage) => 6,
        _ => 0,
    };

    static int Remaining(Stopwatch sw, int timeoutMs)
    {
        long left = timeoutMs - sw.ElapsedMilliseconds;
        if (left <= 0) throw new TimeoutException("QLink request timed out");
        return (int)left;
    }

    Frame ReadFrame(int timeoutMs)
    {
        var f = Frame.Parse(transport.Read(timeoutMs));
        if (f.IsNotification) HandleNotification(f);
        return f;
    }

    void HandleNotification(Frame f)
    {
        if (f.Feature == Features.Root && f.Data.Length > 0)
        {
            if (f.Command == RootNotifications.SessionStateChanged) IsActive = f.Data[0] == 1;
            else if (f.Command == RootNotifications.StateChangeRequested) PendingTakeoverSid = f.Data[0];
            else if (f.Command == RootNotifications.ActiveSessionChanged) IsActive = f.Data[0] == Sid;
        }
        Notification?.Invoke(f);
    }

    /// <summary>Reads and dispatches notifications for up to <paramref name="ms"/> milliseconds.</summary>
    public void Pump(int ms)
    {
        lock (_gate)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                try { ReadFrame((int)Math.Max(1, ms - sw.ElapsedMilliseconds)); }
                catch (TimeoutException) { }
            }
        }
    }

    public void OpenSession()
    {
        Sid = 0;
        var conn = BitConverter.GetBytes(Random.Shared.Next(10000, 99999));
        var d = Send(Features.Root, RootCommands.OpenSession, [.. conn, ClientTypeWeb]);
        if (d.Length < 5) throw new QLinkException(QLinkStatus.InvalidSize, "Short OpenSession reply");
        Sid = d[4];
        IsActive = d.Length > 5 && d[5] == 1;
        SessionTimeoutSeconds = d.Length > 6 ? d[6] : 2;
    }

    /// <summary>Asks the device to make this session Active; waits for the state-change notification.</summary>
    public bool RequestActive(int waitMs = 2500)
    {
        if (IsActive) return true;
        Send(Features.Root, RootCommands.RequestStateChange, [1]);
        var sw = Stopwatch.StartNew();
        while (!IsActive && sw.ElapsedMilliseconds < waitMs) Pump(200);
        return IsActive;
    }

    /// <summary>Accepts another client's takeover request (the web app does the same).</summary>
    public void AcceptPendingTakeover()
    {
        if (PendingTakeoverSid is not { } sid) return;
        PendingTakeoverSid = null;
        Send(Features.Root, RootCommands.SendStateChangeDecision, [sid, 1]);
        IsActive = false;
    }

    public void KeepAlive(int timeoutMs = 1500) => Send(Features.Root, RootCommands.KeepAlive, timeoutMs: timeoutMs);

    /// <summary>Polls a cheap read until the device answers again after a stall.</summary>
    public long WaitReady(int maxMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxMs)
        {
            try { Send(Features.MediaDock, MediaDockCommands.GetState, timeoutMs: 150); return sw.ElapsedMilliseconds; }
            catch (TimeoutException) { }
        }
        throw new TimeoutException($"Keyboard did not answer for {maxMs} ms");
    }

    public void CloseSession()
    {
        if (Sid == 0) return;
        try { Send(Features.Root, RootCommands.CloseSession, [Sid], 1000); }
        catch (Exception) { /* best effort: device may be gone */ }
        Sid = 0;
        IsActive = false;
    }

    public void Dispose()
    {
        CloseSession();
        transport.Dispose();
    }
}
