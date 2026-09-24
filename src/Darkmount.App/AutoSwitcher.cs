namespace Darkmount.App;

/// <summary>
/// Picks the screen: Auto mode shows stats while a game runs and the default screen otherwise.
/// A manual choice (hotkey/tray) holds until the next game start or stop.
/// </summary>
public sealed class AutoSwitcher
{
    bool? _lastInGame;
    ScreenKind? _manual;

    public ScreenKind Current { get; private set; } = ScreenKind.Stats;

    public ScreenKind Update(bool inGame, AppSettings settings)
    {
        if (_lastInGame is { } last && last != inGame) _manual = null;
        _lastInGame = inGame;

        Current = _manual ?? settings.Mode switch
        {
            ScreenMode.Stats => ScreenKind.Stats,
            ScreenMode.Animation => ScreenKind.Animation,
            _ => inGame ? ScreenKind.Stats : settings.DefaultScreen,
        };
        return Current;
    }

    /// <summary>The hotkey's order: Auto (dashboard) → Animation → be quiet! default screen → Auto.</summary>
    public static ScreenMode NextMode(ScreenMode mode) => mode switch
    {
        ScreenMode.Auto or ScreenMode.Stats => ScreenMode.Animation,
        ScreenMode.Animation => ScreenMode.DockDefault,
        _ => ScreenMode.Auto,
    };

    /// <summary>Switches to the next screen until the game state changes.</summary>
    public void Cycle() => _manual = Current == ScreenKind.Stats ? ScreenKind.Animation : ScreenKind.Stats;

    public void ClearManual() => _manual = null;
}
