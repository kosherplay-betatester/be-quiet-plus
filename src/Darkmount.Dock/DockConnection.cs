using System.Diagnostics;
using Darkmount.QLink;

namespace Darkmount.Dock;

public enum DockState
{
    /// <summary>Keyboard not found (or just lost); retrying.</summary>
    Disconnected,
    /// <summary>Session open, waiting for the first complete frame.</summary>
    Connected,
    /// <summary>Our frames are on the dock.</summary>
    Ready,
    PausedForIoCenter,
    PausedByUser,
    /// <summary>Another client (e.g. IO Center Web in a browser) holds the keyboard; we wait politely.</summary>
    PausedForOtherApp,
    /// <summary>Keyboard present but the media dock module is detached.</summary>
    NoMediaDock,
}

/// <summary>
/// Owns the keyboard connection: connect/reconnect, IO Center back-off, clock, keep-alive, config
/// backup/restore and frame presentation. <see cref="Tick"/> is called about once per second;
/// <see cref="Present"/> from the frame pipeline. Both are safe to call from different threads.
/// </summary>
public sealed class DockConnection(Func<IHidTransport?> openTransport, DockConfigGuard guard, Func<bool> ioCenterRunning)
    : IDisposable
{
    static readonly TimeSpan RetryAfterOtherApp = TimeSpan.FromSeconds(60);
    static readonly TimeSpan ClockInterval = TimeSpan.FromHours(1);

    readonly object _io = new();
    IHidTransport? _transport;
    QLinkClient? _client;
    MediaDock? _dock;
    FrameUploader? _uploader;
    DockConfig? _original;
    bool _needsRunningConfig;
    int _stalls;
    DateTime _lastClock, _lastTraffic, _otherAppSince, _resumeAt, _lastDockCheck;
    int _ticking;

    public DockState State { get; private set; } = DockState.Disconnected;
    public string? LastError { get; private set; }

    /// <summary>
    /// Dock idle delay while the app drives it. 1 s: each uploaded image counts as dock activity, so a longer
    /// delay with continuous refreshes keeps the dock on its menu (verified on hardware).
    /// </summary>
    public int IdleSeconds { get; set; } = 1;

    public int HeaderTimeoutMs { get; init; } = 8000;
    public int ChunkTimeoutMs { get; init; } = 5000;

    /// <summary>User-requested pause (tray menu). Applied on the next <see cref="Tick"/>.</summary>
    public bool Paused { get; set; }

    public event Action<DockState>? StateChanged;
    public event Action<string>? Log;

    public void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            if (!Monitor.TryEnter(_io)) return; // an upload is running; it keeps the session alive
            try { TickLocked(); }
            finally { Monitor.Exit(_io); }
        }
        finally { Volatile.Write(ref _ticking, 0); }
    }

    void TickLocked()
    {
        bool connected = _client is not null;
        if (ioCenterRunning()) { if (connected) Release(DockState.PausedForIoCenter); else SetState(DockState.PausedForIoCenter); return; }
        if (Paused) { if (connected) Release(DockState.PausedByUser); else SetState(DockState.PausedByUser); return; }
        if (State == DockState.PausedForOtherApp && DateTime.UtcNow - _otherAppSince < RetryAfterOtherApp) return;

        if (!connected) { Connect(); return; }

        try
        {
            if (_client!.PendingTakeoverSid is not null)
            {
                Log?.Invoke("Another app asked for the keyboard; handing it over");
                _client.AcceptPendingTakeover();
                Release(DockState.PausedForOtherApp, restore: false);
                _otherAppSince = DateTime.UtcNow;
                return;
            }
            if (State == DockState.NoMediaDock)
            {
                if (DateTime.UtcNow - _lastDockCheck > TimeSpan.FromSeconds(5) && TrySetUpDock()) Log?.Invoke("Media dock attached");
            }
            else if (DateTime.UtcNow - _lastClock > ClockInterval) { _dock!.SetDateTime(DateTime.Now); _lastClock = DateTime.UtcNow; }
            if (DateTime.UtcNow - _lastTraffic >= TimeSpan.FromMilliseconds(900)) { _client.KeepAlive(); _lastTraffic = DateTime.UtcNow; }
        }
        catch (Exception e) when (IsDeviceFailure(e)) { Lost(e); }
    }

    void Connect()
    {
        try
        {
            _transport = openTransport();
            if (_transport is null) { SetState(DockState.Disconnected); return; }

            _client = new QLinkClient(_transport);
            _client.Pump(200); // discard stale reports left in the OS buffer by earlier sessions
            _client.OpenSession();
            _stalls = 0;
            _resumeAt = default;
            if (!_client.IsActive && !TryBecomeActive()) return;

            _dock = new MediaDock(_client);
            _lastTraffic = DateTime.UtcNow;
            LastError = null;
            Log?.Invoke($"Connected to Dark Mount (session {_client.Sid})");
            // Without the media dock the session stays open for keyboard features; the dock is re-checked in Tick.
            if (!TrySetUpDock()) SetState(DockState.NoMediaDock);
        }
        catch (BootloaderModeException e) { LastError = e.Message; Drop(); SetState(DockState.Disconnected); }
        catch (Exception e) when (IsDeviceFailure(e)) { Lost(e); }
    }

    bool TrySetUpDock()
    {
        _lastDockCheck = DateTime.UtcNow;
        if (!_dock!.IsConnected()) return false;
        _original = guard.Resolve(_dock.GetConfig());
        _dock.SetDateTime(DateTime.Now);
        _lastClock = DateTime.UtcNow;
        _uploader = new FrameUploader(_dock) { HeaderTimeoutMs = HeaderTimeoutMs, ChunkTimeoutMs = ChunkTimeoutMs };
        _uploader.Log += m => Log?.Invoke(m);
        _needsRunningConfig = true;
        SetState(DockState.Connected);
        return true;
    }

    /// <summary>Takes the Active state only when no other client holds it (never steals from IO Center Web).</summary>
    bool TryBecomeActive()
    {
        var info = _client!.Send(Features.Root, RootCommands.GetActiveSessionInfo);
        byte activeSid = info.Length > 0 ? info[0] : (byte)0;
        if (activeSid != 0 && activeSid != _client.Sid)
        {
            Log?.Invoke($"Session {activeSid} (client type {(info.Length > 2 ? info[2] : 0)}) is using the keyboard; waiting");
            Drop();
            _otherAppSince = DateTime.UtcNow;
            SetState(DockState.PausedForOtherApp);
            return false;
        }
        if (_client.RequestActive()) return true;
        Drop();
        SetState(DockState.Disconnected);
        return false;
    }

    /// <summary>True while the dock is not answering image uploads (usually asleep: press a dock button).</summary>
    public bool DockUnresponsive => _stalls > 0;

    /// <summary>Frames uploaded successfully since start.</summary>
    public int FramesUploaded { get; private set; }

    /// <summary>Frames abandoned because the dock stopped answering.</summary>
    public int FramesStalled { get; private set; }

    /// <summary>Duration of the last successful upload.</summary>
    public TimeSpan LastUploadDuration { get; private set; }


    /// <summary>Uploads a full RGB565 frame. Returns false when the keyboard is not available or backing off.</summary>
    public bool Present(byte[] rgb565)
    {
        lock (_io)
        {
            if (_uploader is null || State is not (DockState.Connected or DockState.Ready)) return false;
            if (!ShowAppScreens) { ShowDockDefault(); return false; }
            if (DateTime.UtcNow < _resumeAt) return false;
            try
            {
                if (_uploader.Upload(rgb565) == UploadResult.Stalled)
                {
                    // Never hammer a busy or sleeping dock: back off 5, 10, 20, then 30 s between attempts.
                    _stalls++;
                    FramesStalled++;
                    var wait = TimeSpan.FromSeconds(Math.Min(30, 5 << Math.Min(_stalls - 1, 3)));
                    _resumeAt = DateTime.UtcNow + wait;
                    _client!.Pump(300); // swallow late replies
                    Log?.Invoke($"Backing off {wait.TotalSeconds:F0} s before the next frame");
                    return false;
                }
                if (_stalls > 0) Log?.Invoke($"Dock responsive again after {_stalls} stalled frame(s)");
                _stalls = 0;
                FramesUploaded++;
                LastUploadDuration = _uploader.LastDuration;
                if (FramesUploaded <= 5 || FramesUploaded % 30 == 0)
                    Log?.Invoke($"Frame {FramesUploaded} uploaded in {LastUploadDuration.TotalMilliseconds:F0} ms");
                _lastTraffic = DateTime.UtcNow;
                ShowingDockDefault = false;
                if (_needsRunningConfig)
                {
                    // Order matters: enable the screensaver only after a complete image exists.
                    var running = DockConfigGuard.Running(_original!, IdleSeconds);
                    _dock!.SetConfig(running);
                    Log?.Invoke($"Dock set to show our screen after {running.IdleSeconds} s idle ({Convert.ToHexString(running.ToBytes())})");
                    _needsRunningConfig = false;
                    SetState(DockState.Ready);
                }
                return true;
            }
            catch (QLinkException e) when (DockWentAway())
            {
                // The dock module was unplugged mid-frame: keep the keyboard session, wait for the dock to return.
                Log?.Invoke($"Media dock detached ({e.Status})");
                _uploader = null;
                _lastDockCheck = DateTime.UtcNow;
                SetState(DockState.NoMediaDock);
                return false;
            }
            catch (Exception e) when (IsDeviceFailure(e)) { Lost(e); return false; }
        }
    }

    /// <summary>
    /// False shows the dock's own be quiet! screen (the user's dock settings) while keeping the keyboard session
    /// for lighting and keys. Applied on the next <see cref="Present"/>.
    /// </summary>
    public bool ShowAppScreens { get; set; } = true;

    /// <summary>True while the dock shows its own screen because <see cref="ShowAppScreens"/> is false.</summary>
    public bool ShowingDockDefault { get; private set; }

    void ShowDockDefault()
    {
        if (State != DockState.Ready || _original is null) { ShowingDockDefault = State == DockState.Connected; return; }
        try
        {
            _dock!.SetConfig(_original);
            _needsRunningConfig = true; // our screensaver config is re-applied after the next complete frame
            ShowingDockDefault = true;
            Log?.Invoke("Showing the dock's own screen");
            SetState(DockState.Connected);
        }
        catch (Exception e) when (IsDeviceFailure(e)) { Lost(e); }
    }

    bool DockWentAway()
    {
        try { return _dock is not null && !_dock.IsConnected(); }
        catch (Exception e) when (IsDeviceFailure(e)) { return false; }
    }

    /// <summary>The user's own dock settings (restored on exit/pause), or null before the first connection.</summary>
    public DockConfig? UserDockConfig => _original ?? guard.Current;

    /// <summary>
    /// Saves new user dock settings (menu colour, clock, idle behaviour). While the app drives the dock the
    /// menu colour and clock format apply immediately; the rest applies when the app pauses or exits.
    /// </summary>
    public void UpdateUserDockConfig(DockConfig config)
    {
        lock (_io)
        {
            guard.Update(config);
            _original = config;
            if (_dock is null || State != DockState.Ready) return;
            try { _dock.SetConfig(DockConfigGuard.Running(config, IdleSeconds)); }
            catch (Exception e) when (IsDeviceFailure(e)) { Lost(e); }
        }
    }

    /// <summary>
    /// Runs keyboard commands (lighting, bindings, …) on the shared session, between frame uploads.
    /// Returns false when the keyboard is not connected.
    /// </summary>
    public bool TryExecute(Action<QLinkClient> action)
    {
        lock (_io)
        {
            if (_client is null) return false;
            try
            {
                action(_client);
                _lastTraffic = DateTime.UtcNow;
                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException or ObjectDisposedException)
            {
                Lost(e);
                return false;
            }
        }
    }

    /// <summary>Restores the user's dock settings and closes the session.</summary>
    void Release(DockState next, bool restore = true)
    {
        if (restore && _dock is not null && _original is not null)
        {
            try { _dock.SetConfig(_original); Log?.Invoke("Restored the dock settings"); }
            catch (Exception e) when (IsDeviceFailure(e)) { Log?.Invoke($"Could not restore dock settings: {e.Message}"); }
        }
        Drop();
        SetState(next);
    }

    void Lost(Exception e)
    {
        LastError = e.Message;
        Log?.Invoke($"Keyboard connection lost: {e.GetType().Name}: {e.Message}");
        Drop();
        SetState(DockState.Disconnected);
    }

    void Drop()
    {
        try { _client?.Dispose(); } catch (Exception) { /* device may be gone */ }
        try { _transport?.Dispose(); } catch (Exception) { }
        _client = null; _transport = null; _dock = null; _uploader = null;
    }

    static bool IsDeviceFailure(Exception e) =>
        e is TimeoutException or IOException or ObjectDisposedException or QLinkException or UnauthorizedAccessException
            or InvalidOperationException;

    void SetState(DockState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    public void Dispose()
    {
        lock (_io)
        {
            if (_client is not null) Release(DockState.Disconnected);
        }
    }
}

/// <summary>Detects the desktop IO Center app, which must own the keyboard while it runs.</summary>
public static class IoCenterDetector
{
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName("IO_Center");
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }
}
