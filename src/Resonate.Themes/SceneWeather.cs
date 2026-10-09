namespace Resonate.Themes;

/// <summary>
/// The weather a special look's scenery drifts over the window
/// (<see cref="ThemeScene"/>): sakura petals that fall slowly with the wind
/// (from the right, where the branch is), turning and fluttering, for Japan;
/// snowflakes of every size, a few crystals that turn slowly and a few soft
/// out-of-focus flakes, for Snow. The smaller petals, flakes and crystals
/// are farther away: they pass behind the player (<see cref="Particle.Behind"/>),
/// the rest in front of everything; the farther petals are also slower and
/// end their fall over the page's middle, so they cross the player. Every motion turns a whole number of
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

    /// <summary>A snow crystal's size at its largest, in pixels.</summary>
    public const double CrystalSize = 20;

    // Snow's last few flakes are large, soft and faint, as if out of focus; a few before them are crystals.
    private const int SoftFlakes = 8;
    private const int Crystals = 10;

    // The share of the smallest flakes and crystals (by the hash their size comes from) that pass behind the player; of the petals, two in five.
    private const double BehindFlakes = 0.5;
    private const double BehindCrystals = 0.5;

    /// <summary>Where across the panels (a share of their width) the farther petals end their fall: over the page's middle, where the player floats.</summary>
    public const double BehindPetalsFrom = 0.35;

    public const double BehindPetalsTo = 0.85;

    /// <summary>How many petals or flakes a scene drifts.</summary>
    public static int Count(ThemeScene scene) => scene switch
    {
        ThemeScene.Japan => 26,
        ThemeScene.Snow => 80,
        _ => 0,
    };

    /// <summary>
    /// Particle <paramref name="index"/> of <paramref name="scene"/>: where it
    /// starts across (0 to 1), how many times a second it falls (whole falls
    /// per loop) and where in its fall it starts, how far the wind carries it
    /// as it falls (a share of the width, to the left when below 0), its
    /// sideways sway in pixels
    /// with its speed and angle, its size in pixels and its opacity, and for
    /// a petal or a crystal its spin in degrees a second and how fast it
    /// flutters or tilts, the picture it is drawn with (none for a plain
    /// flake, a soft dot), and whether it is far enough away to pass behind
    /// the player (the soft flakes, out of focus, are the nearest of all).
    /// </summary>
    public static Particle Get(ThemeScene scene, int index)
    {
        double H(int salt) => StageBars.Hash(index, 40 + salt);
        if (scene == ThemeScene.Japan)
        {
            // The farther petals are smaller, fainter and slower, and blow in from the branch's side to end their fall
            // over the middle of the page, where the player floats, so they pass behind it.
            var behind = index % 5 is 1 or 3;
            var wind = -(0.16 + (0.18 * H(4)));
            var spinTurns = Math.Round(60 + (120 * H(7))) * (H(8) < 0.5 ? -1 : 1);
            return new Particle(
                X: behind ? BehindPetalsFrom + ((BehindPetalsTo - BehindPetalsFrom) * H(1)) - wind : H(1) * 1.2,
                Fall: Math.Round(behind ? 32 + (28 * H(2)) : 46 + (40 * H(2))) / LoopSeconds,
                Start: H(3),
                Wind: wind,
                Sway: behind ? 9 + (12 * H(5)) : 14 + (20 * H(5)),
                SwaySpeed: StageBars.Turns(0.6 + (0.6 * H(6))),
                SwayPhase: Math.Tau * H(9),
                Size: PetalSize * (behind ? 0.55 + (0.2 * H(10)) : 0.8 + (0.4 * H(10))),
                Opacity: behind ? 0.6 + (0.2 * H(11)) : 0.7 + (0.25 * H(11)),
                Spin: 360 * spinTurns / LoopSeconds,
                Flutter: StageBars.Turns(1.5 + (1.5 * H(12))),
                Sprite: H(13) switch
                {
                    < 0.34 => SceneSprite.PetalPink,
                    < 0.64 => SceneSprite.PetalPale,
                    < 0.84 => SceneSprite.PetalWhite,
                    _ => SceneSprite.PetalDeep,
                },
                Behind: behind);
        }

        var soft = index >= Count(ThemeScene.Snow) - SoftFlakes;
        if (!soft && index >= Count(ThemeScene.Snow) - SoftFlakes - Crystals)
        {
            // A crystal falls a little slower than the flakes around it, turning once every half minute to a minute and tilting slowly.
            var turns = Math.Round(16 + (30 * H(7))) * (H(8) < 0.5 ? -1 : 1);
            return new Particle(
                X: H(1),
                Fall: Math.Round(34 + (24 * H(2))) / LoopSeconds,
                Start: H(3),
                Wind: (0.1 * H(4)) - 0.04,
                Sway: 10 + (16 * H(5)),
                SwaySpeed: StageBars.Turns(0.25 + (0.4 * H(6))),
                SwayPhase: Math.Tau * H(9),
                Size: CrystalSize * (0.6 + (0.4 * H(10))),
                Opacity: 0.55 + (0.35 * H(11)),
                Spin: 360 * turns / LoopSeconds,
                Flutter: StageBars.Turns(0.12 + (0.14 * H(13))),
                Sprite: H(12) < 0.6 ? SceneSprite.Crystal : SceneSprite.CrystalPlate,
                Behind: H(10) < BehindCrystals);
        }

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
            Flutter: 0,
            Sprite: null,
            Behind: !soft && H(10) < BehindFlakes);
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
        double Flutter,
        SceneSprite? Sprite,
        bool Behind);
}
