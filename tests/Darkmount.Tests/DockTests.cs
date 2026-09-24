using System.Buffers.Binary;
using Darkmount.Dock;
using Darkmount.QLink;

namespace Darkmount.Tests;

public class DockTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "darkmount-tests-" + Guid.NewGuid().ToString("N"));

    public DockTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static byte[] Frame(byte fill) => Enumerable.Repeat(fill, FrameUploader.FrameBytes).ToArray();

    static List<Frame> SetImages(FakeTransport t) =>
        t.Requests.Where(r => r is { Feature: Features.MediaDock, Command: MediaDockCommands.SetImage }).ToList();

    static uint OffsetOf(Frame r) => BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(1));

    // ---------------------------------------------------------------- FrameUploader

    [Fact]
    public void Upload_sends_header_then_all_pixels_in_4000_byte_chunks()
    {
        var t = new FakeTransport();
        var q = new QLinkClient(t);
        var up = new FrameUploader(q, new MediaDock(q));

        Assert.Equal(UploadResult.Done, up.Upload(Frame(0xAB)));

        var writes = SetImages(t);
        Assert.Equal(1 + 39, writes.Count); // 153,600 / 4,000 = 38.4 → 39 chunks
        Assert.Equal(MediaDock.ImageHeader(320, 240, FrameUploader.FrameBytes), writes[0].Data[5..]);
        Assert.Equal(0u, OffsetOf(writes[0]));
        Assert.Equal([9u, 4009u, 8009u], writes.Skip(1).Take(3).Select(OffsetOf));
        Assert.Equal(9u + 38 * 4000, OffsetOf(writes[^1]));
        Assert.Equal(1600 + 5, writes[^1].Data.Length);
        Assert.All(writes, w => Assert.Equal(MediaDock.SlotScreensaver, w.Data[0]));
    }

    [Fact]
    public void A_stall_mid_upload_abandons_the_frame_without_resending()
    {
        var t = new FakeTransport();
        var q = new QLinkClient(t);
        t.Responder = req => req.Command == MediaDockCommands.SetImage && OffsetOf(req) == 4009 ? null : FakeTransport.Ok(req);
        var up = new FrameUploader(q, new MediaDock(q));

        Assert.Equal(UploadResult.Stalled, up.Upload(Frame(2)));

        var offsets = SetImages(t).Select(OffsetOf).ToList();
        Assert.Equal([0u, 9u, 4009u], offsets); // stopped at the stalled chunk; nothing re-sent
    }

    [Fact]
    public void A_sleeping_dock_that_ignores_the_header_gets_nothing_else()
    {
        var t = new FakeTransport { Responder = req => req.Command == MediaDockCommands.SetImage ? null : FakeTransport.Ok(req) };
        var q = new QLinkClient(t);

        Assert.Equal(UploadResult.Stalled, new FrameUploader(q, new MediaDock(q)).Upload(Frame(2)));
        Assert.Single(SetImages(t));
    }

    [Fact]
    public void Upload_rejects_wrong_sized_frames()
    {
        var q = new QLinkClient(new FakeTransport());
        Assert.Throws<ArgumentException>(() => new FrameUploader(q, new MediaDock(q)).Upload(new byte[10]));
    }

    // ---------------------------------------------------------------- DockConfigGuard

    [Fact]
    public void Guard_saves_a_sane_config_once_and_keeps_it()
    {
        var path = Path.Combine(_dir, "backup.hex");
        var guard = new DockConfigGuard(path);
        var real = DockConfig.UserOriginal with { IdleSeconds = 45 };

        Assert.Equal(real, guard.Resolve(real));
        Assert.Equal(real, new DockConfigGuard(path).Resolve(real with { IdleSeconds = 3, ScreenOffSeconds = 0 }));
    }

    [Fact]
    public void Guard_never_saves_an_app_or_test_config()
    {
        var guard = new DockConfigGuard(Path.Combine(_dir, "backup.hex"));
        var leftover = DockConfig.UserOriginal with { IdleSeconds = 1, ScreenOffSeconds = 0 };

        Assert.Equal(DockConfig.UserOriginal, guard.Resolve(leftover));
    }

    [Fact]
    public void Running_config_shows_the_image_and_never_turns_the_screen_off()
    {
        var running = DockConfigGuard.Running(DockConfig.UserOriginal, idleSeconds: 3);

        Assert.Equal(ScreensaverMode.Image, running.Screensaver);
        Assert.Equal(3, running.IdleSeconds);
        Assert.Equal(0, running.ScreenOffSeconds);
        Assert.Equal((0xDC, 0x4D, 0x00), (running.MenuR, running.MenuG, running.MenuB));
        Assert.True(DockConfigGuard.LooksLikeAppConfig(running));
    }

    // ---------------------------------------------------------------- DockConnection

    sealed class Harness
    {
        public FakeTransport Transport = new();
        public bool IoCenter;
        public DockConnection Conn = null!;
        public byte[] Config = Convert.FromHexString("DC4D0001021E003C00");

        public Harness(string dir)
        {
            Transport.Responder = req => req switch
            {
                { Feature: Features.MediaDock, Command: MediaDockCommands.GetState } => FakeTransport.Reply(req, [1, 1]),
                { Feature: Features.MediaDock, Command: MediaDockCommands.GetConfig } => FakeTransport.Reply(req, Config),
                _ => FakeTransport.Ok(req),
            };
            Conn = new DockConnection(() => Transport, new DockConfigGuard(Path.Combine(dir, "b.hex")), () => IoCenter)
            {
                IdleSeconds = 3,
            };
        }

        public List<Frame> SetConfigs() =>
            Transport.Requests.Where(r => r is { Feature: Features.MediaDock, Command: MediaDockCommands.SetConfig }).ToList();
    }

    [Fact]
    public void Connect_opens_session_sets_clock_and_waits_for_first_frame()
    {
        var h = new Harness(_dir);

        h.Conn.Tick();

        Assert.Equal(DockState.Connected, h.Conn.State);
        var cmds = h.Transport.Requests.Select(r => (r.Feature, r.Command)).ToList();
        Assert.Contains((Features.Root, RootCommands.OpenSession), cmds);
        Assert.Contains((Features.MediaDock, MediaDockCommands.SetDateTime), cmds);
        Assert.Empty(h.SetConfigs()); // screensaver is enabled only after a complete upload
    }

    [Fact]
    public void First_frame_then_running_config_then_Ready()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();

        Assert.True(h.Conn.Present(Frame(3)));

        Assert.Equal(DockState.Ready, h.Conn.State);
        var cfg = DockConfig.FromBytes(Assert.Single(h.SetConfigs()).Data);
        Assert.Equal(DockConfigGuard.Running(DockConfig.UserOriginal, 3), cfg);
        int lastImage = h.Transport.Requests.FindLastIndex(r => r.Command == MediaDockCommands.SetImage);
        int config = h.Transport.Requests.FindIndex(r => r.Command == MediaDockCommands.SetConfig);
        Assert.True(config > lastImage);
    }

    [Fact]
    public void IO_Center_start_restores_the_users_config_and_releases_the_keyboard()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        h.Conn.Present(Frame(3));

        h.IoCenter = true;
        h.Conn.Tick();

        Assert.Equal(DockState.PausedForIoCenter, h.Conn.State);
        Assert.Equal(DockConfig.UserOriginal, DockConfig.FromBytes(h.SetConfigs()[^1].Data));
        Assert.Contains(h.Transport.Requests, r => r is { Feature: Features.Root, Command: RootCommands.CloseSession });
        Assert.True(h.Transport.Disposed);
    }

    [Fact]
    public void Does_not_connect_while_IO_Center_runs()
    {
        var h = new Harness(_dir) { IoCenter = true };

        h.Conn.Tick();

        Assert.Equal(DockState.PausedForIoCenter, h.Conn.State);
        Assert.Empty(h.Transport.Requests);
        Assert.False(h.Conn.Present(Frame(1)));
    }

    [Fact]
    public void User_pause_restores_config_and_resume_reconnects()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        h.Conn.Present(Frame(3));

        h.Conn.Paused = true;
        h.Conn.Tick();
        Assert.Equal(DockState.PausedByUser, h.Conn.State);

        h.Transport = new FakeTransport { Responder = h.Transport.Responder };
        h.Conn.Paused = false;
        h.Conn.Tick();
        Assert.Equal(DockState.Connected, h.Conn.State);
    }

    [Fact]
    public void Dispose_restores_the_users_config()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        h.Conn.Present(Frame(3));

        h.Conn.Dispose();

        Assert.Equal(DockConfig.UserOriginal, DockConfig.FromBytes(h.SetConfigs()[^1].Data));
    }

    [Fact]
    public void A_stalled_frame_makes_the_connection_back_off()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        var respond = h.Transport.Responder;
        h.Transport.Responder = req => req.Command == MediaDockCommands.SetImage ? null : respond(req);

        Assert.False(h.Conn.Present(Frame(1)));
        Assert.True(h.Conn.DockUnresponsive);
        int writes = SetImages(h.Transport).Count;

        Assert.False(h.Conn.Present(Frame(1))); // still backing off: nothing sent
        Assert.Equal(writes, SetImages(h.Transport).Count);
    }

    [Fact]
    public void Missing_keyboard_stays_disconnected()
    {
        var conn = new DockConnection(() => null, new DockConfigGuard(Path.Combine(_dir, "b.hex")), () => false);

        conn.Tick();

        Assert.Equal(DockState.Disconnected, conn.State);
        Assert.False(conn.Present(Frame(0)));
    }
}
