using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// The now-playing stage's visualizer (part of the Home stage, a built-in
/// plugin): a row of slim bars along the bottom of the stage. For the
/// user's own music files they follow the sound of the local files player
/// (its spectrum, <see cref="Skins.SpectrumAnalyser"/>). Resonate never hears
/// Spotify's sound, so for Spotify songs they move on their own: each bar
/// sways at its own pace under a slow wave that rolls across the row, all
/// worked out by the compositor from one clock. The motion repeats exactly
/// after <see cref="LoopSeconds"/>, so the clock can start again with no jump.
/// </summary>
public static class StageBars
{
    /// <summary>The fewest and the most bars.</summary>
    public const int MinCount = 16;

    public const int MaxCount = 96;

    /// <summary>The narrowest step from one bar to the next, in device-independent pixels.</summary>
    public const double MinPitch = 14;

    /// <summary>How much of each step a bar fills; the rest is the gap.</summary>
    public const double Fill = 0.56;

    /// <summary>The tallest a bar grows: this share of the stage's height, and never more than <see cref="MaxHeight"/>.</summary>
    public const double HeightShare = 0.26;

    public const double MaxHeight = 280;

    /// <summary>A bar's height at rest (paused, or nothing playing), in device-independent pixels.</summary>
    public const double RestHeight = 3;

    /// <summary>How long the made-up motion takes to repeat itself.</summary>
    public const double LoopSeconds = 1200;

    /// <summary>
    /// The spectrum's bands the bars spread over (Winamp's semitone bands,
    /// up to about 13 kHz at 48 kHz; the few above rarely move).
    /// </summary>
    public const int UsedBands = 66;

    // The spectrum is 16 rows tall; quiet bands are lifted a little so the row never looks empty.
    private const float SpectrumRows = 15f;
    private const float Lift = 0.75f;

    // The made-up motion: a resting level, each bar's two sways, and the wave across the row.
    private const double Base = 0.58;
    private const double Sway = 0.2;
    private const double Flutter = 0.12;
    private const double Wave = 0.1;
    private const double WaveAcross = 7;
    private const double WaveTurns = 248;

    /// <summary>How many bars fit across <paramref name="width"/>.</summary>
    public static int Count(double width) =>
        width > 0 ? Math.Clamp((int)(width / MinPitch), MinCount, MaxCount) : MinCount;

    /// <summary>The tallest bar for a stage <paramref name="stageHeight"/> tall.</summary>
    public static double Height(double stageHeight) => Math.Max(0, Math.Min(stageHeight * HeightShare, MaxHeight));

    /// <summary>
    /// How tall bar <paramref name="index"/> of <paramref name="count"/> may
    /// grow in the made-up motion (0 to 1): like music's spectrum, fullest in
    /// the lows and mids and lower towards the highs, with each bar a little
    /// different from its neighbours. The same every time.
    /// </summary>
    public static double Shape(int index, int count)
    {
        var x = Across(index, count);
        var hump = Math.Exp(-Math.Pow((x - 0.22) / 0.4, 2));
        var jitter = (Hash(index, 1) - 0.5) * 0.16;
        return Math.Clamp(0.4 + (0.5 * hump) + jitter, 0.2, 0.98);
    }

    /// <summary>
    /// Bar <paramref name="index"/>'s two sways: angular speeds (radians a
    /// second, whole turns per <see cref="LoopSeconds"/>) and starting angles.
    /// </summary>
    public static (double Speed1, double Phase1, double Speed2, double Phase2) Motion(int index)
    {
        var slow = Turns(1.6 + (1.6 * Hash(index, 2)));
        var quick = Turns(3.5 + (3 * Hash(index, 3)));
        return (slow, Math.Tau * Hash(index, 4), quick, Math.Tau * Hash(index, 5));
    }

    /// <summary>The wave's angular speed, in radians a second (whole turns per <see cref="LoopSeconds"/>).</summary>
    public static double WaveSpeed => Math.Tau * WaveTurns / LoopSeconds;

    /// <summary>The made-up height of bar <paramref name="index"/> at <paramref name="seconds"/> on the clock, 0 to 1.</summary>
    public static double Synthetic(int index, int count, double seconds)
    {
        var (speed1, phase1, speed2, phase2) = Motion(index);
        var x = Across(index, count);
        var level = Base
            + (Sway * Math.Sin((seconds * speed1) + phase1))
            + (Flutter * Math.Sin((seconds * speed2) + phase2))
            + (Wave * Math.Sin((seconds * WaveSpeed) - (x * WaveAcross)));
        return Shape(index, count) * level;
    }

    /// <summary>
    /// <see cref="Synthetic"/> as a composition expression, with the clock
    /// read from <paramref name="clock"/> (for example "p.Time"). Numbers are
    /// written the same way in every language.
    /// </summary>
    public static string SyntheticExpression(int index, int count, string clock)
    {
        var (speed1, phase1, speed2, phase2) = Motion(index);
        var x = Across(index, count);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Shape(index, count):0.####}*({Base}+{Sway}*Sin({clock}*{speed1:0.######}+{phase1:0.####})+{Flutter}*Sin({clock}*{speed2:0.######}+{phase2:0.####})+{Wave}*Sin({clock}*{WaveSpeed:0.######}-{x * WaveAcross:0.####}))");
    }

    /// <summary>
    /// The spectrum's bands (heights in rows, 0 to 15, as the analyser gives
    /// them) spread over <paramref name="bars"/>, each 0 to 1.
    /// </summary>
    public static void Resample(ReadOnlySpan<float> bands, Span<float> bars)
    {
        if (bars.IsEmpty)
        {
            return;
        }

        var used = Math.Min(bands.Length, UsedBands);
        if (used == 0)
        {
            bars.Clear();
            return;
        }

        for (var i = 0; i < bars.Length; i++)
        {
            var at = bars.Length > 1 ? i * (used - 1) / (float)(bars.Length - 1) : 0;
            var low = (int)at;
            var high = Math.Min(low + 1, used - 1);
            var rows = bands[low] + ((bands[high] - bands[low]) * (at - low));
            var level = Math.Clamp(rows / SpectrumRows, 0f, 1f);
            bars[i] = MathF.Pow(level, Lift);
        }
    }

    /// <summary>Where bar <paramref name="index"/> sits across the row, 0 (left) to 1 (right).</summary>
    private static double Across(int index, int count) => count > 1 ? index / (double)(count - 1) : 0;

    /// <summary>The nearest speed that turns a whole number of times per loop.</summary>
    private static double Turns(double radiansPerSecond) =>
        Math.Tau * Math.Max(1, Math.Round(radiansPerSecond * LoopSeconds / Math.Tau)) / LoopSeconds;

    /// <summary>A fixed number from 0 to 1 for a bar and a purpose.</summary>
    private static double Hash(int index, int salt)
    {
        var h = (uint)((index * 73856093) ^ (salt * 19349663)) * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }
}
