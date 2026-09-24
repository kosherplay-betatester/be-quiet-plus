namespace Darkmount.App.Macros;

/// <summary>Ready-made example macros, all disabled so they never fire until the user turns them on.</summary>
public static class MacroLibrary
{
    /// <summary>Fresh copies (new ids) each call.</summary>
    public static List<Macro> Examples() =>
    [
        new()
        {
            Name = "Type email signature",
            Trigger = new(TriggerKey.F13),
            Enabled = false,
            Steps = [new TypeTextStep("Best regards,\nYour Name")],
        },
        new()
        {
            Name = "Open Task Manager",
            Trigger = new(TriggerKey.F14),
            Enabled = false,
            Steps = [new KeyTapStep(VirtualKeys.Escape, MacroModifiers.Ctrl | MacroModifiers.Shift)],
        },
        new()
        {
            Name = "Screenshot to clipboard",
            Trigger = new(TriggerKey.F15),
            Enabled = false,
            Steps = [new KeyTapStep(VirtualKeys.S, MacroModifiers.Win | MacroModifiers.Shift)],
        },
    ];
}
