namespace Resonate.Themes;

/// <summary>
/// A timing curve: a cubic Bézier from (0, 0) to (1, 1) with two control
/// points, the same kind the compositor and CSS use. It turns the share of
/// time gone (0 to 1) into the share of the way gone.
/// </summary>
public readonly record struct EasingCurve(double X1, double Y1, double X2, double Y2)
{
    /// <summary>Steady speed from start to end.</summary>
    public static EasingCurve Linear { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// Starts from rest and comes to rest: no speed at either end. That holds
    /// exactly when the first control point is level with the start, the
    /// second level with the end, and both lie strictly inside in time.
    /// </summary>
    public bool EasesInAndOut => Y1 == 0 && Y2 == 1 && X1 is > 0 and < 1 && X2 is > 0 and < 1;

    /// <summary>The share of the way gone at <paramref name="time"/> (the share of the duration gone).</summary>
    public double Evaluate(double time)
    {
        if (time <= 0)
        {
            return 0;
        }

        if (time >= 1)
        {
            return 1;
        }

        // Time rises steadily along the curve (control points between 0 and
        // 1), so halving finds the point for this time; 40 steps is exact to
        // well past what a frame can show.
        double low = 0, high = 1, u = time;
        for (var i = 0; i < 40; i++)
        {
            if (Bernstein(X1, X2, u) < time)
            {
                low = u;
            }
            else
            {
                high = u;
            }

            u = (low + high) / 2;
        }

        return Bernstein(Y1, Y2, u);
    }

    // One coordinate of the curve at parameter u: 3a·u(1−u)² + 3b·u²(1−u) + u³.
    private static double Bernstein(double a, double b, double u) =>
        ((((1 - (3 * b) + (3 * a)) * u) + ((3 * b) - (6 * a))) * u + (3 * a)) * u;
}

/// <summary>How long a switching animation lasts, and how its speed changes on the way.</summary>
public readonly record struct ThemeTransitionSpec(TimeSpan Duration, EasingCurve Curve);

/// <summary>
/// The timing of every switching animation, and the pool "Surprise me" picks
/// from. The owner asked for animations of one to two seconds that speed up
/// gently and slow down only at the very end, so every kind but None eases
/// in and out over 1.2 to 1.6 s. Kept here, away from WinUI, so it is tested.
/// </summary>
public static class ThemeTransitionCatalog
{
    /// <summary>How far apart (in progress) one strip in Blinds starts after the one before.</summary>
    public const double BlindsStagger = 0.05;

    /// <summary>For movement and growing shapes: an ease-in-out cubic.</summary>
    public static EasingCurve Emphasized { get; } = new(0.65, 0, 0.35, 1);

    /// <summary>For fading: an ease-in-out quad, softer than <see cref="Emphasized"/>.</summary>
    public static EasingCurve Soft { get; } = new(0.45, 0, 0.55, 1);

    /// <summary>For a cascade whose parts each ease on their own (Blinds): only a light ease over the whole.</summary>
    public static EasingCurve Gentle { get; } = new(0.3, 0, 0.7, 1);

    /// <summary>
    /// A choice in Customize (a menu, a switch) cross-fades briefly instead:
    /// one to two seconds on every edit would make editing feel slow.
    /// </summary>
    public static ThemeTransitionSpec QuickEdit { get; } = new(TimeSpan.FromMilliseconds(320), Soft);

    /// <summary>What "Surprise me" picks from: every kind that animates.</summary>
    public static IReadOnlyList<ThemeTransitionKind> RandomPool { get; } =
    [
        ThemeTransitionKind.Morph,
        ThemeTransitionKind.Ripple,
        ThemeTransitionKind.Split,
        ThemeTransitionKind.Blinds,
        ThemeTransitionKind.Wipe,
        ThemeTransitionKind.Fade,
        ThemeTransitionKind.Grow,
    ];

    /// <summary>
    /// The duration and curve of a kind. A ripple's duration also depends on
    /// how far it reaches (<see cref="RippleDuration"/>); Random is resolved
    /// before anything plays and times like Morph.
    /// </summary>
    public static ThemeTransitionSpec Spec(ThemeTransitionKind kind) => kind switch
    {
        ThemeTransitionKind.None => new(TimeSpan.Zero, EasingCurve.Linear),
        ThemeTransitionKind.Fade => new(Milliseconds(1200), Soft),
        ThemeTransitionKind.Grow => new(Milliseconds(1300), Emphasized),
        ThemeTransitionKind.Ripple => new(RippleDuration(0), Emphasized),
        ThemeTransitionKind.Blinds => new(Milliseconds(1400), Gentle),
        _ => new(Milliseconds(1200), Emphasized),
    };

    /// <summary>
    /// A ripple that reaches further (in device-independent pixels, to the
    /// window's furthest corner) takes a little longer: 1.2 s in a small
    /// window, up to 1.6 s across a large screen.
    /// </summary>
    public static TimeSpan RippleDuration(double reach) =>
        Milliseconds(Math.Clamp(1000 + (0.12 * Math.Max(reach, 0)), 1200, 1600));

    /// <summary>Random becomes a kind from <see cref="RandomPool"/> (<paramref name="next"/> picks an index below its argument); any other kind stays.</summary>
    public static ThemeTransitionKind Resolve(ThemeTransitionKind kind, Func<int, int> next) =>
        kind == ThemeTransitionKind.Random ? RandomPool[Math.Clamp(next(RandomPool.Count), 0, RandomPool.Count - 1)] : kind;

    /// <summary>
    /// Whether the kind reveals the new look itself through a growing shape
    /// (instead of moving a picture of the old one away). The window must be
    /// opaque for that, so with Mica or acrylic these fall back to Fade.
    /// </summary>
    public static bool RevealsLive(ThemeTransitionKind kind) => kind is ThemeTransitionKind.Ripple or ThemeTransitionKind.Grow;

    /// <summary>
    /// The size (0 to 1) of a shape that reveals the new look at progress
    /// <paramref name="progress"/>: its square root, so the shape's area, which
    /// is what the eye judges, follows the curve. A shape that grew in width
    /// with the curve would seem to rush at first and crawl at the end.
    /// </summary>
    public static double RevealScale(double progress) => Math.Sqrt(Math.Clamp(progress, 0, 1));

    /// <summary>
    /// How far the furthest corner of a window of <paramref name="width"/> by
    /// <paramref name="height"/> is from (<paramref name="x"/>, <paramref name="y"/>):
    /// a circle that size from there covers the whole window.
    /// </summary>
    public static double Reach(double width, double height, double x, double y) =>
        Math.Sqrt(Math.Pow(Math.Max(x, width - x), 2) + Math.Pow(Math.Max(y, height - y), 2));

    /// <summary>
    /// The rounded rectangle that grows from the middle of the window
    /// (Grow): its corner radius, and how far it reaches past each edge at
    /// full size so its rounded corners still cover the window's corners
    /// (a corner of radius r leaves r·(1 − 1/√2) uncovered along the diagonal).
    /// </summary>
    public static (double Corner, double Margin) GrowShape(double width, double height)
    {
        var corner = Math.Clamp(0.07 * Math.Min(width, height), 0, 96);
        return (corner, (0.3 * corner) + 2);
    }

    /// <summary>
    /// One strip's own progress (0 to 1) in Blinds at the whole animation's
    /// <paramref name="progress"/>: strips start <see cref="BlindsStagger"/>
    /// apart, left to right, and each takes the rest of the way, so the first
    /// starts at 0 and the last ends at 1.
    /// </summary>
    public static double StripProgress(double progress, int index, int count) =>
        Math.Clamp((progress - (index * BlindsStagger)) / BlindsShare(count), 0, 1);

    /// <summary>The share of Blinds' progress that each of <paramref name="count"/> strips takes to fold.</summary>
    public static double BlindsShare(int count) => 1 - (Math.Max(count - 1, 0) * BlindsStagger);

    private static TimeSpan Milliseconds(double value) => TimeSpan.FromMilliseconds(value);
}
