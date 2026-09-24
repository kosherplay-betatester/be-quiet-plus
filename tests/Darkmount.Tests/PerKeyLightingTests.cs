using System.Text.Json;
using Darkmount.App;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.Tests;

public class PerKeyLightingTests
{
    static readonly IReadOnlyList<LampPoint> Lamps =
    [
        new(10, 0.1, 0.5, true, 30), // A
        new(11, 0.2, 0.5, true, 31), // S
        new(12, 0.3, 0.5, true, 32), // D
        new(0, 0.0, 0.0, false, 0, 1),
        new(1, 0.5, 0.0, false, 0, 2),
    ];

    static Dictionary<int, LampColor> Render(LightingScene scene) =>
        SceneRenderer.Render(scene, new SceneContext { Seconds = 1, Lamps = Lamps });

    [Fact]
    public void Painted_lamps_get_their_colour_and_the_rest_shows_the_layer_below()
    {
        var paint = new LightLayer
        {
            Effect = SceneEffect.PerKey, AllKeys = false, AllEdges = false, // painted lamps choose themselves
            KeyColors = { [30] = "FF0000", [32] = "0000FF" },
            EdgeColors = { [1] = "00FF00" },
        };
        var below = new LightLayer { Effect = SceneEffect.Static, Colors = ["404040"] };
        var frame = Render(new LightingScene { Layers = [paint, below] });

        Assert.Equal(new LampColor(255, 0, 0), frame[10]);
        Assert.Equal(new LampColor(64, 64, 64), frame[11]);
        Assert.Equal(new LampColor(0, 0, 255), frame[12]);
        Assert.Equal(new LampColor(64, 64, 64), frame[0]);
        Assert.Equal(new LampColor(0, 255, 0), frame[1]);
    }

    [Fact]
    public void Paint_layer_brightness_scales_the_painted_colours()
    {
        var paint = new LightLayer { Effect = SceneEffect.PerKey, Brightness = 50, KeyColors = { [31] = "FFFFFF" } };
        var frame = Render(new LightingScene { Layers = [paint] });

        Assert.InRange(frame[11].R, 120, 135);
        Assert.Equal(new LampColor(0, 0, 0), frame[10]);
    }

    [Fact]
    public void Paint_layers_survive_settings_json_and_clone()
    {
        var settings = new AppSettings
        {
            Scene = new LightingScene
            {
                Name = "Painted",
                Layers = [new LightLayer { Effect = SceneEffect.PerKey, KeyColors = { [30] = "FF0000" }, EdgeColors = { [150] = "00FF00" } }],
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"dmh-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Save(path, settings);
            var loaded = SettingsStore.Load(path).Scene!.Layers[0];
            Assert.Equal(SceneEffect.PerKey, loaded.Effect);
            Assert.Equal("FF0000", loaded.KeyColors[30]);
            Assert.Equal("00FF00", loaded.EdgeColors[150]);
        }
        finally { File.Delete(path); }

        var original = settings.Scene!.Layers[0];
        var copy = original.Clone();
        copy.KeyColors[31] = "123456";
        Assert.False(original.KeyColors.ContainsKey(31));
    }

    [Fact]
    public void Old_settings_without_paint_data_load_with_empty_colours()
    {
        var layer = JsonSerializer.Deserialize<LightLayer>("""{"Name":"x","Effect":"Static"}""")!;
        Assert.Empty(layer.KeyColors);
        Assert.Empty(layer.EdgeColors);
    }

    [Fact]
    public void Per_key_effect_is_in_the_catalogue()
    {
        var info = SceneEffects.Get(SceneEffect.PerKey);
        Assert.False(info.IsOpaque);
        Assert.Empty(info.ColorModes);
    }

    [Fact]
    public void Default_edge_light_table_covers_96_distinct_lamps()
    {
        Assert.Equal(DarkmountKeys.EdgeLights, DarkmountKeys.EdgeLightLampIds.Count);
        Assert.Equal(96, DarkmountKeys.EdgeLightLampIds.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 96), DarkmountKeys.DefaultEdgeLights.Keys.Order());
    }

    [Fact]
    public void Layout_numbers_dark_mount_edge_lights_from_their_bindings()
    {
        LampInfo Lamp(int id, int x, int y, int binding) => new(id, x, y, 0, 1000, LampPurposes.Branding, 255, 255, 255, null, true, binding);
        var lamps = new[] { Lamp(0, 0, 0, 106), Lamp(1, 100, 0, 107), Lamp(2, 50, 50, 30) };
        var map = LampMap.Build(lamps, LampBindingKind.DarkmountKeyId);

        var layout = RgbEffects.Layout(lamps, map);

        Assert.Equal(1, layout.Single(p => p.LampId == 0).Edge);
        Assert.Equal(2, layout.Single(p => p.LampId == 1).Edge);
        Assert.Equal(0, layout.Single(p => p.LampId == 2).Edge);
        Assert.True(layout.Single(p => p.LampId == 2).IsKey);
    }
}
