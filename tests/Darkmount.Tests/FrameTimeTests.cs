using Darkmount.Sensors;

namespace Darkmount.Tests;

public class FrameTimeTests
{
    [Fact]
    public void One_percent_low_is_the_fps_at_the_99th_percentile_frame_time()
    {
        var s = new FrameTimeStats();
        // 990 frames at 10 ms (100 fps) and 10 stutters at 25 ms (40 fps).
        for (int i = 0; i < 990; i++) s.Add(10_000, 1000);
        for (int i = 0; i < 10; i++) s.Add(25_000, 1000);

        var (low1, _, frames) = s.Lows(1000);

        Assert.Equal(1000, frames);
        Assert.Equal(100, low1!.Value, 1); // the 99th percentile is still a 10 ms frame
    }

    [Fact]
    public void Stutters_above_one_percent_pull_the_low_down()
    {
        var s = new FrameTimeStats();
        for (int i = 0; i < 950; i++) s.Add(10_000, 0);
        for (int i = 0; i < 50; i++) s.Add(25_000, 0);

        Assert.Equal(40, s.Lows(0).Low1!.Value, 1);
    }

    [Fact]
    public void Too_few_frames_give_no_value_and_old_frames_expire()
    {
        var s = new FrameTimeStats();
        for (int i = 0; i < 50; i++) s.Add(10_000, 0);
        Assert.Null(s.Lows(0).Low1);

        for (int i = 0; i < 500; i++) s.Add(10_000, 0);
        Assert.NotNull(s.Lows(5_000).Low1);
        Assert.Null(s.Lows(FrameTimeStats.WindowMs + 1).Low1); // all older than the window
    }

    [Fact]
    public void Pauses_longer_than_five_seconds_are_ignored()
    {
        var s = new FrameTimeStats();
        for (int i = 0; i < 200; i++) s.Add(10_000, 0);
        s.Add(8_000_000, 0);
        Assert.Equal(100, s.Lows(0).Low1!.Value, 1);
    }
}
