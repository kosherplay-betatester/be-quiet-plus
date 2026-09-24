using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Darkmount.App.Macros;

public enum RecordedKind { KeyDown, KeyUp, MouseDown, MouseUp }

/// <summary>One raw input event captured by <see cref="MacroRecorder"/>; <see cref="TimeMs"/> is relative to Start().</summary>
public readonly record struct RecordedInput(RecordedKind Kind, long TimeMs, int Vk = 0, MouseButton Button = MouseButton.Left, int X = 0, int Y = 0)
{
    public static RecordedInput KeyDown(int vk, long timeMs) => new(RecordedKind.KeyDown, timeMs, vk);
    public static RecordedInput KeyUp(int vk, long timeMs) => new(RecordedKind.KeyUp, timeMs, vk);
    public static RecordedInput MouseDown(MouseButton button, int x, int y, long timeMs) => new(RecordedKind.MouseDown, timeMs, Button: button, X: x, Y: y);
    public static RecordedInput MouseUp(MouseButton button, long timeMs) => new(RecordedKind.MouseUp, timeMs, Button: button);
}

/// <summary>
/// Records physical keyboard (and optionally mouse-button) input system-wide with low-level hooks, on a dedicated
/// thread with its own message loop (so a busy UI thread can't make Windows drop the hook). Injected input —
/// including our own macro playback — is ignored. Mouse movement and wheel are not recorded.
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    /// <summary>Longer delays (and key holds) are shortened to this.</summary>
    public const int MaxDelayMs = 5000;

    /// <summary>Delays shorter than this are not emitted; their time is carried into the next delay.</summary>
    public const int MinDelayMs = 10;

    readonly object _gate = new();
    readonly List<RecordedInput> _events = [];
    Thread? _thread;
    uint _threadId;
    uint _startTick;
    bool _recordMouse;
    HookProc? _keyboardProc, _mouseProc; // referenced so the GC keeps the callbacks alive while hooked

    public bool IsRecording => _thread is not null;

    /// <summary>Raw events captured so far (for a live "n events" counter).</summary>
    public int EventCount
    {
        get { lock (_gate) return _events.Count; }
    }

    /// <summary>Starts recording. Throws <see cref="Win32Exception"/> if the hooks can't be installed.</summary>
    public void Start(bool recordMouse = false)
    {
        if (_thread is not null) throw new InvalidOperationException("Already recording.");
        lock (_gate) _events.Clear();
        _recordMouse = recordMouse;
        _startTick = unchecked((uint)Environment.TickCount);

        Exception? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => HookThread(ready, e => error = e)) { IsBackground = true, Name = "Macro recorder" };
        thread.Start();
        ready.Wait();
        if (error is not null)
        {
            thread.Join();
            throw error;
        }
        _thread = thread;
    }

    /// <summary>Stops recording and returns the simplified steps (empty if not recording).</summary>
    public List<MacroStep> Stop()
    {
        var thread = _thread;
        if (thread is null) return [];
        _thread = null;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        RecordedInput[] events;
        lock (_gate)
        {
            events = [.. _events];
            _events.Clear();
        }
        return Simplify(events);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Turns raw events into macro steps:
    /// <list type="bullet">
    /// <item>auto-repeat downs, ups without a recorded down, and keys/buttons still held at Stop are dropped;</item>
    /// <item>a key's down immediately followed by its own up becomes a <see cref="KeyTapStep"/> that keeps the hold time
    /// (<see cref="KeyTapStep.HoldMs"/>); anything else stays <see cref="KeyDownStep"/>/<see cref="KeyUpStep"/>;</item>
    /// <item>a mouse press + release becomes a <see cref="MouseClickStep"/> at the press position;</item>
    /// <item>time between steps becomes <see cref="DelayStep"/>s, capped at <see cref="MaxDelayMs"/>; gaps under
    /// <see cref="MinDelayMs"/> are merged into the next delay (holds under it become 0 and are carried the same way).</item>
    /// </list>
    /// No leading or trailing delay is produced.
    /// </summary>
    public static List<MacroStep> Simplify(IReadOnlyList<RecordedInput> events)
    {
        var clean = Clean(events);
        var steps = new List<MacroStep>();
        long? previousEnd = null;
        long pending = 0;
        for (int i = 0; i < clean.Count; i++)
        {
            var e = clean[i];
            long end = e.TimeMs;
            MacroStep step;
            switch (e.Kind)
            {
                case RecordedKind.KeyDown when i + 1 < clean.Count && clean[i + 1] is { Kind: RecordedKind.KeyUp } up && up.Vk == e.Vk:
                    long hold = Math.Max(0, up.TimeMs - e.TimeMs);
                    if (hold < MinDelayMs) hold = 0;
                    else end = up.TimeMs;
                    step = new KeyTapStep(e.Vk, MacroModifiers.None, (int)Math.Min(hold, MaxDelayMs));
                    i++;
                    break;
                case RecordedKind.KeyDown:
                    step = new KeyDownStep(e.Vk);
                    break;
                case RecordedKind.KeyUp:
                    step = new KeyUpStep(e.Vk);
                    break;
                case RecordedKind.MouseDown:
                    step = new MouseClickStep(e.Button, e.X, e.Y);
                    break;
                default:
                    continue; // MouseUp: the click was emitted at its press
            }

            if (previousEnd is long prev)
            {
                pending += Math.Max(0, e.TimeMs - prev);
                if (pending >= MinDelayMs)
                {
                    steps.Add(new DelayStep((int)Math.Min(pending, MaxDelayMs)));
                    pending = 0;
                }
            }
            steps.Add(step);
            previousEnd = end;
        }
        return steps;
    }

    static List<RecordedInput> Clean(IReadOnlyList<RecordedInput> events)
    {
        var clean = new List<RecordedInput>(events.Count);
        var keys = new HashSet<int>();
        var buttons = new HashSet<MouseButton>();
        foreach (var e in events)
        {
            bool keep = e.Kind switch
            {
                RecordedKind.KeyDown => keys.Add(e.Vk),       // false for auto-repeat
                RecordedKind.KeyUp => keys.Remove(e.Vk),      // false when the down wasn't recorded
                RecordedKind.MouseDown => buttons.Add(e.Button),
                RecordedKind.MouseUp => buttons.Remove(e.Button),
                _ => false,
            };
            if (keep) clean.Add(e);
        }

        // Still held at Stop (e.g. the click on the Stop button): drop their last, unmatched press.
        for (int i = clean.Count - 1; i >= 0 && (keys.Count > 0 || buttons.Count > 0); i--)
        {
            var e = clean[i];
            if ((e.Kind == RecordedKind.KeyDown && keys.Remove(e.Vk)) || (e.Kind == RecordedKind.MouseDown && buttons.Remove(e.Button)))
                clean.RemoveAt(i);
        }
        return clean;
    }

    // ------------------------------------------------------------------ hook thread

    void HookThread(ManualResetEventSlim ready, Action<Exception> fail)
    {
        IntPtr keyboardHook = IntPtr.Zero, mouseHook = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // creates the message queue Stop() posts WM_QUIT to
            var module = GetModuleHandle(null);
            _keyboardProc = KeyboardHook;
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook");
            if (_recordMouse)
            {
                _mouseProc = MouseHook;
                mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
                if (mouseHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse hook");
            }
        }
        catch (Exception e)
        {
            Unhook(keyboardHook, mouseHook);
            fail(e);
            ready.Set();
            return;
        }

        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        Unhook(keyboardHook, mouseHook);
    }

    void Unhook(IntPtr keyboardHook, IntPtr mouseHook)
    {
        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        _keyboardProc = null;
        _mouseProc = null;
    }

    IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((data.Flags & LLKHF_INJECTED) == 0)
            {
                var kind = (data.Flags & LLKHF_UP) != 0 ? RecordedKind.KeyUp : RecordedKind.KeyDown;
                Add(new RecordedInput(kind, Elapsed(data.Time), (int)data.VkCode));
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((data.Flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) == 0 && ButtonEvent((int)wParam, data.MouseData) is var (button, down))
            {
                Add(down
                    ? RecordedInput.MouseDown(button, data.X, data.Y, Elapsed(data.Time))
                    : RecordedInput.MouseUp(button, Elapsed(data.Time)));
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    static (MouseButton Button, bool Down)? ButtonEvent(int message, uint mouseData) => message switch
    {
        0x0201 => (MouseButton.Left, true),
        0x0202 => (MouseButton.Left, false),
        0x0204 => (MouseButton.Right, true),
        0x0205 => (MouseButton.Right, false),
        0x0207 => (MouseButton.Middle, true),
        0x0208 => (MouseButton.Middle, false),
        0x020B or 0x020C => ((mouseData >> 16) == 2 ? MouseButton.X2 : MouseButton.X1, message == 0x020B),
        _ => null,
    };

    long Elapsed(uint time) => Math.Max(0, unchecked((int)(time - _startTick)));

    void Add(RecordedInput input)
    {
        lock (_gate) _events.Add(input);
    }

    // ------------------------------------------------------------------ interop

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;
    const uint LLMHF_INJECTED = 0x1, LLMHF_LOWER_IL_INJECTED = 0x2;
    const uint WM_QUIT = 0x0012, PM_NOREMOVE = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);
}
