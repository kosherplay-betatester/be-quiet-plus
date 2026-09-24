namespace Darkmount.App;

/// <summary>What is happening besides games, for <see cref="AutoSwitcher"/>'s smart screens.</summary>
/// <param name="Now">Current time (drives the rotation and how long Now playing stays up).</param>
/// <param name="FocusTimerActive">The focus timer is counting (focus or break).</param>
/// <param name="NewTrack">A different song than last time started playing.</param>
public readonly record struct ScreenSignals(DateTime Now, bool FocusTimerActive = false, bool NewTrack = false);

/// <summary>
/// Picks the screen: Auto mode shows stats while a game runs and otherwise the default screen, the rotation, or a smart
/// screen (the focus timer while it runs, Now playing for a while after a song starts).
/// A manual choice (hotkey/tray) holds until the next game start or stop.
/// </summary>
public sealed class AutoSwitcher
{
    /// <summary>How long Now playing stays on the dock after a new song starts.</summary>
    public static readonly TimeSpan NowPlayingHold = TimeSpan.FromSeconds(20);

    bool? _lastInGame;
    ScreenKind? _manual;
    DateTime _nowPlayingUntil;

    public ScreenKind Current { get; private set; } = ScreenKind.Stats;

    public ScreenKind Update(bool inGame, AppSettings settings, ScreenSignals? signals = null)
    {
        if (_lastInGame is { } last && last != inGame) _manual = null;
        _lastInGame = inGame;
        var sig = signals ?? new ScreenSignals(DateTime.Now);
        if (sig.NewTrack) _nowPlayingUntil = sig.Now + NowPlayingHold;

        Current = _manual ?? settings.Mode switch
        {
            ScreenMode.Stats => ScreenKind.Stats,
            ScreenMode.Animation => ScreenKind.Animation,
            ScreenMode.NowPlaying => ScreenKind.NowPlaying,
            ScreenMode.Clock => ScreenKind.Clock,
            ScreenMode.Network => ScreenKind.Network,
            ScreenMode.FocusTimer => ScreenKind.FocusTimer,
            _ when inGame => ScreenKind.Stats,
            _ => Idle(settings, sig),
        };
        return Current;
    }

    ScreenKind Idle(AppSettings s, ScreenSignals sig)
    {
        if (s.SmartScreens && sig.FocusTimerActive) return ScreenKind.FocusTimer;
        if (s.SmartScreens && sig.Now < _nowPlayingUntil) return ScreenKind.NowPlaying;
        if (s.RotateScreens && s.Rotation.Count > 0)
        {
            long slot = (long)(sig.Now.Ticks / TimeSpan.FromSeconds(Math.Max(5, s.RotateSeconds)).Ticks);
            return s.Rotation[(int)(slot % s.Rotation.Count)];
        }
        return s.DefaultScreen;
    }

    /// <summary>The hotkey's order: Auto (dashboard) → Animation → be quiet! default screen → Auto.</summary>
    public static ScreenMode NextMode(ScreenMode mode) => mode switch
    {
        ScreenMode.Animation => ScreenMode.DockDefault,
        ScreenMode.DockDefault => ScreenMode.Auto,
        _ => ScreenMode.Animation,
    };

    /// <summary>Switches to the next screen until the game state changes.</summary>
    public void Cycle() => _manual = Current == ScreenKind.Stats ? ScreenKind.Animation : ScreenKind.Stats;

    public void ClearManual() => _manual = null;
}
