using System.Globalization;

namespace Resonate.Themes;

/// <summary>
/// Where the visualizer styles beyond plain bars put things and how they
/// move (<see cref="VisualizerStyle"/>): the Retro meter's segments, the
/// Pills, the Silk ribbon's columns, the Aurora's glows and the Pulse rings
/// around the cover, and how the user's Amount and Size read for each.
/// Everything that moves on its own turns a whole number of times per
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

    /// <summary>The styles the menus offer (the retired ones read as their replacements, <see cref="Current"/>).</summary>
    public static IReadOnlyList<VisualizerStyle> Offered { get; } =
    [
        VisualizerStyle.Bars,
        VisualizerStyle.Mirror,
        VisualizerStyle.Lines,
        VisualizerStyle.Pills,
        VisualizerStyle.Silk,
        VisualizerStyle.Aurora,
        VisualizerStyle.Retro,
        VisualizerStyle.Pulse,
    ];

    /// <summary>
    /// A style as it is drawn now: the ones retired on 9 October 2026 (the
    /// owner found them dated) read as the style that replaced them.
    /// </summary>
    public static VisualizerStyle Current(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Dots => VisualizerStyle.Pills,
        VisualizerStyle.Wave or VisualizerStyle.Helix => VisualizerStyle.Silk,
        VisualizerStyle.Embers => VisualizerStyle.Aurora,
        VisualizerStyle.Radial => VisualizerStyle.Pulse,
        _ => style,
    };

    /// <summary>Styles drawn around the cover, which Home shows inside the cover's box.</summary>
    public static bool AroundCover(VisualizerStyle style) => Current(style) is VisualizerStyle.Pulse;

    /// <summary>Styles the player bar can show: Off, and anything that does not need Home's cover.</summary>
    public static bool FitsPlayerBar(VisualizerStyle style) => !AroundCover(style);

    /// <summary>The user's Amount (<see cref="StageBars.MinCount"/> to <see cref="StageBars.MaxCount"/>) as a share, 0 to 1.</summary>
    public static double AmountShare(int amount) =>
        Math.Clamp((amount - StageBars.MinCount) / (double)(StageBars.MaxCount - StageBars.MinCount), 0, 1);

    /// <summary>The user's Size (a fill of 0.2 to 0.9) as a share, 0 to 1.</summary>
    public static double SizeShare(double fill) => Math.Clamp((fill - 0.2) / 0.7, 0, 1);

    /// <summary>A part's size between <paramref name="smallest"/> and <paramref name="largest"/> for the user's Size.</summary>
    public static double SizeBetween(double smallest, double largest, double fill) => smallest + ((largest - smallest) * SizeShare(fill));

    /// <summary>Retro's columns: half the user's Amount, chunky like a hi-fi's, as many as fit.</summary>
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

    /// <summary>How many pills: the user's Amount, as many as fit across <paramref name="width"/> with room to be round.</summary>
    public static int PillCount(double width, int amount)
    {
        var fits = width > 0 ? Math.Max(StageBars.MinCount, (int)(width / 8)) : StageBars.MinCount;
        return Math.Clamp(Math.Min(amount, fits), StageBars.MinCount, StageBars.MaxCount);
    }

    /// <summary>
    /// The Silk ribbon's columns and the levels they follow: twice as many
    /// columns as levels (more for a higher Amount), each between two
    /// levels, so the ribbon's edge is smooth. The columns touch.
    /// </summary>
    public static (int Columns, int Levels) SilkColumns(double width, int amount)
    {
        var levels = (int)Math.Round(12 + (36 * AmountShare(amount)));
        var columns = Math.Max(levels, Math.Min(levels * 4, width > 0 ? (int)(width / 3) : levels * 2));
        return (columns, levels);
    }

    /// <summary>Where Silk column <paramref name="column"/> of <paramref name="columns"/> sits between levels: the lower level and how far towards the next.</summary>
    public static (int Level, double Toward) SilkBlend(int column, int columns, int levels)
    {
        var at = columns > 1 ? column * (levels - 1) / (double)(columns - 1) : 0;
        var low = Math.Min((int)Math.Floor(at), Math.Max(0, levels - 2));
        return (low, Math.Clamp(at - low, 0, 1));
    }

    /// <summary>How tall the Silk ribbon is at its edges against its middle, 0 to 1, so it tapers off at both ends.</summary>
    public static double SilkTaper(int column, int columns)
    {
        var across = columns > 1 ? column / (double)(columns - 1) : 0.5;
        var edge = Math.Min(across, 1 - across);
        return Math.Clamp(edge / 0.12, 0, 1);
    }

    /// <summary>How many glows the Aurora has for the user's Amount: 4 to 10.</summary>
    public static int AuroraGlows(int amount) => 4 + (int)Math.Round(AmountShare(amount) * 6);

    /// <summary>
    /// Aurora glow <paramref name="index"/> of <paramref name="count"/>:
    /// where it sits across (0 to 1), its sideways drift as a share of the
    /// width with its speed (whole turns per loop) and angle. The same every time.
    /// </summary>
    public static (double X, double Drift, double DriftSpeed, double DriftPhase) AuroraGlow(int index, int count)
    {
        var x = count > 1 ? (index + 0.5) / count : 0.5;
        return (
            x,
            0.02 + (0.03 * StageBars.Hash(index, 21)),
            StageBars.Turns(0.12 + (0.18 * StageBars.Hash(index, 22))),
            Math.Tau * StageBars.Hash(index, 23));
    }

    /// <summary>How many rings the Pulse has for the user's Amount: 2 to 5.</summary>
    public static int PulseRingCount(int amount) => 2 + (int)Math.Round(AmountShare(amount) * 3);

    /// <summary>How far Pulse ring <paramref name="ring"/> of <paramref name="rings"/> sits outside the cover at rest.</summary>
    public static double PulseInset(int ring, int rings, double reach) => reach * (0.16 + (0.6 * ring / Math.Max(1, rings - 1)));

    /// <summary>How much Pulse ring <paramref name="ring"/> may grow (beyond 1) so it reaches <paramref name="reach"/> outside a cover <paramref name="side"/> wide.</summary>
    public static double PulseGrowth(int ring, int rings, double side, double reach)
    {
        var half = side / 2;
        return half <= 0 ? 0 : Math.Max(0, ((half + reach) / (half + PulseInset(ring, rings, reach))) - 1);
    }

    /// <summary>A number as a composition expression writes it, the same in every language.</summary>
    public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
