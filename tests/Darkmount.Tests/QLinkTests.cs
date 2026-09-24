using Darkmount.QLink;

namespace Darkmount.Tests;

public class QLinkTests
{
    [Fact]
    public void Crc16_matches_modbus_check_value()
    {
        Assert.Equal(0x4B37, Crc16.Modbus("123456789"u8));
    }

    [Fact]
    public void Single_frame_layout()
    {
        var packets = Frame.Build(sid: 2, reqId: 4, feature: 0x21, command: 7, data: [1, 2, 3]);

        var p = Assert.Single(packets);
        Assert.Equal(64, p.Length);
        Assert.Equal(new byte[] { 9, 0, 2, 0, 4, 0x21, 7, 1, 2, 3 }, p[..10]);
        var crc = Crc16.Modbus(p.AsSpan(0, 62));
        Assert.Equal((byte)crc, p[62]);
        Assert.Equal((byte)(crc >> 8), p[63]);
    }

    [Fact]
    public void Web_app_header_packet_matches_known_bytes()
    {
        // From docs/QLINK_PROTOCOL.md §4.2: SID 2, req 4, media dock SetImage header.
        byte[] data = [0, 0, 0, 0, 0, 0x09, 0x58, 0x02, 0x00, 0x40, 0x01, 0xF0, 0x00, 0x01];
        var p = Frame.Build(2, 4, 0x21, 7, data).Single();
        Assert.Equal(0x66, p[62]);
        Assert.Equal(0xC6, p[63]);
    }

    [Fact]
    public void Large_payload_is_split_into_continuation_frames()
    {
        var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();

        var packets = Frame.Build(1, 9, 0x21, 7, data);

        // 55 bytes in the first frame, 59 in each continuation: 55 + 59 + 59 + 27.
        Assert.Equal(4, packets.Count);
        Assert.Equal(new byte[] { 0x80, 0x81, 0x82, 0x03 }, packets.Select(p => p[1]));
        Assert.Equal(55 + 6, packets[0][0]);
        Assert.Equal(59 + 2, packets[1][0]);
        Assert.Equal(27 + 2, packets[3][0]);
        Assert.Equal(data, packets.SelectMany(p => Frame.Parse(p).Data));
    }

    [Fact]
    public void Parse_round_trips_and_detects_notifications()
    {
        var f = Frame.Parse(Frame.Build(3, 0, 1, 1, [1], status: 0).Single());
        Assert.True(f.IsNotification);
        Assert.Equal(new byte[] { 1 }, f.Data);

        var r = Frame.Parse(Frame.Build(3, 7, 0x21, 2, [5, 6], status: 9).Single());
        Assert.False(r.IsNotification);
        Assert.Equal((3, 9, 7, 0x21, 2), (r.Sid, r.Status, r.ReqId, r.Feature, r.Command));
        Assert.Equal(new byte[] { 5, 6 }, r.Data);
    }

    [Fact]
    public void OpenSession_parses_sid_state_and_timeout()
    {
        var t = new FakeTransport();
        using var q = new QLinkClient(t);

        q.OpenSession();

        Assert.Equal(5, q.Sid);
        Assert.True(q.IsActive);
        Assert.Equal(2, q.SessionTimeoutSeconds);
        var open = t.Requests.Single();
        Assert.Equal(0, open.Sid);
        Assert.Equal(QLinkClient.ClientTypeWeb, open.Data[4]);
    }

    [Fact]
    public void Stale_replies_with_another_request_id_are_skipped()
    {
        var t = new FakeTransport();
        using var q = new QLinkClient(t);
        q.KeepAlive(); // request id 1, so the stale reply below has a non-zero id
        t.Responder = req =>
            // A late reply for an earlier request arrives first.
            FakeTransport.Reply(req with { ReqId = (byte)(req.ReqId - 1) }, [0xEE])
                .Concat(FakeTransport.Reply(req, [0x42]));

        var data = q.Send(Features.MediaDock, MediaDockCommands.GetState);

        Assert.Equal(new byte[] { 0x42 }, data);
    }

    [Fact]
    public void Error_status_throws_QLinkException()
    {
        var t = new FakeTransport { Responder = req => FakeTransport.Reply(req, [], status: 9) };
        using var q = new QLinkClient(t);

        var ex = Assert.Throws<QLinkException>(() => q.Send(Features.MediaDock, MediaDockCommands.GetConfig));
        Assert.Equal(QLinkStatus.NotActive, ex.Status);
    }

    [Fact]
    public void Notifications_are_raised_and_update_session_state()
    {
        var t = new FakeTransport();
        using var q = new QLinkClient(t);
        q.OpenSession();
        var seen = new List<Frame>();
        q.Notification += seen.Add;
        t.Responder = req =>
            Frame.Build(req.Sid, 0, Features.Root, RootNotifications.SessionStateChanged, [0])
                .Concat(FakeTransport.Reply(req, []));

        q.KeepAlive();

        Assert.Single(seen);
        Assert.False(q.IsActive);
    }

    [Theory]
    [InlineData(Features.Dfu, 3)]           // firmware write
    [InlineData(Features.DeviceInfo, 5)]    // factory reset
    [InlineData(Features.Storage, 2)]       // raw user flash
    public void Dangerous_commands_are_refused_before_any_write(byte feature, byte command)
    {
        var t = new FakeTransport();
        using var q = new QLinkClient(t);

        Assert.Throws<InvalidOperationException>(() => q.Send(feature, command));
        Assert.Empty(t.Written);
    }

    [Fact]
    public void Multi_frame_reply_is_concatenated()
    {
        var payload = Enumerable.Range(0, 120).Select(i => (byte)i).ToArray();
        var t = new FakeTransport { Responder = req => FakeTransport.Reply(req, payload) };
        using var q = new QLinkClient(t);

        Assert.Equal(payload, q.Send(Features.MediaDock, MediaDockCommands.GetImage, new byte[9]));
    }

    /// <summary>Real firmware quirk: a finished reply is held until the host sends something else.</summary>
    sealed class HoldingTransport : IHidTransport
    {
        readonly FakeTransport _inner = new();
        byte[]? _held;
        public int SetImageWrites;
        public int GetStateWrites;

        public void Write(ReadOnlySpan<byte> packet)
        {
            var f = Frame.Parse(packet);
            if (_held is not null) { _inner.Enqueue(_held); _held = null; } // any new traffic flushes it
            if (f.IsContinuation) return;
            if (f.Command == MediaDockCommands.SetImage)
            {
                SetImageWrites++;
                _held = FakeTransport.Reply(f, []).Single();
            }
            else
            {
                if (f.Command == MediaDockCommands.GetState) GetStateWrites++;
                foreach (var r in FakeTransport.Reply(f, [1, 1])) _inner.Enqueue(r);
            }
        }

        public byte[] Read(int timeoutMs)
        {
            try { return _inner.Read(timeoutMs); }
            catch (TimeoutException) { Thread.Sleep(Math.Min(timeoutMs, 20)); throw; }
        }

        public void Dispose() { }
    }

    [Fact]
    public void A_held_image_write_reply_is_flushed_by_repeating_the_write()
    {
        var t = new HoldingTransport();
        using var q = new QLinkClient(t) { NudgeAfterMs = 50, NudgeMode = NudgeMode.RepeatRequest };

        q.Send(Features.MediaDock, MediaDockCommands.SetImage, new byte[20], timeoutMs: 2000);

        Assert.Equal(2, t.SetImageWrites);
        Assert.Equal(0, t.GetStateWrites);
        Assert.Equal(1, q.Nudges);
    }

    [Fact]
    public void Sequence_releases_held_replies_by_sending_the_next_part_and_never_repeats()
    {
        var t = new HoldingTransport();
        using var q = new QLinkClient(t);
        var parts = Enumerable.Range(0, 6).Select(i => new byte[] { 0, (byte)i, 0, 0, 0, 1, 2, 3 }).ToList();

        q.SendSequence(Features.MediaDock, MediaDockCommands.SetImage, parts, heldAfterMs: 30, timeoutMs: 1000, finalGraceMs: 60);

        Assert.Equal(6, t.SetImageWrites);   // each part exactly once
        Assert.Equal(0, t.GetStateWrites);
        Assert.Equal(1, q.HeldFinalReplies); // the last reply stayed held and was accepted
    }

    [Fact]
    public void Sequence_with_prompt_replies_sends_each_part_once()
    {
        var t = new FakeTransport();
        using var q = new QLinkClient(t);
        var parts = Enumerable.Range(0, 5).Select(i => new byte[] { 0, (byte)i, 0, 0, 0, 9 }).ToList();

        q.SendSequence(Features.MediaDock, MediaDockCommands.SetImage, parts, heldAfterMs: 30);

        Assert.Equal(5, t.Requests.Count);
        Assert.Equal(0, q.HeldFinalReplies);
    }

    [Fact]
    public void Sequence_stops_on_a_rejected_part()
    {
        var t = new FakeTransport { Responder = req => FakeTransport.Reply(req, [], status: 10) };
        using var q = new QLinkClient(t);

        var ex = Assert.Throws<QLinkException>(() => q.SendSequence(Features.MediaDock, MediaDockCommands.SetImage,
            [new byte[] { 0, 0, 0, 0, 0, 1 }, new byte[] { 0, 1, 0, 0, 0, 1 }], heldAfterMs: 30));
        Assert.Equal(QLinkStatus.InvalidState, ex.Status);
    }

    [Fact]
    public void Sequence_times_out_when_the_device_is_silent()
    {
        var t = new FakeTransport { Responder = _ => null };
        using var q = new QLinkClient(t);
        var parts = Enumerable.Range(0, 4).Select(i => new byte[] { 0, (byte)i, 0, 0, 0, 1 }).ToList();

        Assert.Throws<TimeoutException>(() => q.SendSequence(Features.MediaDock, MediaDockCommands.SetImage, parts,
            heldAfterMs: 20, timeoutMs: 100, finalGraceMs: 50));
        Assert.Equal(2, t.Requests.Count); // at most two outstanding, never a repeat
    }

    [Fact]
    public void GetState_nudge_mode_sends_a_status_request()
    {
        var t = new HoldingTransport();
        using var q = new QLinkClient(t) { NudgeAfterMs = 50, NudgeMode = NudgeMode.GetState };

        q.Send(Features.MediaDock, MediaDockCommands.SetImage, new byte[20], timeoutMs: 2000);

        Assert.Equal(1, t.SetImageWrites);
        Assert.Equal(1, t.GetStateWrites);
    }

    [Fact]
    public void Non_image_requests_are_never_nudged_or_repeated()
    {
        var t = new FakeTransport { Responder = _ => null };
        using var q = new QLinkClient(t) { NudgeAfterMs = 10 };

        Assert.Throws<TimeoutException>(() => q.Send(Features.MediaDock, MediaDockCommands.SetConfig, new byte[9], timeoutMs: 100));
        Assert.Single(t.Written);
    }

    [Fact]
    public void Timeout_propagates_as_TimeoutException()
    {
        var t = new FakeTransport { Responder = _ => null };
        using var q = new QLinkClient(t);

        Assert.Throws<TimeoutException>(() => q.Send(Features.MediaDock, MediaDockCommands.GetState, timeoutMs: 10));
    }

    [Fact]
    public void DockConfig_round_trips_the_users_original_settings()
    {
        var bytes = Convert.FromHexString("DC4D0001021E003C00");

        var c = DockConfig.FromBytes(bytes);

        Assert.Equal((0xDC, 0x4D, 0x00), (c.MenuR, c.MenuG, c.MenuB));
        Assert.True(c.Clock24h);
        Assert.Equal(ScreensaverMode.Image, c.Screensaver);
        Assert.Equal(30, c.IdleSeconds);
        Assert.Equal(60, c.ScreenOffSeconds);
        Assert.Equal(bytes, c.ToBytes());
    }

    [Fact]
    public void DateTime_is_encoded_as_local_wall_clock_seconds()
    {
        var bytes = MediaDock.EncodeDateTime(new DateTime(2026, 9, 24, 15, 0, 0, DateTimeKind.Local));

        Assert.Equal(1790262000u, BitConverter.ToUInt32(bytes));
    }
}
