using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Resonate.Themes;

namespace Resonate.App.Helpers;

/// <summary>
/// Ticks <see cref="SlowDrift.FramesPerSecond"/> times a second on the
/// interface thread while running, with the seconds it has run (paused time
/// left out), for slow, soft motion that would otherwise be a composition
/// animation that never ends (see <see cref="SlowDrift"/>). The window then
/// redraws for it that often, not at the display's refresh rate.
/// </summary>
internal sealed class SlowClock
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Action<double> _tick;
    private long _startedAt;
    private double _before;

    /// <param name="tick">Shows the motion at the seconds given; called on each tick.</param>
    public SlowClock(DispatcherQueue queue, Action<double> tick)
    {
        _tick = tick;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.0 / SlowDrift.FramesPerSecond);
        _timer.IsRepeating = true;
    }

    public bool IsRunning { get; private set; }

    /// <summary>The seconds run so far, paused time left out.</summary>
    public double Seconds => IsRunning ? _before + Stopwatch.GetElapsedTime(_startedAt).TotalSeconds : _before;

    /// <summary>Carries on from where it paused.</summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _startedAt = Stopwatch.GetTimestamp();

        // The handler only while running: one left on the timer would keep its owner (and the page around it) in memory.
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Holds the motion where it is.</summary>
    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _before = Seconds;
        IsRunning = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    /// <summary>Pauses and goes back to the start.</summary>
    public void Reset()
    {
        Pause();
        _before = 0;
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => _tick(Seconds);
}
