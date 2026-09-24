using Darkmount.Screens;

namespace Darkmount.App.Sources;

/// <summary>
/// A Pomodoro focus timer: Focus → Break (every <see cref="LongBreakEvery"/>th focus: Long break) → Ready. A break
/// starts automatically when a focus period ends; the next focus waits for <see cref="Start"/> so an unattended timer
/// never counts pomodoros nobody did. Time comes from the injected clock, so the state is a pure function of the
/// calls made and the times passed to <see cref="Info(DateTime)"/>. Thread-safe.
/// </summary>
public sealed class PomodoroTimer
{
    private enum Stage { Focus, Break, LongBreak }
    private enum RunState { Ready, Running, Paused }

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    private TimeSpan _focus = TimeSpan.FromMinutes(25), _break = TimeSpan.FromMinutes(5), _longBreak = TimeSpan.FromMinutes(15);
    private int _longBreakEvery = 4;

    private Stage _stage = Stage.Focus;
    private RunState _run = RunState.Ready;
    private DateTime _endsAt;          // while running
    private TimeSpan _remaining;       // while paused
    private TimeSpan _total;           // length of the current phase
    private int _focusInCycle;         // completed focus periods since the last long break
    private int _completed;            // completed focus periods on _completedDate
    private DateTime _completedDate;
    private string _reportedPhase = PomodoroInfo.Ready;

    /// <param name="clock">Time source (default <see cref="DateTime.Now"/>); tests inject a fake clock.</param>
    public PomodoroTimer(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>
    /// Raised with the new displayed phase ("Focus", "Break", "Long break", "Paused", "Ready") whenever it changes —
    /// from a control call or, for timed transitions, from the <see cref="Info()"/> call that notices them. Raised outside
    /// the timer's lock; exceptions from handlers are swallowed so polling never throws.
    /// </summary>
    public event Action<string>? PhaseChanged;

    public TimeSpan FocusDuration
    {
        get { lock (_gate) return _focus; }
        set { ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero); lock (_gate) _focus = value; }
    }

    public TimeSpan BreakDuration
    {
        get { lock (_gate) return _break; }
        set { ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero); lock (_gate) _break = value; }
    }

    public TimeSpan LongBreakDuration
    {
        get { lock (_gate) return _longBreak; }
        set { ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero); lock (_gate) _longBreak = value; }
    }

    /// <summary>Every Nth completed focus period is followed by a long break (default 4).</summary>
    public int LongBreakEvery
    {
        get { lock (_gate) return _longBreakEvery; }
        set { ArgumentOutOfRangeException.ThrowIfLessThan(value, 1); lock (_gate) _longBreakEvery = value; }
    }

    public bool IsRunning { get { lock (_gate) return _run == RunState.Running; } }
    public bool IsPaused { get { lock (_gate) return _run == RunState.Paused; } }

    /// <summary>Starts a focus period when ready, resumes when paused; does nothing while running.</summary>
    public void Start() => Mutate(now =>
    {
        if (_run == RunState.Ready) Begin(Stage.Focus, now);
        else if (_run == RunState.Paused) ResumeAt(now);
    });

    /// <summary>Freezes the countdown.</summary>
    public void Pause() => Mutate(now =>
    {
        if (_run != RunState.Running) return;
        _remaining = _endsAt > now ? _endsAt - now : TimeSpan.Zero;
        _run = RunState.Paused;
    });

    /// <summary>Continues a paused countdown.</summary>
    public void Resume() => Mutate(now =>
    {
        if (_run == RunState.Paused) ResumeAt(now);
    });

    /// <summary>Start / pause / resume in one control (for a hotkey or tray click).</summary>
    public void Toggle() => Mutate(now =>
    {
        switch (_run)
        {
            case RunState.Ready: Begin(Stage.Focus, now); break;
            case RunState.Running:
                _remaining = _endsAt > now ? _endsAt - now : TimeSpan.Zero;
                _run = RunState.Paused;
                break;
            case RunState.Paused: ResumeAt(now); break;
        }
    });

    /// <summary>Stops and returns to Ready with a fresh focus period and a new long-break cycle. Today's count is kept.</summary>
    public void Reset() => Mutate(_ =>
    {
        _stage = Stage.Focus;
        _run = RunState.Ready;
        _focusInCycle = 0;
    });

    /// <summary>
    /// Ends the current phase now and starts the next one: focus → break (a skipped focus is not counted),
    /// break → focus.
    /// </summary>
    public void Skip() => Mutate(now =>
    {
        if (_stage == Stage.Focus) Begin(Stage.Break, now);
        else Begin(Stage.Focus, now);
    });

    /// <summary>Current state using the injected clock.</summary>
    public PomodoroInfo Info() => Info(_clock());

    /// <summary>State at <paramref name="now"/>, first applying any phase transitions that are due.</summary>
    public PomodoroInfo Info(DateTime now) => Mutate(now, n => Snapshot(n));

    // ---------------------------------------------------------------- core

    private void Mutate(Action<DateTime> action) => Mutate(_clock(), now => { action(now); return 0; });

    private T Mutate<T>(DateTime now, Func<DateTime, T> action)
    {
        var events = new List<string>(2);
        T result;
        lock (_gate)
        {
            Advance(now, events);
            result = action(now);
            Report(events);
        }
        foreach (var phase in events)
        {
            try { PhaseChanged?.Invoke(phase); }
            catch (Exception) { /* a faulty handler must not break the timer or the caller's polling */ }
        }
        return result;
    }

    /// <summary>Applies due transitions. Phases chain from their exact end time, so late polling loses nothing.</summary>
    private void Advance(DateTime now, List<string> events)
    {
        for (int guard = 0; _run == RunState.Running && now >= _endsAt && guard < 8; guard++)
        {
            var endedAt = _endsAt;
            if (_stage == Stage.Focus)
            {
                CountCompleted(endedAt);
                _focusInCycle++;
                if (_focusInCycle >= _longBreakEvery)
                {
                    _focusInCycle = 0;
                    Begin(Stage.LongBreak, endedAt);
                }
                else
                {
                    Begin(Stage.Break, endedAt);
                }
            }
            else
            {
                _stage = Stage.Focus;
                _run = RunState.Ready;
            }
            Report(events);
        }
    }

    private void Begin(Stage stage, DateTime at)
    {
        _stage = stage;
        _total = stage switch { Stage.Focus => _focus, Stage.Break => _break, _ => _longBreak };
        _endsAt = at + _total;
        _run = RunState.Running;
    }

    private void ResumeAt(DateTime now)
    {
        _endsAt = now + _remaining;
        _run = RunState.Running;
    }

    private void CountCompleted(DateTime at)
    {
        if (at.Date != _completedDate)
        {
            _completedDate = at.Date;
            _completed = 0;
        }
        _completed++;
    }

    private string DisplayPhase => _run switch
    {
        RunState.Ready => PomodoroInfo.Ready,
        RunState.Paused => PomodoroInfo.Paused,
        _ => StageName(_stage),
    };

    private static string StageName(Stage s) => s switch
    {
        Stage.Focus => PomodoroInfo.Focus,
        Stage.Break => PomodoroInfo.Break,
        _ => PomodoroInfo.LongBreak,
    };

    private void Report(List<string> events)
    {
        string phase = DisplayPhase;
        if (phase == _reportedPhase) return;
        _reportedPhase = phase;
        events.Add(phase);
    }

    private PomodoroInfo Snapshot(DateTime now)
    {
        int today = now.Date == _completedDate ? _completed : 0;
        return _run switch
        {
            RunState.Ready => new PomodoroInfo(PomodoroInfo.Ready, _focus, _focus, today),
            RunState.Paused => new PomodoroInfo(PomodoroInfo.Paused, _remaining, _total, today),
            _ => new PomodoroInfo(StageName(_stage), _endsAt > now ? _endsAt - now : TimeSpan.Zero, _total, today),
        };
    }
}
