using System.Buffers.Binary;
using Darkmount.Keyboard;
using Darkmount.QLink;

namespace Darkmount.Tests;

public class KeyboardProtocolCommandTests
{
    public static TheoryData<byte, byte> NewlyAllowed => new()
    {
        { 7, 1 }, { 7, 2 }, { 7, 3 }, { 7, 4 }, { 7, 5 }, { 7, 6 },
        { 16, 1 }, { 16, 2 }, { 16, 3 }, { 16, 5 }, { 16, 6 }, { 16, 14 },
        { 17, 1 }, { 17, 2 }, { 17, 3 }, { 17, 4 }, { 17, 5 },
    };

    [Theory]
    [MemberData(nameof(NewlyAllowed))]
    public void Keyboard_lighting_and_binding_commands_are_allowed(byte feature, byte command) =>
        Assert.True(CommandAllowlist.IsAllowed(feature, command));

    [Theory]
    [InlineData(16, 16)] // LIGHTINGS SetCalibration
    [InlineData(16, 13)] // PerformRealtimeUpdates
    [InlineData(16, 8)]  // SetLayerMask
    [InlineData(18, 2)]  // MACROS SetMacros
    [InlineData(7, 7)]   // SetSnapTapConfig
    [InlineData(6, 2)]   // SetPollingRate
    [InlineData(3, 5)]   // DEVICE_INFO FactoryReset
    [InlineData(16, 4)]  // SetLayersLayout
    [InlineData(16, 10)] // SetLayerName
    [InlineData(17, 6)]  // GetBinding (raw pass-through, unused)
    public void Dangerous_keyboard_commands_stay_blocked(byte feature, byte command)
    {
        Assert.False(CommandAllowlist.IsAllowed(feature, command));
        var t = new FakeTransport();
        using var q = new QLinkClient(t);
        Assert.Throws<InvalidOperationException>(() => q.Send(feature, command, [0]));
        Assert.Empty(t.Written);
    }

    [Fact]
    public void Command_constants_match_the_protocol()
    {
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, new[] { KeyboardCommands.GetLayout, KeyboardCommands.GetConfig,
            KeyboardCommands.SetConfig, KeyboardCommands.GetState, KeyboardCommands.SetState, KeyboardCommands.GetSnapTapConfig });
        Assert.Equal(new byte[] { 1, 2, 3, 5, 6, 14 }, new[] { LightingCommands.GetLightingMode, LightingCommands.SetLightingMode,
            LightingCommands.GetLayersLayout, LightingCommands.GetLayerConfig, LightingCommands.SetLayerConfig,
            LightingCommands.GetGlobalLayers });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, new[] { BindingCommands.GetBindings, BindingCommands.SetBinding,
            BindingCommands.ClearBinding, BindingCommands.GetConfig, BindingCommands.SetConfig });
    }
}

/// <summary>Every request is compared byte for byte (incl. CRC) with docs/QLINK_KEYBOARD.md Appendix A.</summary>
public class KeyboardProtocolGoldenTests
{
    static readonly byte[] StaticLayer = [0, 0, 100, 50, 0, 0xFF, 0x28, 0x00];

    static readonly Dictionary<string, (string Packet, Action<QLinkClient> Call, byte[] Reply)> Cases = new()
    {
        ["KeyboardGetConfig"] = ("06 00 02 00 0a 07 02 | 00 x55 | crc 57 03", q => new KeyboardSettings(q).GetLocks(), [0x0C]),
        ["KeyboardSetConfig"] = ("07 00 02 00 0b 07 03 0c | 00 x54 | crc 51 9e",
            q => new KeyboardSettings(q).SetLocks(GameModeLocks.Win | GameModeLocks.AltTab), []),
        ["KeyboardGetState"] = ("06 00 02 00 0c 07 04 | 00 x55 | crc 44 23", q => new KeyboardSettings(q).GetState(), [0]),
        ["KeyboardSetState"] = ("07 00 02 00 0d 07 05 01 | 00 x54 | crc 17 b6", q => new KeyboardSettings(q).SetGameMode(true), [0]),
        ["KeyboardGetLayout"] = ("06 00 02 00 0e 07 01 | 00 x55 | crc 56 c3", q => new KeyboardSettings(q).GetLayout(), [0, 0]),
        ["LightingGetMode"] = ("06 00 02 00 0f 10 01 | 00 x55 | crc 14 29", q => new Lighting(q).GetMode(), [1]),
        ["LightingSetModeGeneral"] = ("07 00 02 00 10 10 02 01 | 00 x54 | crc 91 23", q => new Lighting(q).SetMode(LightingMode.General), []),
        ["LightingGetLayerConfig"] = ("07 00 02 00 11 10 05 00 | 00 x54 | crc da 77", q => new Lighting(q).GetLayerConfig(0), StaticLayer),
        ["LightingSetStatic"] = ("0f 00 02 00 12 10 06 00 00 00 64 32 00 ff 28 00 | 00 x46 | crc 27 c0",
            q => new Lighting(q).SetLayerConfig(0, new LayerConfig(Effect.Static, Direction.Up, 100, 50, ColorMode.Single,
                [new GradientStop(Rgb.Parse("FF2800"), 0)])), []),
        ["LightingSetColorWaveGradient"] = ("19 00 02 00 13 10 06 00 01 03 50 32 02 03 ff 00 00 00 00 ff 00 32 00 00 ff 64 | 00 x36 | crc 97 ff",
            q => new Lighting(q).SetLayerConfig(0, new LayerConfig(Effect.ColorWave, Direction.Right, 80, 50, ColorMode.Gradient,
                [new(Rgb.Parse("FF0000"), 0), new(Rgb.Parse("00FF00"), 50), new(Rgb.Parse("0000FF"), 100)])), []),
        ["LightingSetReactiveDual"] = ("12 00 02 00 14 10 06 00 04 00 64 32 01 ff 28 00 ff ff ff | 00 x43 | crc 6c d1",
            q => new Lighting(q).SetLayerConfig(0, new LayerConfig(Effect.Reactive, Direction.Up, 100, 50, ColorMode.Dual,
                [new(Rgb.Parse("FF2800"), 0), new(Rgb.Parse("FFFFFF"), 100)])), []),
        ["BindingsGetConfig"] = ("06 00 02 00 15 11 04 | 00 x55 | crc 12 4a", q => new Bindings(q).GetEnabled(), [1]),
        ["BindingsSetConfigEnabled"] = ("07 00 02 00 16 11 05 01 | 00 x54 | crc 47 ff", q => new Bindings(q).SetEnabled(true), []),
        ["BindingsGetBindings0"] = ("08 00 02 00 17 11 01 00 00 | 00 x53 | crc 04 24", q => new Bindings(q).GetAll(), [0, 0]),
        ["SetCapsToEsc"] = ("0b 00 02 00 18 11 02 1d 00 01 00 29 | 00 x50 | crc f1 d8",
            q => Set(q, 29, Layer.Common, new BindingAction.StandardKey(KeyModifiers.None, 0x29)), []),
        ["SetB1ToCtrlShiftEsc"] = ("0b 00 02 00 19 11 02 6d 00 01 03 29 | 00 x50 | crc 32 47",
            q => Set(q, 109, Layer.Common, new BindingAction.StandardKey(KeyModifiers.LeftCtrl | KeyModifiers.LeftShift, 0x29)), []),
        ["SetFnF1ToMute"] = ("0a 00 02 00 1a 11 02 58 80 02 03 | 00 x51 | crc 21 88",
            q => Set(q, 88, Layer.Fn, new BindingAction.Media(MediaAction.Mute)), []),
        ["SetLWinDisabled"] = ("09 00 02 00 1b 11 02 67 00 00 | 00 x52 | crc b4 98",
            q => Set(q, 103, Layer.Common, new BindingAction.Disabled()), []),
        ["SetB8ToCalculator"] = ("0a 00 02 00 1c 11 02 74 00 07 02 | 00 x51 | crc 3e 96",
            q => Set(q, 116, Layer.Common, new BindingAction.WindowsShortcut(WindowsShortcutAction.Calculator)), []),
        ["SetB2ToMouseAutoFire"] = ("0c 00 02 00 1d 11 02 6e 00 03 01 00 14 | 00 x49 | crc 02 45",
            q => Set(q, 110, Layer.Common, new BindingAction.MouseButton(MouseButtonKind.Left, AutoFire: 20)), []),
        ["SetFnUpToBrightnessUp"] = ("0a 00 02 00 1e 11 02 41 80 09 04 | 00 x51 | crc bf da",
            q => Set(q, 65, Layer.Fn, new BindingAction.Backlight(BacklightAction.IncreaseBrightness)), []),
        ["SetB3ToEuro"] = ("0c 00 02 00 1f 11 02 6f 00 0b 02 ac 20 | 00 x49 | crc e4 00",
            q => Set(q, 111, Layer.Common, new BindingAction.AltCode('€')), []),
        ["SetB4ToBrowser"] = ("15 00 02 00 20 11 02 70 00 06 0b 68 74 74 70 73 3a 2f 2f 61 2e 62 | 00 x40 | crc 92 82",
            q => Set(q, 112, Layer.Common, new BindingAction.OpenBrowser("https://a.b")), []),
        ["ClearCaps"] = ("08 00 02 00 21 11 03 1d 00 | 00 x53 | crc 52 49", q => new Bindings(q).Clear(29, Layer.Common), []),
        ["ClearFnF1"] = ("08 00 02 00 22 11 03 58 80 | 00 x53 | crc 33 d4", q => new Bindings(q).Clear(88, Layer.Fn), []),
    };

    static void Set(QLinkClient q, byte key, Layer layer, BindingAction action) =>
        new Bindings(q).SetBinding(new KeyBinding(key, layer, action));

    public static TheoryData<string> Names
    {
        get
        {
            var d = new TheoryData<string>();
            foreach (var k in Cases.Keys) d.Add(k);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Request_matches_appendix_A(string name)
    {
        var (packet, call, reply) = Cases[name];
        var golden = KeyboardGolden.Packet(packet);
        var t = new FakeTransport { Responder = req => FakeTransport.Reply(req, reply) };
        using var q = new QLinkClient(t);

        call(q);

        var request = t.Requests.Last();
        Assert.Equal(Convert.ToHexString(golden), Convert.ToHexString(KeyboardGolden.Reframe(golden, request)));
    }
}

public class KeyboardProtocolSettingsTests
{
    [Fact]
    public void Locks_are_read_and_written_as_one_full_byte()
    {
        var dev = new KeyboardFakeDevice { LockMask = 0x15 };
        var s = new KeyboardSettings(new QLinkClient(dev.Transport));

        Assert.Equal(GameModeLocks.ShiftTab | GameModeLocks.Win | GameModeLocks.CapsLock, s.GetLocks());
        s.SetLocks(GameModeLocks.All);
        Assert.Equal(0x1F, dev.LockMask);
        s.SetLocks(GameModeLocks.None);
        Assert.Equal(0, dev.LockMask);
    }

    [Theory]
    [InlineData(0x00, true, 0x01)]
    [InlineData(0x02, true, 0x03)]  // snap-tap bit preserved
    [InlineData(0x03, false, 0x02)]
    [InlineData(0x01, false, 0x00)]
    public void SetGameMode_writes_bit0_and_keeps_bit1_from_a_fresh_read(byte before, bool on, byte expected)
    {
        var dev = new KeyboardFakeDevice { State = before };
        var s = new KeyboardSettings(new QLinkClient(dev.Transport));

        s.SetGameMode(on);

        Assert.Equal(expected, dev.State);
        Assert.Equal([(byte)7, (byte)7], dev.Transport.Requests.Select(r => r.Feature));
        Assert.Equal([(byte)4, (byte)5], dev.Transport.Requests.Select(r => r.Command));
        Assert.Equal(on, s.GetState().HasFlag(KeyboardStateFlags.GameMode));
    }

    [Theory]
    [InlineData(0, 0, PhysicalLayout.Ansi, VisualLayout.US)]
    [InlineData(1, 1, PhysicalLayout.Iso, VisualLayout.DE)]
    [InlineData(1, 3, PhysicalLayout.Iso, VisualLayout.FR)]
    [InlineData(0, 2, PhysicalLayout.Ansi, VisualLayout.US)] // ANSI is always US (web rule)
    [InlineData(1, 0, PhysicalLayout.Iso, VisualLayout.UK)]  // ISO + US becomes UK (web rule)
    public void GetLayout_decodes_physical_and_visual(byte physical, byte visual, PhysicalLayout p, VisualLayout v)
    {
        var dev = new KeyboardFakeDevice { Physical = physical, Visual = visual };
        Assert.Equal((p, v), new KeyboardSettings(new QLinkClient(dev.Transport)).GetLayout());
    }
}

public class KeyboardProtocolLightingTests
{
    static readonly byte[] StaticGolden = Convert.FromHexString("0000006432" + "00" + "FF2800");
    static readonly byte[] WaveGolden = Convert.FromHexString("0001035032" + "02" + "03FF00000000FF00320000FF64");
    static readonly byte[] ReactiveGolden = Convert.FromHexString("0004006432" + "01" + "FF2800FFFFFF");

    [Fact]
    public void Decode_reads_the_appendix_payloads_back()
    {
        var s = LayerConfig.Decode(StaticGolden.AsSpan(1));
        Assert.Equal(new LayerConfig(Effect.Static, Direction.Up, 100, 50, ColorMode.Single, [new(new Rgb(255, 40, 0), 0)]), s);

        var w = LayerConfig.Decode(WaveGolden.AsSpan(1));
        Assert.Equal((Effect.ColorWave, Direction.Right, 80, 50, ColorMode.Gradient), (w.Effect, w.Direction, w.Brightness, w.Speed, w.ColorMode));
        Assert.Equal(new GradientStop[] { new(new Rgb(255, 0, 0), 0), new(new Rgb(0, 255, 0), 50), new(new Rgb(0, 0, 255), 100) }, w.Colors);

        var r = LayerConfig.Decode(ReactiveGolden.AsSpan(1));
        Assert.Equal(ColorMode.Dual, r.ColorMode);
        Assert.Equal([new Rgb(255, 40, 0), new Rgb(255, 255, 255)], r.Colors.Select(c => c.Color));
    }

    [Fact]
    public void Encode_then_decode_round_trips()
    {
        foreach (var golden in new[] { StaticGolden, WaveGolden, ReactiveGolden })
            Assert.Equal(golden, LayerConfig.Decode(golden.AsSpan(1)).Encode(0));
    }

    [Fact]
    public void Oriented_gradient_layout_has_type_angle_and_stops()
    {
        var cfg = new LayerConfig(Effect.Static, Direction.Up, 100, 50, ColorMode.OrientedGradient,
            [new(new Rgb(1, 2, 3), 0), new(new Rgb(4, 5, 6), 100)]) { GradientType = GradientType.Conic, Angle = 300 };

        var bytes = cfg.Encode(0);

        Assert.Equal(Convert.FromHexString("000000643203" + "02" + "2C01" + "02" + "01020300" + "04050664"), bytes);
        Assert.Equal(cfg, LayerConfig.Decode(bytes.AsSpan(1)));
    }

    [Fact]
    public void Ripple_appends_width_range_and_fade_out()
    {
        var cfg = new LayerConfig(Effect.Ripple, Direction.Omnidirectional, 100, 50, ColorMode.Single, [new(new Rgb(9, 9, 9), 0)])
        { RippleWidth = 3, RippleRange = 9, RippleFadeOut = 1 };

        var bytes = cfg.Encode(0);

        Assert.Equal(Convert.FromHexString("0007066432" + "00" + "090909" + "030901"), bytes);
        Assert.Equal(cfg, LayerConfig.Decode(bytes.AsSpan(1)));
        var defaults = cfg with { RippleWidth = 5, RippleRange = 7, RippleFadeOut = 0 };
        Assert.Equal(new LayerConfig(Effect.Ripple, Direction.Omnidirectional, 100, 50, ColorMode.Single, [new(new Rgb(9, 9, 9), 0)]), defaults);
    }

    [Fact]
    public void Effect_values_above_100_decode_as_Off()
    {
        Assert.Equal(Effect.Off, LayerConfig.Decode(Convert.FromHexString("C8006432000A0B0C")).Effect);
    }

    [Fact]
    public void Truncated_or_empty_replies_do_not_crash()
    {
        Assert.Throws<FormatException>(() => LayerConfig.Decode([1, 2]));
        var partial = LayerConfig.Decode(Convert.FromHexString("0100643202" + "05" + "FF000000" + "00FF"));
        Assert.Single(partial.Colors);
    }

    [Fact]
    public void Brightness_speed_and_positions_are_clamped()
    {
        var cfg = new LayerConfig(Effect.Breathing, Direction.Up, 150, -5, ColorMode.Gradient,
            [new(new Rgb(1, 1, 1), -10), new(new Rgb(2, 2, 2), 130)]);

        var b = cfg.Encode(0);

        Assert.Equal(100, b[3]);
        Assert.Equal(0, b[4]);
        Assert.Equal(0, b[10]);   // [6] = N, stop 1 = [7..10], stop 2 = [11..14]
        Assert.Equal(100, b[14]);
    }

    [Theory]
    [InlineData(ColorMode.Single, 2)]
    [InlineData(ColorMode.Single, 0)]
    [InlineData(ColorMode.Dual, 1)]
    [InlineData(ColorMode.Dual, 3)]
    [InlineData(ColorMode.Gradient, 1)]
    [InlineData(ColorMode.Gradient, 8)]
    [InlineData(ColorMode.OrientedGradient, 1)]
    public void Wrong_colour_counts_are_rejected(ColorMode mode, int count)
    {
        var stops = Enumerable.Range(0, count).Select(i => new GradientStop(new Rgb(1, 2, 3), i * 10)).ToList();
        var cfg = new LayerConfig(Effect.Static, Direction.Up, 100, 50, mode, stops);
        Assert.Throws<ArgumentException>(() => cfg.Encode(0));
    }

    [Fact]
    public void Seven_gradient_stops_are_accepted()
    {
        var cfg = new LayerConfig(Effect.Tornado, Direction.Clockwise, 100, 50, ColorMode.Gradient, LightingEffects.Rainbow);
        Assert.Equal(6 + 1 + 28, cfg.Encode(0).Length);
    }

    [Fact]
    public void Dark_Mount_effect_table_matches_the_web_app()
    {
        var effects = LightingEffects.DarkMount;
        Assert.Equal([Effect.Static, Effect.ColorWave, Effect.Tornado, Effect.Breathing, Effect.Reactive, Effect.Matrix],
            effects.Select(e => e.Effect));

        var st = LightingEffects.Find(Effect.Static)!;
        Assert.Equal([ColorMode.Single], st.ColorModes);
        Assert.Empty(st.Directions);
        Assert.False(st.HasSpeed);

        var wave = LightingEffects.Find(Effect.ColorWave)!;
        Assert.Equal([Direction.Left, Direction.Up, Direction.Down, Direction.Right], wave.Directions);
        Assert.Equal((ColorMode.Single, Direction.Right), (wave.Default.ColorMode, wave.Default.Direction));
        Assert.True(wave.HasSpeed);

        Assert.Equal([Direction.Clockwise, Direction.CounterClockwise], LightingEffects.Find(Effect.Tornado)!.Directions);
        var matrix = LightingEffects.Find(Effect.Matrix)!;
        Assert.Equal([ColorMode.Dual, ColorMode.Gradient], matrix.ColorModes);
        Assert.Equal((ColorMode.Gradient, Direction.Down), (matrix.Default.ColorMode, matrix.Default.Direction));
        Assert.Equal(new Rgb(0x0D, 0x02, 0x08), matrix.Default.Colors[0].Color);
        Assert.Equal([new Rgb(0, 255, 0), new Rgb(0, 0, 0)], matrix.DefaultColors[ColorMode.Dual].Select(c => c.Color));
        Assert.Equal(ColorMode.Dual, LightingEffects.Find(Effect.Reactive)!.Default.ColorMode);
        Assert.Null(LightingEffects.Find(Effect.Ripple));

        Assert.Equal([0, 17, 33, 50, 67, 83, 100], LightingEffects.Rainbow.Select(s => s.Position));
        foreach (var e in effects)
        {
            Assert.Contains(e.Default.ColorMode, e.ColorModes);
            Assert.Equal(e.Effect, e.Default.Effect);
            foreach (var mode in e.ColorModes) Assert.True(e.DefaultColors.ContainsKey(mode));
            e.Default.Encode(0); // valid
        }

        var f = LightingEffects.FactoryDefault;
        Assert.Equal((Effect.Static, Direction.Right, 50, 50), (f.Effect, f.Direction, f.Brightness, f.Speed));
    }

    [Fact]
    public void Lighting_reads_and_writes_mode_and_layer_zero()
    {
        var dev = new KeyboardFakeDevice { LightingMode = 0 };
        var l = new Lighting(new QLinkClient(dev.Transport));

        Assert.Equal(LightingMode.Off, l.GetMode());
        l.SetMode(LightingMode.General);
        Assert.Equal(1, dev.LightingMode);

        var cfg = LightingEffects.Find(Effect.Tornado)!.Default;
        l.SetLayerConfig(0, cfg);
        Assert.Equal(cfg, l.GetLayerConfig());
        Assert.Equal([(byte)0], dev.Transport.Requests.Last().Data);
    }

    [Fact]
    public void Undefined_lighting_modes_are_refused()
    {
        var dev = new KeyboardFakeDevice();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Lighting(new QLinkClient(dev.Transport)).SetMode((LightingMode)7));
        Assert.Empty(dev.Transport.Written);
    }

    [Fact]
    public void Rgb_parses_and_formats_hex()
    {
        Assert.Equal(new Rgb(0xFF, 0x28, 0x00), Rgb.Parse("#ff2800"));
        Assert.Equal("FF2800", new Rgb(0xFF, 0x28, 0x00).ToString());
    }
}

public class KeyboardProtocolBindingTests
{
    public static readonly BindingAction[] AllActions =
    [
        new BindingAction.Disabled(),
        new BindingAction.StandardKey(KeyModifiers.LeftCtrl | KeyModifiers.RightAlt, 0x04),
        new BindingAction.StandardKey(KeyModifiers.None, 0x9A), // any usage 0x04–0xE7, not only the web's list
        new BindingAction.FKey(HidUsage.FKey(13)),
        new BindingAction.FKey(HidUsage.FKey(24)),
        new BindingAction.Media(MediaAction.PlayPause),
        new BindingAction.Media(MediaAction.SpecificSound, "{0.0.0.00000000}.{abc}"),
        new BindingAction.MouseButton(MouseButtonKind.Right, DoubleClick: true),
        new BindingAction.MouseButton(MouseButtonKind.Forward, WhilePressed: true),
        new BindingAction.MouseButton(MouseButtonKind.Middle, AutoFire: 50),
        new BindingAction.MouseScroll(MouseScrollDirection.Left),
        new BindingAction.OpenFolder(@"C:\Games"),
        new BindingAction.OpenFile(@"C:\Windows\notepad.exe"),
        new BindingAction.OpenBrowser("https://example.com/\u00FC"),
        new BindingAction.WindowsShortcut(WindowsShortcutAction.TaskManager),
        new BindingAction.Profile([3]),
        new BindingAction.Profile([1, .. "0123456789abcdef"u8.ToArray()]),
        new BindingAction.Backlight(BacklightAction.NextEffect),
        new BindingAction.Backlight(BacklightAction.SelectEffect, Effect.Matrix),
        new BindingAction.Macro([1, 7]),
        new BindingAction.AltCode(0x20AC),
        new BindingAction.Raw(11, [3, 1, 2]),
    ];

    public static TheoryData<int> ActionIndexes
    {
        get
        {
            var d = new TheoryData<int>();
            for (int i = 0; i < AllActions.Length; i++) d.Add(i);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(ActionIndexes))]
    public void Every_action_type_round_trips(int index)
    {
        var b = new KeyBinding(30, Layer.Fn, AllActions[index]);

        var bytes = b.Encode();

        Assert.Equal(bytes.Length, BindingCodec.RecordLength(bytes));
        Assert.Equal(b, KeyBinding.Decode(bytes));
        Assert.Equal(bytes, KeyBinding.Decode(bytes).Encode());
    }

    [Fact]
    public void Record_layouts_match_the_protocol()
    {
        static string Hex(BindingAction a, byte key = 1, Layer layer = Layer.Common) =>
            Convert.ToHexString(new KeyBinding(key, layer, a).Encode());

        Assert.Equal("010001003A", Hex(new BindingAction.FKey(0x3A)));
        Assert.Equal("01800301C400", Hex(new BindingAction.MouseButton(MouseButtonKind.Backward, WhilePressed: true, DoubleClick: true), 1, Layer.Fn));
        Assert.Equal("0100030101" + "00", Hex(new BindingAction.MouseButton(MouseButtonKind.Right)));
        Assert.Equal("0100030201", Hex(new BindingAction.MouseScroll(MouseScrollDirection.Down)));
        Assert.Equal("0100090105", Hex(new BindingAction.Backlight(BacklightAction.SelectEffect, Effect.Matrix)));
        Assert.Equal("01000200", Hex(new BindingAction.Media(MediaAction.None)));
        Assert.Equal("010002" + "0A03616263", Hex(new BindingAction.Media(MediaAction.SpecificSound, "abc")));
        Assert.Equal("010002" + "0A", Hex(new BindingAction.Media(MediaAction.SpecificSound)));
        Assert.Equal("01000B" + "02E900", Hex(new BindingAction.AltCode('\u00E9')));
    }

    [Fact]
    public void F_keys_decode_as_FKey_and_other_standard_keys_keep_modifiers()
    {
        Assert.Equal(new BindingAction.FKey(0x68), KeyBinding.Decode([5, 0, 1, 0, 0x68]).Action);
        Assert.Equal(new BindingAction.StandardKey(KeyModifiers.LeftShift, 0x68), KeyBinding.Decode([5, 0, 1, 2, 0x68]).Action);
        Assert.Equal(new BindingAction.StandardKey(KeyModifiers.None, 0x29), KeyBinding.Decode([5, 0, 1, 0, 0x29]).Action);
        Assert.Equal(13, ((BindingAction.FKey)KeyBinding.Decode([5, 0, 1, 0, 0x68]).Action).Number);
    }

    [Theory]
    [InlineData(0x03)]
    [InlineData(0xE8)]
    public void Invalid_usages_are_rejected(byte usage)
    {
        Assert.Throws<ArgumentException>(() => new KeyBinding(30, Layer.Common, new BindingAction.StandardKey(KeyModifiers.None, usage)).Encode());
    }

    [Fact]
    public void Invalid_payloads_are_rejected()
    {
        static void Bad(BindingAction a) => Assert.ThrowsAny<ArgumentException>(() => new KeyBinding(30, Layer.Common, a).Encode());
        Bad(new BindingAction.FKey(0x29));
        Bad(new BindingAction.MouseButton(MouseButtonKind.Left, AutoFire: 51));
        Bad(new BindingAction.OpenBrowser("https://\u20AC")); // not Latin-1
        Bad(new BindingAction.OpenFile(new string('a', 256)));
        Bad(new BindingAction.Backlight(BacklightAction.SelectEffect));
    }

    [Fact]
    public void GetAll_pages_from_the_count_read_until_the_total()
    {
        var dev = new KeyboardFakeDevice { PageSize = 2 };
        for (byte k = 1; k <= 5; k++)
            dev.Records.Add(new KeyBinding(k, k % 2 == 0 ? Layer.Fn : Layer.Common, new BindingAction.Media(MediaAction.Mute)).Encode());
        var b = new Bindings(new QLinkClient(dev.Transport));

        var all = b.GetAll();

        Assert.Equal([1, 2, 3, 4, 5], all.Select(x => (int)x.KeyId));
        Assert.Equal(Layer.Fn, all[1].Layer);
        var starts = dev.Transport.Requests.Select(r => BinaryPrimitives.ReadUInt16LittleEndian(r.Data)).ToArray();
        Assert.Equal([(ushort)0, (ushort)2, (ushort)4], starts);
    }

    [Fact]
    public void GetAll_stops_when_a_page_returns_no_records()
    {
        var dev = new KeyboardFakeDevice();
        var rec = new KeyBinding(29, Layer.Common, new BindingAction.Disabled()).Encode();
        dev.BindingsPage = start => start == 0 ? [7, 3, .. rec] : [7, 3];

        var all = new Bindings(new QLinkClient(dev.Transport)).GetAll();

        Assert.Single(all);
        Assert.Equal(2, dev.Transport.Requests.Count);
    }

    [Fact]
    public void Garbled_records_stop_parsing_without_crashing()
    {
        var ok1 = new KeyBinding(29, Layer.Common, new BindingAction.StandardKey(KeyModifiers.None, 0x29)).Encode();
        var ok2 = new KeyBinding(88, Layer.Fn, new BindingAction.Media(MediaAction.Mute)).Encode();
        var dev = new KeyboardFakeDevice();
        // ok1, ok2, unknown type 0x55 (length unknown) then more bytes that must not be read as records.
        dev.BindingsPage = start => start == 0 ? [3, 0, .. ok1, .. ok2, 0x10, 0x00, 0x55, 1, 2, 3, 4] : [3, 0];

        var all = new Bindings(new QLinkClient(dev.Transport)).GetAll();

        Assert.Equal([29, 88], all.Select(x => (int)x.KeyId));
    }

    [Theory]
    [InlineData("100003")]                 // truncated mouse record (no sub-type)
    [InlineData("10000309")]               // unknown mouse sub-type
    [InlineData("1000050A6162")]           // string shorter than its length byte
    [InlineData("100001")]                 // truncated standard key
    [InlineData("10")]                     // truncated header
    public void Truncated_or_unknown_records_are_skipped(string tailHex)
    {
        var ok = new KeyBinding(29, Layer.Common, new BindingAction.Disabled()).Encode();
        var list = new List<KeyBinding>();

        int n = BindingCodec.ParseRecords([.. ok, .. Convert.FromHexString(tailHex)], 10, list);

        Assert.Equal(1, n);
        Assert.Single(list);
    }

    [Fact]
    public void Known_length_records_with_unknown_payload_become_Raw()
    {
        var list = new List<KeyBinding>();
        BindingCodec.ParseRecords(Convert.FromHexString("1E000B094142" + "1D0000"), 10, list);

        Assert.Equal(2, list.Count);
        Assert.Equal(new BindingAction.Raw(11, [9, 0x41, 0x42]), list[0].Action);
        Assert.True(list[0].Action.IsReadOnly);
        Assert.Equal(new BindingAction.Disabled(), list[1].Action);
        Assert.Equal(Convert.FromHexString("1E000B094142"), list[0].Encode());
    }

    [Fact]
    public void Single_record_decode_keeps_unexpected_lengths_as_Raw()
    {
        // A web-written macro record ([action][macroId u16]) is 6 bytes, the read form is 5.
        var b = KeyBinding.Decode(Convert.FromHexString("1E000A010700"));
        Assert.IsType<BindingAction.Raw>(b.Action);
        Assert.Equal(Convert.FromHexString("1E000A010700"), b.Encode());
        Assert.IsType<BindingAction.Raw>(KeyBinding.Decode(Convert.FromHexString("1E0063AABB")).Action);
    }

    [Fact]
    public void Parse_is_bounded_by_the_requested_count()
    {
        var rec = new KeyBinding(29, Layer.Common, new BindingAction.Disabled()).Encode();
        var list = new List<KeyBinding>();
        Assert.Equal(2, BindingCodec.ParseRecords([.. rec, .. rec, .. rec], 2, list));
    }

    [Fact]
    public void Locked_keys_and_unknown_ids_are_never_written()
    {
        var dev = new KeyboardFakeDevice();
        var b = new Bindings(new QLinkClient(dev.Transport));
        var any = new BindingAction.Media(MediaAction.Mute);

        Assert.Throws<InvalidOperationException>(() => b.SetBinding(new KeyBinding(KeyIds.Fn, Layer.Common, any)));
        Assert.Throws<InvalidOperationException>(() => b.SetBinding(new KeyBinding(KeyIds.Fn, Layer.Fn, any)));
        Assert.Throws<InvalidOperationException>(() => b.SetBinding(new KeyBinding(KeyIds.R, Layer.Fn, any)));
        Assert.Throws<InvalidOperationException>(() => b.SetBinding(new KeyBinding(KeyIds.Pause, Layer.Fn, any)));
        Assert.Throws<InvalidOperationException>(() => b.Clear(KeyIds.Pause, Layer.Fn));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetBinding(new KeyBinding(107, Layer.Common, any)));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetBinding(new KeyBinding(0, Layer.Common, any)));
        Assert.Empty(dev.Transport.Written);

        b.SetBinding(new KeyBinding(KeyIds.R, Layer.Common, any));    // R is only locked on the Fn layer
        b.SetBinding(new KeyBinding(KeyIds.Pause, Layer.Common, any));
        Assert.Equal(2, dev.Records.Count);
    }

    [Fact]
    public void Enabled_switch_reads_and_writes()
    {
        var dev = new KeyboardFakeDevice { BindingsEnabled = 0 };
        var b = new Bindings(new QLinkClient(dev.Transport));

        Assert.False(b.GetEnabled());
        b.SetEnabled(true);
        Assert.Equal(1, dev.BindingsEnabled);
    }

    [Fact]
    public void Factory_defaults_match_the_doc()
    {
        var d = BindingDefaults.Factory;

        Assert.Equal(20, d.Count);
        Assert.Equal(12, d.Count(x => x.Layer == Layer.Common));
        Assert.Contains(new KeyBinding(118, Layer.Common, new BindingAction.Media(MediaAction.PlayPause)), d);
        Assert.Contains(new KeyBinding(117, Layer.Fn, new BindingAction.Media(MediaAction.Mute)), d);
        Assert.Contains(new KeyBinding(65, Layer.Fn, new BindingAction.Backlight(BacklightAction.IncreaseBrightness)), d);
        Assert.Contains(new KeyBinding(62, Layer.Fn, new BindingAction.Backlight(BacklightAction.PrevEffect)), d);
        Assert.Contains(new KeyBinding(116, Layer.Common, new BindingAction.WindowsShortcut(WindowsShortcutAction.SleepPC)), d);
        var b1 = d.Single(x => x.KeyId == 109);
        Assert.Equal(new BindingAction.OpenBrowser("https://www.bequiet.com/en"), b1.Action);
        Assert.Equal(4 + 26, b1.Encode().Length);
        Assert.Equal(8, BindingDefaults.For(Layer.Fn).Count);
    }

    [Fact]
    public void KeyBinding_json_is_the_hex_record()
    {
        var b = new KeyBinding(29, Layer.Common, new BindingAction.StandardKey(KeyModifiers.None, 0x29));

        var json = System.Text.Json.JsonSerializer.Serialize(b);

        Assert.Equal("\"1D00010029\"", json);
        Assert.Equal(b, System.Text.Json.JsonSerializer.Deserialize<KeyBinding>(json));
    }
}

public class KeyboardProtocolTableTests
{
    [Fact]
    public void Key_table_is_complete()
    {
        var ids = KeyIds.All.Select(k => (int)k.Id).ToArray();
        Assert.Equal(Enumerable.Range(1, 105).Concat(Enumerable.Range(109, 12)), ids);
        Assert.Null(KeyIds.Find(0));
        Assert.Null(KeyIds.Find(107));

        var esc = KeyIds.Find(87)!;
        Assert.Equal(("KEY_ID_ESC", "Escape", (byte?)0x29, KeyZone.Keyboard), (esc.Name, esc.Label, esc.HidUsage, esc.Zone));
        Assert.Equal(KeyZone.Numpad, KeyIds.Get(70).Zone);
        Assert.Equal(KeyZone.DisplayKey, KeyIds.Get(109).Zone);
        Assert.Equal(KeyZone.DockButton, KeyIds.Get(120).Zone);
        Assert.Equal("KEY_ID_NEXT_TRACK", KeyIds.Get(120).Name);
        Assert.Equal(29, KeyIds.FindByName("KEY_ID_CAP")!.Id);
        Assert.Equal("Z", KeyIds.Get(21).LabelFor(VisualLayout.DE));
        Assert.Equal("A", KeyIds.Get(16).LabelFor(VisualLayout.FR));
        Assert.Equal("#", KeyIds.Get(28).LabelFor(VisualLayout.UK));
        Assert.Equal("\\", KeyIds.Get(28).LabelFor(VisualLayout.US));
        Assert.True(KeyIds.Get(105).IsoOnly);

        // Every keyboard/numpad key with a known usage maps to a valid HID usage.
        foreach (var k in KeyIds.All.Where(k => k.HidUsage is not null)) Assert.True(HidUsage.IsValid(k.HidUsage!.Value));
        Assert.Equal(KeyIds.All.Count, KeyIds.All.Select(k => k.Name).Distinct().Count());
    }

    [Fact]
    public void Rebindable_flags_follow_the_locked_key_list()
    {
        Assert.False(KeyIds.IsRebindable(55, Layer.Common));
        Assert.False(KeyIds.IsRebindable(55, Layer.Fn));
        Assert.False(KeyIds.IsRebindable(19, Layer.Fn));
        Assert.False(KeyIds.IsRebindable(102, Layer.Fn));
        Assert.True(KeyIds.IsRebindable(19, Layer.Common));
        Assert.True(KeyIds.IsRebindable(102, Layer.Common));
        Assert.True(KeyIds.IsRebindable(117, Layer.Fn));
        Assert.False(KeyIds.IsRebindable(0, Layer.Common));
        Assert.False(KeyIds.IsRebindable(108, Layer.Common));
    }

    [Fact]
    public void Hid_usages_cover_the_whole_keyboard_page()
    {
        for (int u = 0x04; u <= 0xE7; u++) Assert.False(string.IsNullOrWhiteSpace(HidUsage.NameOf((byte)u)));
        Assert.False(HidUsage.IsValid(0x03));
        Assert.False(HidUsage.IsValid(0xE8));
        Assert.Equal("A", HidUsage.NameOf(0x04));
        Assert.Equal("F13", HidUsage.NameOf(0x68));
        Assert.Equal("Left Ctrl", HidUsage.NameOf(0xE0));

        Assert.Equal(0x3A, HidUsage.FKey(1));
        Assert.Equal(0x45, HidUsage.FKey(12));
        Assert.Equal(0x68, HidUsage.FKey(13));
        Assert.Equal(0x73, HidUsage.FKey(24));
        Assert.Equal(24, HidUsage.FKeyNumber(0x73));
        Assert.Equal(0, HidUsage.FKeyNumber(0x29));
        Assert.Throws<ArgumentOutOfRangeException>(() => HidUsage.FKey(25));

        Assert.True(HidUsage.TryParse("F13", out var f13) && f13 == 0x68);
        Assert.True(HidUsage.TryParse("0x9a", out var raw) && raw == 0x9A);
        Assert.True(HidUsage.TryParse("esc", out var esc) && esc == 0x29);
        Assert.False(HidUsage.TryParse("0xF0", out _));
        Assert.False(HidUsage.TryParse("nope", out _));

        var offered = HidUsage.Offered.Select(u => (int)u.Usage).ToHashSet();
        foreach (int u in Enumerable.Range(0x04, 26).Concat(Enumerable.Range(0x1E, 10)).Concat(Enumerable.Range(0xE0, 8)))
            Assert.Contains(u, offered);
        for (int n = 1; n <= 24; n++) Assert.Contains(HidUsage.FKey(n), offered);
        Assert.DoesNotContain(0x66, offered); // Power is never offered
        Assert.Equal(offered.Count, HidUsage.Offered.Count);
    }

    [Fact]
    public void Ansi_geometry_with_numpad_on_the_right()
    {
        var keys = KeyGeometry.Keys(PhysicalLayout.Ansi, NumpadSide.Right);

        var expected = KeyIds.All.Where(k => k.Zone is KeyZone.Keyboard or KeyZone.Numpad or KeyZone.DisplayKey && !k.IsoOnly)
            .Select(k => (int)k.Id).Order();
        Assert.Equal(expected, keys.Select(k => (int)k.KeyId).Order());
        Assert.Equal(new KeyRect(87, 607 - 540, 188, 72, 78), keys.Single(k => k.KeyId == 87));
        Assert.Equal(new KeyRect(85, 3051 - 540, 429, 72, 191), keys.Single(k => k.KeyId == 85));
        var (w, h) = KeyGeometry.BoundingSize(keys);
        Assert.True(w <= KeyGeometry.CanvasSize(NumpadSide.Right).Width && h <= KeyGeometry.CanvasHeight);
        Assert.All(keys, k => Assert.True(k.X >= 0 && k.Y >= 0));
        AssertNoOverlap(keys);
    }

    [Fact]
    public void Numpad_left_and_none_variants()
    {
        var left = KeyGeometry.Keys(PhysicalLayout.Ansi, NumpadSide.Left);
        Assert.Equal(new KeyRect(87, 607, 188, 72, 78), left.Single(k => k.KeyId == 87));
        Assert.Equal(new KeyRect(109, 76, 60, 84, 84), left.Single(k => k.KeyId == 109));
        AssertNoOverlap(left);

        var none = KeyGeometry.Keys(PhysicalLayout.Ansi, NumpadSide.None);
        Assert.DoesNotContain(none, k => k.KeyId is >= 70 and <= 86 or >= 109);
        Assert.True(KeyGeometry.BoundingSize(none).Width <= KeyGeometry.CanvasSize(NumpadSide.None).Width);
        Assert.Equal((2108, 886), KeyGeometry.CanvasSize(NumpadSide.None));
        Assert.Equal((2649, 886), KeyGeometry.CanvasSize(NumpadSide.Left));
    }

    [Fact]
    public void Iso_geometry_differs_where_documented()
    {
        var iso = KeyGeometry.Keys(PhysicalLayout.Iso, NumpadSide.Left);
        Assert.Equal(new KeyRect(41, 2102, 430, 126, 190), iso.Single(k => k.KeyId == 41));
        Assert.Equal(new KeyRect(28, 2018, 540, 74, 78), iso.Single(k => k.KeyId == 28));
        Assert.Equal(new KeyRect(42, 607, 647, 102, 78), iso.Single(k => k.KeyId == 42));
        Assert.Equal(new KeyRect(105, 747, 648, 72, 78), iso.Single(k => k.KeyId == 105));
        AssertNoOverlap(iso);
        Assert.DoesNotContain(KeyGeometry.Keys(PhysicalLayout.Ansi, NumpadSide.Left), k => k.KeyId == 105);
    }

    [Fact]
    public void Dock_buttons_form_a_two_by_two_grid()
    {
        var dock = KeyGeometry.DockButtons(1000, 50);
        Assert.Equal([117, 118, 119, 120], dock.Select(k => (int)k.KeyId));
        Assert.Equal((1000, 50), (dock[0].X, dock[0].Y));
        Assert.True(dock[1].X > dock[0].X && dock[1].Y == dock[0].Y);   // top right
        Assert.True(dock[2].Y > dock[0].Y && dock[2].X == dock[0].X);   // bottom left

        var all = KeyGeometry.Layout(PhysicalLayout.Ansi, NumpadSide.Right, includeDock: true);
        Assert.Equal(4, all.Count(k => k.KeyId >= 117));
        AssertNoOverlap(all);
        var (w, h) = KeyGeometry.BoundingSize(all);
        Assert.All(all, k => Assert.True(k.Right <= w && k.Bottom <= h));
        Assert.Equal((byte?)87, KeyGeometry.HitTest(all, 607 - 540 + 10, 200)?.KeyId);
        Assert.Null(KeyGeometry.HitTest(all, 0, 0));
    }

    static void AssertNoOverlap(IReadOnlyList<KeyRect> keys)
    {
        for (int i = 0; i < keys.Count; i++)
            for (int j = i + 1; j < keys.Count; j++)
            {
                var (a, b) = (keys[i], keys[j]);
                bool overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
                Assert.False(overlap, $"{a} overlaps {b}");
            }
    }
}

public class KeyboardProtocolBackupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dmh-kbd-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static readonly KeyBinding CapsToEsc = new(29, Layer.Common, new BindingAction.StandardKey(KeyModifiers.None, 0x29));
    static readonly KeyBinding FnF1Mute = new(88, Layer.Fn, new BindingAction.Media(MediaAction.Mute));
    static readonly KeyBinding FnF1VolUp = new(88, Layer.Fn, new BindingAction.Media(MediaAction.VolumeUp));
    static readonly KeyBinding B1Browser = new(109, Layer.Common, new BindingAction.OpenBrowser("https://www.bequiet.com/en"));
    static readonly KeyBinding LWinOff = new(103, Layer.Common, new BindingAction.Disabled());

    static KeyboardFakeDevice Device()
    {
        var dev = new KeyboardFakeDevice { LockMask = 0x04, State = 0x01 };
        dev.Records.AddRange([CapsToEsc.Encode(), FnF1Mute.Encode(), B1Browser.Encode()]);
        dev.SeedImage(0, [0xFF, 0xD8, 1, 2, 3]);
        dev.SeedImage(1, [0xFF, 0xD8, 4, 5, 6]);
        return dev;
    }

    [Fact]
    public void Read_collects_every_setting()
    {
        var dev = Device();

        var s = KeyboardBackup.Read(new QLinkClient(dev.Transport), includeDisplayKeys: true);

        Assert.Equal(GameModeLocks.Win, s.LockMask);
        Assert.Equal(KeyboardStateFlags.GameMode, s.GameModeState);
        Assert.Equal(LightingMode.General, s.LightingMode);
        Assert.Equal(Effect.Static, s.LayerConfig!.Effect);
        Assert.True(s.BindingsEnabled);
        Assert.Equal([CapsToEsc, FnF1Mute, B1Browser], s.Bindings!);
        Assert.Equal([0, 1], s.DisplayKeyJpegs!.Keys.Order());
        Assert.Equal(new byte[] { 0xFF, 0xD8, 4, 5, 6 }, s.DisplayKeyJpegs[1]);
        Assert.Empty(dev.Writes);
    }

    [Fact]
    public void Read_tolerates_firmware_without_GetState_and_skips_images_when_asked()
    {
        var dev = Device();
        var inner = dev.Transport.Responder;
        dev.Transport.Responder = req => req is { Feature: 7, Command: 4 } ? FakeTransport.Reply(req, [], 13) : inner(req);

        var s = KeyboardBackup.Read(new QLinkClient(dev.Transport), includeDisplayKeys: false);

        Assert.Null(s.GameModeState);
        Assert.Null(s.DisplayKeyJpegs);
        Assert.DoesNotContain(dev.Transport.Requests, r => r.Feature == Features.Numpad);
    }

    [Fact]
    public void Json_round_trips_and_stores_bindings_as_hex_records()
    {
        var s = KeyboardBackup.Read(new QLinkClient(Device().Transport), includeDisplayKeys: true);

        var json = KeyboardBackup.ToJson(s);
        var back = KeyboardBackup.FromJson(json);

        Assert.Contains("\"1D00010029\"", json);
        Assert.Equal(s.LockMask, back.LockMask);
        Assert.Equal(s.GameModeState, back.GameModeState);
        Assert.Equal(s.LightingMode, back.LightingMode);
        Assert.Equal(s.LayerConfig, back.LayerConfig);
        Assert.Equal(s.BindingsEnabled, back.BindingsEnabled);
        Assert.Equal(s.Bindings, back.Bindings);
        Assert.Equal(s.DisplayKeyJpegs![1], back.DisplayKeyJpegs![1]);
    }

    [Fact]
    public void SaveOnce_never_overwrites_and_Load_reads_it_back()
    {
        Assert.Null(KeyboardBackup.Load(_dir));
        var first = KeyboardBackup.Read(new QLinkClient(Device().Transport), includeDisplayKeys: false);
        var second = new KeyboardSnapshot { LockMask = GameModeLocks.All };

        Assert.True(KeyboardBackup.SaveOnce(first, _dir));
        Assert.False(KeyboardBackup.SaveOnce(second, _dir));

        var loaded = KeyboardBackup.Load(_dir)!;
        Assert.Equal(GameModeLocks.Win, loaded.LockMask);
        Assert.Equal(first.Bindings, loaded.Bindings);
        Assert.EndsWith(Path.Combine("OverMount", "keyboard-backup"), KeyboardBackup.DefaultFolder);
    }

    [Fact]
    public void Apply_writes_only_differences_in_the_documented_order()
    {
        var dev = Device();
        var q = new QLinkClient(dev.Transport);
        var current = KeyboardBackup.Read(q, includeDisplayKeys: true);
        var target = KeyboardBackup.FromJson(KeyboardBackup.ToJson(current));
        target.LockMask = GameModeLocks.Win | GameModeLocks.AltTab;
        target.LayerConfig = LightingEffects.Find(Effect.ColorWave)!.Default;
        target.Bindings = [CapsToEsc, FnF1VolUp, LWinOff];                   // B1 removed, F1 changed, LWin new
        target.DisplayKeyJpegs = new() { [0] = [0xFF, 0xD8, 1, 2, 3], [1] = [0xFF, 0xD8, 9], [2] = [0xFF, 0xD8, 7] };
        dev.Transport.Requests.Clear();

        int writes = KeyboardBackup.Apply(q, target, current);

        var log = dev.Writes.Select(Describe).Distinct().ToList(); // one image = several SetImage chunks
        Assert.Equal(
        [
            "7/3 0C",
            "16/6",
            "17/3 6D00",
            "17/2 588002",
            "17/2 670000",
            "32/2 key110",
            "32/2 key111",
        ], log);
        Assert.Equal(7, writes);
        Assert.Equal(dev.Writes.Count, dev.Transport.Requests.Count); // no reads when current is supplied

        // The device now holds the target.
        var after = KeyboardBackup.Read(q, includeDisplayKeys: true);
        Assert.Equal(target.LockMask, after.LockMask);
        Assert.Equal(target.LayerConfig, after.LayerConfig);
        Assert.Equal(target.Bindings.OrderBy(b => b.KeyId), after.Bindings!.OrderBy(b => b.KeyId));
        Assert.Equal(target.DisplayKeyJpegs[1], after.DisplayKeyJpegs![1]);
        Assert.Equal(target.DisplayKeyJpegs[2], after.DisplayKeyJpegs[2]);
    }

    static string Describe(Frame w) => (w.Feature, w.Command) switch
    {
        (Features.Numpad, _) => $"32/2 key{BinaryPrimitives.ReadUInt16LittleEndian(w.Data)}",
        (Features.Lightings, 6) => "16/6",
        (Features.Bindings, 2) => $"17/2 {Convert.ToHexString(w.Data.AsSpan(0, 3))}",
        _ => $"{w.Feature}/{w.Command} {Convert.ToHexString(w.Data)}",
    };

    [Fact]
    public void Apply_of_an_identical_snapshot_writes_nothing()
    {
        var dev = Device();
        var q = new QLinkClient(dev.Transport);
        var current = KeyboardBackup.Read(q, includeDisplayKeys: true);

        Assert.Equal(0, KeyboardBackup.Apply(q, KeyboardBackup.FromJson(KeyboardBackup.ToJson(current)), current));
        Assert.Empty(dev.Writes);
    }

    [Fact]
    public void Apply_without_current_reads_the_device_first_and_null_fields_are_left_alone()
    {
        var dev = Device();
        var q = new QLinkClient(dev.Transport);
        var target = new KeyboardSnapshot { LightingMode = LightingMode.Off, BindingsEnabled = false, Bindings = [CapsToEsc] };

        KeyboardBackup.Apply(q, target, null);

        Assert.Equal(["16/2 00", "17/5 00", "17/3 5880", "17/3 6D00"], dev.Writes.Select(Describe));
        Assert.Equal(0x04, dev.LockMask);  // LockMask null in target: untouched
    }

    [Fact]
    public void Apply_never_touches_locked_keys()
    {
        var dev = Device();
        dev.Records.Add([55, 0x80, 2, 3]); // a (hypothetical) reported binding on Fn+FN
        var q = new QLinkClient(dev.Transport);
        var target = new KeyboardSnapshot { Bindings = [CapsToEsc, FnF1Mute, B1Browser, new(102, Layer.Fn, new BindingAction.Disabled())] };

        KeyboardBackup.Apply(q, target, null);

        Assert.Empty(dev.Writes);
    }

    [Fact]
    public void Apply_writes_every_target_image_when_current_is_unknown()
    {
        var dev = Device();
        var q = new QLinkClient(dev.Transport);
        var target = new KeyboardSnapshot { DisplayKeyJpegs = new() { [0] = [0xFF, 0xD8, 1, 2, 3] } };

        KeyboardBackup.Apply(q, target, null);

        Assert.Equal(["32/2 key109"], dev.Writes.Select(Describe).Distinct());
    }
}
