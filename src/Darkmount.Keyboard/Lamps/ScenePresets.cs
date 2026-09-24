namespace Darkmount.Keyboard.Lamps;

/// <summary>Premade scenes for the lighting studio. <see cref="All"/> returns fresh copies, so callers may edit them freely.</summary>
public static class ScenePresets
{
    public static IReadOnlyList<LightingScene> All => Build();

    enum On { All, Keys, Edges }

    const byte W = 17, A = 30, S = 31, D = 32;

    static LightLayer Fx(SceneEffect effect, string[]? colors = null, SceneDirection? direction = null, int speed = 5,
        int brightness = 100, On on = On.All, int[]? keys = null, string? name = null)
    {
        var layer = SceneEffects.CreateLayer(effect);
        if (colors is { Length: > 0 })
        {
            layer.Colors = [.. colors];
            layer.ColorMode = colors.Length switch { 1 => SceneColorMode.Single, 2 => SceneColorMode.Dual, _ => SceneColorMode.Gradient };
        }
        if (direction is { } d) layer.Direction = d;
        if (name is not null) layer.Name = name;
        layer.Speed = speed;
        layer.Brightness = brightness;
        layer.AllKeys = on != On.Edges && keys is null;
        layer.Keys = keys is null ? [] : [.. keys];
        layer.AllEdges = on != On.Keys && keys is null;
        return layer;
    }

    static LightingScene Scene(string name, string description, params LightLayer[] layers) =>
        new() { Name = name, Description = description, Layers = [.. layers] };

    static readonly string[] Rainbow = ["FF0000", "FFFF00", "00FF00", "00FFFF", "0000FF", "FF00FF"];

    static List<LightingScene> Build() =>
    [
        Scene("Rainbow wave", "The full spectrum rolling from left to right across keys and edge lights.",
            Fx(SceneEffect.Rainbow, direction: SceneDirection.Right)),

        Scene("Cyberpunk", "Hot pink fading into electric cyan, with white flashes under your fingers.",
            Fx(SceneEffect.Reactive, ["FFFFFF"], speed: 6, on: On.Keys, name: "Key flash"),
            Fx(SceneEffect.ColorWave, ["FF0080", "B000FF", "00E5FF"], SceneDirection.Right, speed: 4, name: "Neon wave")),

        Scene("Synthwave", "Retro sunset stripes rising up the keys under a pulsing magenta-cyan frame.",
            Fx(SceneEffect.Breathing, ["FF00C8", "00E5FF"], speed: 4, on: On.Edges, name: "Frame"),
            Fx(SceneEffect.ColorWave, ["FF2A6D", "D100D1", "7B2FF7", "05D9E8"], SceneDirection.Up, speed: 3, on: On.Keys, name: "Stripes")),

        Scene("Aurora borealis", "Slow curtains of green, teal and violet shimmering over a night sky.",
            Fx(SceneEffect.Aurora, speed: 4)),

        Scene("Fire", "Flames licking up from the space bar: deep red, orange and yellow.",
            Fx(SceneEffect.Fire, speed: 6)),

        Scene("Ocean", "Rolling blue swells; every key press sends a ripple across the water.",
            Fx(SceneEffect.Ripple, ["E0FFFF", "0080FF"], speed: 5, name: "Splashes"),
            Fx(SceneEffect.Ocean, direction: SceneDirection.Right, speed: 4, name: "Waves")),

        Scene("Matrix", "Green digital rain running down the key columns over a faint green glow.",
            Fx(SceneEffect.Matrix, direction: SceneDirection.Down, speed: 5, name: "Code rain"),
            Fx(SceneEffect.Static, ["001A06"], name: "Glow")),

        Scene("Starry night", "Stars twinkling on a deep navy sky.",
            Fx(SceneEffect.Twinkle, ["FFF4D6", "050520"], speed: 3)),

        Scene("Rainy day", "Blue raindrops streaming down over a dark stormy background.",
            Fx(SceneEffect.Rain, ["C8E6FF", "2060FF"], SceneDirection.Down, speed: 5, name: "Drops"),
            Fx(SceneEffect.Static, ["05081A"], name: "Storm")),

        Scene("Lava lamp", "Glowing orange blobs drifting slowly through molten red.",
            Fx(SceneEffect.Lava, speed: 4)),

        Scene("Candy", "Pastel pink, sky blue, lemon and lilac flowing gently.",
            Fx(SceneEffect.Candy, direction: SceneDirection.Right, speed: 4)),

        Scene("Heartbeat", "A red lub-dub pulse spreading out from the centre.",
            Fx(SceneEffect.Heartbeat, ["FF0020", "100002"], speed: 5)),

        Scene("Police", "Red and blue emergency flashes alternating left and right.",
            Fx(SceneEffect.Police, ["FF0000", "0028FF"], speed: 6)),

        Scene("Knight Rider", "KITT's red scanner sweeping back and forth with a glowing trail.",
            Fx(SceneEffect.Scanner, ["FF0000"], SceneDirection.Right, speed: 5, name: "Scanner"),
            Fx(SceneEffect.Static, ["1A0000"], name: "Dim red")),

        Scene("be quiet! orange", "The be quiet! signature orange on the keys, gently breathing on the edges.",
            Fx(SceneEffect.Breathing, ["FF2800"], speed: 3, on: On.Edges, name: "Breathing edges"),
            Fx(SceneEffect.Static, ["FF2800"], on: On.Keys, name: "Orange keys")),

        Scene("Gamer WASD", "Movement keys and arrows in bright red over a dim base; every key flashes white when pressed.",
            Fx(SceneEffect.Reactive, ["FFFFFF"], speed: 6, on: On.Keys, name: "Key flash"),
            Fx(SceneEffect.Static, ["FF0000"], keys: [W, A, S, D, KeyIds.Up, KeyIds.Left, KeyIds.Down, KeyIds.Right], name: "WASD + arrows"),
            Fx(SceneEffect.Static, ["FF2800"], brightness: 15, name: "Dim base")),

        Scene("Typing heatmap", "Keys warm from cold blue to hot red the more you type on them.",
            Fx(SceneEffect.TypingHeatmap)),

        Scene("Performance meter", "CPU load on F1–F12, GPU load on the number row, CPU temperature on the edges.",
            Fx(SceneEffect.PerformanceMeter, ["FF2800"])),

        Scene("CPU temperature", "The whole keyboard shows the CPU temperature: green when cool, red (pulsing) when hot.",
            Fx(SceneEffect.CpuTemperature)),

        Scene("Audio pulse", "Light swells out from the centre with whatever is playing.",
            Fx(SceneEffect.AudioPulse)),

        Scene("Audio spectrum", "A spectrum analyser: bass on the left, treble on the right, green to red bars.",
            Fx(SceneEffect.AudioSpectrum, name: "Bars"),
            Fx(SceneEffect.Static, ["05050F"], name: "Backdrop")),

        Scene("Tornado rainbow", "A rainbow spiral whirling around the middle of the keyboard.",
            Fx(SceneEffect.Tornado, Rainbow, SceneDirection.Clockwise, speed: 5)),

        Scene("Breathing ice", "Frosty whites and blues slowly breathing in and out.",
            Fx(SceneEffect.Breathing, ["DFF6FF", "7FD4FF", "2F7BFF"], speed: 3)),

        Scene("Toxic", "Radioactive green and yellow plasma; keys flare yellow when pressed.",
            Fx(SceneEffect.Reactive, ["F2FF00"], speed: 6, on: On.Keys, name: "Flare"),
            Fx(SceneEffect.Plasma, ["1AFF00", "B6FF00", "FFF200", "00FF6A"], speed: 4, name: "Plasma")),

        Scene("Sunset", "Orange melting into pink and purple, drifting slowly across the keys.",
            Fx(SceneEffect.ColorWave, ["FF6A00", "FF2E63", "B5179E", "5A189A"], SceneDirection.Left, speed: 2)),

        Scene("Ripple", "Every key press sends rings of light across a dark blue keyboard.",
            Fx(SceneEffect.Ripple, ["FFFFFF", "00A0FF"], speed: 5, name: "Rings"),
            Fx(SceneEffect.Static, ["000A2E"], name: "Dark blue")),

        Scene("Screen sync (Ambilight)", "Keys mirror the screen above them and the edge lights glow with its borders.",
            Fx(SceneEffect.ScreenSync)),

        Scene("Plasma", "Swirling neon interference patterns.",
            Fx(SceneEffect.Plasma, speed: 4)),

        Scene("Galaxy", "A slow violet and cyan spiral galaxy turning counter-clockwise.",
            Fx(SceneEffect.Tornado, ["1A0033", "6A00FF", "FF00C8", "00D4FF", "1A0033"], SceneDirection.CounterClockwise, speed: 2)),

        Scene("Neon pulse", "Magenta and cyan rings flowing out from the centre.",
            Fx(SceneEffect.ColorWave, ["FF00FF", "00FFFF"], SceneDirection.Outward, speed: 6)),

        Scene("Christmas", "Red, green and gold lights twinkling like a Christmas tree.",
            Fx(SceneEffect.Twinkle, ["FF1010", "10FF10", "FFD700", "FFFFFF"], speed: 4)),

        Scene("Color cycle", "The whole keyboard glides slowly through the rainbow.",
            Fx(SceneEffect.ColorCycle, Rainbow, speed: 3)),
    ];
}
