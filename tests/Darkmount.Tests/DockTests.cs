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
    public void Upload_sends_header_then_every_pixel_once_in_single_frame_writes()
    {
        var t = new FakeTransport();
        var q = new QLinkClient(t);
        var up = new FrameUploader(new MediaDock(q));

        Assert.Equal(UploadResult.Done, up.Upload(Frame(0xAB)));

        var writes = SetImages(t);
        Assert.Equal(1 + 3072, writes.Count); // header + 153,600 / 50
        Assert.Equal(1 + 3072, t.Written.Count); // every write fits in one 64-byte packet
        Assert.Equal(MediaDock.ImageHeader(320, 240, FrameUploader.FrameBytes), writes[0].Data[5..]);
        Assert.Equal(Enumerable.Range(0, 3072).Select(i => 9u + (uint)i * 50).Prepend(0u), writes.Select(OffsetOf));
        Assert.All(writes, w => Assert.Equal(MediaDock.SlotScreensaver, w.Data[0]));
    }

    [Fact]
    public void A_silent_dock_stalls_the_frame_without_repeating_anything()
    {
        var t = new FakeTransport();
        var q = new QLinkClient(t);
        t.Responder = req => req.Command == MediaDockCommands.SetImage && OffsetOf(req) > 0 ? null : FakeTransport.Ok(req);
        var up = new FrameUploader(new MediaDock(q)) { HeaderTimeoutMs = 200, ChunkTimeoutMs = 100 };

        Assert.Equal(UploadResult.Stalled, up.Upload(Frame(2)));
        var offsets = SetImages(t).Select(OffsetOf).ToList();
        Assert.Equal([0u, 9u, 59u, 109u, 159u], offsets); // header + one window of 4, nothing re-sent
    }

    [Fact]
    public void A_sleeping_dock_that_ignores_the_header_gets_nothing_else()
    {
        var t = new FakeTransport { Responder = req => req.Command == MediaDockCommands.SetImage ? null : FakeTransport.Ok(req) };
        var q = new QLinkClient(t);

        Assert.Equal(UploadResult.Stalled, new FrameUploader(new MediaDock(q)) { HeaderTimeoutMs = 100 }.Upload(Frame(2)));
        Assert.Single(SetImages(t));
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
                HeaderTimeoutMs = 200,
                ChunkTimeoutMs = 200,
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
    public void Unplugging_the_dock_mid_frame_keeps_the_keyboard_session()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        h.Conn.Present(Frame(3));
        bool unplugged = false;
        var respond = h.Transport.Responder;
        h.Transport.Responder = req => req switch
        {
            { Command: MediaDockCommands.SetImage } when unplugged => FakeTransport.Reply(req, [], status: (byte)QLinkStatus.InvalidState),
            { Feature: Features.MediaDock, Command: MediaDockCommands.GetState } when unplugged => FakeTransport.Reply(req, [0, 0]),
            _ => respond(req),
        };

        unplugged = true;
        Assert.False(h.Conn.Present(Frame(4)));

        Assert.Equal(DockState.NoMediaDock, h.Conn.State);
        Assert.False(h.Transport.Disposed);
        Assert.True(h.Conn.TryExecute(q => q.KeepAlive()));
    }

    [Fact]
    public void Detached_media_dock_keeps_the_session_for_keyboard_features()
    {
        var h = new Harness(_dir);
        var respond = h.Transport.Responder;
        h.Transport.Responder = req => req is { Feature: Features.MediaDock, Command: MediaDockCommands.GetState }
            ? FakeTransport.Reply(req, [0, 0]) : respond(req);

        h.Conn.Tick();

        Assert.Equal(DockState.NoMediaDock, h.Conn.State);
        Assert.False(h.Conn.Present(Frame(1)));
        Assert.True(h.Conn.TryExecute(q => q.KeepAlive()));
        Assert.False(h.Transport.Disposed);
    }

    [Fact]
    public void Guard_update_saves_user_choices_but_rejects_app_style_configs()
    {
        var path = Path.Combine(_dir, "g.hex");
        var guard = new DockConfigGuard(path);
        var blue = DockConfig.UserOriginal with { MenuR = 0, MenuG = 0x80, MenuB = 0xFF, Clock24h = false };

        guard.Update(blue);

        Assert.Equal(blue, new DockConfigGuard(path).Current);
        Assert.Throws<ArgumentException>(() => guard.Update(blue with { IdleSeconds = 2 }));
    }

    [Fact]
    public void Changing_dock_settings_while_running_applies_colour_and_clock_now()
    {
        var h = new Harness(_dir);
        h.Conn.Tick();
        h.Conn.Present(Frame(3));
        var green = DockConfig.UserOriginal with { MenuR = 0, MenuG = 0xFF, MenuB = 0 };

        h.Conn.UpdateUserDockConfig(green);

        var sent = DockConfig.FromBytes(h.SetConfigs()[^1].Data);
        Assert.Equal(DockConfigGuard.Running(green, 3), sent);
        h.Conn.Dispose();
        Assert.Equal(green, DockConfig.FromBytes(h.SetConfigs()[^1].Data)); // restored on exit
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
