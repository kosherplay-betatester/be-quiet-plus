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

    byte NextRequestId() => _reqId = (byte)(_reqId == 255 ? 1 : _reqId + 1);

    /// <summary>Sends one request (split into continuation frames if longer than 55 bytes) and waits for its reply.</summary>
    public byte[] Send(byte feature, byte command, ReadOnlySpan<byte> data = default, int timeoutMs = 3000)
    {
        if (!CommandAllowlist.IsAllowed(feature, command))
            throw new InvalidOperationException($"Command {feature}/{command} is not on the safety allowlist.");

        lock (_gate)
        {
            byte expected = NextRequestId();
            foreach (var packet in Frame.Build(Sid, expected, feature, command, data))
                transport.Write(packet);

            var sw = Stopwatch.StartNew();
            while (true)
            {
                var f = ReadFrame(Remaining(sw, timeoutMs));
                if (f.IsNotification || f.IsContinuation) continue;
                if (f.Feature != feature || f.Command != command || f.ReqId != expected) continue;
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

    /// <summary>Largest payload that fits in a single 64-byte frame.</summary>
    public const int MaxSingleFramePayload = Frame.FirstFrameData;

    /// <summary>
    /// Sends many single-frame requests of one command in order, keeping up to <paramref name="window"/> in flight,
    /// and <b>never repeating one</b>. This is how image data must be sent: large multi-frame writes make the
    /// firmware hold replies back, and the dock rejects an image if any chunk arrives twice. Measured on the
    /// Dark Mount: a 150 KB dock frame in ~1.4 s with window 4, with no held replies.
    /// </summary>
    /// <exception cref="TimeoutException">A reply did not arrive within <paramref name="replyTimeoutMs"/>.</exception>
    /// <exception cref="QLinkException">The device rejected one of the requests.</exception>
    public void SendWindowed(byte feature, byte command, IReadOnlyList<byte[]> payloads, int window = 4, int replyTimeoutMs = 3000)
    {
        if (!CommandAllowlist.IsAllowed(feature, command))
            throw new InvalidOperationException($"Command {feature}/{command} is not on the safety allowlist.");
        if (payloads.Any(p => p.Length > MaxSingleFramePayload))
            throw new ArgumentException($"Each payload must fit in one frame ({MaxSingleFramePayload} bytes).", nameof(payloads));

        lock (_gate)
        {
            var outstanding = new Queue<byte>(window);
            int next = 0;
            var sinceReply = Stopwatch.StartNew();
            while (next < payloads.Count || outstanding.Count > 0)
            {
                while (next < payloads.Count && outstanding.Count < Math.Max(1, window))
                {
                    byte id = NextRequestId();
                    transport.Write(Frame.Build(Sid, id, feature, command, payloads[next++])[0]);
                    outstanding.Enqueue(id);
                }

                var f = ReadFrame(Remaining(sinceReply, replyTimeoutMs));
                if (f.IsNotification || f.IsContinuation) continue;
                if (f.Feature != feature || f.Command != command || !outstanding.Contains(f.ReqId)) continue;
                if (f.Status != 0)
                    throw new QLinkException((QLinkStatus)f.Status, $"{feature}/{command} failed: {(QLinkStatus)f.Status}");

                // Replies arrive in order; anything older than this reply is implicitly done.
                while (outstanding.Count > 0 && outstanding.Dequeue() != f.ReqId) { }
                sinceReply.Restart();
            }
        }
    }

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
