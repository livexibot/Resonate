using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// Where the visualizer styles beyond plain bars put things and how they
/// move (<see cref="VisualizerStyle"/>): the Retro meter's segments, the
/// Wave's and Helix's travelling wave, the Embers' rising sparks, and the
/// Radial bars and Pulse rings around the cover. Everything that moves on
/// its own turns a whole number of times per
/// <see cref="StageBars.LoopSeconds"/>, so the clock can start again with
/// no jump.
/// </summary>
public static class VisualizerShapes
{
    /// <summary>The least and most segments in a Retro column, and the height each wants.</summary>
    public const int RetroMinSegments = 5;

    public const int RetroMaxSegments = 16;

    public const double RetroSegmentPitch = 11;

    /// <summary>The share of each Retro segment left dark between it and the next.</summary>
    public const double RetroGap = 0.3;

    /// <summary>Retro lights a segment once the level is this close to it, so a quiet band still shows one.</summary>
    public const double RetroRound = 0.35;

    /// <summary>How fast a Retro peak falls back, in levels a second (the whole column in a little over two seconds).</summary>
    public const float PeakFall = 0.45f;

    /// <summary>How many sound levels the Embers follow (each spark one of them).</summary>
    public const int EmberChannels = 12;

    /// <summary>The Pulse's rings around the cover.</summary>
    public const int PulseRings = 3;

    /// <summary>The space the Radial bars leave between the cover and their feet.</summary>
    public const double RadialGap = 6;

    // The Wave crosses the row this many times, and the Helix's strands twist this often.
    private const double WaveCycles = 2.5;
    private const double HelixCycles = 2;

    /// <summary>Styles drawn around the cover, which Home shows inside the cover's box.</summary>
    public static bool AroundCover(VisualizerStyle style) => style is VisualizerStyle.Radial or VisualizerStyle.Pulse;

    /// <summary>Styles the player bar can show: Off, and anything that does not need Home's cover.</summary>
    public static bool FitsPlayerBar(VisualizerStyle style) => !AroundCover(style);

    /// <summary>Retro's columns: half the user's bar count, chunky like a hi-fi's, as many as fit.</summary>
    public static int RetroColumns(double width, int wanted)
    {
        var fits = width > 0 ? Math.Max(8, (int)(width / 16)) : 8;
        return Math.Clamp(Math.Min(wanted / 2, fits), 8, 48);
    }

    /// <summary>How many segments a Retro column <paramref name="height"/> tall has.</summary>
    public static int RetroSegments(double height) =>
        Math.Clamp((int)Math.Round(Math.Max(0, height) / RetroSegmentPitch), RetroMinSegments, RetroMaxSegments);

    /// <summary>How many of a column's <paramref name="segments"/> a <paramref name="level"/> (0 to 1) lights.</summary>
    public static int RetroLit(double level, int segments) =>
        Math.Min(segments, (int)Math.Floor((Math.Clamp(level, 0, 1) * segments) + RetroRound));

    /// <summary>A peak that falls back <see cref="PeakFall"/> a second, but never below the level.</summary>
    public static float Peak(float peak, float level, float seconds) => Math.Max(level, peak - (PeakFall * Math.Max(0, seconds)));

    /// <summary>How many dots a Wave (or each Helix strand) has across <paramref name="width"/>.</summary>
    public static int WaveDots(double width, bool helix)
    {
        var pitch = helix ? 12 : 9;
        var fits = width > 0 ? (int)(width / pitch) : 24;
        return Math.Clamp(fits, 24, StageBars.MaxCount);
    }

    /// <summary>Where dot <paramref name="index"/> of <paramref name="count"/> is in the wave, in radians.</summary>
    public static double WavePhase(int index, int count, bool helix) =>
        count > 0 ? Math.Tau * (helix ? HelixCycles : WaveCycles) * index / count : 0;

    /// <summary>How fast the wave travels, in radians a second (whole turns per loop).</summary>
    public static double WaveSpeed(bool helix) => StageBars.Turns(helix ? 1.6 : 2.4);

    /// <summary>How many sparks rise across <paramref name="width"/>.</summary>
    public static int EmberCount(double width) =>
        width > 0 ? Math.Clamp((int)(width / 26), 14, 56) : 14;

    /// <summary>
    /// Spark <paramref name="index"/>: where it rises across the row (0 to
    /// 1), how many times a second it rises and where in its rise it starts
    /// (whole rises per loop), its sideways drift in pixels with its speed
    /// and angle, the level it follows and its size against the others.
    /// The same every time.
    /// </summary>
    public static (double X, double Rise, double Start, double Drift, double DriftSpeed, double DriftPhase, int Channel, double Size) Ember(int index)
    {
        var rises = Math.Round(90 + (90 * StageBars.Hash(index, 12)));
        return (
            StageBars.Hash(index, 11),
            rises / StageBars.LoopSeconds,
            StageBars.Hash(index, 13),
            6 + (14 * StageBars.Hash(index, 14)),
            StageBars.Turns(0.5 + (0.9 * StageBars.Hash(index, 15))),
            Math.Tau * StageBars.Hash(index, 16),
            Math.Min(EmberChannels - 1, (int)(StageBars.Hash(index, 17) * EmberChannels)),
            0.7 + (0.6 * StageBars.Hash(index, 18)));
    }

    /// <summary>How many Radial bars go around a cover <paramref name="side"/> wide: the user's count, as many as fit.</summary>
    public static int RadialCount(double side, int wanted) => StageBars.Count(side * 4, wanted);

    /// <summary>The levels the Radial bars follow: one per pair, mirrored left and right.</summary>
    public static int RadialChannels(int count) => (count / 2) + 1;

    /// <summary>The level Radial bar <paramref name="index"/> follows: the lows at the bottom, the highs at the top.</summary>
    public static int RadialChannel(int index, int count) => Math.Min(index, count - index);

    /// <summary>
    /// Radial bar <paramref name="index"/>'s foot (just outside a square
    /// cover <paramref name="side"/> wide, from its top left) and its angle
    /// in degrees, clockwise from pointing up; bar 0 points down.
    /// </summary>
    public static (double X, double Y, double Degrees) RadialPlace(int index, int count, double side, double gap)
    {
        var degrees = 180 + (index * 360.0 / Math.Max(1, count));
        var radians = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        var reach = (side / 2 / Math.Max(Math.Abs(sin), Math.Abs(cos))) + gap;
        return ((side / 2) + (reach * sin), (side / 2) - (reach * cos), degrees % 360);
    }

    /// <summary>How far Pulse ring <paramref name="ring"/> sits outside the cover at rest.</summary>
    public static double PulseInset(int ring, double reach) => reach * (0.16 + (0.2 * ring));

    /// <summary>How much Pulse ring <paramref name="ring"/> may grow (beyond 1) so it reaches <paramref name="reach"/> outside a cover <paramref name="side"/> wide.</summary>
    public static double PulseGrowth(int ring, double side, double reach)
    {
        var half = side / 2;
        return half <= 0 ? 0 : Math.Max(0, ((half + reach) / (half + PulseInset(ring, reach))) - 1);
    }

    /// <summary>A number as a composition expression writes it, the same in every language.</summary>
    public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
