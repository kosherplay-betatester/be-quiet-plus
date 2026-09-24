using System.Text.Json.Serialization;

namespace Darkmount.App.Macros;

/// <summary>
/// One action of a macro. Serialised polymorphically with a "type" discriminator, e.g.
/// <c>{ "type": "Delay", "Ms": 100 }</c>. Steps are immutable; edit with <c>step with { … }</c>.
/// There is deliberately no "run shell command" step.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyTapStep), "KeyTap")]
[JsonDerivedType(typeof(KeyDownStep), "KeyDown")]
[JsonDerivedType(typeof(KeyUpStep), "KeyUp")]
[JsonDerivedType(typeof(TypeTextStep), "TypeText")]
[JsonDerivedType(typeof(DelayStep), "Delay")]
[JsonDerivedType(typeof(MouseClickStep), "MouseClick")]
[JsonDerivedType(typeof(MouseMoveStep), "MouseMove")]
[JsonDerivedType(typeof(ScrollStep), "Scroll")]
[JsonDerivedType(typeof(LaunchProgramStep), "LaunchProgram")]
[JsonDerivedType(typeof(OpenUrlStep), "OpenUrl")]
[JsonDerivedType(typeof(OpenFolderStep), "OpenFolder")]
[JsonDerivedType(typeof(MediaKeyStep), "MediaKey")]
public abstract record MacroStep
{
    /// <summary>Short text for step lists, e.g. "Press Ctrl+C" or "Wait 100 ms".</summary>
    public abstract string Describe();
}

/// <summary>Press and release <paramref name="Vk"/> with modifiers held around it; the key is held for <paramref name="HoldMs"/>.</summary>
public sealed record KeyTapStep(int Vk, MacroModifiers Modifiers = MacroModifiers.None, int HoldMs = 0) : MacroStep
{
    public override string Describe() => $"Press {VirtualKeys.Combo(Vk, Modifiers)}" + (HoldMs > 0 ? $" (hold {HoldMs} ms)" : "");
}

/// <summary>Press a key and keep it down (the player releases it when the macro ends at the latest).</summary>
public sealed record KeyDownStep(int Vk) : MacroStep
{
    public override string Describe() => $"Hold down {VirtualKeys.Name(Vk)}";
}

public sealed record KeyUpStep(int Vk) : MacroStep
{
    public override string Describe() => $"Release {VirtualKeys.Name(Vk)}";
}

/// <summary>Type text as Unicode characters (layout independent). Line breaks are sent as Enter, tabs as Tab.</summary>
public sealed record TypeTextStep(string Text, int PerCharDelayMs = 0) : MacroStep
{
    public override string Describe()
    {
        var oneLine = (Text ?? "").ReplaceLineEndings(" ");
        return $"Type \"{(oneLine.Length > 30 ? oneLine[..29] + "…" : oneLine)}\"";
    }
}

public sealed record DelayStep(int Ms) : MacroStep
{
    public override string Describe() => $"Wait {Ms} ms";
}

/// <summary>Click a mouse button; with both <paramref name="X"/> and <paramref name="Y"/> the cursor first moves there (screen pixels).</summary>
public sealed record MouseClickStep(MouseButton Button = MouseButton.Left, int? X = null, int? Y = null) : MacroStep
{
    public override string Describe() => $"{Button} click" + (X is int x && Y is int y ? $" at {x}, {y}" : "");
}

/// <summary>Move the cursor to screen pixel (X, Y), or by (X, Y) when <paramref name="Relative"/>.</summary>
public sealed record MouseMoveStep(int X, int Y, bool Relative = false) : MacroStep
{
    public override string Describe() => Relative ? $"Move mouse by {X}, {Y}" : $"Move mouse to {X}, {Y}";
}

/// <summary>Turn the mouse wheel by <paramref name="Delta"/> notches; positive scrolls up.</summary>
public sealed record ScrollStep(int Delta) : MacroStep
{
    public override string Describe() => Delta >= 0 ? $"Scroll up {Delta}" : $"Scroll down {-Delta}";
}

/// <summary>Start a program or open a file with its default app. <paramref name="Path"/> must exist (or be a program on PATH).</summary>
public sealed record LaunchProgramStep(string Path, string? Arguments = null, string? WorkingDirectory = null) : MacroStep
{
    public override string Describe() => $"Launch {System.IO.Path.GetFileName(Path ?? "")}";
}

/// <summary>Open an http(s) link in the default browser.</summary>
public sealed record OpenUrlStep(string Url) : MacroStep
{
    public override string Describe() => $"Open {Url}";
}

public sealed record OpenFolderStep(string Path) : MacroStep
{
    public override string Describe() => $"Open folder {Path}";
}

public sealed record MediaKeyStep(MediaKey Key) : MacroStep
{
    public override string Describe() => "Media: " + Key switch
    {
        MediaKey.PlayPause => "Play/Pause",
        MediaKey.Next => "Next track",
        MediaKey.Previous => "Previous track",
        MediaKey.Stop => "Stop",
        MediaKey.VolumeUp => "Volume up",
        MediaKey.VolumeDown => "Volume down",
        MediaKey.Mute => "Mute",
        _ => Key.ToString(),
    };
}
