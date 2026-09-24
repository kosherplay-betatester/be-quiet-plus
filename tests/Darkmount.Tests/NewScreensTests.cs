using Darkmount.App.Sources;
using Darkmount.Screens;
using Darkmount.Sensors;
using SkiaSharp;

namespace Darkmount.Tests;

/// <summary>Now playing, Clock, Network and Focus timer screens plus their data sources.</summary>
public class NewScreensTests
{
    // Thursday evening, so the calendar highlights the 24th.
    private static readonly DateTime Evening = new(2026, 9, 24, 21, 47, 12);

    private static SKBitmap Render(IDockScreen screen, ScreenContext ctx, string name)
    {
        var bmp = DockRenderer.Render(screen, ctx);
        ScreenRenderTests.Save(bmp, name);
        ScreenRenderTests.AssertValidFrame(bmp);
        return bmp;
    }

    private static ScreenContext Ctx(MediaInfo? media = null, NetworkInfo? network = null, PomodoroInfo? pomodoro = null,
        Snapshot? snapshot = null, MetricHistory? history = null) => new()
    {
        Snapshot = snapshot ?? new Snapshot(),
        History = history ?? new MetricHistory(),
        Now = Evening,
        Media = media,
        Network = network,
        Pomodoro = pomodoro,
    };

    /// <summary>Counts pixels within <paramref name="tolerance"/> (per channel) of <paramref name="color"/>.</summary>
    private static int CountNear(SKBitmap bmp, SKColor color, int tolerance)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (Math.Abs(p.Red - color.Red) <= tolerance && Math.Abs(p.Green - color.Green) <= tolerance &&
                    Math.Abs(p.Blue - color.Blue) <= tolerance) n++;
            }
        return n;
    }

    /// <summary>A 300×300 "album cover": purple→orange gradient with a solid yellow disc in the middle.</summary>
    internal static byte[] FakeArtwork(int size = 300)
    {
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            using var bg = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                    [new SKColor(0x5B, 0x21, 0xB6), new SKColor(0xF9, 0x73, 0x16)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, size, size, bg);
            using var disc = new SKPaint { Color = new SKColor(0xFA, 0xCC, 0x15), IsAntialias = true };
            canvas.DrawCircle(size / 2f, size / 2f, size * 0.2f, disc);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ================================================================ Now playing

    [Fact]
    public void NowPlaying_Name() => Assert.Equal("Now playing", new NowPlayingScreen().Name);

    [Fact]
    public void NowPlaying_WithArtwork_DrawsArtInLeftSquare()
    {
        var media = new MediaInfo("Midnight City", "M83", "Hurry Up, We're Dreaming", "Spotify",
            TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(243), Playing: true, FakeArtwork());
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying");

        var art = NowPlayingScreen.ArtRect;
        Assert.InRange(art.Width, 120, 140);
        Assert.Equal(art.Width, art.Height);
        Assert.True(art.Left < 40, "artwork sits on the left");
        var center = bmp.GetPixel((int)art.MidX, (int)art.MidY);
        Assert.True(center.Red > 220 && center.Green > 180 && center.Blue < 60, $"art centre pixel {center}");
    }

    [Fact]
    public void NowPlaying_WideArtwork_IsCoverCropped()
    {
        // A 3:1 banner: the disc (centre) must still land in the middle of the square, filling it edge to edge.
        using var src = new SKBitmap(new SKImageInfo(600, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(src))
        {
            c.Clear(new SKColor(0x10, 0x60, 0xE0));
            using var red = new SKPaint { Color = new SKColor(0xE0, 0x20, 0x20) };
            c.DrawRect(250, 0, 100, 200, red);
        }
        using var img = SKImage.FromBitmap(src);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        var media = new MediaInfo("Wide", "Artist", null, null, TimeSpan.Zero, TimeSpan.FromMinutes(3), true, png.ToArray());
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying-wideart");

        var art = NowPlayingScreen.ArtRect;
        var mid = bmp.GetPixel((int)art.MidX, (int)art.MidY);
        var edge = bmp.GetPixel((int)art.Left + 4, (int)art.MidY);
        Assert.True(mid.Red > 180 && mid.Blue < 80, $"centre {mid}");
        Assert.True(edge.Blue > 160 && edge.Red < 80, $"left edge {edge}"); // blue, not letterboxed background
    }

    [Fact]
    public void NowPlaying_PausedWithoutArtwork_LongTitle()
    {
        var media = new MediaInfo(
            "Bohemian Rhapsody - Remastered 2011 Version With An Unreasonably Long Title That Must Be Cut",
            "Queen", "A Night at the Opera (2011 Remaster)", "Chrome",
            TimeSpan.FromSeconds(301), TimeSpan.FromSeconds(354), Playing: false, ArtworkPng: null);
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying-paused-noart");
    }

    [Fact]
    public void NowPlaying_NonLatinTitles()
    {
        var media = new MediaInfo("שיר לשלום", "להקת הנח\"ל", null, "Spotify",
            TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(200), Playing: true, FakeArtwork(120));
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying-hebrew");

        var jp = new MediaInfo("夜に駆ける", "YOASOBI", "THE BOOK", "Media Player",
            TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(261), Playing: true, null);
        using var bmp2 = Render(new NowPlayingScreen(), Ctx(jp), "nowplaying-japanese");
    }

    [Fact]
    public void NowPlaying_LiveStream_NoDuration()
    {
        var media = new MediaInfo("lofi hip hop radio 📚 beats to relax/study to", "Lofi Girl", null, "Edge",
            TimeSpan.FromMinutes(95), TimeSpan.Zero, Playing: true, null);
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying-live");
    }

    [Fact]
    public void NowPlaying_Null_ShowsNothingPlaying()
    {
        using var bmp = Render(new NowPlayingScreen(), Ctx(), "nowplaying-null");
    }

    [Fact]
    public void NowPlaying_CorruptArtwork_FallsBackToPlaceholder()
    {
        var media = new MediaInfo("Song", "Artist", null, null, TimeSpan.Zero, TimeSpan.FromMinutes(3), true, [1, 2, 3, 4]);
        using var bmp = Render(new NowPlayingScreen(), Ctx(media), "nowplaying-badart");
    }

    // ================================================================ Clock

    [Fact]
    public void Clock_Name() => Assert.Equal("Clock", new ClockScreen().Name);

    [Fact]
    public void Clock_WithTemperatures()
    {
        var s = new Snapshot { CpuTemp = 52, GpuTemp = 47 };
        using var bmp = Render(new ClockScreen(firstDayOfWeek: DayOfWeek.Monday), Ctx(snapshot: s), "clock");
    }

    [Fact]
    public void Clock_NoStats_SundayFirst_12Hour()
    {
        using var bmp = Render(new ClockScreen(use24Hour: false, firstDayOfWeek: DayOfWeek.Sunday), Ctx(), "clock-12h-null");
    }

    [Fact]
    public void Clock_HotCpu_Morning_SixWeekMonth()
    {
        var s = new Snapshot { CpuTemp = 93, GpuTemp = 84 };
        var ctx = new ScreenContext { Snapshot = s, History = new MetricHistory(), Now = new DateTime(2026, 8, 31, 7, 5, 0) };
        using var bmp = Render(new ClockScreen(firstDayOfWeek: DayOfWeek.Monday), ctx, "clock-hot");
    }

    [Theory]
    [InlineData(2026, 9, DayOfWeek.Monday, 1, 5)]   // 1 Sep 2026 is a Tuesday → column 1, 5 rows
    [InlineData(2026, 9, DayOfWeek.Sunday, 2, 5)]
    [InlineData(2026, 8, DayOfWeek.Monday, 5, 6)]   // 1 Aug 2026 is a Saturday → needs 6 rows
    [InlineData(2026, 2, DayOfWeek.Sunday, 0, 4)]   // Feb 2026 starts on Sunday, 28 days
    public void Clock_CalendarGrid(int year, int month, DayOfWeek first, int expectedOffset, int expectedRows)
    {
        var (offset, rows) = ClockScreen.MonthGrid(year, month, first);
        Assert.Equal(expectedOffset, offset);
        Assert.Equal(expectedRows, rows);
    }

    // ================================================================ Network

    private static MetricHistory NetWaves()
    {
        var h = new MetricHistory();
        for (int i = 0; i < 60; i++)
        {
            double down = 2.5e6 + 1.8e6 * Math.Sin(i * 0.3) + (i is > 40 and < 50 ? 9e6 : 0) + 4e5 * Math.Sin(i * 1.7);
            double up = 3.2e5 + 1.5e5 * Math.Sin(i * 0.5 + 1) + (i % 13 == 0 ? 6e5 : 0);
            h.AddNetwork(Math.Max(0, down), Math.Max(0, up));
        }
        return h;
    }

    [Fact]
    public void Network_Name() => Assert.Equal("Network", new NetworkScreen().Name);

    [Fact]
    public void Network_Typical()
    {
        var net = new NetworkInfo(11.8 * 1024 * 1024, 420 * 1024, "Ethernet");
        using var bmp = Render(new NetworkScreen(), Ctx(network: net, history: NetWaves()), "network");
        Assert.True(CountNear(bmp, Theme.Mem, 40) > 150, "download graph in blue");
    }

    [Fact]
    public void Network_Idle_LongAdapterName()
    {
        var h = new MetricHistory();
        for (int i = 0; i < 20; i++) h.AddNetwork(300 + 100 * Math.Sin(i), 120);
        var net = new NetworkInfo(350, 90, "Intel(R) Wi-Fi 6E AX211 160MHz Wireless Network Adapter");
        using var bmp = Render(new NetworkScreen(), Ctx(network: net, history: h), "network-idle");
    }

    [Fact]
    public void Network_Null()
    {
        using var bmp = Render(new NetworkScreen(), Ctx(), "network-null");
    }

    [Theory]
    [InlineData(0, "0.0", "KB/s")]
    [InlineData(350, "0.3", "KB/s")]
    [InlineData(1024 * 42.5, "42.5", "KB/s")]
    [InlineData(1024 * 845, "845", "KB/s")]
    [InlineData(1024 * 1000, "0.98", "MB/s")]
    [InlineData(1024 * 1024 * 11.84, "11.8", "MB/s")]
    [InlineData(1024 * 1024 * 118.4, "118", "MB/s")]
    [InlineData(1024.0 * 1024 * 1024 * 2.5, "2.50", "GB/s")]
    [InlineData(-5, "0.0", "KB/s")]
    [InlineData(double.NaN, "--", "KB/s")]
    public void Network_FormatRate(double bytesPerSec, string value, string unit)
    {
        var (v, u) = NetworkScreen.FormatRate(bytesPerSec);
        Assert.Equal(value, v);
        Assert.Equal(unit, u);
    }

    [Fact]
    public void MetricHistory_AddNetwork_Keeps60_AndStatsUnaffected()
    {
        var h = new MetricHistory();
        for (int i = 0; i < 75; i++) h.AddNetwork(i * 10, i);
        Assert.Equal(60, h.NetDown.Count);
        Assert.Equal(60, h.NetUp.Count);
        Assert.Equal(150, h.NetDown[0]);
        Assert.Equal(740, h.NetDown[^1]);
        Assert.Equal(74, h.NetUp[^1]);
        Assert.Empty(h.CpuTemp);

        h.Add(new Snapshot { CpuTemp = 50 });
        Assert.Single(h.CpuTemp);
        Assert.Equal(60, h.NetDown.Count);
    }

    // ================================================================ Focus timer screen

    [Fact]
    public void Pomodoro_Name() => Assert.Equal("Focus timer", new PomodoroScreen().Name);

    [Theory]
    [InlineData("Focus", 17 * 60 + 32, 25 * 60, 3, "pomodoro-focus")]
    [InlineData("Break", 3 * 60 + 5, 5 * 60, 4, "pomodoro-break")]
    [InlineData("Long break", 12 * 60 + 40, 15 * 60, 8, "pomodoro-longbreak")]
    [InlineData("Paused", 11 * 60 + 20, 25 * 60, 2, "pomodoro-paused")]
    [InlineData("Ready", 25 * 60, 25 * 60, 0, "pomodoro-ready")]
    [InlineData("Focus", 45, 25 * 60, 11, "pomodoro-focus-many")]
    public void Pomodoro_Renders(string phase, int remainingSec, int totalSec, int done, string file)
    {
        var info = new PomodoroInfo(phase, TimeSpan.FromSeconds(remainingSec), TimeSpan.FromSeconds(totalSec), done);
        using var bmp = Render(new PomodoroScreen(), Ctx(pomodoro: info), file);
    }

    [Fact]
    public void Pomodoro_FocusIsOrange_PausedIsDimmed()
    {
        var focus = new PomodoroInfo("Focus", TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(25), 1);
        using var a = DockRenderer.Render(new PomodoroScreen(), Ctx(pomodoro: focus));
        Assert.True(CountNear(a, Theme.Cpu, 30) > 300, "focus ring is orange");

        var brk = new PomodoroInfo("Break", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), 1);
        using var b = DockRenderer.Render(new PomodoroScreen(), Ctx(pomodoro: brk));
        Assert.True(CountNear(b, Theme.Gpu, 30) > 100, "break ring is green");

        var paused = focus with { Phase = "Paused" };
        using var p = DockRenderer.Render(new PomodoroScreen(), Ctx(pomodoro: paused));
        Assert.True(CountNear(p, Theme.Cpu, 30) < 20, "paused state has no bright accent ring");
    }

    [Fact]
    public void Pomodoro_Null()
    {
        using var bmp = Render(new PomodoroScreen(), Ctx(), "pomodoro-null");
    }

    // ================================================================ PomodoroTimer

    private sealed class FakeClock(DateTime start)
    {
        public DateTime Now { get; set; } = start;
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void Timer_BeforeStart_IsReady()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now);
        var info = t.Info(clock.Now);
        Assert.Equal(PomodoroInfo.Ready, info.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), info.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(25), info.Total);
        Assert.Equal(0, info.CompletedToday);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromMinutes(25), t.Info(clock.Now).Remaining);
    }

    [Fact]
    public void Timer_FocusCountsDown_ThenBreakStarts()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now);
        var phases = new List<string>();
        t.PhaseChanged += phases.Add;

        t.Start();
        Assert.Equal(new PomodoroInfo("Focus", TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), 0), t.Info(clock.Now));

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), t.Info(clock.Now).Remaining);

        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(20)); // polled 20 s late
        var info = t.Info(clock.Now);
        Assert.Equal("Break", info.Phase);
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(20), info.Remaining); // break started on time
        Assert.Equal(TimeSpan.FromMinutes(5), info.Total);
        Assert.Equal(1, info.CompletedToday);

        clock.Advance(TimeSpan.FromMinutes(5));
        info = t.Info(clock.Now);
        Assert.Equal(PomodoroInfo.Ready, info.Phase); // next focus waits for Start()
        Assert.Equal(TimeSpan.FromMinutes(25), info.Remaining);
        Assert.Equal(["Focus", "Break", "Ready"], phases);
    }

    [Fact]
    public void Timer_EveryFourthFocus_GetsLongBreak()
    {
        var clock = new FakeClock(Evening.Date.AddHours(9));
        var t = new PomodoroTimer(() => clock.Now);
        var breaks = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            t.Start();
            clock.Advance(TimeSpan.FromMinutes(25));
            var info = t.Info(clock.Now);
            breaks.Add(info.Phase);
            Assert.Equal(i, info.CompletedToday);
            clock.Advance(info.Total);
            Assert.Equal(PomodoroInfo.Ready, t.Info(clock.Now).Phase);
        }
        Assert.Equal(["Break", "Break", "Break", "Long break", "Break"], breaks);
    }

    [Fact]
    public void Timer_LongBreakIs15Minutes_AndConfigurable()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now)
        {
            FocusDuration = TimeSpan.FromMinutes(50),
            BreakDuration = TimeSpan.FromMinutes(10),
            LongBreakDuration = TimeSpan.FromMinutes(30),
            LongBreakEvery = 2,
        };
        Assert.Equal(TimeSpan.FromMinutes(50), t.Info(clock.Now).Total);
        t.Start();
        clock.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal(new PomodoroInfo("Break", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), 1), t.Info(clock.Now));
        clock.Advance(TimeSpan.FromMinutes(10));
        t.Start();
        clock.Advance(TimeSpan.FromMinutes(50));
        Assert.Equal(new PomodoroInfo("Long break", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), 2), t.Info(clock.Now));

        Assert.Equal(TimeSpan.FromMinutes(25), new PomodoroTimer().FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), new PomodoroTimer().BreakDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), new PomodoroTimer().LongBreakDuration);
        Assert.Equal(4, new PomodoroTimer().LongBreakEvery);
    }

    [Fact]
    public void Timer_PauseStopsCountdown_ResumeContinues()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now);
        var phases = new List<string>();
        t.PhaseChanged += phases.Add;

        t.Start();
        clock.Advance(TimeSpan.FromMinutes(5));
        t.Pause();
        Assert.True(t.IsPaused);
        var info = t.Info(clock.Now);
        Assert.Equal("Paused", info.Phase);
        Assert.Equal(TimeSpan.FromMinutes(20), info.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(25), info.Total);

        clock.Advance(TimeSpan.FromMinutes(40)); // longer than the whole focus period
        Assert.Equal(TimeSpan.FromMinutes(20), t.Info(clock.Now).Remaining);
        Assert.Equal("Paused", t.Info(clock.Now).Phase);

        t.Resume();
        Assert.False(t.IsPaused);
        Assert.Equal(new PomodoroInfo("Focus", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(25), 0), t.Info(clock.Now));
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal("Break", t.Info(clock.Now).Phase);
        Assert.Equal(["Focus", "Paused", "Focus", "Break"], phases);
    }

    [Fact]
    public void Timer_Toggle_StartPauseResume()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now);
        t.Toggle();
        Assert.Equal("Focus", t.Info(clock.Now).Phase);
        t.Toggle();
        Assert.Equal("Paused", t.Info(clock.Now).Phase);
        t.Toggle();
        Assert.Equal("Focus", t.Info(clock.Now).Phase);
    }

    [Fact]
    public void Timer_Skip_And_Reset()
    {
        var clock = new FakeClock(Evening);
        var t = new PomodoroTimer(() => clock.Now);
        t.Start();
        clock.Advance(TimeSpan.FromMinutes(3));
        t.Skip(); // abandon focus → break, not counted
        var info = t.Info(clock.Now);
        Assert.Equal("Break", info.Phase);
        Assert.Equal(TimeSpan.FromMinutes(5), info.Remaining);
        Assert.Equal(0, info.CompletedToday);

        t.Skip(); // skip the break → the next focus starts right away
        Assert.Equal(new PomodoroInfo("Focus", TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), 0), t.Info(clock.Now));

        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(1, t.Info(clock.Now).CompletedToday);
        clock.Advance(TimeSpan.FromMinutes(1));
        t.Reset();
        info = t.Info(clock.Now);
        Assert.Equal(PomodoroInfo.Ready, info.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), info.Remaining);
        Assert.Equal(1, info.CompletedToday); // finished pomodoros are kept
    }

    [Fact]
    public void Timer_CompletedToday_ResetsAtMidnight()
    {
        var clock = new FakeClock(new DateTime(2026, 9, 24, 23, 0, 0));
        var t = new PomodoroTimer(() => clock.Now);
        t.Start();
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(1, t.Info(clock.Now).CompletedToday);
        clock.Advance(TimeSpan.FromMinutes(40)); // 00:05 next day
        Assert.Equal(0, t.Info(clock.Now).CompletedToday);
        t.Start();
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(1, t.Info(clock.Now).CompletedToday);
    }

    [Fact]
    public void Timer_LongAbsence_DoesNotRunAway()
    {
        var clock = new FakeClock(Evening.Date.AddHours(9));
        var t = new PomodoroTimer(() => clock.Now);
        t.Start();
        clock.Advance(TimeSpan.FromHours(5));
        var info = t.Info(clock.Now);
        Assert.Equal(PomodoroInfo.Ready, info.Phase);
        Assert.Equal(1, info.CompletedToday);
    }

    [Fact]
    public void Timer_IsThreadSafe()
    {
        var t = new PomodoroTimer();
        Parallel.For(0, 2000, i =>
        {
            switch (i % 6)
            {
                case 0: t.Start(); break;
                case 1: t.Pause(); break;
                case 2: t.Resume(); break;
                case 3: t.Skip(); break;
                case 4: t.Toggle(); break;
                default: _ = t.Info(); break;
            }
        });
        Assert.NotNull(t.Info());
    }

    // ================================================================ NetworkMonitor

    [Fact]
    public void NetworkMonitor_FirstSampleIsBaseline_ThenRatesFromDeltas()
    {
        var m = new NetworkMonitor(sampleNow: false);
        Assert.Null(m.Update([new("eth0", "Ethernet", 1_000, 500, true)], TimeSpan.FromSeconds(10)));

        var r = m.Update([new("eth0", "Ethernet", 1_000 + 5_000_000, 500 + 1_000_000, true)], TimeSpan.FromSeconds(15));
        Assert.NotNull(r);
        Assert.Equal(1_000_000, r.DownloadBytesPerSec, 3);
        Assert.Equal(200_000, r.UploadBytesPerSec, 3);
        Assert.Equal("Ethernet", r.AdapterName);

        // Half-second interval: 100 KB → 200 KB/s.
        r = m.Update([new("eth0", "Ethernet", 5_001_000 + 100_000, 1_000_500, true)], TimeSpan.FromSeconds(15.5));
        Assert.Equal(200_000, r!.DownloadBytesPerSec, 3);
        Assert.Equal(0, r.UploadBytesPerSec, 3);
    }

    [Fact]
    public void NetworkMonitor_SumsAdapters_IgnoresNewAndResetCounters()
    {
        var m = new NetworkMonitor(sampleNow: false);
        m.Update([new("wifi", "Wi-Fi", 100, 100, true), new("eth", "Ethernet", 1000, 1000, true)], TimeSpan.Zero);

        // Wi-Fi +2 MB, Ethernet counter reset (negative delta → 0), VPN is new (no baseline yet → 0).
        var r = m.Update(
        [
            new("wifi", "Wi-Fi", 100 + 2_000_000, 100 + 400_000, true),
            new("eth", "Ethernet", 10, 10, true),
            new("vpn", "WireGuard", 9_999_999, 9_999_999, true),
        ], TimeSpan.FromSeconds(2));
        Assert.Equal(1_000_000, r!.DownloadBytesPerSec, 3);
        Assert.Equal(200_000, r.UploadBytesPerSec, 3);
        Assert.Equal("Wi-Fi", r.AdapterName); // busiest adapter

        // Both adapters now count.
        r = m.Update(
        [
            new("wifi", "Wi-Fi", 2_000_100 + 1_000, 400_100, true),
            new("eth", "Ethernet", 10 + 3_000, 10, true),
            new("vpn", "WireGuard", 9_999_999 + 6_000, 9_999_999, true),
        ], TimeSpan.FromSeconds(3));
        Assert.Equal(10_000, r!.DownloadBytesPerSec, 3);
        Assert.Equal("WireGuard", r.AdapterName);
    }

    [Fact]
    public void NetworkMonitor_PrefersAdaptersWithGateway()
    {
        // A Hyper-V / WSL virtual switch has no gateway and mirrors host traffic: it must not double the rate.
        var m = new NetworkMonitor(sampleNow: false);
        m.Update([new("eth", "Ethernet", 0, 0, true), new("wsl", "vEthernet (WSL)", 0, 0, false)], TimeSpan.Zero);
        var r = m.Update([new("eth", "Ethernet", 4_000, 0, true), new("wsl", "vEthernet (WSL)", 4_000, 0, false)], TimeSpan.FromSeconds(1));
        Assert.Equal(4_000, r!.DownloadBytesPerSec, 3);

        // With no gateway anywhere (offline LAN), all adapters count.
        var lan = new NetworkMonitor(sampleNow: false);
        lan.Update([new("a", "A", 0, 0, false), new("b", "B", 0, 0, false)], TimeSpan.Zero);
        r = lan.Update([new("a", "A", 1_000, 0, false), new("b", "B", 3_000, 0, false)], TimeSpan.FromSeconds(1));
        Assert.Equal(4_000, r!.DownloadBytesPerSec, 3);
        Assert.Equal("B", r.AdapterName);
    }

    [Fact]
    public void NetworkMonitor_IgnoresFilterDriverInterfaces()
    {
        // Windows lists lightweight-filter / QoS modules as extra interfaces that mirror the adapter's counters
        // (seen on a real machine). Without a gateway anywhere they must still not multiply the rate.
        AdapterCounters[] At(long rx) =>
        [
            new("wifi", "Wi-Fi", rx, 0, false),
            new("f1", "Wi-Fi-WFP Native MAC Layer LightWeight Filter-0000", rx, 0, false),
            new("f2", "Wi-Fi-QoS Packet Scheduler-0000", rx, 0, false),
            new("vm", "VMware Network Adapter VMnet8", 0, 0, false),
        ];
        var m = new NetworkMonitor(sampleNow: false);
        m.Update(At(0), TimeSpan.Zero);
        var r = m.Update(At(4_000), TimeSpan.FromSeconds(1));
        Assert.Equal(4_000, r!.DownloadBytesPerSec, 3);
        Assert.Equal("Wi-Fi", r.AdapterName);
    }

    [Fact]
    public void NetworkMonitor_NoAdapters_ReturnsNull_AndIdleKeepsName()
    {
        var m = new NetworkMonitor(sampleNow: false);
        Assert.Null(m.Update([], TimeSpan.Zero));
        Assert.Null(m.Update([], TimeSpan.FromSeconds(5)));

        m.Update([new("eth", "Ethernet", 0, 0, true), new("wifi", "Wi-Fi", 0, 0, true)], TimeSpan.FromSeconds(6));
        Assert.Equal("Wi-Fi", m.Update([new("eth", "Ethernet", 0, 0, true), new("wifi", "Wi-Fi", 500, 0, true)], TimeSpan.FromSeconds(7))!.AdapterName);
        // Idle interval: the name must not flip back to the first adapter.
        Assert.Equal("Wi-Fi", m.Update([new("eth", "Ethernet", 0, 0, true), new("wifi", "Wi-Fi", 500, 0, true)], TimeSpan.FromSeconds(8))!.AdapterName);
    }

    [Fact]
    public void NetworkMonitor_TooShortInterval_ReturnsPreviousRate()
    {
        var m = new NetworkMonitor(sampleNow: false);
        m.Update([new("eth", "Ethernet", 0, 0, true)], TimeSpan.Zero);
        var r1 = m.Update([new("eth", "Ethernet", 1_000, 0, true)], TimeSpan.FromSeconds(1));
        var r2 = m.Update([new("eth", "Ethernet", 900_000, 0, true)], TimeSpan.FromSeconds(1.01));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void NetworkMonitor_RealPoll_DoesNotThrow()
    {
        var m = new NetworkMonitor();
        Thread.Sleep(300);
        var r = m.Poll();
        if (r != null)
        {
            Assert.True(r.DownloadBytesPerSec >= 0);
            Assert.True(r.UploadBytesPerSec >= 0);
        }
    }

    // ================================================================ MediaSessionReader

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("C:\\Program Files\\VideoLAN\\VLC\\vlc.exe", "VLC")]
    [InlineData("foobar2000.exe", "foobar2000")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Media_FriendlyAppName(string? aumid, string? expected) =>
        Assert.Equal(expected, MediaSessionReader.FriendlyAppName(aumid));

    [Fact]
    public void Media_ArtworkIsDownscaledPng()
    {
        var big = FakeArtwork(640);
        var png = MediaSessionReader.EncodeArtwork(big);
        Assert.NotNull(png);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png![..4]);
        using var bmp = SKBitmap.Decode(png);
        Assert.Equal(256, bmp.Width);
        Assert.Equal(256, bmp.Height);

        var small = MediaSessionReader.EncodeArtwork(FakeArtwork(100));
        using var bmp2 = SKBitmap.Decode(small);
        Assert.Equal(100, bmp2.Width);

        Assert.Null(MediaSessionReader.EncodeArtwork([1, 2, 3]));
        Assert.Null(MediaSessionReader.EncodeArtwork([]));
    }

    [Fact]
    public void Media_PositionIsExtrapolatedWhilePlaying()
    {
        var updated = new DateTimeOffset(2026, 9, 24, 21, 0, 0, TimeSpan.Zero);
        var now = updated.AddSeconds(7);
        TimeSpan P(double s) => TimeSpan.FromSeconds(s);

        Assert.Equal(P(67), MediaSessionReader.CurrentPosition(P(60), P(0), P(200), updated, now, playing: true, rate: 1));
        Assert.Equal(P(60), MediaSessionReader.CurrentPosition(P(60), P(0), P(200), updated, now, playing: false, rate: 1));
        Assert.Equal(P(74), MediaSessionReader.CurrentPosition(P(60), P(0), P(200), updated, now, playing: true, rate: 2));
        Assert.Equal(P(200), MediaSessionReader.CurrentPosition(P(198), P(0), P(200), updated, now, playing: true, rate: 1)); // clamped
        Assert.Equal(P(50), MediaSessionReader.CurrentPosition(P(60), P(10), P(210), updated, now.AddSeconds(-7), playing: true, rate: 1)); // start offset
        // A bogus LastUpdatedTime (never set: year 1601) must not extrapolate.
        Assert.Equal(P(60), MediaSessionReader.CurrentPosition(P(60), P(0), P(200), DateTimeOffset.MinValue, now, playing: true, rate: 1));
        // No duration (live stream): position still reported, not clamped to zero.
        Assert.Equal(P(67), MediaSessionReader.CurrentPosition(P(60), P(0), P(0), updated, now, playing: true, rate: 1));
    }

    [Fact]
    public void Media_RealPoll_DoesNotThrow()
    {
        using var reader = new MediaSessionReader();
        var info = reader.Poll(); // null when nothing plays or the API is unavailable
        if (info != null)
        {
            Assert.NotNull(info.Title);
            Assert.True(info.Position >= TimeSpan.Zero);
        }
        _ = reader.Poll(); // second poll reuses the manager / artwork cache
    }
}
