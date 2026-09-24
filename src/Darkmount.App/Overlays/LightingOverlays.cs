using Darkmount.Keyboard;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.App.Overlays;

/// <summary>Which live overlays are drawn on top of the lighting scene.</summary>
public sealed class OverlaySettings
{
    /// <summary>Caps / Num / Scroll Lock keys glow while on.</summary>
    public bool LockKeys { get; set; } = true;

    /// <summary>F1–F12 show the volume for 2 s when it changes (dock dial, media keys).</summary>
    public bool VolumeBar { get; set; } = true;

    /// <summary>A key glows red while the microphone is muted.</summary>
    public bool MicMute { get; set; } = true;

    /// <summary>Dark Mount key id of the mic-mute light (default: Pause).</summary>
    public int MicMuteKey { get; set; } = 102;

    /// <summary>Holding Ctrl, Alt or Win highlights the keys with common shortcuts.</summary>
    public bool ShortcutHelper { get; set; } = true;

    /// <summary>F1–F12 show the focus timer's progress while it runs.</summary>
    public bool FocusTimerBar { get; set; } = true;
}

public enum HeldModifier { None, Ctrl, Alt, Win }

/// <summary>Live state the overlays react to (gathered by the app a few times per second).</summary>
public sealed record OverlayState
{
    public bool CapsLock { get; init; }
    public bool NumLock { get; init; }
    public bool ScrollLock { get; init; }
    public bool ShowVolume { get; init; }
    public double Volume { get; init; }
    public bool SpeakersMuted { get; init; }
    public bool MicMuted { get; init; }
    public HeldModifier Modifier { get; init; }

    /// <summary>Focus timer progress 0..1 and whether it is a focus (true) or break (false) phase; null when idle.</summary>
    public (double Progress, bool Focus)? Timer { get; init; }
}

/// <summary>Draws the overlays onto a finished frame (lamp id → colour). Pure and unit-tested.</summary>
public static class LightingOverlays
{
    // HID usages, so the overlays follow the keys whatever the visual layout.
    static readonly byte[] FRow = [0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45];
    static readonly byte[] CtrlShortcuts = [0x04, 0x06, 0x19, 0x1B, 0x1D, 0x1C, 0x16, 0x09, 0x13, 0x11, 0x17, 0x1A, 0x15, 0xE0, 0xE4];
    static readonly byte[] AltShortcuts = [0x2B, 0x3D, 0xE2, 0xE6];
    static readonly byte[] WinShortcuts = [0x07, 0x08, 0x0F, 0x15, 0x0C, 0x16, 0x19, 0x2B, 0x4F, 0x50, 0x51, 0x52, 0xE3];
    const byte CapsUsage = 0x39, NumUsage = 0x53, ScrollUsage = 0x47;

    static readonly Dictionary<byte, int> KeyByUsage =
        KeyIds.All.Where(k => k.HidUsage is not null).GroupBy(k => k.HidUsage!.Value).ToDictionary(g => g.Key, g => (int)g.First().Id);

    public static readonly LampColor Cyan = new(0, 220, 255), Red = new(255, 0, 0), White = new(255, 255, 255);

    /// <param name="frame">Lamp id → colour; modified in place.</param>
    /// <param name="lampOfKey">Dark Mount key id → lamp id (null if the key has no lamp).</param>
    public static void Apply(Dictionary<int, LampColor> frame, OverlaySettings s, OverlayState st, Func<int, int?> lampOfKey)
    {
        void Set(int keyId, LampColor c) { if (lampOfKey(keyId) is { } lamp) frame[lamp] = c; }
        void SetUsage(byte usage, LampColor c) { if (KeyByUsage.TryGetValue(usage, out int k)) Set(k, c); }

        if (s.ShortcutHelper && st.Modifier != HeldModifier.None)
        {
            var keys = st.Modifier switch { HeldModifier.Ctrl => CtrlShortcuts, HeldModifier.Alt => AltShortcuts, _ => WinShortcuts };
            foreach (var lamp in frame.Keys.ToList()) frame[lamp] = Dim(frame[lamp], 0.12);
            foreach (var u in keys) SetUsage(u, Cyan);
        }

        if (s.FocusTimerBar && st.Timer is { } t) Bar(t.Progress, t.Focus ? new LampColor(255, 110, 0) : new LampColor(0, 220, 90), SetUsage);

        if (s.VolumeBar && st.ShowVolume)
            Bar(st.SpeakersMuted ? 0 : st.Volume, st.SpeakersMuted ? Red : White, SetUsage, dimRest: true);

        if (s.LockKeys)
        {
            if (st.CapsLock) SetUsage(CapsUsage, White);
            if (st.NumLock) SetUsage(NumUsage, White);
            if (st.ScrollLock) SetUsage(ScrollUsage, White);
        }

        if (s.MicMute && st.MicMuted) Set(s.MicMuteKey, Red);
    }

    /// <summary>Lights the first round(fraction × 12) F-keys; the rest off (or dim).</summary>
    static void Bar(double fraction, LampColor color, Action<byte, LampColor> set, bool dimRest = false)
    {
        int lit = (int)Math.Round(Math.Clamp(fraction, 0, 1) * FRow.Length);
        for (int i = 0; i < FRow.Length; i++) set(FRow[i], i < lit ? color : dimRest ? new LampColor(20, 20, 20) : new LampColor(0, 0, 0));
    }

    static LampColor Dim(LampColor c, double k) => new((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
}
