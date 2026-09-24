using System.Diagnostics;
using System.Drawing.Drawing2D;
using Darkmount.Dock;
using Darkmount.QLink;
using Darkmount.Screens;

namespace Darkmount.App;

/// <summary>Tray icon, menu and the lifetime of the dock connection and frame pipeline.</summary>
public sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _status = new() { Enabled = false };
    readonly ToolStripMenuItem _auto = new("Auto (stats in games)"), _stats = new("Stats"), _anim = new("Animation"),
        _dockDefault = new("be quiet! default screen");
    readonly ToolStripMenuItem _pause = new("Pause dock"), _autostart = new("Start with Windows");
    readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 1000 };
    readonly System.Threading.Timer _tickTimer;
    readonly HotkeyWindow _hotkey = new();
    readonly DockConnection _dock;
    readonly SynchronizationContext _ui;
    readonly KeyboardService _keyboard;
    readonly DockActivityWatcher _dockActivity = new();

    AppSettings _settings;
    FramePipeline _pipeline;
    SettingsForm? _settingsForm;
    bool _toldToWake;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = SettingsStore.Load(SettingsStore.DefaultPath);

        _dock = new DockConnection(OpenKeyboard, new DockConfigGuard(DockConfigGuard.DefaultPath), IoCenterDetector.IsRunning);
        _dock.Log += Log.Write;
        _dock.StateChanged += s => _ui.Post(_ => OnDockState(s), null);

        _pipeline = new FramePipeline(_dock, () => _settings) { DockInUse = _dockActivity.ActiveWithin };
        _pipeline.FrameRendered += OnFrame;
        _keyboard = new KeyboardService(_dock);

        _tray = new NotifyIcon { Icon = CreateIcon(), Text = "Darkmount Hub", Visible = true, ContextMenuStrip = BuildMenu() };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _hotkey.Pressed += CycleScreen;
        if (!_hotkey.Register(_settings.Hotkey)) Log.Write($"Hotkey '{_settings.Hotkey}' could not be registered");

        if (_settings.StartWithWindows != Autostart.IsEnabled()) TrySetAutostart(_settings.StartWithWindows);

        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => _ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, executeOnlyOnce: true);

        _tickTimer = new System.Threading.Timer(_ => _dock.Tick(), null, 0, 1000);
        _uiTimer.Tick += (_, _) => UpdateStatus();
        _uiTimer.Start();
        _pipeline.Start();
        Log.Write("Darkmount Hub started");
    }

    static IHidTransport? OpenKeyboard() => HidSharpTransport.TryOpen();

    ContextMenuStrip BuildMenu()
    {
        _auto.Click += (_, _) => SetMode(ScreenMode.Auto);
        _stats.Click += (_, _) => SetMode(ScreenMode.Stats);
        _anim.Click += (_, _) => SetMode(ScreenMode.Animation);
        _dockDefault.Click += (_, _) => SetMode(ScreenMode.DockDefault);

        var animations = new ToolStripMenuItem("Animation source");
        foreach (var kind in new[] { AnimationKind.Plasma, AnimationKind.Matrix, AnimationKind.Starfield })
            animations.DropDownItems.Add(kind.ToString(), null, (_, _) => SetAnimation(kind, null));
        animations.DropDownItems.Add(new ToolStripSeparator());
        animations.DropDownItems.Add("GIF or video file…", null, (_, _) => ChooseAnimationFile());
        animations.DropDownItems.Add("Folder of pictures…", null, (_, _) => ChooseAnimationFolder());

        _pause.Click += (_, _) => { _dock.Paused = !_dock.Paused; _pause.Checked = _dock.Paused; _dock.Tick(); };
        _autostart.Click += (_, _) =>
        {
            _settings.StartWithWindows = !_settings.StartWithWindows;
            TrySetAutostart(_settings.StartWithWindows);
            SaveSettings();
        };

        _menu.Items.AddRange([
            _status, new ToolStripSeparator(),
            _auto, _stats, _anim, _dockDefault, animations, new ToolStripSeparator(),
            _pause, new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()), _autostart,
            new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogs()), new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        ]);
        _menu.Opening += (_, _) => UpdateMenuChecks();
        return _menu;
    }

    void UpdateMenuChecks()
    {
        _auto.Checked = _settings.Mode == ScreenMode.Auto;
        _stats.Checked = _settings.Mode == ScreenMode.Stats;
        _anim.Checked = _settings.Mode == ScreenMode.Animation;
        _dockDefault.Checked = _settings.Mode == ScreenMode.DockDefault;
        _pause.Checked = _dock.Paused;
        _autostart.Checked = Autostart.IsEnabled();
        UpdateStatus();
    }

    void SetMode(ScreenMode mode)
    {
        _settings.Mode = mode;
        _pipeline.Switcher.ClearManual();
        SaveSettings();
        _pipeline.RefreshNow();
    }

    void SetAnimation(AnimationKind kind, string? path)
    {
        _settings.AnimationKind = kind;
        _settings.AnimationPath = path;
        if (_settings.Mode is ScreenMode.Stats or ScreenMode.DockDefault) _settings.Mode = ScreenMode.Animation;
        SaveSettings();
        _pipeline.RefreshNow();
    }

    void ChooseAnimationFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a GIF or video for the dock",
            Filter = "Animations and videos|*.gif;*.mp4;*.mkv;*.mov;*.wmv;*.avi;*.webm|All files|*.*",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var kind = Path.GetExtension(dlg.FileName).Equals(".gif", StringComparison.OrdinalIgnoreCase) ? AnimationKind.Gif : AnimationKind.Video;
        SetAnimation(kind, dlg.FileName);
    }

    void ChooseAnimationFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder of pictures to show on the dock" };
        if (dlg.ShowDialog() == DialogResult.OK) SetAnimation(AnimationKind.Folder, dlg.SelectedPath);
    }

    /// <summary>Hotkey: Auto (dashboard) → Animation → be quiet! default screen → Auto.</summary>
    void CycleScreen() => SetMode(AutoSwitcher.NextMode(_settings.Mode));

    void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false }) { _settingsForm.Activate(); return; }
        _settingsForm = new SettingsForm(_settings, ApplySettings, StatusReport, _keyboard, _dock);
        _settingsForm.Show();
    }

    string StatusReport()
    {
        var s = _pipeline.LastSnapshot;
        static string V(double? v, string unit, string fmt = "F0") => v is { } x ? x.ToString(fmt) + unit : "--";
        var lines = new List<string>
        {
            $"Dock           {_dock.State}{(_dock.DockUnresponsive ? " (not responding: press a dock button)" : "")}",
            $"Screen         {_pipeline.CurrentScreen}",
            $"Frames         {_dock.FramesUploaded} uploaded, {_dock.FramesStalled} stalled",
            $"Last upload    {(_dock.LastUploadDuration.TotalMilliseconds > 0 ? $"{_dock.LastUploadDuration.TotalMilliseconds:F0} ms" : "--")}",
            "",
            $"CPU            {V(s?.CpuTemp, " °C")}   {V(s?.CpuLoad, " %")}   {V(s?.CpuPower, " W")}",
            $"GPU            {V(s?.GpuTemp, " °C")}   {V(s?.GpuLoad, " %")}   {V(s?.GpuPower, " W")}",
            $"RAM            {V(s?.RamUsedMb / 1024, " GB", "F1")} / {V(s?.RamTotalMb / 1024, " GB", "F1")}",
            $"VRAM           {V(s?.VramUsedMb / 1024, " GB", "F1")} / {V(s?.VramTotalMb / 1024, " GB", "F1")}",
            $"Game           {s?.GameName ?? "none"}   FPS {V(s?.Fps, "")}   {s?.FpsLowLabel} {V(s?.FpsLow, "")}",
        };
        if (s?.Hints is { Count: > 0 } hints)
        {
            lines.Add("");
            lines.AddRange(hints.Select(h => "• " + h));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Applies settings saved in the settings window.</summary>
    void ApplySettings(AppSettings updated)
    {
        bool sensorsChanged = System.Text.Json.JsonSerializer.Serialize(updated.Sensors)
                              != System.Text.Json.JsonSerializer.Serialize(_settings.Sensors);
        _settings = updated;
        SaveSettings();
        if (!_hotkey.Register(updated.Hotkey))
            _tray.ShowBalloonTip(4000, "Darkmount Hub", $"The hotkey '{updated.Hotkey}' is not available.", ToolTipIcon.Warning);
        TrySetAutostart(updated.StartWithWindows);
        if (sensorsChanged) RestartPipeline();
        _pipeline.RefreshNow();
    }

    void RestartPipeline()
    {
        _pipeline.FrameRendered -= OnFrame;
        _pipeline.Dispose();
        _pipeline = new FramePipeline(_dock, () => _settings) { DockInUse = _dockActivity.ActiveWithin };
        _pipeline.FrameRendered += OnFrame;
        _pipeline.Start();
    }

    void OnFrame(SkiaSharp.SKBitmap frame)
    {
        if (_settingsForm is { IsDisposed: false } form) form.ShowPreview(frame);
        else frame.Dispose();
    }

    void OnDockState(DockState state)
    {
        Log.Write($"Dock state: {state}");
        UpdateStatus();
        if (state == DockState.Ready && !_toldToWake)
        {
            _toldToWake = true;
            _tray.ShowBalloonTip(5000, "Darkmount Hub is on the dock",
                "If the dock screen is dark, press a dock button once to wake it.", ToolTipIcon.Info);
        }
    }

    void UpdateStatus()
    {
        string state = _dock.State switch
        {
            DockState.Connected or DockState.Ready when _dock.DockUnresponsive => "Dock not responding: press a dock button to wake it",
            DockState.Connected when _dock.ShowingDockDefault => "Dock shows its be quiet! screen (keyboard features active)",
            DockState.Ready => $"Showing {_pipeline.CurrentScreen} on the dock",
            DockState.Connected => "Connecting to the dock…",
            DockState.PausedForIoCenter => "Paused: IO Center is running",
            DockState.PausedByUser => "Paused",
            DockState.PausedForOtherApp => "Paused: another app is using the keyboard",
            DockState.NoMediaDock => "Media dock not attached",
            _ => _dock.LastError is { } err ? $"Keyboard not available ({err})" : "Keyboard not found",
        };
        _status.Text = state;
        var hints = _pipeline.LastSnapshot?.Hints ?? [];
        var tip = hints.Count > 0 ? $"{state}\n{hints[0]}" : $"Darkmount Hub\n{state}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    void TrySetAutostart(bool enabled)
    {
        try { Autostart.Set(enabled); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"Autostart change failed: {e.Message}");
        }
    }

    void SaveSettings()
    {
        try { SettingsStore.Save(SettingsStore.DefaultPath, _settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"Saving settings failed: {e.Message}"); }
    }

    static void OpenLogs()
    {
        Directory.CreateDirectory(Log.Directory);
        Process.Start(new ProcessStartInfo("explorer.exe", Log.Directory) { UseShellExecute = true });
    }

    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(24, 26, 30));
            g.FillRectangle(body, 2, 5, 28, 22);
            using var border = new Pen(Color.FromArgb(255, 138, 31), 2);
            g.DrawRectangle(border, 2, 5, 27, 21);
            using var wave = new Pen(Color.FromArgb(76, 217, 100), 2.5f);
            g.DrawLines(wave, [new(5, 20), new(10, 14), new(15, 18), new(20, 10), new(26, 15)]);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        Log.Write("Exiting");
        _uiTimer.Stop();
        _tickTimer.Dispose();
        _pipeline.Dispose();
        _dock.Dispose();
        _hotkey.Dispose();
        _dockActivity.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm?.Close();
        base.ExitThreadCore();
    }
}
