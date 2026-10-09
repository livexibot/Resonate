namespace Resonate.Themes;

/// <summary>
/// The weather a special look's scenery drifts over the window
/// (<see cref="ThemeScene"/>): sakura petals that fall slowly with the wind
/// (from the right, where the branch is), turning and fluttering, for Japan;
/// snowflakes of every size, a few crystals that turn slowly and a few soft
/// out-of-focus flakes, for Snow; neon dust rising from the sun, for
/// Synthwave; beads of chrome rising and wobbling, for Liquid Chrome; rain in
/// the city's neon, for Cyberpunk; drops running down the window in fits
/// and starts, with fine rain beyond them, for Afterhours. The smaller
/// petals, flakes, motes, beads and streaks are farther away: they pass
/// behind the player (<see cref="Particle.Behind"/>), the rest in front of
/// everything; the farther petals are also slower and end their fall over
/// the page's middle, so they cross the player. Every motion turns a whole number of
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

    /// <summary>A neon mote's size at its largest, a chrome bead's and a raindrop's on the window, in pixels.</summary>
    public const double MoteSize = 12;

    public const double BeadSize = 22;

    public const double DropSize = 18;

    // Synthwave's last few motes are large, soft and faint; Afterhours' first few are drops on the window, the rest rain beyond it.
    private const int SoftMotes = 5;
    private const int Drops = 14;

    // Neon: the sun's pink and orange, the grid's cyan and the mountains' violet; the rain mostly pale, some of it lit by the signs.
    private static readonly ThemeColor[] Neon = [ThemeColor.FromRgb(0xFF4FD8), ThemeColor.FromRgb(0x3DF2FF), ThemeColor.FromRgb(0xB06CFF), ThemeColor.FromRgb(0xFF9A3D)];
    private static readonly ThemeColor[] RainLight = [ThemeColor.FromRgb(0xCFE6FF), ThemeColor.FromRgb(0x5CF2FF), ThemeColor.FromRgb(0xFF5CE1)];

    /// <summary>How many petals, flakes, motes, beads or drops a scene drifts.</summary>
    public static int Count(ThemeScene scene) => scene switch
    {
        ThemeScene.Japan => 26,
        ThemeScene.Snow => 80,
        ThemeScene.Synthwave => 44,
        ThemeScene.LiquidChrome => 22,
        ThemeScene.Cyberpunk => 96,
        ThemeScene.Afterhours => 50,
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
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count(scene));
        double H(int salt) => StageBars.Hash(index, 40 + salt);
        switch (scene)
        {
            case ThemeScene.Synthwave:
                return Mote(index, H);
            case ThemeScene.LiquidChrome:
                return Bead(index, H);
            case ThemeScene.Cyberpunk:
                return Rain(index, H);
            case ThemeScene.Afterhours:
                return index < Drops ? Drop(H) : Drizzle(H);
        }

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

    /// <summary>A glowing speck of neon dust rising from the sun, slow and swaying; the last few large, soft and faint.</summary>
    private static Particle Mote(int index, Func<int, double> h)
    {
        var soft = index >= Count(ThemeScene.Synthwave) - SoftMotes;
        var behind = !soft && index % 5 is 1 or 3;
        var size = soft ? 2.6 + (1.2 * h(10)) : behind ? 0.3 + (0.2 * h(10)) : 0.55 + (0.45 * h(10));
        return new Particle(
            X: h(1),
            Fall: Math.Round(behind ? 24 + (16 * h(2)) : 36 + (34 * h(2))) / LoopSeconds,
            Start: h(3),
            Wind: 0.04 * (h(4) - 0.5),
            Sway: 10 + (26 * h(5)),
            SwaySpeed: StageBars.Turns(0.15 + (0.3 * h(6))),
            SwayPhase: Math.Tau * h(9),
            Size: MoteSize * size,
            Opacity: soft ? 0.12 + (0.08 * h(11)) : behind ? 0.4 + (0.25 * h(11)) : 0.6 + (0.35 * h(11)),
            Spin: 0,
            Flutter: 0,
            Sprite: null,
            Behind: behind,
            Rise: true,
            Tint: Neon[h(13) switch { < 0.38 => 0, < 0.68 => 1, < 0.88 => 2, _ => 3 }]);
    }

    /// <summary>A bead of chrome rising slowly, swaying and wobbling as liquid does.</summary>
    private static Particle Bead(int index, Func<int, double> h)
    {
        var behind = index % 5 is 1 or 3;
        return new Particle(
            X: 0.03 + (0.94 * h(1)),
            Fall: Math.Round(behind ? 16 + (12 * h(2)) : 24 + (20 * h(2))) / LoopSeconds,
            Start: h(3),
            Wind: 0.05 * (h(4) - 0.5),
            Sway: behind ? 6 + (8 * h(5)) : 10 + (16 * h(5)),
            SwaySpeed: StageBars.Turns(0.2 + (0.3 * h(6))),
            SwayPhase: Math.Tau * h(9),
            Size: BeadSize * (behind ? 0.25 + (0.15 * h(10)) : 0.45 + (0.55 * h(10))),
            Opacity: behind ? 0.55 + (0.2 * h(11)) : 0.85 + (0.15 * h(11)),
            Spin: 0,
            Flutter: StageBars.Turns(0.35 + (0.45 * h(12))),
            Sprite: SceneSprite.ChromeBead,
            Behind: behind,
            Rise: true);
    }

    /// <summary>A streak of rain, slanting with the wind, quick and faint; some of it lit cyan or magenta by the signs.</summary>
    private static Particle Rain(int index, Func<int, double> h)
    {
        var behind = index % 2 == 1;
        return new Particle(
            X: h(1) * 1.2,
            Fall: Math.Round(LoopSeconds / (behind ? 0.85 + (0.4 * h(2)) : 0.55 + (0.3 * h(2)))) / LoopSeconds,
            Start: h(3),
            Wind: 0,
            Sway: 0,
            SwaySpeed: StageBars.Turns(1),
            SwayPhase: 0,
            Size: behind ? 16 + (14 * h(10)) : 34 + (30 * h(10)),
            Opacity: behind ? 0.14 + (0.14 * h(11)) : 0.24 + (0.24 * h(11)),
            Spin: 0,
            Flutter: 0,
            Sprite: null,
            Behind: behind,
            Width: behind ? 0.9 : 1.3,
            Slant: -0.17 - (0.03 * h(4)),
            Tint: RainLight[h(13) switch { < 0.7 => 0, < 0.85 => 1, _ => 2 }]);
    }

    /// <summary>A drop on the window: it gathers somewhere on the glass, then runs down in fits and starts, leaving a wet trail.</summary>
    private static Particle Drop(Func<int, double> h) => new(
        X: 0.03 + (0.94 * h(1)),
        Fall: Math.Round(LoopSeconds / (16 + (14 * h(2)))) / LoopSeconds,
        Start: h(3),
        Wind: 0,
        Sway: 1.5 + (2.5 * h(5)),
        SwaySpeed: StageBars.Turns(0.6 + (0.6 * h(6))),
        SwayPhase: Math.Tau * h(9),
        Size: DropSize * (0.5 + (0.5 * h(10))),
        Opacity: 0.75 + (0.2 * h(11)),
        Spin: 0,
        Flutter: 0,
        Sprite: SceneSprite.Droplet,
        Behind: false,
        Halts: 2 + (int)(3 * h(12)),
        From: 0.05 + (0.5 * h(14)),
        Tail: 2.5 + (1.5 * h(15)));

    /// <summary>Fine rain beyond the window, in the city's warm and cool light.</summary>
    private static Particle Drizzle(Func<int, double> h) => new(
        X: h(1) * 1.08,
        Fall: Math.Round(LoopSeconds / (1.1 + (0.6 * h(2)))) / LoopSeconds,
        Start: h(3),
        Wind: 0,
        Sway: 0,
        SwaySpeed: StageBars.Turns(1),
        SwayPhase: 0,
        Size: 14 + (12 * h(10)),
        Opacity: 0.12 + (0.12 * h(11)),
        Spin: 0,
        Flutter: 0,
        Sprite: null,
        Behind: true,
        Width: 0.9,
        Slant: -0.05 - (0.02 * h(4)),
        Tint: h(13) < 0.55 ? ThemeColor.FromRgb(0xFFE6C8) : ThemeColor.FromRgb(0xCFE0FF));

    /// <summary>
    /// One petal, flake, mote, bead, streak or drop (see <see cref="Get"/>).
    /// <see cref="Rise"/>: it rises from the bottom instead of falling.
    /// <see cref="Width"/>: a streak that wide and <see cref="Size"/> long
    /// (0: round). <see cref="Slant"/>: a streak's sideways drift for each
    /// unit it falls, which it leans along (instead of <see cref="Wind"/>).
    /// <see cref="Halts"/>: how many times a fall stops and starts again.
    /// <see cref="From"/>: where its fall starts, a share of the height.
    /// <see cref="Tint"/>: a soft dot's or a streak's colour (null: white).
    /// <see cref="Tail"/>: a drop's wet trail, as long as that many of its sizes.
    /// </summary>
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
        bool Behind,
        bool Rise = false,
        double Width = 0,
        double Slant = 0,
        int Halts = 0,
        double From = 0,
        ThemeColor? Tint = null,
        double Tail = 0);
}
