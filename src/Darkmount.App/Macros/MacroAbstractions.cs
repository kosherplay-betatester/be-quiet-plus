namespace Darkmount.App.Macros;

/// <summary>Synthesised keyboard and mouse input. Real implementation: <see cref="SendInputSink"/>.</summary>
public interface IInputSink
{
    /// <param name="vk">Windows virtual-key code (1–254).</param>
    /// <param name="extended">Send with KEYEVENTF_EXTENDEDKEY (see <see cref="VirtualKeys.IsExtended"/>).</param>
    void SendKeyDown(int vk, bool extended);

    void SendKeyUp(int vk, bool extended);

    /// <summary>Types one UTF-16 code unit (down + up) with KEYEVENTF_UNICODE; surrogate pairs are sent as two calls.</summary>
    void SendUnicodeChar(char c);

    /// <summary>Moves the cursor to screen pixel (x, y) on the virtual desktop, or by (x, y) when relative.</summary>
    void SendMouseMove(int x, int y, bool relative);

    void SendMouseButton(MouseButton button, bool down);

    /// <summary>Wheel movement in WHEEL_DELTA units (120 = one notch up).</summary>
    void SendMouseWheel(int delta);
}

/// <summary>Launching things. Real implementation: <see cref="ShellLauncher"/>. Throws <see cref="MacroStepException"/> on invalid targets.</summary>
public interface IShell
{
    void Launch(string path, string? arguments, string? workingDirectory);
    void OpenUrl(string url);
    void OpenFolder(string path);
}

/// <summary>Physical key state. Real implementation: <see cref="AsyncKeyState"/>.</summary>
public interface IKeyState
{
    bool IsDown(int vk);
}

/// <summary>Cancellable waiting. Real implementation: <see cref="PreciseTimer"/>.</summary>
public interface IMacroTimer
{
    /// <summary>Waits <paramref name="milliseconds"/>; returns false if <paramref name="token"/> was cancelled.</summary>
    bool Wait(int milliseconds, CancellationToken token);
}

/// <summary>A step could not run (missing program, disallowed URL, invalid key…). The message is user-facing.</summary>
public sealed class MacroStepException(string message, Exception? inner = null) : Exception(message, inner);
