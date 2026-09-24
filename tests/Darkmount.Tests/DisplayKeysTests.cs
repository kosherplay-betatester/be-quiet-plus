using System.Buffers.Binary;
using Darkmount.Keyboard;
using Darkmount.QLink;
using SkiaSharp;

namespace Darkmount.Tests;

public class DisplayKeysTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dmh-keys-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    /// <summary>In-memory numpad: SetImage writes into a per-key blob, GetImage reads from it.</summary>
    static FakeTransport Numpad(Dictionary<ushort, byte[]> store)
    {
        var t = new FakeTransport();
        t.Responder = req =>
        {
            if (req.Feature != Features.Numpad) return FakeTransport.Ok(req);
            ushort key = BinaryPrimitives.ReadUInt16LittleEndian(req.Data);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(req.Data.AsSpan(2));
            var blob = store.TryGetValue(key, out var b) ? b : store[key] = new byte[64 * 1024];
            if (req.Command == NumpadCommands.SetImage)
            {
                req.Data.AsSpan(6).CopyTo(blob.AsSpan(offset));
                return FakeTransport.Reply(req, []);
            }
            if (req.Command == NumpadCommands.GetImage)
            {
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(req.Data.AsSpan(6));
                return FakeTransport.Reply(req, blob.AsSpan(offset, size).ToArray());
            }
            return FakeTransport.Reply(req, [1, 1]);
        };
        return t;
    }

    static SKBitmap Marker()
    {
        // Red top-left quadrant on blue: tells us whether rotation round-trips upright.
        var bmp = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Blue);
        c.DrawRect(0, 0, 100, 100, new SKPaint { Color = SKColors.Red });
        return bmp;
    }

    [Fact]
    public void Encoded_key_image_is_stored_rotated_and_decodes_upright()
    {
        using var src = Marker();
        var jpeg = DisplayKeys.EncodeForKey(src);

        using var stored = SKBitmap.Decode(jpeg);
        Assert.Equal((120, 120), (stored.Width, stored.Height));
        // Rotated 90° clockwise: the red corner moves from top-left to top-right.
        Assert.True(stored.GetPixel(110, 10).Red > 200);
        Assert.True(stored.GetPixel(10, 10).Blue > 200);

        using var upright = DisplayKeys.DecodeStored(jpeg)!;
        Assert.True(upright.GetPixel(10, 10).Red > 200);
        Assert.True(upright.GetPixel(110, 110).Blue > 200);
    }

    [Fact]
    public void Write_then_read_returns_the_same_image_for_the_right_key()
    {
        var store = new Dictionary<ushort, byte[]>();
        var keys = new DisplayKeys(new QLinkClient(Numpad(store)));
        using var src = Marker();

        keys.WriteImage(2, src);

        Assert.True(store.ContainsKey(111)); // key 3 = id 111
        Assert.Equal(DisplayKeys.EncodeForKey(src), keys.ReadStoredJpeg(2));
        using var back = keys.ReadImage(2)!;
        Assert.True(back.GetPixel(10, 10).Red > 200);
        Assert.Null(keys.ReadStoredJpeg(5)); // empty key
    }

    [Fact]
    public void Backup_happens_once_and_restore_writes_the_originals_back()
    {
        var store = new Dictionary<ushort, byte[]>();
        var keys = new DisplayKeys(new QLinkClient(Numpad(store)));
        using var src = Marker();
        keys.WriteImage(0, src);
        var original = keys.ReadStoredJpeg(0)!;

        Assert.Equal(1, DisplayKeyBackup.BackupOnce(keys, _dir));
        Assert.Equal(0, DisplayKeyBackup.BackupOnce(keys, _dir)); // never overwrites an existing backup

        using var other = new SKBitmap(50, 50);
        keys.WriteImage(0, other);
        Assert.NotEqual(original, keys.ReadStoredJpeg(0));

        Assert.Equal(1, DisplayKeyBackup.Restore(keys, _dir));
        Assert.Equal(original, keys.ReadStoredJpeg(0));
    }
}
