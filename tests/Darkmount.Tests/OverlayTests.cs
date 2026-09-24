using Darkmount.App.Overlays;
using Darkmount.Keyboard;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.Tests;

public class OverlayTests
{
    // Pretend every key id maps to a lamp with the same number.
    static readonly Func<int, int?> Lamp = id => id;

    static Dictionary<int, LampColor> Frame() =>
        KeyIds.All.ToDictionary(k => (int)k.Id, _ => new LampColor(10, 20, 30));

    static int KeyOf(byte usage) => KeyIds.All.First(k => k.HidUsage == usage).Id;

    [Fact]
    public void Caps_lock_glows_white_when_on()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { CapsLock = true }, Lamp);
        Assert.Equal(LightingOverlays.White, f[KeyIds.CapsLock]);
    }

    [Fact]
    public void Nothing_changes_when_no_overlay_is_active()
    {
        var f = Frame();
        var before = new Dictionary<int, LampColor>(f);
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState(), Lamp);
        Assert.Equal(before, f);
    }

    [Fact]
    public void Volume_bar_lights_a_proportional_number_of_F_keys()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { ShowVolume = true, Volume = 0.5 }, Lamp);
        Assert.Equal(LightingOverlays.White, f[KeyOf(0x3A)]); // F1
        Assert.Equal(LightingOverlays.White, f[KeyOf(0x3F)]); // F6
        Assert.NotEqual(LightingOverlays.White, f[KeyOf(0x40)]); // F7
    }

    [Fact]
    public void Muted_speakers_show_a_red_empty_bar()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { ShowVolume = true, Volume = 0.8, SpeakersMuted = true }, Lamp);
        Assert.NotEqual(LightingOverlays.White, f[KeyOf(0x3A)]);
    }

    [Fact]
    public void Holding_ctrl_highlights_copy_and_paste_and_dims_the_rest()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { Modifier = HeldModifier.Ctrl }, Lamp);
        Assert.Equal(LightingOverlays.Cyan, f[KeyOf(0x06)]); // C
        Assert.Equal(LightingOverlays.Cyan, f[KeyOf(0x19)]); // V
        Assert.True(f[KeyOf(0x0B)].B < 30);                  // H dimmed
    }

    [Fact]
    public void Mic_mute_key_turns_red_and_can_be_disabled()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { MicMuted = true }, Lamp);
        Assert.Equal(LightingOverlays.Red, f[102]);

        var g = Frame();
        LightingOverlays.Apply(g, new OverlaySettings { MicMute = false }, new OverlayState { MicMuted = true }, Lamp);
        Assert.NotEqual(LightingOverlays.Red, g[102]);
    }

    [Fact]
    public void Focus_timer_shows_progress_on_the_F_row()
    {
        var f = Frame();
        LightingOverlays.Apply(f, new OverlaySettings(), new OverlayState { Timer = (0.25, true) }, Lamp);
        Assert.Equal(new LampColor(255, 110, 0), f[KeyOf(0x3C)]); // F3 lit (3 of 12)
        Assert.Equal(new LampColor(0, 0, 0), f[KeyOf(0x3D)]);     // F4 off
    }
}
