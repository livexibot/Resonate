using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Resonate.Themes;

namespace Resonate.App.Helpers;

/// <summary>
/// The one clock of a special look's scenery (<see cref="ThemeScene"/>):
/// <c>Time</c>, the seconds that go round every
/// <see cref="SceneWeather.LoopSeconds"/>, and <c>Gather</c>, the seconds
/// since the scene appeared, which stops once its decorations have gathered
/// (<see cref="SceneDecor.GatherSeconds"/>). Both sit in <see cref="Props"/>
/// for the layers' expressions. It ticks only while some layer wants it
/// (<see cref="Want"/>), on a timer every 15 ms (about 64 times a second),
/// so the window redraws for it that often, not at the display's refresh
/// rate, and the interface thread is not asked for a frame on every one;
/// paused time is left out.
/// </summary>
internal sealed class SceneClock
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(15);

    // The gathering runs a little past its end, so every expression that waits for it has arrived.
    private const double GatherLimit = SceneDecor.GatherSeconds + 1;

    private readonly HashSet<object> _wanted = [];
    private readonly DispatcherQueueTimer _timer;
    private long _lastTick;
    private double _time;
    private double _gather;
    private bool _ticking;

    public SceneClock(Compositor compositor)
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TickEvery;
        _timer.IsRepeating = true;
        Props = compositor.CreatePropertySet();
        Props.InsertScalar("Time", 0);
        Props.InsertScalar("Gather", 0);
    }

    /// <summary>Raised once when the decorations have all gathered.</summary>
    public event EventHandler? GatheredNow;

    /// <summary><c>Time</c> and <c>Gather</c>, in seconds.</summary>
    public CompositionPropertySet Props { get; }

    public double Time => _time;

    public double Gather => _gather;

    /// <summary>Whether the decorations have all gathered.</summary>
    public bool Gathered => _gather >= SceneDecor.GatherSeconds;

    /// <summary>Whether <paramref name="owner"/> needs the clock to tick; it ticks while anyone does.</summary>
    public void Want(object owner, bool want)
    {
        if (want)
        {
            _wanted.Add(owner);
        }
        else
        {
            _wanted.Remove(owner);
        }

        var tick = _wanted.Count > 0;
        if (tick == _ticking)
        {
            return;
        }

        _ticking = tick;

        // The handler only while ticking, like SlowClock's.
        _timer.Tick -= OnTick;
        if (tick)
        {
            _lastTick = Stopwatch.GetTimestamp();
            _timer.Tick += OnTick;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>Starts the gathering again, for a scene that has just appeared.</summary>
    public void Restart()
    {
        _gather = 0;
        Props.InsertScalar("Gather", 0);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        // At most a tenth of a second at once, so a stall is not a leap.
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastTick == 0 ? 0 : Math.Min(0.1, Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds);
        _lastTick = now;
        _time = (_time + seconds) % SceneWeather.LoopSeconds;
        Props.InsertScalar("Time", (float)_time);
        if (_gather < GatherLimit)
        {
            var before = _gather;
            _gather = Math.Min(GatherLimit, _gather + seconds);
            Props.InsertScalar("Gather", (float)_gather);
            if (before < SceneDecor.GatherSeconds && _gather >= SceneDecor.GatherSeconds)
            {
                GatheredNow?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
