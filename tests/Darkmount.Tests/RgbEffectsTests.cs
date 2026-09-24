using Darkmount.Keyboard.Lamps;

namespace Darkmount.Tests;

public class RgbEffectsTests
{
    static readonly IReadOnlyList<LampPoint> Lamps =
    [
        new(0, 0.0, 0.5, true), new(1, 0.5, 0.5, true), new(2, 1.0, 0.5, true), new(3, 0.5, 0.0, false), new(4, 0.5, 1.0, false),
    ];

    [Fact]
    public void Whole_keyboard_effects_are_a_single_colour()
    {
        foreach (var kind in new[] { RgbEffectKind.Static, RgbEffectKind.Breathing, RgbEffectKind.ColorCycle, RgbEffectKind.CpuTemperature })
        {
            var f = RgbEffects.Render(new RgbEffectSettings { Effect = kind }, 1.3, Lamps, 60);
            Assert.NotNull(f.All);
            Assert.Null(f.Keys);
        }
    }

    [Fact]
    public void Per_key_effects_colour_every_key_and_share_one_edge_colour()
    {
        var f = RgbEffects.Render(new RgbEffectSettings { Effect = RgbEffectKind.RainbowWave }, 0.7, Lamps);

        Assert.Null(f.All);
        Assert.Equal([0, 1, 2], f.Keys!.Keys.Order());
        Assert.NotNull(f.Edges);
        Assert.NotEqual(f.Keys[0], f.Keys[2]); // the rainbow varies across the keyboard
    }

    [Fact]
    public void Rainbow_moves_over_time()
    {
        var s = new RgbEffectSettings { Effect = RgbEffectKind.RainbowWave };
        Assert.NotEqual(RgbEffects.Render(s, 0, Lamps).Keys![1], RgbEffects.Render(s, 0.5, Lamps).Keys![1]);
    }

    [Fact]
    public void Brightness_scales_colours()
    {
        var full = RgbEffects.Render(new RgbEffectSettings { Effect = RgbEffectKind.Static, Color = "FF8000", Brightness = 100 }, 0, Lamps).All!.Value;
        var half = RgbEffects.Render(new RgbEffectSettings { Effect = RgbEffectKind.Static, Color = "FF8000", Brightness = 50 }, 0, Lamps).All!.Value;

        Assert.Equal((255, 128, 0), (full.R, full.G, full.B));
        Assert.Equal((128, 64, 0), (half.R, half.G, half.B));
    }

    [Theory]
    [InlineData(40, 0, 255, 0)]    // cool: green
    [InlineData(90, 255, 0, 0)]    // hot: red
    public void Cpu_temperature_goes_from_green_to_red(double celsius, int r, int g, int b)
    {
        var c = RgbEffects.TemperatureColor(celsius);
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Fact]
    public void Unknown_temperature_is_blue_not_alarming()
    {
        var c = RgbEffects.TemperatureColor(null);
        Assert.True(c.B > c.R && c.B > c.G);
    }
}
