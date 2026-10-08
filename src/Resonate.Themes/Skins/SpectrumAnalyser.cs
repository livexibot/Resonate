using System.Diagnostics;

namespace Resonate.Themes.Skins;

/// <summary>How many bars the spectrum shows.</summary>
public enum VisualiserBars
{
    /// <summary>Each of Winamp's 75 semitone-wide bands as a bar of its own.</summary>
    Wide = 75,

    /// <summary>Winamp's classic 19 bars, each the mean of four bands.</summary>
    Classic = 19,
}

/// <summary>How the bars move.</summary>
public enum VisualiserMotion
{
    /// <summary>Fractions of a row: bars fall 45 rows a second, peak caps hang 0.35 s and then drop faster and faster.</summary>
    Smooth,

    /// <summary>Winamp's own ("moderate" falloff, peak speed-up 1.1): whole rows moved 60 times per second of music; bars fall 3/4 row a step, caps speed up 10 % a step.</summary>
    Stepped,
}

/// <summary>
/// One picture for the visualiser, made on the audio thread. Heights are in
/// rows of a 16-row display: a bar fills rows 0 to <c>Bars[i]</c> (at most
/// 15) and its peak cap is one row tall with its bottom at <c>Peaks[i]</c>
/// (0 = no cap), so nothing reaches above 16.
/// </summary>
public sealed class VisualiserFrame
{
    public const int MaxBars = SpectrumAnalyser.Bands;

    /// <summary>How many entries of <see cref="Bars"/> and <see cref="Peaks"/> are in use: 75 or 19.</summary>
    public int BarCount { get; internal set; }

    public float[] Bars { get; } = new float[MaxBars];

    public float[] Peaks { get; } = new float[MaxBars];

    /// <summary>The oscilloscope's trace, oldest sample first: the mono sample doubled and clipped to -1..1 (half of full scale reaches the edge, as in Winamp).</summary>
    public float[] Scope { get; } = new float[SpectrumAnalyser.ScopePoints];

    /// <summary>Counts up with every new picture, so a view can skip drawing the same one twice.</summary>
    public long Sequence { get; internal set; }

    /// <summary><see cref="Stopwatch.GetTimestamp"/> when it was made. Older than about 250 ms: the music stopped flowing; show the bars at rest.</summary>
    public long Timestamp { get; internal set; }

    /// <summary>Every bar and cap is down and the trace is flat; nothing moves until there is sound again.</summary>
    public bool IsAtRest { get; internal set; }
}

/// <summary>
/// Winamp's spectrum analyser and oscilloscope (classic_vis.cpp, as spotifast's vis.rs ports it), fed with the local files player's sound:
/// a 512-point FFT under a Hann window, Winamp's 75 semitone bands with
/// Hermite edges, its bar falloff and peak caps, and a 76-point scope
/// reading every seventh sample. One thread (the audio thread) calls
/// <see cref="Process"/>; one other thread (the interface thread) calls
/// <see cref="Read"/>. Neither waits for the other and neither allocates:
/// pictures pass through three preallocated frames swapped with
/// <see cref="Interlocked.Exchange(ref int, int)"/>.
/// </summary>
public sealed class SpectrumAnalyser
{
    public const int FftSize = 512;
    public const int Bands = 75;
    public const int ScopePoints = 76;
    public const int ScopeStride = 7;

    /// <summary>The most samples the windows may look back, so the trace matches what the speakers play.</summary>
    public const int MaxLag = 2048;

    private const int Bins = FftSize / 2;
    private const int ScopeSpan = ((ScopePoints - 1) * ScopeStride) + 1;
    private const int RingSize = 4096;
    private const int RingMask = RingSize - 1;

    private const float MaxHeight = 15f;
    private const float ChannelSum = 2f;
    private const float SpectrumScale = 0.5f;
    private const float StepSeconds = 1f / 60;
    private const float StepFall = 12f / 16;
    private const float PeakSpeedup = 1.1f;
    private const int MaxSteps = 3;
    private const float SmoothFall = StepFall / StepSeconds;
    private const float PeakHold = 0.35f;
    private const float PeakGravity = 2.4f * MaxHeight;
    private const float RestLevel = 0.01f * MaxHeight;
    private const float SilentSample = 1e-6f;

    private const int SlotMask = 3;
    private const int Fresh = 4;

    // Audio thread only.
    private readonly float[] _ring = new float[RingSize];
    private readonly float[] _window = new float[FftSize];
    private readonly int[] _reversed = new int[FftSize];
    private readonly float[] _cos = new float[Bins];
    private readonly float[] _sin = new float[Bins];
    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imaginary = new float[FftSize];
    private readonly float[] _spectrum = new float[Bins];
    private readonly float[] _bandEdges = new float[Bands + 1];
    private readonly float[] _columns = new float[Bands + 1];
    private readonly float[] _levels = new float[Bands];
    private readonly float[] _peaks = new float[Bands];
    private readonly float[] _held = new float[Bands];
    private readonly float[] _speed = new float[Bands];
    private readonly int[] _stepPeaks = new int[Bands];
    private readonly float[] _stepHeights = new float[Bands];
    private int _head;
    private int _silentRun = RingSize;
    private float _stepClock;
    private int _appliedBars = (int)VisualiserBars.Wide;
    private int _appliedMotion = (int)VisualiserMotion.Smooth;
    private bool _publishedRest;
    private long _sequence;

    // Shared, written by the interface thread.
    private volatile int _bars = (int)VisualiserBars.Wide;
    private volatile int _motion = (int)VisualiserMotion.Smooth;
    private volatile int _lag;
    private int _resetRequested;
    private int _processing;

    // The hand-over: the audio thread owns one frame, the interface thread one, and the third waits between them.
    private readonly VisualiserFrame[] _frames = [new(), new(), new()];
    private int _writeSlot;
    private int _middle = 1;
    private int _readSlot = 2;

    public SpectrumAnalyser()
    {
        var bits = (int)Math.Log2(FftSize);
        for (var i = 0; i < FftSize; i++)
        {
            var reversed = 0;
            for (var b = 0; b < bits; b++)
            {
                reversed |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reversed[i] = reversed;
            _window[i] = 0.5f - (0.5f * MathF.Cos(MathF.Tau * i / FftSize));
        }

        for (var k = 0; k < Bins; k++)
        {
            _cos[k] = MathF.Cos(MathF.Tau * k / FftSize);
            _sin[k] = -MathF.Sin(MathF.Tau * k / FftSize);
        }

        // Winamp's semitone warp: 75 bands from bin 1 to bin 256.
        var scale = 255f / MathF.Pow(2, 75f / 12);
        for (var x = 0; x <= Bands; x++)
        {
            _bandEdges[x] = ((MathF.Pow(2, x / 12f) - 1) * scale) + 1;
        }

        foreach (var frame in _frames)
        {
            frame.BarCount = Bands;
            frame.IsAtRest = true;
        }
    }

    public VisualiserBars Bars
    {
        get => (VisualiserBars)_bars;
        set => _bars = (int)value;
    }

    public VisualiserMotion Motion
    {
        get => (VisualiserMotion)_motion;
        set => _motion = (int)value;
    }

    /// <summary>How many samples behind the newest the pictures look (the graph's latency), 0 to <see cref="MaxLag"/>.</summary>
    public int LagSamples
    {
        get => _lag;
        set => _lag = Math.Clamp(value, 0, MaxLag);
    }

    /// <summary>Drops every bar and cap at the next quantum (a new graph, a new song).</summary>
    public void Reset() => Volatile.Write(ref _resetRequested, 1);

    /// <summary>
    /// The audio thread: one quantum of the graph's sound, interleaved 32-bit
    /// float, <paramref name="channels"/> per frame. Publishes a new picture.
    /// </summary>
    public void Process(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        if (channels <= 0 || sampleRate <= 0)
        {
            return;
        }

        // One writer at a time: a second audio thread (an old graph's last quantum
        // while a new graph starts) skips its quantum instead of waiting.
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ProcessQuantum(interleaved, channels, sampleRate);
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
        }
    }

    private void ProcessQuantum(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        var frames = interleaved.Length / channels;
        if (frames == 0)
        {
            return;
        }

        var silent = Push(interleaved, channels, frames);
        var lag = _lag;

        var bars = _bars;
        var motion = _motion;
        if (Interlocked.Exchange(ref _resetRequested, 0) != 0 || bars != _appliedBars || motion != _appliedMotion)
        {
            _appliedBars = bars;
            _appliedMotion = motion;
            ClearState();
        }

        // Silence after the bars came to rest: nothing to compute and nothing new to show.
        if (silent && _publishedRest && _silentRun >= lag + ScopeSpan)
        {
            return;
        }

        var elapsed = Math.Min(frames / (float)sampleRate, 0.25f);
        var newest = _head - lag;
        Spectrum(newest);
        WinampBands();
        if (motion == (int)VisualiserMotion.Stepped)
        {
            Step(bars, elapsed);
        }
        else
        {
            Glide(bars, elapsed);
        }

        Publish(bars, motion, newest, lag);
    }

    /// <summary>
    /// The interface thread (one thread only): the newest picture. It stays
    /// the same object, unchanged, until the next call.
    /// </summary>
    public VisualiserFrame Read()
    {
        if ((Volatile.Read(ref _middle) & Fresh) != 0)
        {
            _readSlot = Interlocked.Exchange(ref _middle, _readSlot) & SlotMask;
        }

        return _frames[_readSlot];
    }

    /// <summary>Mixes to mono (the mean of the front pair) into the ring; true when the quantum was silent.</summary>
    private bool Push(ReadOnlySpan<float> interleaved, int channels, int frames)
    {
        var silent = true;
        var head = _head;
        for (var f = 0; f < frames; f++)
        {
            var at = f * channels;
            var mono = channels >= 2 ? (interleaved[at] + interleaved[at + 1]) * 0.5f : interleaved[at];
            if (MathF.Abs(mono) > SilentSample)
            {
                silent = false;
            }

            _ring[head & RingMask] = mono;
            head++;
        }

        _head = head & RingMask;
        _silentRun = silent ? Math.Min(_silentRun + frames, RingSize) : 0;
        return silent;
    }

    /// <summary>Winamp's FFT of the 512 samples ending at <paramref name="end"/>: 256 magnitudes, each halved.</summary>
    private void Spectrum(int end)
    {
        var start = end - FftSize;
        for (var i = 0; i < FftSize; i++)
        {
            var from = _reversed[i];
            _real[i] = _ring[(start + from) & RingMask] * ChannelSum * _window[from];
            _imaginary[i] = 0;
        }

        for (var size = 2; size <= FftSize; size <<= 1)
        {
            var half = size >> 1;
            var stride = FftSize / size;
            for (var first = 0; first < FftSize; first += size)
            {
                for (var m = 0; m < half; m++)
                {
                    var wr = _cos[m * stride];
                    var wi = _sin[m * stride];
                    var i = first + m;
                    var j = i + half;
                    var tr = (wr * _real[j]) - (wi * _imaginary[j]);
                    var ti = (wr * _imaginary[j]) + (wi * _real[j]);
                    _real[j] = _real[i] - tr;
                    _imaginary[j] = _imaginary[i] - ti;
                    _real[i] += tr;
                    _imaginary[i] += ti;
                }
            }
        }

        for (var k = 0; k < Bins; k++)
        {
            _spectrum[k] = MathF.Sqrt((_real[k] * _real[k]) + (_imaginary[k] * _imaginary[k])) * SpectrumScale;
        }
    }

    /// <summary>Winamp's 75 bands (classic_vis.cpp): each sums its share of the bins, fractional edges through a Hermite curve, clipped at 255.</summary>
    private void WinampBands()
    {
        for (var x = 0; x < Bands; x++)
        {
            var low = _bandEdges[x];
            var next = _bandEdges[x + 1];
            var value = 0f;
            var bin = (int)low;
            var end = Math.Min((int)next, Bins - 1);
            var fraction = low;
            var weight = bin + 1 - low;
            var hermite = true;
            while (true)
            {
                if (bin == end)
                {
                    weight = next - fraction;
                    hermite = true;
                }

                value += hermite
                    ? Hermite(fraction - bin, Bin(Math.Max(bin - 1, 0)), Bin(bin), Bin(bin + 1), Bin(bin + 2)) * weight
                    : Bin(bin);
                hermite = false;
                bin++;
                if (bin > end)
                {
                    break;
                }

                fraction = bin;
            }

            _columns[x] = Math.Min(value, 255f);
        }

        _columns[Bands] = 0;
    }

    private float Bin(int index) => index < Bins ? _spectrum[index] : 0f;

    private static float Hermite(float x, float y0, float y1, float y2, float y3)
    {
        var c1 = 0.5f * (y2 - y0);
        var c3 = (1.5f * (y1 - y2)) + (0.5f * (y3 - y0));
        var c2 = y0 - y1 + c1 - c3;
        return (((((c3 * x) + c2) * x) + c1) * x) + y1;
    }

    /// <summary>The sound under bar <paramref name="bar"/>, in rows (a band, or the mean of four for 19 bars).</summary>
    private float Target(int bars, int bar)
    {
        var sound = bars == (int)VisualiserBars.Classic
            ? (_columns[4 * bar] + _columns[(4 * bar) + 1] + _columns[(4 * bar) + 2] + _columns[(4 * bar) + 3]) / 4
            : _columns[bar];
        return Math.Min(sound, MaxHeight);
    }

    /// <summary>Smooth motion: up at once, down at Winamp's rate by the audio clock; caps hang, then fall under gravity.</summary>
    private void Glide(int bars, float elapsed)
    {
        for (var b = 0; b < bars; b++)
        {
            var level = Math.Max(_levels[b] - (SmoothFall * elapsed), Target(bars, b));
            if (level < RestLevel)
            {
                level = 0;
            }

            _levels[b] = level;
            if (level >= _peaks[b])
            {
                _peaks[b] = level;
                _held[b] = 0;
                _speed[b] = 0;
            }
            else if (_held[b] < PeakHold)
            {
                _held[b] += elapsed;
            }
            else
            {
                _speed[b] += PeakGravity * elapsed;
                _peaks[b] = Math.Max(_peaks[b] - (_speed[b] * elapsed), level);
            }

            if (_peaks[b] < RestLevel)
            {
                _peaks[b] = 0;
            }
        }
    }

    /// <summary>
    /// Winamp's own steps, 60 per second of audio: as many as the quantum
    /// covers (a 10 ms quantum makes zero or one), at most three, and after a
    /// longer gap never owing more than one.
    /// </summary>
    private void Step(int bars, float elapsed)
    {
        _stepClock += elapsed;
        var steps = Math.Min((int)(_stepClock / StepSeconds), MaxSteps);
        _stepClock = Math.Min(_stepClock - (steps * StepSeconds), StepSeconds);
        for (var s = 0; s < steps; s++)
        {
            for (var b = 0; b < bars; b++)
            {
                var target = MathF.Truncate(Target(bars, b));
                var falloff = _levels[b] - StepFall;
                if (falloff <= target)
                {
                    falloff = target;
                }

                _levels[b] = falloff;
                if (_stepPeaks[b] <= (int)MathF.Round(falloff * 256))
                {
                    _stepPeaks[b] = (int)(falloff * 256);
                    _speed[b] = 3;
                }

                var peakRow = _stepPeaks[b] / 256;
                _stepPeaks[b] = Math.Max(_stepPeaks[b] - (int)MathF.Round(_speed[b]), 0);
                _speed[b] *= PeakSpeedup;
                _stepHeights[b] = MathF.Round(falloff);
                _peaks[b] = peakRow >= 1 ? peakRow : 0;
            }
        }
    }

    private void Publish(int bars, int motion, int end, int lag)
    {
        var frame = _frames[_writeSlot];
        frame.BarCount = bars;
        var rest = _silentRun >= lag + ScopeSpan;
        var heights = motion == (int)VisualiserMotion.Stepped ? _stepHeights : _levels;
        for (var b = 0; b < bars; b++)
        {
            frame.Bars[b] = heights[b];
            frame.Peaks[b] = _peaks[b];
            rest &= heights[b] == 0 && _peaks[b] == 0 && (motion != (int)VisualiserMotion.Stepped || _stepPeaks[b] == 0);
        }

        // Winamp's scope: every seventh sample, doubled, so half of full scale reaches the edge.
        var start = end - ScopeSpan;
        for (var p = 0; p < ScopePoints; p++)
        {
            frame.Scope[p] = Math.Clamp(_ring[(start + (p * ScopeStride)) & RingMask] * 2f, -1f, 1f);
        }

        frame.IsAtRest = rest;
        frame.Sequence = ++_sequence;
        frame.Timestamp = Stopwatch.GetTimestamp();
        _publishedRest = rest;
        _writeSlot = Interlocked.Exchange(ref _middle, _writeSlot | Fresh) & SlotMask;
    }

    private void ClearState()
    {
        Array.Clear(_levels);
        Array.Clear(_peaks);
        Array.Clear(_held);
        Array.Clear(_speed);
        Array.Clear(_stepPeaks);
        Array.Clear(_stepHeights);
        _stepClock = 0;
        _publishedRest = false;
    }
}
