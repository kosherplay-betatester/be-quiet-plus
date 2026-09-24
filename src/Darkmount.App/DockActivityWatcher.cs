using System.Runtime.InteropServices;

namespace Darkmount.App;

/// <summary>
/// Notices when the user turns the dock dial or presses a dock/media button: those reach Windows as consumer-control
/// (volume/media) input from the Dark Mount. Uses Raw Input in listen-only mode, so the keys keep working normally.
/// The frame pipeline pauses uploads briefly after such activity so the dock's own controls stay responsive.
/// </summary>
public sealed class DockActivityWatcher : NativeWindow, IDisposable
{
    const int WmInput = 0x00FF;
    const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007, RideVInputSink = 0x00000100;

    readonly Dictionary<IntPtr, bool> _isDarkMount = [];
    long _lastActivityTicks;

    public DockActivityWatcher()
    {
        CreateHandle(new CreateParams());
        var devices = new[]
        {
            new RawInputDevice { UsagePage = 0x0C, Usage = 0x01, Flags = RideVInputSink, Target = Handle }, // consumer control
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            Log.Write($"Raw input registration failed ({Marshal.GetLastWin32Error()}); dock pauses on use are off");
    }

    /// <summary>When the user last used the dial or a media/dock key on the Dark Mount (UTC).</summary>
    public DateTime LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);

    public bool ActiveWithin(TimeSpan span) => DateTime.UtcNow - LastActivityUtc < span;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmInput)
        {
            var device = SourceDevice(m.LParam);
            if (device != IntPtr.Zero && IsDarkMount(device))
                Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        }
        base.WndProc(ref m);
    }

    static IntPtr SourceDevice(IntPtr rawInput)
    {
        uint size = (uint)Marshal.SizeOf<RawInputHeader>();
        return GetRawInputData(rawInput, RidInput, out var header, ref size, (uint)Marshal.SizeOf<RawInputHeader>()) == uint.MaxValue
            ? IntPtr.Zero
            : header.Device;
    }

    bool IsDarkMount(IntPtr device)
    {
        if (_isDarkMount.TryGetValue(device, out bool known)) return known;
        uint chars = 0;
        GetRawInputDeviceInfo(device, RidiDeviceName, IntPtr.Zero, ref chars);
        bool match = false;
        if (chars > 0)
        {
            var buffer = Marshal.AllocHGlobal((int)chars * 2);
            try
            {
                if (GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref chars) > 0)
                    match = Marshal.PtrToStringUni(buffer)?.Contains("VID_373F&PID_0001", StringComparison.OrdinalIgnoreCase) == true;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return _isDarkMount[device] = match;
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)]
    struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr rawInput, uint command, out RawInputHeader data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
}
