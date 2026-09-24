using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Darkmount.App.Macros;

/// <summary>
/// <see cref="IInputSink"/> over user32 SendInput. Keys are sent with both the virtual-key code and the scan code of
/// the foreground window's keyboard layout (so games reading scan codes / raw input see the right key), with
/// KEYEVENTF_EXTENDEDKEY for E0-prefixed keys. Text uses KEYEVENTF_UNICODE, which is layout independent.
/// Throws <see cref="Win32Exception"/> when Windows rejects the input (e.g. the secure desktop / lock screen).
/// Note: UIPI silently drops input aimed at windows of a higher integrity level (elevated apps).
/// </summary>
public sealed class SendInputSink : IInputSink
{
    /// <summary>Written to dwExtraInfo of every event so hooks can recognise Darkmount's own input ("DMMC").</summary>
    public const int ExtraInfoTag = 0x444D4D43;

    /// <summary>Marshalled size of INPUT; must be 40 bytes on x64 for SendInput to accept it.</summary>
    public static int NativeInputSize => Marshal.SizeOf<INPUT>();

    public void SendKeyDown(int vk, bool extended) => SendKey(vk, extended, up: false);
    public void SendKeyUp(int vk, bool extended) => SendKey(vk, extended, up: true);

    public void SendUnicodeChar(char c)
    {
        Send(
            Keyboard(0, c, KEYEVENTF_UNICODE),
            Keyboard(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
    }

    public void SendMouseMove(int x, int y, bool relative)
    {
        if (relative)
        {
            Send(Mouse(x, y, 0, MOUSEEVENTF_MOVE));
            return;
        }
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        Send(Mouse(Normalise(x - left, width), Normalise(y - top, height), 0,
            MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK));
    }

    public void SendMouseButton(MouseButton button, bool down)
    {
        var (flags, data) = button switch
        {
            MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
            MouseButton.Right => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
            MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.X1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton.X2 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2),
            _ => throw new MacroStepException($"Unknown mouse button {button}"),
        };
        Send(Mouse(0, 0, data, flags));
    }

    public void SendMouseWheel(int delta) => Send(Mouse(0, 0, unchecked((uint)delta), MOUSEEVENTF_WHEEL));

    static void SendKey(int vk, bool extended, bool up)
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
        uint scan = MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC_EX, layout);
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (extended || (scan >> 8) is 0xE0 or 0xE1) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(Keyboard((ushort)vk, (ushort)(scan & 0xFF), flags));
    }

    static int Normalise(int pixel, int size)
        => size <= 1 ? 0 : (int)Math.Round(Math.Clamp(pixel, 0, size - 1) * 65535.0 / (size - 1));

    static INPUT Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        Type = INPUT_KEYBOARD,
        U = new InputUnion { Keyboard = new KEYBDINPUT { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = ExtraInfoTag } },
    };

    static INPUT Mouse(int dx, int dy, uint data, uint flags) => new()
    {
        Type = INPUT_MOUSE,
        U = new InputUnion { Mouse = new MOUSEINPUT { Dx = dx, Dy = dy, MouseData = data, Flags = flags, ExtraInfo = ExtraInfoTag } },
    };

    static void Send(params INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the synthesised input");
    }

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8,
        MOUSEEVENTF_RIGHTUP = 0x10, MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_XDOWN = 0x80,
        MOUSEEVENTF_XUP = 0x100, MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    const uint XBUTTON1 = 1, XBUTTON2 = 2;
    const uint MAPVK_VK_TO_VSC_EX = 4;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
}

/// <summary><see cref="IKeyState"/> over GetAsyncKeyState.</summary>
public sealed class AsyncKeyState : IKeyState
{
    public bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
}
