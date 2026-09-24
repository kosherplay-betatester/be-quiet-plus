using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Darkmount.Sensors;

/// <summary>Values Windows provides without any monitoring tool.</summary>
public static class SystemInfo
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static readonly Lazy<double?> s_vramTotalMb = new(ReadVramTotalMb);

    /// <summary>Installed physical memory visible to Windows, in MB (null on failure).</summary>
    public static double? RamTotalMb => MemoryStatus() is { } m ? m.ullTotalPhys / 1048576.0 : null;

    /// <summary>Physical memory in use, in MB (null on failure).</summary>
    public static double? RamUsedMb => MemoryStatus() is { } m ? (m.ullTotalPhys - m.ullAvailPhys) / 1048576.0 : null;

    /// <summary>Dedicated memory of the largest display adapter, in MB (read once from the registry).</summary>
    public static double? VramTotalMb => s_vramTotalMb.Value;

    /// <summary>Process id owning the foreground window, or 0.</summary>
    public static int ForegroundProcessId()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0) return 0;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
        catch
        {
            return 0;
        }
    }

    private static MEMORYSTATUSEX? MemoryStatus()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m) ? m : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadVramTotalMb()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null) return null;

            long best = 0;
            foreach (string sub in classKey.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsAsciiDigit)) continue;
                try
                {
                    using var adapter = classKey.OpenSubKey(sub);
                    if (adapter is null) continue;
                    long size = ToLong(adapter.GetValue("HardwareInformation.qwMemorySize"))
                        ?? ToLong(adapter.GetValue("HardwareInformation.MemorySize"))
                        ?? 0;
                    best = Math.Max(best, size);
                }
                catch
                {
                    // inaccessible adapter key — skip
                }
            }
            return best > 0 ? best / 1048576.0 : null;
        }
        catch
        {
            return null;
        }
    }

    private static long? ToLong(object? value) => value switch
    {
        long l => l,
        int i => (uint)i,
        byte[] { Length: >= 8 } b => BitConverter.ToInt64(b, 0),
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
