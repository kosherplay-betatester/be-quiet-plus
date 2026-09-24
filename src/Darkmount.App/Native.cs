using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Darkmount.App;

/// <summary>System-wide hotkey via RegisterHotKey, e.g. "Ctrl+Alt+Shift+D".</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WmHotkey = 0x0312;
    const int Id = 0xD0C;
    bool _registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    /// <summary>Registers the hotkey; returns false if the text is invalid or the combination is taken.</summary>
    public bool Register(string hotkey)
    {
        Unregister();
        if (!TryParse(hotkey, out uint mods, out uint key)) return false;
        _registered = RegisterHotKey(Handle, Id, mods | 0x4000 /* MOD_NOREPEAT */, key);
        return _registered;
    }

    public static bool TryParse(string text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x2; break;
                case "alt": modifiers |= 0x1; break;
                case "shift": modifiers |= 0x4; break;
                case "win": modifiers |= 0x8; break;
                default:
                    if (!Enum.TryParse<Keys>(part, ignoreCase: true, out var k)) return false;
                    key = (uint)k;
                    break;
            }
        }
        return key != 0 && modifiers != 0;
    }

    void Unregister()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && (int)m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>Start with Windows via HKCU\...\Run.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "DarkmountHub";

    static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string v && v == Command;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, Command);
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
