namespace Resonate.Themes;

/// <summary>
/// The weather a special look's scenery drifts over the window
/// (<see cref="ThemeScene"/>): sakura petals that fall slowly with the wind,
/// turning and fluttering, for Japan; snowflakes of every size, and a few
/// soft out-of-focus ones, for Snow. Every motion turns a whole number of
/// times per <see cref="LoopSeconds"/>, so the clock can start again with no
/// jump. The same every time.
/// </summary>
public static class SceneWeather
{
    /// <summary>How long the clock runs before it starts again.</summary>
    public const double LoopSeconds = StageBars.LoopSeconds;

    /// <summary>A petal's and a snowflake's size at its largest, in pixels.</summary>
    public const double PetalSize = 18;

    public const double FlakeSize = 7;

    // Snow's last few flakes are large, soft and faint, as if out of focus.
    private const int SoftFlakes = 8;

    /// <summary>How many petals or flakes a scene drifts.</summary>
    public static int Count(ThemeScene scene) => scene switch
    {
        ThemeScene.Japan => 26,
        ThemeScene.Snow => 72,
        _ => 0,
    };

    /// <summary>
    /// Particle <paramref name="index"/> of <paramref name="scene"/>: where it
    /// starts across (0 to 1), how many times a second it falls (whole falls
    /// per loop) and where in its fall it starts, how far the wind carries it
    /// right as it falls (a share of the width), its sideways sway in pixels
    /// with its speed and angle, its size in pixels and its opacity, and for
    /// a petal its spin in degrees a second and its flutter's speed.
    /// </summary>
    public static Particle Get(ThemeScene scene, int index)
    {
        double H(int salt) => StageBars.Hash(index, 40 + salt);
        if (scene == ThemeScene.Japan)
        {
            var spinTurns = Math.Round(60 + (120 * H(7))) * (H(8) < 0.5 ? -1 : 1);
            return new Particle(
                X: (H(1) * 1.2) - 0.2,
                Fall: Math.Round(46 + (40 * H(2))) / LoopSeconds,
                Start: H(3),
                Wind: 0.16 + (0.18 * H(4)),
                Sway: 14 + (20 * H(5)),
                SwaySpeed: StageBars.Turns(0.6 + (0.6 * H(6))),
                SwayPhase: Math.Tau * H(9),
                Size: PetalSize * (0.7 + (0.5 * H(10))),
                Opacity: 0.7 + (0.25 * H(11)),
                Spin: 360 * spinTurns / LoopSeconds,
                Flutter: StageBars.Turns(1.5 + (1.5 * H(12))));
        }

        var soft = index >= Count(ThemeScene.Snow) - SoftFlakes;
        var size = soft ? 2.6 + (1.6 * H(10)) : 0.35 + (0.85 * H(10));
        return new Particle(
            X: H(1),
            Fall: Math.Round((soft ? 30 : 40) + (60 * Math.Min(1, size) * H(2))) / LoopSeconds,
            Start: H(3),
            Wind: (0.1 * H(4)) - 0.04,
            Sway: 6 + (18 * H(5)),
            SwaySpeed: StageBars.Turns(0.3 + (0.6 * H(6))),
            SwayPhase: Math.Tau * H(9),
            Size: FlakeSize * size,
            Opacity: soft ? 0.12 + (0.08 * H(11)) : 0.45 + (0.5 * H(11)),
            Spin: 0,
            Flutter: 0);
    }

    /// <summary>One petal or flake (see <see cref="Get"/>).</summary>
    public readonly record struct Particle(
        double X,
        double Fall,
        double Start,
        double Wind,
        double Sway,
        double SwaySpeed,
        double SwayPhase,
        double Size,
        double Opacity,
        double Spin,
        double Flutter);
}
