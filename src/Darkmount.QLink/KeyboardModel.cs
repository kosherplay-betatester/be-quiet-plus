namespace Darkmount.QLink;

/// <summary>
/// be quiet! keyboards that speak QLink (from the IO Center Web device table, docs/QLINK_KEYBOARD.md).
/// The Light Mount models share the protocol but have no media dock and no display keys.
/// </summary>
public sealed record KeyboardModel(string Name, int ProductId, int? BootloaderProductId, bool HasMediaDock, bool HasDisplayKeys, bool HasNumpad)
{
    public static KeyboardModel DarkMount { get; } = new("Dark Mount", 0x0001, 0x0009, HasMediaDock: true, HasDisplayKeys: true, HasNumpad: true);
    public static KeyboardModel LightMount { get; } = new("Light Mount", 0x0002, 0x000A, HasMediaDock: false, HasDisplayKeys: false, HasNumpad: true);
    public static KeyboardModel LightMountTkl { get; } = new("Light Mount TKL", 0x0018, null, HasMediaDock: false, HasDisplayKeys: false, HasNumpad: false);
    public static KeyboardModel LightMountTkl2 { get; } = new("Light Mount TKL", 0x0029, null, HasMediaDock: false, HasDisplayKeys: false, HasNumpad: false);

    public const int VendorId = 0x373F;

    public static IReadOnlyList<KeyboardModel> All { get; } = [DarkMount, LightMount, LightMountTkl, LightMountTkl2];

    public static KeyboardModel? ForProductId(int pid) => All.FirstOrDefault(m => m.ProductId == pid);

    /// <summary>Firmware-update (bootloader) product ids: the app never talks to a keyboard in this mode.</summary>
    public static IReadOnlySet<int> BootloaderProductIds { get; } =
        All.Where(m => m.BootloaderProductId is not null).Select(m => m.BootloaderProductId!.Value).ToHashSet();
}
