using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Darkmount.App.Macros;

/// <summary>
/// <see cref="IMacroTimer"/> using a high-resolution waitable timer (≈1 ms accuracy instead of the default 15.6 ms
/// timer tick, and without raising the system timer resolution). Falls back to a normal wait if unavailable.
/// </summary>
public sealed class PreciseTimer : IMacroTimer
{
    public bool Wait(int milliseconds, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        if (milliseconds <= 0) return true;
        using var timer = TryCreate(milliseconds);
        if (timer is null) return !token.WaitHandle.WaitOne(milliseconds);
        return WaitHandle.WaitAny([timer, token.WaitHandle]) == 0;
    }

    static TimerWaitHandle? TryCreate(int milliseconds)
    {
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        long due = -10_000L * milliseconds; // relative, in 100 ns units
        if (!SetWaitableTimer(handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            handle.Dispose();
            return null;
        }
        return new TimerWaitHandle(handle);
    }

    sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    const uint TIMER_ALL_ACCESS = 0x1F0003;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetWaitableTimer(SafeWaitHandle hTimer, ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine, bool fResume);
}
