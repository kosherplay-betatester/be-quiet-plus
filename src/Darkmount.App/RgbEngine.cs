using System.Diagnostics;
using Darkmount.Dock;
using Darkmount.Keyboard.Lamps;

namespace Darkmount.App;

/// <summary>
/// Drives the keyboard's 201 LEDs through the standard HID LampArray interface: host RGB animations at up to 30 fps
/// and a red flash while an alert is active. Whenever it is off (or IO Center / Windows Dynamic Lighting owns the
/// lights) it hands the lighting back to the keyboard's own effect.
/// </summary>
public sealed class RgbEngine : IDisposable
{
    readonly Func<AppSettings> _settings;
    readonly Func<double?> _cpuTemp;
    readonly Func<bool> _alertActive;
    readonly Thread _thread;
    volatile bool _stop;

    LampArrayDevice? _device;
    IReadOnlyList<LampPoint> _layout = [];
    List<(int First, int Last)> _edgeRuns = [];
    readonly Dictionary<int, LampColor> _sent = [];
    LampColor? _sentAll;
    bool _hostControl;

    public string Status { get; private set; } = "Off";
    public double Fps { get; private set; }

    public RgbEngine(Func<AppSettings> settings, Func<double?> cpuTemp, Func<bool> alertActive)
    {
        _settings = settings;
        _cpuTemp = cpuTemp;
        _alertActive = alertActive;
        _thread = new Thread(Run) { IsBackground = true, Name = "RGB engine" };
    }

    public void Start() => _thread.Start();

    void Run()
    {
        var clock = Stopwatch.StartNew();
        var fpsWindow = Stopwatch.StartNew();
        int frames = 0;
        while (!_stop)
        {
            try
            {
                var s = _settings();
                bool alert = s.RgbAlertFlash && _alertActive();
                bool wanted = s.RgbEnabled || alert;

                if (!wanted || IoCenterDetector.IsRunning())
                {
                    HandBack(IoCenterDetector.IsRunning() ? "Paused: IO Center is running" : "Off (keyboard effect)");
                    Thread.Sleep(250);
                    continue;
                }
                if (!EnsureDevice()) { Thread.Sleep(2000); continue; }

                if (!_hostControl) { _device!.SetAutonomousMode(false); _hostControl = true; _sent.Clear(); _sentAll = null; }

                var frame = alert
                    ? new RgbFrame((clock.ElapsedMilliseconds / 350) % 2 == 0 ? new LampColor(255, 0, 0) : new LampColor(40, 0, 0), null, null)
                    : RgbEffects.Render(s.Rgb, clock.Elapsed.TotalSeconds, _layout, _cpuTemp());
                Send(frame);
                Status = alert ? "Alert flash" : $"{s.Rgb.Effect} animation";

                frames++;
                if (fpsWindow.ElapsedMilliseconds >= 2000)
                {
                    Fps = frames * 1000.0 / fpsWindow.ElapsedMilliseconds;
                    frames = 0;
                    fpsWindow.Restart();
                }
            }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException or InvalidOperationException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Write($"RGB engine: {e.Message}");
                CloseDevice(handBack: false);
                Status = "Keyboard lighting not available";
                Thread.Sleep(2000);
            }
        }
        HandBack("Off");
    }

    bool EnsureDevice()
    {
        if (_device is not null) return true;
        var device = LampArrayDevice.Open();
        if (device is null) { Status = "Keyboard lighting interface not found"; return false; }
        if (device.DevicePath is { } path && DynamicLighting.Read(path).WindowsMayDrive)
        {
            device.Dispose();
            Status = "Windows Dynamic Lighting controls the keyboard: turn off \"Use Dynamic Lighting on my devices\"";
            return false;
        }
        var lamps = device.Lamps;
        _layout = RgbEffects.Layout(lamps, device.Map);
        _edgeRuns = Runs(_layout.Where(p => !p.IsKey).Select(p => p.LampId));
        _device = device;
        Log.Write($"RGB engine: {lamps.Count} lamps, {_layout.Count(p => p.IsKey)} keys, {_edgeRuns.Count} edge run(s)");
        return true;
    }

    /// <summary>Sends only what changed: one range for whole-keyboard colours, per-key deltas plus edge ranges otherwise.</summary>
    void Send(RgbFrame frame)
    {
        if (frame.All is { } all)
        {
            if (_sentAll == all) { Thread.Sleep(33); return; }
            _device!.SetFrame(new Dictionary<int, LampColor>(), [(0, _layout.Count - 1, all)]);
            _sentAll = all;
            _sent.Clear();
            return;
        }

        var changed = new Dictionary<int, LampColor>();
        foreach (var (id, c) in frame.Keys ?? new Dictionary<int, LampColor>())
            if (_sentAll is not null || !_sent.TryGetValue(id, out var old) || old != c) changed[id] = c;
        var ranges = new List<(int, int, LampColor)>();
        if (frame.Edges is { } edge && (_sentAll is not null || !_sent.TryGetValue(-1, out var oldEdge) || oldEdge != edge))
        {
            ranges.AddRange(_edgeRuns.Select(r => (r.First, r.Last, edge)));
            _sent[-1] = edge;
        }
        if (changed.Count == 0 && ranges.Count == 0) { Thread.Sleep(33); return; }
        _device!.SetFrame(changed, ranges);
        foreach (var (id, c) in changed) _sent[id] = c;
        _sentAll = null;
    }

    static List<(int First, int Last)> Runs(IEnumerable<int> ids)
    {
        var runs = new List<(int, int)>();
        foreach (int id in ids.Order())
        {
            if (runs.Count > 0 && runs[^1].Item2 == id - 1) runs[^1] = (runs[^1].Item1, id);
            else runs.Add((id, id));
        }
        return runs;
    }

    void HandBack(string status)
    {
        Status = status;
        CloseDevice(handBack: true);
    }

    void CloseDevice(bool handBack)
    {
        if (_device is null) return;
        try { if (handBack && _hostControl) _device.SetAutonomousMode(true); }
        catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException) { }
        _device.Dispose();
        _device = null;
        _hostControl = false;
        Fps = 0;
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
        CloseDevice(handBack: true);
    }
}
