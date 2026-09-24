namespace Darkmount.QLink;

public static class Features
{
    public const byte Root = 1, Dfu = 2, DeviceInfo = 3, Storage = 4, Hub = 5, UsbDevice = 6, Keyboard = 7;
    public const byte Lightings = 16, Bindings = 17, Macros = 18, Numpad = 32, MediaDock = 33, KeyValueStorage = 52;
}

public static class RootCommands
{
    public const byte OpenSession = 1, CloseSession = 2, KeepAlive = 3, GetSupportedFeatures = 4, GetQLinkVersion = 5,
        GetActiveSessionInfo = 6, RequestStateChange = 7, SendStateChangeDecision = 8;
}

public static class RootNotifications
{
    public const byte SessionStateChanged = 1, ActiveSessionChanged = 2, StateChangeRequested = 3, StateChangeDecision = 4;
}

public static class DeviceInfoCommands
{
    public const byte GetDeviceInfo = 1, GetSerialNumber = 2;
}

public static class MediaDockCommands
{
    public const byte GetState = 1, GetConfig = 2, SetConfig = 3, GetDateTime = 4, SetDateTime = 5, GetImage = 6, SetImage = 7;
}

public static class NumpadCommands
{
    public const byte GetState = 1, SetImage = 2, GetImage = 3;
}

public enum QLinkStatus : byte
{
    Success = 0, InvalidSessionId, InvalidFeatureId, InvalidCommandId, InvalidRequestId, InvalidParameter, Timeout,
    Busy, NotAuthorized, NotActive, InvalidState, InvalidSize, InsufficientResources, NotSupported,
}

/// <summary>
/// The only (feature, command) pairs this app may send. Firmware update, factory reset, serial number,
/// raw storage, hub tunnelling, calibration and polling-rate commands are deliberately absent.
/// </summary>
public static class CommandAllowlist
{
    static readonly HashSet<(byte, byte)> Allowed =
    [
        (Features.Root, RootCommands.OpenSession), (Features.Root, RootCommands.CloseSession),
        (Features.Root, RootCommands.KeepAlive), (Features.Root, RootCommands.GetSupportedFeatures),
        (Features.Root, RootCommands.GetQLinkVersion), (Features.Root, RootCommands.GetActiveSessionInfo),
        (Features.Root, RootCommands.RequestStateChange), (Features.Root, RootCommands.SendStateChangeDecision),
        (Features.DeviceInfo, DeviceInfoCommands.GetDeviceInfo), (Features.DeviceInfo, DeviceInfoCommands.GetSerialNumber),
        (Features.MediaDock, MediaDockCommands.GetState), (Features.MediaDock, MediaDockCommands.GetConfig),
        (Features.MediaDock, MediaDockCommands.SetConfig), (Features.MediaDock, MediaDockCommands.GetDateTime),
        (Features.MediaDock, MediaDockCommands.SetDateTime), (Features.MediaDock, MediaDockCommands.GetImage),
        (Features.MediaDock, MediaDockCommands.SetImage),
        (Features.Numpad, NumpadCommands.GetState), (Features.Numpad, NumpadCommands.SetImage),
        (Features.Numpad, NumpadCommands.GetImage),
    ];

    public static bool IsAllowed(byte feature, byte command) => Allowed.Contains((feature, command));

    /// <summary>Adds more pairs (used by later phases, e.g. lighting); never DFU/factory commands.</summary>
    public static void Allow(byte feature, byte command)
    {
        if (feature is Features.Dfu or Features.Storage or Features.Hub or Features.KeyValueStorage
            || (feature == Features.DeviceInfo && command >= 3))
            throw new InvalidOperationException($"Command {feature}/{command} can never be allowed.");
        Allowed.Add((feature, command));
    }
}
