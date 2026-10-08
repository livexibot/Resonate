using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Audio;

/// <summary>
/// Lends the Home stage's visualizer Spotify's sound (the owner's choice of
/// 8 October 2026): while the bars show, the user's switch is on and a
/// Spotify song plays, it hears the program that plays it on this PC (the
/// Spotify app, or Resonate's own player) through
/// <see cref="IAppSoundCapture"/>, evens out its loudness
/// (<see cref="SoundLeveller"/>) and hands it on. Only that program is
/// heard, never another app or a microphone; nothing is kept. When no sound
/// arrives (the music plays on a phone, or the program changed), it says so
/// through <see cref="HearsSound"/>, and after a few seconds looks for the
/// program again.
/// </summary>
public sealed class SpotifySoundListener : ISoundSink, IDisposable
{
    /// <summary>Sound that stops for this long means nothing is heard (the bars sway on their own again).</summary>
    public static readonly TimeSpan HearingHold = TimeSpan.FromSeconds(2);

    /// <summary>While nothing is heard, the program to hear is looked for again this often (Spotify restarted, the own player started).</summary>
    public static readonly TimeSpan LookAgainAfter = TimeSpan.FromSeconds(3);

    /// <summary>After Windows refused or the capture stopped by itself, it is tried again this much later.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    /// <summary>A sample this loud or louder counts as sound (about -72 dB); digital silence and dither do not.</summary>
    public const float LoudEnough = 1f / 4096;

    private readonly PlayerRouter _player;
    private readonly IAppSoundCapture _capture;
    private readonly Func<int?> _findProgram;
    private readonly ISoundSink _output;
    private readonly TimeProvider _time;
    private readonly Action<Action> _schedule;
    private readonly Lock _gate = new();
    private readonly SoundLeveller _leveller = new();
    private readonly ITimer _retry;
    private readonly Action _update;

    // Set from any thread without waiting; read under _gate.
    private volatile bool _wanted;
    private volatile bool _enabled = true;

    // Under _gate.
    private bool _disposed;
    private int? _program;
    private long _retryAt;

    // Read by the capture thread.
    private volatile bool _listening;
    private long _lastSound;
    private long _lookedAt;
    private int _hearing;
    private int _resetLeveller;
    private int _writing;
    private int _lookAgain;
    private int _updateQueued;
    private float[] _levelled = [];

    /// <param name="player">Says whether a Spotify song plays.</param>
    /// <param name="capture">Windows' process loopback, or a fake.</param>
    /// <param name="findProgram">The process that plays Spotify's sound on this PC now, or null when none does. May take a few milliseconds; never called on the interface thread.</param>
    /// <param name="output">Gets the levelled sound on the capture thread (the stage's analyser).</param>
    /// <param name="time">The clock (tests use a fake one).</param>
    /// <param name="schedule">Runs the work of looking for the program and starting or stopping the capture off the caller's thread; tests run it at once.</param>
    public SpotifySoundListener(
        PlayerRouter player,
        IAppSoundCapture capture,
        Func<int?> findProgram,
        ISoundSink output,
        TimeProvider? time = null,
        Action<Action>? schedule = null)
    {
        _player = player;
        _capture = capture;
        _findProgram = findProgram;
        _output = output;
        _time = time ?? TimeProvider.System;
        _schedule = schedule ?? (work => ThreadPool.UnsafeQueueUserWorkItem(static w => w(), work, preferLocal: false));
        _lookedAt = _time.GetTimestamp();
        _update = RunUpdate;
        _retry = _time.CreateTimer(static state => ((SpotifySoundListener)state!).OnRetry(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        player.StateChanged += OnStateChanged;
        capture.Failed += OnCaptureFailed;
    }

    /// <summary>Raised on any thread when <see cref="HearsSound"/> changes.</summary>
    public event EventHandler? HearingChanged;

    /// <summary>The bars show and want sound. May be set from any thread and never waits; the work happens elsewhere.</summary>
    public bool Wanted
    {
        get => _wanted;
        set
        {
            if (_wanted != value)
            {
                _wanted = value;
                QueueUpdate();
            }
        }
    }

    /// <summary>The user's switch ("Listen to Spotify"). May be set from any thread and never waits.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                QueueUpdate();
            }
        }
    }

    /// <summary>A program's sound is being captured.</summary>
    public bool IsListening => _listening;

    /// <summary>The process being heard, or null.</summary>
    public int? HeardProcessId
    {
        get
        {
            lock (_gate)
            {
                return _program;
            }
        }
    }

    /// <summary>Sound arrived within <see cref="HearingHold"/>: the bars can follow it.</summary>
    public bool HearsSound => Volatile.Read(ref _hearing) != 0;

    /// <summary>The program that plays Spotify's sound may have changed (the control mode switched, the own player started): look for it again.</summary>
    public void LookAgain()
    {
        Volatile.Write(ref _lookAgain, 1);
        QueueUpdate();
    }

    public void Dispose()
    {
        _player.StateChanged -= OnStateChanged;
        _capture.Failed -= OnCaptureFailed;
        lock (_gate)
        {
            _disposed = true;
            _retry.Dispose();
            Update();
        }
    }

    /// <summary>The capture thread: one block of the program's sound, or an empty one when nothing came for a while.</summary>
    public void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (!_listening)
        {
            return;
        }

        // One writer at a time: an old capture's last block while a new one starts is dropped, not waited for.
        if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var now = _time.GetTimestamp();
            if (IsLoud(interleaved))
            {
                Volatile.Write(ref _lastSound, now);
            }

            var last = Volatile.Read(ref _lastSound);
            var hearing = last != 0 && _time.GetElapsedTime(last, now) < HearingHold;
            SetHearing(hearing);
            if (!hearing && _time.GetElapsedTime(Volatile.Read(ref _lookedAt), now) >= LookAgainAfter)
            {
                // Silence for a while: Spotify may have restarted, or another program plays now.
                Volatile.Write(ref _lookedAt, now);
                LookAgain();
            }

            if (interleaved.IsEmpty || channels <= 0 || sampleRate <= 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _resetLeveller, 0) != 0)
            {
                _leveller.Reset();
            }

            if (_levelled.Length < interleaved.Length)
            {
                // Only while the first blocks come in, or if Windows sends larger ones later.
                _levelled = new float[Math.Max(interleaved.Length, 2048)];
            }

            var levelled = _levelled.AsSpan(0, interleaved.Length);
            _leveller.Apply(interleaved, levelled, channels, sampleRate);
            _output.Write(levelled, channels, sampleRate);
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
        }
    }

    private static bool IsLoud(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            if (MathF.Abs(sample) >= LoudEnough)
            {
                return true;
            }
        }

        return false;
    }

    private void OnStateChanged(object? sender, EventArgs e) => QueueUpdate();

    private void OnCaptureFailed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_listening)
            {
                return;
            }

            _listening = false;
            _program = null;
            WaitBeforeRetry();
        }

        SetHearing(false);
    }

    // Under _gate. A song that just plays on brings no news to wake the listener, so a timer does.
    private void WaitBeforeRetry()
    {
        _retryAt = _time.GetTimestamp() + (long)(RetryAfterFailure.TotalSeconds * _time.TimestampFrequency);
        if (!_disposed)
        {
            _retry.Change(RetryAfterFailure, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnRetry()
    {
        lock (_gate)
        {
            _retryAt = 0;
        }

        QueueUpdate();
    }

    // State changes come often and from several threads; the newest state is looked at once.
    private void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            _schedule(_update);
        }
    }

    private void RunUpdate()
    {
        lock (_gate)
        {
            // Cleared under the lock: a change during this update queues the next one.
            Volatile.Write(ref _updateQueued, 0);
            try
            {
                Update();
            }
            catch (Exception)
            {
                // Looking for the program or starting the capture failed (a thread pool thread: nothing may escape).
                // The bars sway on their own, and it is tried again later.
                var listening = _listening;
                _listening = false;
                _program = null;
                if (listening)
                {
                    _capture.Stop();
                }

                SetHearing(false);
                WaitBeforeRetry();
            }
        }
    }

    // Under _gate.
    private void Update()
    {
        var lookAgain = Interlocked.Exchange(ref _lookAgain, 0) != 0;
        var listen = !_disposed
            && _wanted
            && _enabled
            && _capture.IsSupported
            && _player.ActiveSource == PlaybackSource.Spotify
            && _player.State.IsPlaying;
        if (!listen)
        {
            StopListening();
            return;
        }

        if (_listening && !lookAgain)
        {
            return;
        }

        var now = _time.GetTimestamp();
        if (!_listening && now < _retryAt)
        {
            return;
        }

        Volatile.Write(ref _lookedAt, now);
        var program = _findProgram();
        if (program is not { } found || found <= 0)
        {
            StopListening();

            // Not running yet (Spotify starting, the own player connecting): look again shortly.
            if (!_disposed)
            {
                _retry.Change(LookAgainAfter, Timeout.InfiniteTimeSpan);
            }

            return;
        }

        if (_listening && found == _program)
        {
            return;
        }

        // A new program (or the first): its sound starts from silence, at its own loudness.
        _program = found;
        Volatile.Write(ref _lastSound, 0);
        Volatile.Write(ref _resetLeveller, 1);
        SetHearing(false);
        _listening = true;
        _capture.Start(found, this);
    }

    // Under _gate.
    private void StopListening()
    {
        if (!_listening)
        {
            return;
        }

        _listening = false;
        _program = null;
        _capture.Stop();
        SetHearing(false);
    }

    private void SetHearing(bool hearing)
    {
        var value = hearing ? 1 : 0;
        if (Interlocked.Exchange(ref _hearing, value) != value)
        {
            HearingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
