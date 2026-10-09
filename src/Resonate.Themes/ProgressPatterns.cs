namespace Resonate.Themes;

/// <summary>
/// The lines the moving progress bars draw for the part already played: a
/// wave, Liquid's second wave under it, a heartbeat trace and a row of dots.
/// Each repeats every <see cref="Period"/> pixels, so the bar draws one period
/// more than it is wide and rolls it along by one period, again and again,
/// without a jump. Heights are pixels above the bar's middle.
/// </summary>
public static class ProgressPatterns
{
    public const double WaveLength = 22;
    public const double WaveHeight = 3.2;

    /// <summary>Liquid's second wave: longer, lower, rolling the other way.</summary>
    public const double UnderLength = 36;
    public const double UnderHeight = 2.4;

    /// <summary>One heartbeat, flat line included.</summary>
    public const double BeatLength = 56;

    /// <summary>The tallest point of the heartbeat.</summary>
    public const double BeatHeight = 6;

    public const double DotSpacing = 8;

    // One heartbeat as (share of the beat, height): flat, a small bump, a dip, the spike, a dip below, flat, a soft bump.
    private static readonly (double At, double Height)[] Beat =
    [
        (0, 0), (0.48, 0), (0.52, 1.2), (0.56, 0), (0.6, 0), (0.62, -1.4), (0.655, BeatHeight), (0.69, -3.2),
        (0.72, 0), (0.8, 0), (0.85, 1.8), (0.9, 0), (1, 0),
    ];

    /// <summary>The style draws the played part as a line that rolls along while playing.</summary>
    public static bool Rolls(ProgressStyle style) =>
        style is ProgressStyle.Wave or ProgressStyle.Liquid or ProgressStyle.Heartbeat or ProgressStyle.Dots;

    /// <summary>How far the line goes before it repeats.</summary>
    public static double Period(ProgressStyle style) => style switch
    {
        ProgressStyle.Heartbeat => BeatLength,
        ProgressStyle.Dots => DotSpacing,
        _ => WaveLength,
    };

    /// <summary>How long one period takes to roll by while playing.</summary>
    public static TimeSpan RollTime(ProgressStyle style) => TimeSpan.FromSeconds(style switch
    {
        ProgressStyle.Heartbeat => 1.4,
        ProgressStyle.Dots => 0.5,
        _ => 1.1,
    });

    /// <summary>Liquid's second wave takes this long for its own period, rolling the other way.</summary>
    public static TimeSpan UnderRollTime => TimeSpan.FromSeconds(2.6);

    /// <summary>The volume bar keeps a style that stands still: the moving ones become the plain line there.</summary>
    public static ProgressStyle ForVolume(ProgressStyle style) =>
        style is ProgressStyle.Wave or ProgressStyle.Liquid or ProgressStyle.Heartbeat or ProgressStyle.Shimmer or ProgressStyle.Ripple
            ? ProgressStyle.Line
            : style;

    public static double WaveAt(double x) => Math.Sin(x / WaveLength * Math.Tau) * WaveHeight;

    public static double UnderAt(double x) => Math.Sin(x / UnderLength * Math.Tau) * UnderHeight;

    public static double BeatAt(double x)
    {
        var at = (x % BeatLength + BeatLength) % BeatLength / BeatLength;
        for (var i = 1; i < Beat.Length; i++)
        {
            if (at <= Beat[i].At)
            {
                var (fromAt, from) = Beat[i - 1];
                var (toAt, to) = Beat[i];
                return from + ((to - from) * (at - fromAt) / (toAt - fromAt));
            }
        }

        return 0;
    }

    /// <summary>
    /// The points of the line from 0 to at least <paramref name="width"/>:
    /// the waves every 1.5 px, the heartbeat at its corners (so the spike
    /// stays sharp). Empty for a style that draws no line.
    /// </summary>
    public static IReadOnlyList<(double X, double Height)> Line(ProgressStyle style, double width, bool under = false)
    {
        var points = new List<(double, double)>();
        if (style == ProgressStyle.Heartbeat)
        {
            for (double start = 0; start <= width; start += BeatLength)
            {
                foreach (var (at, height) in Beat)
                {
                    if (start > 0 && at == 0)
                    {
                        continue;
                    }

                    points.Add((start + (at * BeatLength), height));
                }
            }

            return points;
        }

        if (style is not (ProgressStyle.Wave or ProgressStyle.Liquid))
        {
            return points;
        }

        for (double x = 0; ; x += 1.5)
        {
            points.Add((x, under ? UnderAt(x) : WaveAt(x)));
            if (x >= width)
            {
                return points;
            }
        }
    }
}
