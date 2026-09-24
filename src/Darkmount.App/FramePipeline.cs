using System.Diagnostics;
using Darkmount.Dock;
using Darkmount.Screens;
using Darkmount.Sensors;
using SkiaSharp;

namespace Darkmount.App;

/// <summary>
/// Background loop: sample sensors → pick screen → render 320×240 → upload when changed. Runs every
/// <see cref="AppSettings.RefreshMs"/> (the dock needs ~1.6 s per frame), sooner when a new alert fires.
/// </summary>
public sealed class FramePipeline : IDisposable
{
    readonly DockConnection _dock;
    readonly Func<AppSettings> _settings;
    readonly SensorHub _sensors;
    readonly MetricHistory _history = new();
    readonly AutoSwitcher _switcher = new();
    readonly AlertEngine _alerts;
    readonly StatsScreen _stats = new();
    readonly Thread _thread;
    readonly AutoResetEvent _wake = new(false);
    volatile bool _stop;

    AnimationScreen? _animation;
    (AnimationKind, string?) _animationKey;
    byte[]? _lastUploaded;
    bool _wasInGame;

    /// <summary>Raised after each render with a copy of the frame (the receiver disposes it).</summary>
    public event Action<SKBitmap>? FrameRendered;

    public Snapshot? LastSnapshot { get; private set; }
    public ScreenKind CurrentScreen => _switcher.Current;
    public AutoSwitcher Switcher => _switcher;

    public FramePipeline(DockConnection dock, Func<AppSettings> settings)
    {
        _dock = dock;
        _settings = settings;
        _sensors = new SensorHub(settings().Sensors);
        _alerts = new AlertEngine(settings().Alerts);
        _thread = new Thread(Run) { IsBackground = true, Name = "Dock frame pipeline", Priority = ThreadPriority.BelowNormal };
    }

    public void Start() => _thread.Start();

    /// <summary>Renders and uploads a new frame as soon as possible (screen switch, settings change).</summary>
    public void RefreshNow() => _wake.Set();

    void Run()
    {
        while (!_stop)
        {
            var started = Stopwatch.StartNew();
            try { RenderAndPresent(); }
            catch (Exception e) { Log.Write($"Frame pipeline error: {e}"); }

            // Wait for the next frame, checking for new alerts every 500 ms so they show immediately.
            int refresh = Math.Clamp(_settings().RefreshMs, 1500, 60000);
            while (!_stop && started.ElapsedMilliseconds < refresh)
            {
                if (_wake.WaitOne(500)) break;
                if (CheckForNewAlert()) break;
            }
        }
    }

    bool CheckForNewAlert()
    {
        try
        {
            _alerts.Evaluate(_sensors.Sample(), DateTime.Now);
            return _alerts.HasNewAlert;
        }
        catch (Exception e) { Log.Write($"Alert check failed: {e.Message}"); return false; }
    }

    void RenderAndPresent()
    {
        var settings = _settings();
        _alerts.Settings = settings.Alerts;

        var snapshot = _sensors.Sample();
        LastSnapshot = snapshot;
        if (_wasInGame && !snapshot.InGame) _history.ClearFps();
        _wasInGame = snapshot.InGame;
        _history.Add(snapshot);

        var alerts = _alerts.Evaluate(snapshot, DateTime.Now);
        var kind = _switcher.Update(snapshot.InGame, settings);
        IDockScreen screen = kind == ScreenKind.Animation ? Animation(settings) : _stats;

        var ctx = new ScreenContext { Snapshot = snapshot, History = _history, Alerts = alerts, Now = DateTime.Now };
        using var frame = DockRenderer.Render(screen, ctx);
        FrameRendered?.Invoke(frame.Copy());

        _dock.ShowAppScreens = settings.Mode != ScreenMode.DockDefault;
        if (_dock.State is not (DockState.Connected or DockState.Ready)) { _lastUploaded = null; return; }

        var pixels = Rgb565.FromBitmap(frame);
        if (!_dock.ShowAppScreens) { _dock.Present(pixels); _lastUploaded = null; return; } // releases the screen
        if (_dock.State == DockState.Ready && _lastUploaded is not null && pixels.AsSpan().SequenceEqual(_lastUploaded)) return;
        if (_dock.Present(pixels)) _lastUploaded = pixels;
    }

    AnimationScreen Animation(AppSettings s)
    {
        var key = (s.AnimationKind, s.AnimationPath);
        if (_animation is null || _animationKey != key)
        {
            _animation?.Dispose();
            _animation = new AnimationScreen(AnimationSources.Create(s.AnimationKind, s.AnimationPath));
            _animationKey = key;
        }
        return _animation;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(5));
        _animation?.Dispose();
        _sensors.Dispose();
        _wake.Dispose();
    }
}
