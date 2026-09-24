using Darkmount.Dock;
using Darkmount.QLink;

namespace Darkmount.App;

public sealed class KeyboardUnavailableException(string message) : Exception(message);

/// <summary>
/// Runs keyboard commands (lighting, bindings, display keys…) on a background thread over the shared
/// session, so settings pages never block the UI and never collide with dock frame uploads.
/// </summary>
public sealed class KeyboardService(DockConnection dock)
{
    /// <summary>Why keyboard settings can't be edited right now, or null when they can.</summary>
    public string? UnavailableReason => dock.State switch
    {
        DockState.Connected or DockState.Ready or DockState.NoMediaDock => null,
        DockState.PausedForIoCenter => "IO Center is running. Close it to edit keyboard settings here.",
        DockState.PausedByUser => "Darkmount Hub is paused. Resume it from the tray menu to edit keyboard settings.",
        DockState.PausedForOtherApp => "Another app (e.g. IO Center Web) is using the keyboard.",
        _ => "The keyboard is not connected.",
    };

    public Task<T> Run<T>(Func<QLinkClient, T> work) => Task.Run(() =>
    {
        if (UnavailableReason is { } reason) throw new KeyboardUnavailableException(reason);
        T result = default!;
        if (!dock.TryExecute(q => result = work(q)))
            throw new KeyboardUnavailableException(UnavailableReason ?? "The keyboard stopped responding.");
        return result;
    });

    public Task Run(Action<QLinkClient> work) => Run<bool>(q => { work(q); return true; });
}
