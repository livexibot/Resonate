using System.Diagnostics;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;

namespace Resonate.App.Demo;

/// <summary>Made-up music files for "--demo" (nothing is read from the disk), and an engine that only pretends to play them.</summary>
public static class DemoLocalFiles
{
    private static readonly (string Title, string Artist, string Album, int Seconds, int DaysAgo)[] Songs =
    [
        ("Kitchen Radio (demo)", "Juniper Lane", "Home Recordings", 184, 0),
        ("Rain on the Skylight", "Juniper Lane", "Home Recordings", 211, 0),
        ("Bus Stop Melody", "Mira Sol", "Voice Memos", 97, 2),
        ("Garage Session 3", "Velvet Static", "Live in the Garage", 342, 5),
        ("Garage Session 4", "Velvet Static", "Live in the Garage", 298, 5),
        ("Lullaby for Ada", "The Quiet Hours", "Lullabies", 165, 12),
        ("Wedding March (piano)", "Echo Harbor", "Family Videos", 223, 40),
        ("Old Cassette, Side A", "Saltwater Radio", "Cassette Transfers", 1265, 90),
    ];

    public static IReadOnlyList<TrackInfo> Tracks(DateTimeOffset now) =>
        Songs.Select((song, i) => new LocalFile
        {
            Path = $@"C:\Users\Demo\Music\{song.Album}\{i + 1:00} {song.Title}.flac",
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            TrackNumber = i + 1,
            DurationMs = song.Seconds * 1000L,
            AddedAt = now.AddDays(-song.DaysAgo).AddMinutes(-i * 7),
        }.ToTrackInfo()).ToList();
}

/// <summary>
/// Plays nothing: keeps time like a player would, so the demo's player bar
/// moves, and makes up sound for the visualiser while a demo file "plays"
/// (CI's screenshots show it moving). Demo Spotify songs get nothing, as in
/// the real app.
/// </summary>
public sealed class DemoLocalAudio : ILocalAudioEngine
{
    private const int Rate = 48_000;
    private const int Channels = 2;

    // 10 ms, like a real audio graph's quantum.
    private const int QuantumFrames = Rate / 100;
    private static readonly TimeSpan QuantumLength = TimeSpan.FromMilliseconds(10);

    // Never more than 40 ms at once after the timer was late, and 2 s of silence after a pause lets every bar fall.
    private const int MostQuantaAtOnce = 4;
    private const int QuietQuanta = 200;

    private readonly Stopwatch _clock = new();
    private readonly Lock _gate = new();
    private readonly float[] _quantum = new float[QuantumFrames * Channels];
    private readonly DemoSignal _signal = new();
    private TimeSpan _start;
    private ILocalAudioSink? _sink;
    private bool _playing;
    private bool _feeding;
    private bool _newSong;
    private bool _disposed;

    public event EventHandler<LocalTrackEnded>? TrackEnded
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? Failed
    {
        add { }
        remove { }
    }

    public TimeSpan Position => _start + _clock.Elapsed;

    public TimeSpan Duration => TimeSpan.Zero;

    public Task<TimeSpan> OpenAsync(string path, TimeSpan position, bool play, CancellationToken cancellationToken)
    {
        _start = position;
        _clock.Reset();
        if (play)
        {
            _clock.Start();
        }

        lock (_gate)
        {
            _newSong = true;
        }

        SetPlaying(play);
        return Task.FromResult(TimeSpan.Zero);
    }

    public Task PlayAsync()
    {
        _clock.Start();
        SetPlaying(true);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        _clock.Stop();
        SetPlaying(false);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _start = position;
        if (_clock.IsRunning)
        {
            _clock.Restart();
        }
        else
        {
            _clock.Reset();
        }

        return Task.CompletedTask;
    }

    public void SetNext(string? path)
    {
    }

    public void SetLooping(bool looping)
    {
    }

    public void SetVolume(double volume)
    {
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
    }

    public void SetSink(ILocalAudioSink? sink)
    {
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(_sink, sink))
            {
                return;
            }

            // Before the feeding loop can see it, as a real graph starts a sink before its first quantum.
            sink?.Start(0);
            _sink = sink;
        }

        Feed();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _sink = null;
        }

        _clock.Stop();
    }

    private void SetPlaying(bool playing)
    {
        lock (_gate)
        {
            _playing = playing;
        }

        Feed();
    }

    /// <summary>Starts the feeding loop when there is a sink and something plays; it stops by itself once there is neither.</summary>
    private void Feed()
    {
        lock (_gate)
        {
            if (_feeding || _disposed || _sink is null || !_playing)
            {
                return;
            }

            _feeding = true;
        }

        _ = Task.Run(FeedAsync);
    }

    /// <summary>
    /// The only writer to the sink (as a graph's audio thread is): 480
    /// frames of 48 kHz stereo at a time, as many as real time says are due,
    /// so the bars move at the music's pace even when the timer ticks late.
    /// </summary>
    private async Task FeedAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(QuantumLength);
            var started = Stopwatch.GetTimestamp();
            var written = 0L;
            var quiet = 0;
            while (true)
            {
                ILocalAudioSink sink;
                bool playing;
                lock (_gate)
                {
                    // Decided under the lock, so a play that comes now starts a new loop.
                    if (_disposed || _sink is null || (!_playing && quiet >= QuietQuanta))
                    {
                        _feeding = false;
                        return;
                    }

                    sink = _sink;
                    playing = _playing;
                    if (_newSong)
                    {
                        _newSong = false;
                        _signal.Restart();
                    }
                }

                var due = ((long)(Stopwatch.GetElapsedTime(started).TotalSeconds * Rate) - written) / QuantumFrames;
                if (due > MostQuantaAtOnce)
                {
                    // Back from a long stall: skip what was missed instead of rushing through it.
                    written += (due - MostQuantaAtOnce) * QuantumFrames;
                    due = MostQuantaAtOnce;
                }

                for (var q = 0; q < due; q++)
                {
                    if (playing)
                    {
                        _signal.Fill(_quantum);
                        quiet = 0;
                    }
                    else
                    {
                        Array.Clear(_quantum);
                        quiet++;
                    }

                    sink.Write(_quantum, Channels, Rate);
                    written += QuantumFrames;
                }

                await timer.WaitForNextTickAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // A sink that failed: stop feeding it; the next play starts again.
            lock (_gate)
            {
                _feeding = false;
            }
        }
    }

    /// <summary>
    /// A made-up song at 120 beats a minute, the same on every run: a kick on
    /// the beat, a buzzing bass, a held chord, a melody, a bell between beats
    /// and hi-hats over a soft hiss. Saw and square waves (rich in overtones)
    /// rather than pure tones, so every part of the visualiser's range moves,
    /// as it does with real music.
    /// </summary>
    private sealed class DemoSignal
    {
        private const int Beat = Rate / 2;
        private const int Eighth = Beat / 2;
        private const int Sixteenth = Beat / 4;
        private const int Bar = Beat * 4;

        private const float KickVolume = 0.45f;
        private const float BassVolume = 0.25f;
        private const float ChordVolume = 0.12f;
        private const float MelodyVolume = 0.2f;
        private const float BellVolume = 0.15f;
        private const float HatVolume = 0.25f;
        private const float HissVolume = 0.03f;

        private static readonly float[] BassNotes = [55f, 55f, 73.42f, 65.41f];
        private static readonly float[][] Chords =
        [
            [261.63f, 329.63f, 392f],
            [220f, 261.63f, 329.63f],
            [174.61f, 220f, 261.63f],
            [196f, 246.94f, 293.66f],
        ];

        private static readonly float[] Melody = [523.25f, 587.33f, 659.26f, 783.99f, 880f, 1046.5f, 1318.51f, 1567.98f];

        // Each sound fades by the same factor every sample.
        private static readonly float KickFade = MathF.Exp(-9f / Rate);
        private static readonly float KickDrop = MathF.Exp(-35f / Rate);
        private static readonly float BassFade = MathF.Exp(-3f / Rate);
        private static readonly float MelodyFade = MathF.Exp(-5f / Rate);
        private static readonly float BellFade = MathF.Exp(-12f / Rate);
        private static readonly float HatFade = MathF.Exp(-45f / Rate);

        private readonly float[] _chordPhases = new float[3];
        private long _frame;
        private float _kickPhase;
        private float _kickLevel;
        private float _kickPitch;
        private float _bassPhase;
        private float _bassLevel;
        private float _melodyPhase;
        private float _melodyLevel;
        private float _melodyNote;
        private float _bellPhase;
        private float _bellLevel;
        private float _hatLevel;
        private float _lastNoise;
        private uint _noise;

        public DemoSignal() => Restart();

        public void Restart()
        {
            _frame = 0;
            Array.Clear(_chordPhases);
            _kickPhase = _kickLevel = _kickPitch = 0;
            _bassPhase = _bassLevel = 0;
            _melodyPhase = _melodyLevel = 0;
            _melodyNote = Melody[0];
            _bellPhase = _bellLevel = 0;
            _hatLevel = _lastNoise = 0;
            _noise = 2_463_534_242;
        }

        /// <summary>Fills interleaved stereo frames with the next part of the song.</summary>
        public void Fill(float[] stereo)
        {
            for (var i = 0; i + 1 < stereo.Length; i += 2)
            {
                var frame = _frame++;
                var inBeat = (int)(frame % Beat);
                var beat = (int)(frame / Beat % 4);
                var bar = (int)(frame / Bar % 4);
                if (inBeat == 0)
                {
                    _kickLevel = 1;
                    _kickPitch = 1;
                    _kickPhase = 0;
                }

                if (frame % Eighth == 0)
                {
                    _bassLevel = 1;
                    _melodyLevel = 1;
                    _melodyNote = Melody[(int)(((frame / Eighth * 3) + bar) % Melody.Length)];
                }

                if (inBeat == Beat / 2)
                {
                    _bellLevel = 1;
                }

                if (frame % Sixteenth == 0)
                {
                    _hatLevel = inBeat == Beat / 2 ? 1f : 0.5f;
                }

                var kick = MathF.Sin(MathF.Tau * Advance(ref _kickPhase, 45f + (120f * _kickPitch))) * _kickLevel * KickVolume;
                var bass = Saw(ref _bassPhase, BassNotes[beat]) * _bassLevel * BassVolume;
                var chord = (Saw(ref _chordPhases[0], Chords[bar][0]) + Saw(ref _chordPhases[1], Chords[bar][1]) + Saw(ref _chordPhases[2], Chords[bar][2])) * ChordVolume;
                var melody = (Advance(ref _melodyPhase, _melodyNote) < 0.5f ? 1f : -1f) * _melodyLevel * MelodyVolume;
                var bell = MathF.Sin(MathF.Tau * Advance(ref _bellPhase, 3520f)) * _bellLevel * BellVolume;

                // Hi-hats: noise with the lows taken out (the difference of neighbouring samples).
                var noise = NextNoise();
                var hat = (noise - _lastNoise) * 0.5f * _hatLevel * HatVolume;
                _lastNoise = noise;

                _kickLevel *= KickFade;
                _kickPitch *= KickDrop;
                _bassLevel *= BassFade;
                _melodyLevel *= MelodyFade;
                _bellLevel *= BellFade;
                _hatLevel *= HatFade;

                // The melody leans left and the bell right; the visualiser hears their mean.
                var centre = kick + bass + chord + hat + (noise * HissVolume);
                stereo[i] = Math.Clamp(centre + (1.3f * melody) + (0.7f * bell), -1f, 1f);
                stereo[i + 1] = Math.Clamp(centre + (0.7f * melody) + (1.3f * bell), -1f, 1f);
            }
        }

        /// <summary>Where a wave is in its cycle (0 to 1), then moves it on by one sample.</summary>
        private static float Advance(ref float phase, float hz)
        {
            var now = phase;
            phase += hz / Rate;
            if (phase >= 1)
            {
                phase -= 1;
            }

            return now;
        }

        /// <summary>A saw wave: every overtone, each softer than the last.</summary>
        private static float Saw(ref float phase, float hz) => (2 * Advance(ref phase, hz)) - 1;

        /// <summary>Xorshift: the same noise on every run, from -1 to 1.</summary>
        private float NextNoise()
        {
            var x = _noise;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _noise = x;
            return ((x >> 8) * (2f / (1 << 24))) - 1f;
        }
    }
}
