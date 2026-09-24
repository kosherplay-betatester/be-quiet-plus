using Darkmount.QLink;

namespace Darkmount.Keyboard;

/// <summary>Key combinations Game Mode blocks (KEYBOARD GetConfig/SetConfig lock mask, docs/QLINK_KEYBOARD.md §1.2).</summary>
[Flags]
public enum GameModeLocks : byte
{
    None = 0,
    ShiftTab = 0x01,
    AltF4 = 0x02,
    Win = 0x04,
    AltTab = 0x08,
    CapsLock = 0x10,
    All = ShiftTab | AltF4 | Win | AltTab | CapsLock,
}

/// <summary>KEYBOARD GetState/SetState bits.</summary>
[Flags]
public enum KeyboardStateFlags : byte
{
    None = 0,
    /// <summary>Game Mode on (toggled on the keyboard with Fn+Pause).</summary>
    GameMode = 0x01,
    /// <summary>Snap Tap / "Quick Tap" (a K3 feature; unknown on the Dark Mount).</summary>
    SnapTap = 0x02,
}

public enum PhysicalLayout : byte { Ansi = 0, Iso = 1, Jis = 2 }

public enum VisualLayout : byte { US = 0, DE = 1, UK = 2, FR = 3, NO = 4 }

/// <summary>
/// KEYBOARD feature (7): Game Mode lock mask, Game Mode state and layout. SetConfig/SetState are persistent
/// writes; call them only on a user action. Snap-tap writes and layout changes are deliberately not offered.
/// </summary>
public sealed class KeyboardSettings(QLinkClient q)
{
    /// <summary>Reads which combinations Game Mode blocks.</summary>
    public GameModeLocks GetLocks() => (GameModeLocks)First(q.Send(Features.Keyboard, KeyboardCommands.GetConfig), "GetConfig");

    /// <summary>Writes the complete lock mask (every bit is sent, set or clear).</summary>
    public void SetLocks(GameModeLocks locks) => q.Send(Features.Keyboard, KeyboardCommands.SetConfig, [(byte)locks]);

    /// <summary>Reads the state byte (needs keyboard MCU firmware ≥ 1.2.0; older firmware answers with an error).</summary>
    public KeyboardStateFlags GetState() =>
        (KeyboardStateFlags)First(q.Send(Features.Keyboard, KeyboardCommands.GetState), "GetState");

    /// <summary>Turns Game Mode on or off: writes bit 0 and keeps the snap-tap bit from a fresh read.</summary>
    public void SetGameMode(bool on)
    {
        var snapTap = GetState() & KeyboardStateFlags.SnapTap;
        var state = snapTap | (on ? KeyboardStateFlags.GameMode : KeyboardStateFlags.None);
        q.Send(Features.Keyboard, KeyboardCommands.SetState, [(byte)state]);
    }

    /// <summary>
    /// Reads the layout. Like the web app, an ANSI board always reports US and an ISO board never reports US (→ UK).
    /// </summary>
    public (PhysicalLayout Physical, VisualLayout Visual) GetLayout()
    {
        var d = q.Send(Features.Keyboard, KeyboardCommands.GetLayout);
        if (d.Length < 2) throw new QLinkException(QLinkStatus.InvalidSize, "Short GetLayout reply");
        var physical = (PhysicalLayout)d[0];
        var visual = (VisualLayout)d[1];
        if (physical == PhysicalLayout.Ansi) visual = VisualLayout.US;
        else if (physical == PhysicalLayout.Iso && visual == VisualLayout.US) visual = VisualLayout.UK;
        return (physical, visual);
    }

    static byte First(byte[] reply, string what) =>
        reply.Length > 0 ? reply[0] : throw new QLinkException(QLinkStatus.InvalidSize, $"Empty {what} reply");
}
