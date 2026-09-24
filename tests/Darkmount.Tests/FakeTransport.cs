using Darkmount.QLink;

namespace Darkmount.Tests;

/// <summary>
/// In-memory keyboard: records written packets and answers through a user-supplied responder.
/// The responder gets each complete request (reassembled from its frames) and returns the reply
/// packets to queue, or null to simulate a stall (no answer).
/// </summary>
public sealed class FakeTransport : IHidTransport
{
    readonly Queue<byte[]> _inbox = new();
    readonly List<byte[]> _pending = [];

    public List<byte[]> Written { get; } = [];
    public List<Frame> Requests { get; } = [];
    public Func<Frame, IEnumerable<byte[]>?> Responder { get; set; } = Ok;
    public bool Disposed { get; private set; }

    public void Write(ReadOnlySpan<byte> packet)
    {
        var p = packet.ToArray();
        Written.Add(p);
        _pending.Add(p);
        var f = Frame.Parse(p);
        if (f.HasMore) return;

        // Reassemble a multi-frame request into one logical frame.
        var first = Frame.Parse(_pending[0]);
        var data = _pending.SelectMany(x => Frame.Parse(x).Data).ToArray();
        _pending.Clear();
        var request = first with { Data = data };
        Requests.Add(request);
        foreach (var reply in Responder(request) ?? []) _inbox.Enqueue(reply);
    }

    public byte[] Read(int timeoutMs)
    {
        if (_inbox.Count == 0) throw new TimeoutException("fake: no data");
        return _inbox.Dequeue();
    }

    public void Enqueue(byte[] packet) => _inbox.Enqueue(packet);

    public void Dispose() => Disposed = true;

    /// <summary>Default responder: success with empty data (OpenSession returns SID 5, Active, 2 s).</summary>
    public static IEnumerable<byte[]> Ok(Frame req) =>
        req is { Feature: Features.Root, Command: RootCommands.OpenSession }
            ? Reply(req, [0x39, 0x30, 0, 0, 5, 1, 2])
            : Reply(req, []);

    public static IEnumerable<byte[]> Reply(Frame req, byte[] data, byte status = 0) =>
        Frame.Build(req.Sid, req.ReqId, req.Feature, req.Command, data, status);
}
