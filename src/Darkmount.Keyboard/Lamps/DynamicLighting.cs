using Microsoft.Win32;

namespace Darkmount.Keyboard.Lamps;

/// <summary>
/// Read-only view of Windows Dynamic Lighting (Settings → Personalization → Dynamic Lighting) for a LampArray device.
/// </summary>
/// <remarks>
/// <para>Windows' own LampArray client drives every compatible device while "Use Dynamic Lighting on my devices" is on:
/// it takes the device out of autonomous mode and streams its effect (or a foreground app's / the chosen background
/// controller's colours). Our direct HID feature reports would then fight Windows' reports. When the toggle is off,
/// Windows hands the device back to autonomous mode (its firmware effects) and leaves it alone.</para>
/// <para>How the app should cope: before taking control (<see cref="LampArrayDevice.SetAutonomousMode"/> false), read
/// <see cref="Read"/>; if <see cref="WindowsMayDrive"/> is true, ask the user to switch "Use Dynamic Lighting on my devices"
/// off (or turn it off for this device) and link to <see cref="SettingsUri"/>; never change the setting ourselves.
/// The supported alternative is to be the Dynamic Lighting <em>background controller</em> through
/// <c>Windows.Devices.Lights.LampArray</c>: that needs package identity (MSIX or sparse package) declaring the
/// <c>com.microsoft.windows.lighting</c> AppExtension, and <c>LampArray.IsAvailable</c>/<c>AvailabilityChanged</c> need
/// UniversalApiContract v15 (TFM 10.0.22621+; this solution targets 10.0.19041).</para>
/// <para>The registry values read here are undocumented (observed on Windows 11 build 26200); treat them as a hint.</para>
/// </remarks>
public sealed record DynamicLighting(bool? GlobalEnabled, bool? DeviceEnabled)
{
    public const string SettingsUri = "ms-settings:personalization-lighting";
    const string LightingKey = @"Software\Microsoft\Lighting";

    /// <summary>True when Windows Dynamic Lighting is (probably) driving the device.</summary>
    public bool WindowsMayDrive => GlobalEnabled == true && DeviceEnabled != false;

    /// <param name="hidDevicePath">HidSharp device path of the LampArray collection (\\?\hid#vid_…&amp;mi_03#…), optional.</param>
    public static DynamicLighting Read(string? hidDevicePath = null)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(LightingKey);
            bool? global = Flag(root);
            bool? device = null;
            if (root is not null && DeviceKeyName(hidDevicePath) is { } name)
            {
                using var deviceKey = root.OpenSubKey(@"Devices\" + name);
                device = Flag(deviceKey);
            }
            return new DynamicLighting(global, device);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return new DynamicLighting(null, null);
        }
    }

    /// <summary>"\\?\hid#vid_373f&amp;…#{guid}" → "hid#vid_373f&amp;…#{guid}" (registry key names are case-insensitive).</summary>
    public static string? DeviceKeyName(string? hidDevicePath)
    {
        if (string.IsNullOrWhiteSpace(hidDevicePath)) return null;
        var name = hidDevicePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? hidDevicePath[4..] : hidDevicePath;
        int suffix = name.IndexOf('\\');
        return suffix >= 0 ? name[..suffix] : name;
    }

    static bool? Flag(RegistryKey? key) => key?.GetValue("AmbientLightingEnabled") is int v ? v != 0 : null;
}
