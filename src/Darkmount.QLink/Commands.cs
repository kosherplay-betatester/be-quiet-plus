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

/// <summary>KEYBOARD (feature 7). 7 SetSnapTapConfig is deliberately absent. See docs/QLINK_KEYBOARD.md §1.</summary>
public static class KeyboardCommands
{
    public const byte GetLayout = 1, GetConfig = 2, SetConfig = 3, GetState = 4, SetState = 5, GetSnapTapConfig = 6;
}

/// <summary>LIGHTINGS (feature 16): only the commands the web app uses for the Dark Mount plus read-only ones (§2).</summary>
public static class LightingCommands
{
    public const byte GetLightingMode = 1, SetLightingMode = 2, GetLayersLayout = 3, GetLayerConfig = 5, SetLayerConfig = 6,
        GetGlobalLayers = 14;
}

/// <summary>BINDINGS (feature 17), §3.</summary>
public static class BindingCommands
{
    public const byte GetBindings = 1, SetBinding = 2, ClearBinding = 3, GetConfig = 4, SetConfig = 5;
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
        (Features.Keyboard, KeyboardCommands.GetLayout), (Features.Keyboard, KeyboardCommands.GetConfig),
        (Features.Keyboard, KeyboardCommands.SetConfig), (Features.Keyboard, KeyboardCommands.GetState),
        (Features.Keyboard, KeyboardCommands.SetState), (Features.Keyboard, KeyboardCommands.GetSnapTapConfig),
        (Features.Lightings, LightingCommands.GetLightingMode), (Features.Lightings, LightingCommands.SetLightingMode),
        (Features.Lightings, LightingCommands.GetLayersLayout), (Features.Lightings, LightingCommands.GetLayerConfig),
        (Features.Lightings, LightingCommands.SetLayerConfig), (Features.Lightings, LightingCommands.GetGlobalLayers),
        (Features.Bindings, BindingCommands.GetBindings), (Features.Bindings, BindingCommands.SetBinding),
        (Features.Bindings, BindingCommands.ClearBinding), (Features.Bindings, BindingCommands.GetConfig),
        (Features.Bindings, BindingCommands.SetConfig),
    ];

    public static bool IsAllowed(byte feature, byte command) => Allowed.Contains((feature, command));

}
