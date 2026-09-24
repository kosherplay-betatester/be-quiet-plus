using System.Buffers.Binary;
using Darkmount.QLink;

namespace Darkmount.Tests;

/// <summary>
/// In-memory Dark Mount for keyboard/lighting/bindings/numpad-image commands. Keeps the state the
/// real firmware would keep and records every request (via <see cref="FakeTransport.Requests"/>).
/// </summary>
sealed class KeyboardFakeDevice
{
    public FakeTransport Transport { get; } = new();

    public byte LockMask;
    public byte State;
    public byte Physical, Visual;
    public byte LightingMode = 1;
    /// <summary>GetLayerConfig reply (no layer id): Static, dir 0, 100 %, speed 50, single FF2800.</summary>
    public byte[] LayerConfig = [0, 0, 100, 50, 0, 0xFF, 0x28, 0x00];
    public byte BindingsEnabled = 1;
    /// <summary>Binding records in device order.</summary>
    public List<byte[]> Records = [];
    /// <summary>Max records per GetBindings reply.</summary>
    public int PageSize = 100;
    /// <summary>Overrides the GetBindings reply for a start index (raw data), for garbled-data tests.</summary>
    public Func<int, byte[]?>? BindingsPage;
    public bool NumpadConnected = true;
    public Dictionary<ushort, byte[]> ImageStore { get; } = [];

    static readonly HashSet<(byte, byte)> WriteCommands =
    [
        (Features.Keyboard, 3), (Features.Keyboard, 5), (Features.Lightings, 2), (Features.Lightings, 6),
        (Features.Bindings, 2), (Features.Bindings, 3), (Features.Bindings, 5), (Features.Numpad, NumpadCommands.SetImage),
    ];

    public KeyboardFakeDevice() => Transport.Responder = Respond;

    /// <summary>Every write request so far, in order.</summary>
    public List<Frame> Writes => Transport.Requests.Where(r => WriteCommands.Contains((r.Feature, r.Command))).ToList();

    /// <summary>Stores a display-key image blob exactly as DisplayKeys would write it.</summary>
    public void SeedImage(int index, byte[] jpeg)
    {
        var blob = Blob((ushort)(109 + index));
        BinaryPrimitives.WriteUInt32LittleEndian(blob, (uint)(jpeg.Length + 9));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4), 120);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6), 120);
        blob[8] = 3;
        jpeg.CopyTo(blob, 9);
    }

    byte[] Blob(ushort key) => ImageStore.TryGetValue(key, out var b) ? b : ImageStore[key] = new byte[16 * 1024];

    IEnumerable<byte[]> Respond(Frame req)
    {
        var d = req.Data;
        switch (req.Feature, req.Command)
        {
            case (Features.Keyboard, 1): return FakeTransport.Reply(req, [Physical, Visual]);
            case (Features.Keyboard, 2): return FakeTransport.Reply(req, [LockMask]);
            case (Features.Keyboard, 3): LockMask = d[0]; return FakeTransport.Reply(req, [0, 0]);
            case (Features.Keyboard, 4): return FakeTransport.Reply(req, [State]);
            case (Features.Keyboard, 5): State = d[0]; return FakeTransport.Reply(req, []);
            case (Features.Lightings, 1): return FakeTransport.Reply(req, [LightingMode]);
            case (Features.Lightings, 2): LightingMode = d[0]; return FakeTransport.Reply(req, []);
            case (Features.Lightings, 5): return FakeTransport.Reply(req, LayerConfig);
            case (Features.Lightings, 6): LayerConfig = d[1..]; return FakeTransport.Reply(req, []);
            case (Features.Bindings, 4): return FakeTransport.Reply(req, [BindingsEnabled]);
            case (Features.Bindings, 5): BindingsEnabled = d[0]; return FakeTransport.Reply(req, []);
            case (Features.Bindings, 1): return FakeTransport.Reply(req, BindingsReply(d[0] | d[1] << 8));
            case (Features.Bindings, 2):
                Records.RemoveAll(r => SameKey(r, d));
                Records.Add(d);
                return FakeTransport.Reply(req, [0, 0]);
            case (Features.Bindings, 3):
                Records.RemoveAll(r => SameKey(r, d));
                return FakeTransport.Reply(req, []);
            case (Features.Numpad, NumpadCommands.GetState): return FakeTransport.Reply(req, [(byte)(NumpadConnected ? 1 : 0), 2]);
            case (Features.Numpad, NumpadCommands.SetImage):
            {
                var blob = Blob(BinaryPrimitives.ReadUInt16LittleEndian(d));
                d.AsSpan(6).CopyTo(blob.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(2))));
                return FakeTransport.Reply(req, []);
            }
            case (Features.Numpad, NumpadCommands.GetImage):
            {
                var blob = Blob(BinaryPrimitives.ReadUInt16LittleEndian(d));
                int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(2));
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(6));
                return FakeTransport.Reply(req, blob.AsSpan(offset, size).ToArray());
            }
            default: return FakeTransport.Ok(req);
        }
    }

    static bool SameKey(byte[] record, byte[] req) => record[0] == req[0] && (record[1] & 0x80) == (req[1] & 0x80);

    byte[] BindingsReply(int start)
    {
        if (BindingsPage?.Invoke(start) is { } custom) return custom;
        int common = Records.Count(r => (r[1] & 0x80) == 0);
        var page = Records.Skip(start).Take(PageSize).SelectMany(r => r);
        return [(byte)common, (byte)(Records.Count - common), .. page];
    }
}
