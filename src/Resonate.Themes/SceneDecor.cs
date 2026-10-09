using System.Numerics;

namespace Resonate.Themes;

/// <summary>
/// The special looks' decorations over the window, as plain shapes the app
/// draws (see <see cref="ThemeScene"/>): a cherry branch reaching over the
/// page's top right corner, with petals that gather in piles on the player's
/// shoulders and on the page's top edge, for Japan; a snowy pine bough in
/// the same place, snow that settles on the panels and the player, icicles
/// that grow under it and frost in the corners, for Snow. Everything here is
/// in the content's units (the app multiplies by App size), the same every
/// time, and laid out in the frame of the edge or corner it sits on: x along
/// the edge from its left end, y down from its straight part.
/// </summary>
public static class SceneDecor
{
    /// <summary>How long after a scene appears its decorations have all gathered, in seconds.</summary>
    public const double GatherSeconds = 150;

    /// <summary>How long a petal takes to fall onto its pile, in seconds.</summary>
    public const double FlightSeconds = 2.8;

    /// <summary>The seconds between two petals landing (the piles take turns).</summary>
    public const double LandingEvery = 2;

    /// <summary>When the first petal that falls lands; the lowest few are there from the start.</summary>
    public const double FirstLanding = 2.5;

    /// <summary>How many of each pile's lowest petals are there from the start, so it never begins bare.</summary>
    public const int SeededPetals = 3;

    /// <summary>A landing time for petals that are there from the start.</summary>
    public const double Landed = -1000;

    /// <summary>
    /// How far below an edge's straight part its rounded corners lie at
    /// <paramref name="x"/>, for an edge <paramref name="width"/> long whose
    /// corners have <paramref name="radius"/>: 0 along the straight part,
    /// <paramref name="radius"/> at the very ends.
    /// </summary>
    public static double EdgeDrop(double x, double width, double radius)
    {
        radius = Math.Clamp(radius, 0, Math.Max(0, width / 2));
        var from = Math.Clamp(Math.Min(x, width - x), 0, radius);
        if (radius <= 0 || from >= radius)
        {
            return 0;
        }

        var dx = radius - from;
        return radius - Math.Sqrt(Math.Max(0, (radius * radius) - (dx * dx)));
    }

    /// <summary>The edge's slope at <paramref name="x"/> in radians: 0 along the straight part, steep near the ends.</summary>
    public static double EdgeSlope(double x, double width, double radius)
    {
        const double h = 0.25;
        var a = EdgeDrop(x - h, width, radius);
        var b = EdgeDrop(x + h, width, radius);
        return Math.Atan2(b - a, 2 * h);
    }

    // ------------------------------------------------------------ petals

    /// <summary>
    /// A petal or blossom resting in a pile, where it lands and where it falls
    /// from. <see cref="Angle"/> is in degrees; <see cref="Squash"/> shortens
    /// it across, as a petal lying on the edge is seen from the side.
    /// <see cref="Spin"/> is how far it turns on its way down, in degrees.
    /// <see cref="Stirs"/>: it lies on top and shivers when a breeze passes.
    /// </summary>
    public readonly record struct PilePetal(
        SceneSprite Sprite,
        double X,
        double Y,
        double Size,
        double Angle,
        double Squash,
        double FromX,
        double FromY,
        double Spin,
        bool Stirs,
        double Breeze);

    /// <summary>
    /// A pile of <paramref name="count"/> petals on the left end of an edge
    /// <paramref name="width"/> long with corners of <paramref name="radius"/>:
    /// a low mound up to <paramref name="height"/> tall that starts on the
    /// rounded corner and runs about <paramref name="length"/> along the
    /// straight part, highest near the corner, with a blossom or two on top
    /// when <paramref name="blossoms"/>. Lowest petals first, which is the
    /// order they land in. The app draws a pile for the right end mirrored;
    /// <paramref name="mirrored"/> makes its petals still come from the right
    /// of the window, with the wind.
    /// </summary>
    public static IReadOnlyList<PilePetal> Pile(int seed, double width, double radius, double length, double height, int count, bool blossoms, bool mirrored, double petalSize = 11)
    {
        var random = new Seeded(seed);
        radius = Math.Clamp(radius, 0, Math.Max(0, width / 2));
        var start = radius * 0.35;
        var end = Math.Max(start + 8, Math.Min(radius + length, (width / 2) - 6));
        var peak = start + ((end - start) * 0.34);
        var petals = new List<(PilePetal Petal, double Height)>(count + 2);

        double Mound(double x)
        {
            // Rises quickly from the corner, falls slowly along the edge.
            var t = x <= peak ? (x - start) / (peak - start) : 1 - ((x - peak) / (end - peak));
            t = Math.Clamp(t, 0, 1);
            return Math.Pow(Math.Sin(t * Math.PI / 2), x <= peak ? 0.8 : 1.6);
        }

        for (var i = 0; i < count; i++)
        {
            // More petals where the mound is high.
            double x;
            do
            {
                x = start + (random.Next() * (end - start));
            }
            while (random.Next() > 0.25 + (0.75 * Mound(x)));

            var layer = Math.Sqrt(random.Next());
            var lift = layer * height * Mound(x);
            var size = petalSize * (0.8 + (0.4 * random.Next()));
            var y = EdgeDrop(x, width, radius) - lift + (size * 0.1);
            var slope = EdgeSlope(x, width, radius) * 180 / Math.PI;

            // Lying along the edge, seen from the side, but no two quite alike; now and then one points the other way.
            var angle = 90 + slope + ((random.Next() - 0.5) * 56) + (random.Next() < 0.3 ? 180 : 0);
            var sprite = random.Next() switch
            {
                < 0.36 => SceneSprite.PetalPink,
                < 0.66 => SceneSprite.PetalPale,
                < 0.86 => SceneSprite.PetalWhite,
                _ => SceneSprite.PetalDeep,
            };
            petals.Add((Flying(new PilePetal(sprite, x, y, size, angle, 0.36 + (0.38 * random.Next()), 0, 0, 0, layer > 0.62, 0), random, mirrored), lift));
        }

        if (blossoms)
        {
            // A whole blossom near the top of the mound, and sometimes a second.
            var tops = Blossoms(seed);
            for (var i = 0; i < tops; i++)
            {
                var x = peak + ((random.Next() - 0.35) * (end - start) * 0.4);
                var lift = height * Mound(x) * 0.9;
                var size = petalSize * (1.15 + (0.2 * random.Next()));
                var y = EdgeDrop(x, width, radius) - lift - (size * 0.08);
                var blossom = new PilePetal(SceneSprite.Blossom, x, y, size, (random.Next() - 0.5) * 50, 0.62 + (0.2 * random.Next()), 0, 0, 0, true, 0);
                petals.Add((Flying(blossom, random, mirrored), lift + height));
            }
        }

        return [.. petals.OrderBy(p => p.Height).Select(p => p.Petal)];
    }

    /// <summary>How many whole blossoms lie on a pile made with <paramref name="seed"/> (and blossoms): the same however long its edge.</summary>
    public static int Blossoms(int seed) => new Seeded(seed ^ 0x0B1055).Next() < 0.7 ? 1 : 2;

    /// <summary>Where a petal falls from: above and to the right (with the wind), turning as it comes.</summary>
    private static PilePetal Flying(PilePetal petal, Seeded random, bool mirrored)
    {
        var drift = 50 + (45 * random.Next());
        return petal with
        {
            FromX = petal.X + (mirrored ? -drift : drift),
            FromY = petal.Y - (120 + (60 * random.Next())),
            Spin = (random.Next() < 0.5 ? -1 : 1) * (200 + (220 * random.Next())),
            Breeze = random.Next(),
        };
    }

    /// <summary>
    /// When each petal of several piles lands, in seconds after the scene
    /// appears: each pile's <see cref="SeededPetals"/> lowest are there from
    /// the start (<see cref="Landed"/>), then the piles take turns, one petal
    /// every <see cref="LandingEvery"/> seconds, lowest first.
    /// </summary>
    public static double[][] LandingTimes(IReadOnlyList<int> counts)
    {
        var times = counts.Select(c => new double[Math.Max(0, c)]).ToArray();
        var next = FirstLanding;
        var left = true;
        for (var round = 0; left; round++)
        {
            left = false;
            for (var pile = 0; pile < times.Length; pile++)
            {
                if (round >= times[pile].Length)
                {
                    continue;
                }

                left = true;
                if (round < SeededPetals)
                {
                    times[pile][round] = Landed;
                    continue;
                }

                times[pile][round] = next;
                next += LandingEvery;
            }
        }

        return times;
    }

    // ------------------------------------------------------------ snow

    /// <summary>How far through the gathering the snow on the edges has all settled (it starts thin).</summary>
    public const double SnowSettles = 0.45;

    /// <summary>How far through the gathering icicles start to grow, once the snow above them has settled.</summary>
    public const double IciclesFrom = 0.5;

    /// <summary>
    /// Snow lying along a top edge: <see cref="Outline"/> is the snow itself
    /// (its lumpy top, then its underside drooping a little over the edge),
    /// <see cref="Shade"/> the blue shadow along its underside, <see cref="Crest"/>
    /// the bright line along its top, <see cref="Icicles"/> what hangs under it
    /// and <see cref="Glints"/> where it catches the light. All in the edge's
    /// frame: x from the left end, y down from the straight part.
    /// </summary>
    public sealed record SnowCap(
        IReadOnlyList<Vector2> Outline,
        IReadOnlyList<Vector2> Shade,
        IReadOnlyList<Vector2> Crest,
        IReadOnlyList<Icicle> Icicles,
        IReadOnlyList<Vector2> Glints);

    /// <summary>
    /// An icicle hanging from (<see cref="X"/>, <see cref="Y"/>):
    /// <see cref="Length"/> long, <see cref="Width"/> wide at its root, its tip
    /// <see cref="Bend"/> to the side. It grows in from <see cref="GrowAt"/>
    /// seconds after the scene appears, over <see cref="GrowFor"/> seconds.
    /// </summary>
    public readonly record struct Icicle(double X, double Y, double Length, double Width, double Bend, double GrowAt, double GrowFor);

    /// <summary>
    /// Snow on the top edge of a panel or the player, <paramref name="width"/>
    /// long with corners of <paramref name="radius"/>: about
    /// <paramref name="depth"/> deep along the straight part, thinning to
    /// nothing a little way down each corner, with up to
    /// <paramref name="icicles"/> icicles at most <paramref name="icicleLength"/>
    /// long under it (none when 0), more of them towards the corners.
    /// </summary>
    public static SnowCap Cap(int seed, double width, double radius, double depth, int icicles, double icicleLength)
    {
        radius = Math.Clamp(radius, 0, Math.Max(0, width / 2));
        if (width < 24 || depth <= 0)
        {
            return new SnowCap([], [], [], [], []);
        }

        // From partway down one corner to partway down the other.
        var reach = radius * 0.24;
        var from = reach;
        var to = width - reach;
        var half = (to - from) / 2;
        var rise = Math.Min((radius * 0.8) + 28, (to - from) / 3);
        double Envelope(double x) => Smooth(from, from + rise, x) * Smooth(to, to - rise, x);

        var shape = new Seeded(seed);
        var phases = (shape.Next() * Math.Tau, shape.Next() * Math.Tau);
        double Thickness(double x) =>
            depth * Envelope(x) * (0.8 + (0.13 * Math.Sin((x / 41) + phases.Item1)) + (0.07 * Math.Sin((x / 15) + phases.Item2)));

        // Lobes, icicles and glints are laid out from each corner towards the
        // middle, each side with numbers of its own, so an edge that grows or
        // shrinks keeps them where they were beside its corners; those that
        // reach the middle fade away rather than jump.
        double At(bool left, double along) => left ? from + along : to - along;
        double Fade(double along) => Smooth(half, half - 30, along);

        var lobes = new List<(double X, double Half, double Deep, bool Left, double Along)>();
        foreach (var left in (ReadOnlySpan<bool>)[true, false])
        {
            var random = new Seeded((seed * 31) + (left ? 1 : 2));
            for (var along = rise * 0.4; ;)
            {
                var size = 6 + (9 * random.Next());
                var deep = (1.6 + (2.6 * random.Next())) * Math.Min(1, depth / 12);
                var gap = 8 + (40 * random.Next());
                var centre = along + size;
                if (centre >= half)
                {
                    break;
                }

                lobes.Add((At(left, centre), size, deep * Fade(centre), left, centre));
                along += (2 * size) + gap;
            }
        }

        double Droop(double x)
        {
            var droop = 1.2;
            foreach (var (lx, size, deep, _, _) in lobes)
            {
                var d = Math.Abs(x - lx) / size;
                if (d < 1)
                {
                    droop += deep * Math.Pow(Math.Cos(d * Math.PI / 2), 1.5);
                }
            }

            return droop * Envelope(x);
        }

        var steps = Math.Max(16, (int)Math.Ceiling((to - from) / 2.5));
        var top = new List<Vector2>(steps + 1);
        var under = new List<Vector2>(steps + 1);
        var shade = new List<Vector2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var x = from + ((to - from) * i / steps);
            var edge = EdgeDrop(x, width, radius);
            var droop = Droop(x);
            top.Add(new Vector2((float)x, (float)(edge - Thickness(x))));
            under.Add(new Vector2((float)x, (float)(edge + droop)));
            shade.Add(new Vector2((float)x, (float)(edge + droop - (Math.Min(3.2, depth * 0.3) * Envelope(x)))));
        }

        var outline = new List<Vector2>(top);
        outline.AddRange(Enumerable.Reverse(under));
        var band = new List<Vector2>(under);
        band.AddRange(Enumerable.Reverse(shade));

        // Icicles hang more often near the corners, from the lowest point of a lobe when one is close.
        var hanging = new List<Icicle>();
        if (icicles > 0 && icicleLength > 0)
        {
            foreach (var left in (ReadOnlySpan<bool>)[true, false])
            {
                var random = new Seeded((seed * 31) + (left ? 3 : 4));
                var count = left ? (icicles + 1) / 2 : icicles / 2;
                for (var i = 0; i < count; i++)
                {
                    var along = (rise * 0.5) + (Math.Pow(random.Next(), 1.5) * 300);
                    var grow = (Length: random.Next(), Width: random.Next(), Bend: random.Next(), At: random.Next(), For: random.Next());
                    var lobe = lobes.Where(l => l.Left == left && Math.Abs(l.Along - along) < l.Half).Select(l => l.Along).DefaultIfEmpty(along).First();
                    along = lobe;
                    var fade = Fade(along);
                    var x = At(left, along);
                    if (fade <= 0.05 || hanging.Any(h => Math.Abs(h.X - x) < 5))
                    {
                        continue;
                    }

                    var corner = 1 - Math.Clamp(along / 300, 0, 1);
                    var length = icicleLength * (0.3 + (0.7 * grow.Length)) * (0.65 + (0.35 * corner)) * fade;
                    hanging.Add(new Icicle(
                        x,
                        EdgeDrop(x, width, radius) + Droop(x) - 0.6,
                        length,
                        Math.Clamp(length * (0.2 + (0.1 * grow.Width)), 2.2, 6.5),
                        (grow.Bend - 0.5) * length * 0.18,
                        GatherSeconds * (IciclesFrom + (0.25 * grow.At)),
                        20 + (17 * grow.For)));
                }
            }
        }

        var glints = new List<Vector2>();
        foreach (var left in (ReadOnlySpan<bool>)[true, false])
        {
            var random = new Seeded((seed * 31) + (left ? 5 : 6));
            for (var along = rise + 40; ;)
            {
                along += 60 * random.Next();
                var lift = 0.35 + (0.4 * random.Next());
                if (along >= half - 20)
                {
                    break;
                }

                var x = At(left, along);
                glints.Add(new Vector2((float)x, (float)(EdgeDrop(x, width, radius) - (Thickness(x) * lift))));
                along += 160;
            }
        }

        return new SnowCap(outline, band, top, hanging, glints);
    }

    /// <summary>
    /// Icicles hanging from the bottom edge of something
    /// <paramref name="width"/> wide with corners of <paramref name="radius"/>
    /// (the player): their roots lie on the edge, y measured up from its
    /// straight part, and they hang down from there. They keep their places
    /// beside the corners however wide it is.
    /// </summary>
    public static IReadOnlyList<Icicle> Fringe(int seed, double width, double radius, int count, double maxLength)
    {
        var random = new Seeded(seed);
        var hanging = new List<Icicle>(count);
        radius = Math.Clamp(radius, 0, Math.Max(0, width / 2));
        for (var i = 0; i < count && width > 40; i++)
        {
            // In two groups, one towards each end, just inside the corners.
            var left = i % 2 == 0;
            var along = (radius * 0.7) + (random.Next() * random.Next() * 160);
            var length = maxLength * (0.35 + (0.65 * random.Next()));
            var (thin, bend, at, grow) = (random.Next(), random.Next(), random.Next(), random.Next());
            var x = left ? along : width - along;
            if (along > (width / 2) - 10 || hanging.Any(h => Math.Abs(h.X - x) < 6))
            {
                continue;
            }

            hanging.Add(new Icicle(
                x,
                EdgeDrop(x, width, radius) - 0.5,
                length,
                Math.Clamp(length * (0.24 + (0.08 * thin)), 2.2, 5.5),
                (bend - 0.5) * length * 0.15,
                GatherSeconds * (IciclesFrom + (0.25 * at)),
                20 + (17 * grow)));
        }

        return hanging;
    }

    /// <summary>
    /// An icicle's outline, its root at (0, 0) and hanging down: slightly
    /// ringed sides narrowing to a fine tip.
    /// </summary>
    public static IReadOnlyList<Vector2> IcicleOutline(Icicle icicle)
    {
        var steps = Math.Clamp((int)(icicle.Length / 1.5), 6, 24);
        var left = new List<Vector2>(steps + 2);
        var right = new List<Vector2>(steps + 2);
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var half = icicle.Width / 2 * Math.Pow(1 - t, 0.85) * (1 + (0.07 * Math.Sin(t * icicle.Length / 2.2)));
            var centre = icicle.Bend * t * t;
            var y = icicle.Length * t;
            left.Add(new Vector2((float)(centre - half), (float)y));
            right.Add(new Vector2((float)(centre + half), (float)y));
        }

        // A little flare where it meets the snow.
        left.Insert(0, new Vector2((float)(-icicle.Width * 0.75), -1.2f));
        right.Insert(0, new Vector2((float)(icicle.Width * 0.75), -1.2f));
        right.Reverse();
        return [.. left, .. right];
    }

    /// <summary>
    /// Frost growing out of a rounded corner (<paramref name="radius"/>), at
    /// most <paramref name="reach"/> from it: feathery ferns, each a spine
    /// with fine barbs at 60 degrees on both sides, shorter towards its tip,
    /// fainter the further they grow. In the frame of a top left corner (the
    /// edges along x and y); the app mirrors it to the others.
    /// </summary>
    public static IReadOnlyList<FrostStroke> Frost(int seed, double radius, double reach)
    {
        var random = new Seeded(seed);
        var strokes = new List<FrostStroke>();
        var centre = new Vector2((float)radius, (float)radius);

        void Fern(Vector2 start, double angle, double length, double opacity, bool shoots)
        {
            var spine = new List<Vector2> { start };
            var at = start;
            var heading = angle;
            var bend = (random.Next() - 0.5) * 0.02;
            var walked = 0.0;
            var barbs = new List<(Vector2 At, double Heading, double Walked)>();
            var nextBarb = 2.5;
            while (walked < length)
            {
                var step = Math.Min(2, length - walked);
                heading += bend;
                at += new Vector2((float)(Math.Cos(heading) * step), (float)(Math.Sin(heading) * step));
                walked += step;
                spine.Add(at);
                if (walked >= nextBarb)
                {
                    barbs.Add((at, heading, walked));
                    nextBarb += 2.6 + random.Next();
                }
            }

            strokes.Add(new FrostStroke(spine, 0.9, opacity));
            foreach (var (point, along, distance) in barbs)
            {
                // Longest a third of the way along, none at the tip.
                var t = distance / length;
                var barb = Math.Min(length * 0.22, 11) * Math.Sin(Math.Min(1, t * 1.6) * Math.PI) * (0.75 + (0.5 * random.Next()));
                if (barb < 1.2)
                {
                    continue;
                }

                foreach (var k in (ReadOnlySpan<int>)[-1, 1])
                {
                    var b = along + (k * Math.PI / 3);
                    var end = point + new Vector2((float)(Math.Cos(b) * barb), (float)(Math.Sin(b) * barb));
                    strokes.Add(new FrostStroke([point, end], 0.6, opacity * 0.8));
                    if (shoots && barb > 7 && random.Next() < 0.35)
                    {
                        Fern(Vector2.Lerp(point, end, 0.5f), b, barb * 0.9, opacity * 0.7, false);
                    }
                }
            }
        }

        // Ferns from the corner's curve, spreading inwards, and shorter ones from each edge beside it.
        var ferns = 5 + (int)(random.Next() * 2);
        for (var i = 0; i < ferns; i++)
        {
            var around = Math.PI + (Math.PI / 2 * (0.15 + (0.7 * (i + random.Next()) / ferns)));
            var start = centre + new Vector2((float)(Math.Cos(around) * radius), (float)(Math.Sin(around) * radius));
            var inwards = around + Math.PI + ((random.Next() - 0.5) * 0.9);
            Fern(start, inwards, reach * (0.5 + (0.45 * random.Next())), 0.42, true);
        }

        foreach (var alongX in (ReadOnlySpan<bool>)[true, false])
        {
            var count = 1 + (int)(random.Next() * 2);
            for (var i = 0; i < count; i++)
            {
                var along = radius + ((reach - radius) * (0.2 + (0.6 * random.Next())));
                var start = alongX ? new Vector2((float)along, 0) : new Vector2(0, (float)along);
                var angle = (alongX ? Math.PI / 2 : 0) + ((random.Next() - 0.5) * 0.9);
                Fern(start, angle, Math.Max(10, (reach - along) * (0.3 + (0.3 * random.Next()))), 0.3, false);
            }
        }

        return strokes;
    }

    /// <summary>One line of frost: its points, how thick and how bright.</summary>
    public readonly record struct FrostStroke(IReadOnlyList<Vector2> Points, double Width, double Opacity);

    // ------------------------------------------------------------ boughs

    /// <summary>
    /// A branch over the page's top right corner (<see cref="Bough(ThemeScene)"/>),
    /// <see cref="Width"/> by <see cref="Height"/>: its right edge goes at the
    /// page's right edge and <see cref="PanelTop"/> at the page's top, so the
    /// part above reaches into the title bar (only left of its buttons, which
    /// keep the right <see cref="ButtonRoom"/>). It sways about
    /// <see cref="Pivot"/>, where it comes in.
    /// </summary>
    public sealed record BoughArt(
        double Width,
        double Height,
        double PanelTop,
        Vector2 Pivot,
        IReadOnlyList<BoughFill> Fills,
        IReadOnlyList<SpriteSpot> Sprites,
        IReadOnlyList<Icicle> Icicles,
        IReadOnlyList<Vector2> Shedding);

    /// <summary>Shapes drawn in one colour, or a gradient from <see cref="Top"/> to <see cref="Bottom"/> down the bounds of all of them together.</summary>
    public sealed record BoughFill(IReadOnlyList<IReadOnlyList<Vector2>> Figures, ThemeColor Top, ThemeColor Bottom, double Opacity);

    /// <summary>A picture placed on a bough (a blossom, a bud, a glint): centre, size, turn in degrees and squash across.</summary>
    public readonly record struct SpriteSpot(SceneSprite Sprite, Vector2 Centre, double Size, double Angle, double Squash, double Opacity);

    /// <summary>
    /// The width at the right of a bough that stays clear above the page, for
    /// the title bar's buttons: a bough is never drawn so small that they
    /// need more (see <see cref="SceneLayout.Bough"/>).
    /// </summary>
    public const double ButtonRoom = 256;

    /// <summary>A bough's size, in the content's units (see <see cref="BoughArt"/>).</summary>
    public const double BoughWidth = 560;

    /// <summary>The scene's branch: sakura for Japan, a snowy spruce bough for Snow; null for the others.</summary>
    public static BoughArt? Bough(ThemeScene scene) => scene switch
    {
        ThemeScene.Japan => CherryBranch,
        ThemeScene.Snow => SpruceBough,
        _ => null,
    };

    private const double BoughHeight = 260;
    private const double BoughPanelTop = 70;

    private static readonly Lazy<BoughArt> CherryBranchArt = new(MakeCherryBranch);
    private static readonly Lazy<BoughArt> SpruceBoughArt = new(MakeSpruceBough);

    private static BoughArt CherryBranch => CherryBranchArt.Value;

    private static BoughArt SpruceBough => SpruceBoughArt.Value;

    private static BoughArt MakeCherryBranch()
    {
        var random = new Seeded(0x5A_C0_A1);
        var limbs = new (double Start, double End, Vector2[] Points)[]
        {
            // The main limb comes in from the right, dips, then rises to the left.
            (27, 3.5, [new(590, 98), new(530, 100), new(470, 104), new(420, 101), new(372, 94), new(330, 84), new(296, 68), new(262, 54), new(226, 44), new(186, 40), new(146, 44), new(118, 52)]),
            (10, 2, [new(472, 104), new(452, 126), new(428, 146), new(400, 162), new(366, 172)]),
            (8, 1.8f, [new(374, 95), new(346, 112), new(312, 126), new(274, 134), new(236, 134)]),
            (5, 1.4f, [new(262, 55), new(244, 36), new(228, 20), new(212, 8)]),
            (4, 1.2f, [new(186, 41), new(168, 62), new(156, 84), new(150, 102)]),
            (4.5, 1.3f, [new(422, 101), new(432, 124), new(438, 146)]),
        };

        var wood = new List<IReadOnlyList<Vector2>>();
        var light = new List<IReadOnlyList<Vector2>>();
        foreach (var (start, end, points) in limbs)
        {
            var line = Smooth(points);
            wood.Add(Tapered(line, start, end, random, 0.6));
            light.Add(Highlight(line, start, end));
        }

        // Clusters of blossoms along the wood, with buds.
        var clusters = new Vector2[]
        {
            new(512, 96), new(462, 100), new(408, 98), new(352, 90), new(306, 70), new(270, 52), new(234, 40), new(194, 34), new(150, 40),
            new(120, 52), new(448, 130), new(418, 154), new(384, 170), new(352, 176), new(332, 116), new(292, 132), new(250, 138),
            new(224, 20), new(208, 6), new(160, 70), new(150, 100), new(434, 136), new(440, 152),
        };

        var sprites = new List<SpriteSpot>();
        foreach (var centre in clusters)
        {
            var blooms = 2 + (int)(random.Next() * 3);
            for (var i = 0; i < blooms; i++)
            {
                var spot = centre + new Vector2((float)((random.Next() - 0.5) * 26), (float)((random.Next() - 0.4) * 22));
                var size = 21 + (11 * random.Next());
                sprites.Add(new SpriteSpot(SceneSprite.Blossom, spot, size, random.Next() * 360, 0.62 + (0.38 * random.Next()), 1));
            }

            if (random.Next() < 0.7)
            {
                var bud = centre + new Vector2((float)((random.Next() - 0.5) * 34), (float)((random.Next() - 0.2) * 26));
                sprites.Add(new SpriteSpot(SceneSprite.PetalDeep, bud, 9 + (4 * random.Next()), (random.Next() - 0.5) * 120, 0.62, 1));
            }
        }

        // Nothing but wood may reach above the page beside the title bar's buttons.
        sprites.RemoveAll(s => s.Centre.X + (s.Size / 2) > BoughWidth - ButtonRoom && s.Centre.Y - (s.Size / 2) < BoughPanelTop - 6);

        var fills = new List<BoughFill>
        {
            new(wood, ThemeColor.FromRgb(0x3A2030), ThemeColor.FromRgb(0x190C14), 0.98),
            new(light, ThemeColor.FromRgb(0x8A5468), ThemeColor.FromRgb(0x5A3042), 0.5),
        };

        // Blossoms in front of the wood, smaller ones first.
        sprites.Sort((a, b) => a.Size.CompareTo(b.Size));
        var shedding = clusters.Select(c => c + new Vector2(0, 8)).ToList();
        return new BoughArt(BoughWidth, BoughHeight, BoughPanelTop, new Vector2(590, 98), fills, sprites, [], shedding);
    }

    private static BoughArt MakeSpruceBough()
    {
        var random = new Seeded(0x5B_0A_61);
        var limb = Smooth([new(590, 104), new(530, 108), new(470, 112), new(410, 115), new(352, 113), new(300, 106), new(250, 96), new(206, 86), new(166, 80), new(128, 80), new(100, 84)]);
        var wood = new List<IReadOnlyList<Vector2>> { Tapered(limb, 15, 3, random, 0.4) };
        var back = new List<IReadOnlyList<Vector2>>();
        var front = new List<IReadOnlyList<Vector2>>();
        var lit = new List<IReadOnlyList<Vector2>>();
        var snow = new List<IReadOnlyList<Vector2>>();
        var snowShade = new List<IReadOnlyList<Vector2>>();
        var rime = new List<IReadOnlyList<Vector2>>();
        var cones = new List<IReadOnlyList<Vector2>>();
        var coneScales = new List<IReadOnlyList<Vector2>>();

        // Flat sprays of twigs off both sides of the limb, reaching forwards (to the left) and drooping a little.
        var twigs = new List<List<Vector2>>();
        var side = 1;
        for (var i = 4; i < limb.Count - 2; i += 7)
        {
            var at = limb[i];
            if (at.X > 572)
            {
                continue;
            }

            side = -side;
            var direction = Vector2.Normalize(limb[Math.Max(0, i - 2)] - limb[Math.Min(limb.Count - 1, i + 2)]);
            var heading = Math.Atan2(direction.Y, direction.X) + (side * (0.32 + (0.3 * random.Next())));
            var length = 34 + (26 * random.Next());
            var end = at + new Vector2((float)(Math.Cos(heading) * length), (float)((Math.Sin(heading) * length) + 5));
            var twig = Smooth([at, Vector2.Lerp(at, end, 0.5f) + new Vector2(0, -1), end]);
            twigs.Add(twig);
            wood.Add(Tapered(twig, 2.4, 0.8, random, 0.2));

            // Two short shoots off each twig.
            foreach (var k in (ReadOnlySpan<int>)[-1, 1])
            {
                var from = twig[(int)(twig.Count * (0.35 + (0.2 * random.Next())))];
                var shoot = heading + (k * (0.55 + (0.2 * random.Next())));
                var shootLength = 12 + (10 * random.Next());
                var tip = from + new Vector2((float)(Math.Cos(shoot) * shootLength), (float)((Math.Sin(shoot) * shootLength) + 2));
                var spray = Smooth([from, tip]);
                twigs.Add(spray);
            }
        }

        twigs.Add(limb);
        foreach (var twig in twigs)
        {
            // Needles both sides of each twig, pointing forwards: dark behind, lighter in front, lit on top.
            var main = ReferenceEquals(twig, limb);
            for (var i = 1; i < twig.Count; i++)
            {
                var a = twig[i - 1];
                var b = twig[i];
                var along = b - a;
                if (along.LengthSquared() < 1e-4f)
                {
                    continue;
                }

                along = Vector2.Normalize(along);
                var forwards = Math.Atan2(along.Y, along.X);
                var t = (double)i / twig.Count;
                var reach = (main ? 12 : 9.5) * (1 - (0.4 * t));
                foreach (var k in (ReadOnlySpan<int>)[-1, 1, -1, 1])
                {
                    var angle = forwards + (k * (0.85 + (0.3 * random.Next())));
                    var length = reach * (0.8 + (0.4 * random.Next()));
                    var root = Vector2.Lerp(a, b, (float)random.Next());
                    var tip = root + new Vector2((float)(Math.Cos(angle) * length), (float)(Math.Sin(angle) * length));
                    var needle = Needle(root, tip, 1.4);
                    var upper = tip.Y < root.Y;
                    (upper ? (random.Next() < 0.55 ? lit : front) : (random.Next() < 0.6 ? back : front)).Add(needle);
                    if (random.Next() < 0.22)
                    {
                        // Hoarfrost on the tip.
                        rime.Add(Needle(Vector2.Lerp(root, tip, 0.62f), tip + ((tip - root) * 0.08f), 1.7));
                    }
                }
            }
        }

        // Snow resting on the limb and on the flattest sprays, on top of the needles.
        var (limbSnow, limbShade) = SnowOnLine(limb.Where(p => p.X < 560).ToList(), 10, 7, random);
        snow.Add(limbSnow);
        snowShade.Add(limbShade);
        foreach (var twig in twigs.Where(t => !ReferenceEquals(t, limb) && t.Count > 6))
        {
            var slope = Math.Abs(Math.Atan2(twig[^1].Y - twig[0].Y, twig[^1].X - twig[0].X));
            var flatness = Math.Abs(Math.Cos(slope));
            if (flatness > 0.9 && random.Next() < 0.7)
            {
                var (lump, shade) = SnowOnLine(twig, 7 * flatness, 5, random);
                snow.Add(lump);
                snowShade.Add(shade);
            }
        }

        // Two cones hanging under the limb.
        foreach (var x in (ReadOnlySpan<float>)[436, 342])
        {
            var top = Near(limb, x) + new Vector2(0, 5);
            var (body, scales) = Cone(top, 30 + (6 * (float)random.Next()), (random.Next() - 0.5) * 12);
            cones.Add(body);
            coneScales.AddRange(scales);
        }

        var icicles = new List<Icicle>();
        foreach (var (x, length) in (ReadOnlySpan<(float, double)>)[(500, 22), (476, 13), (392, 28), (372, 15), (306, 19), (262, 11)])
        {
            var root = Near(limb, x);
            icicles.Add(new Icicle(root.X, root.Y + 6, length, Math.Clamp(length * 0.24, 2.6, 6), (random.Next() - 0.5) * 3, GatherSeconds * (0.3 + (0.3 * random.Next())), 20 + (17 * random.Next())));
        }

        var fills = new List<BoughFill>
        {
            new(back, ThemeColor.FromRgb(0x163634), ThemeColor.FromRgb(0x0E2425), 1),
            new(wood, ThemeColor.FromRgb(0x3A2820), ThemeColor.FromRgb(0x22160F), 1),
            new(front, ThemeColor.FromRgb(0x24504B), ThemeColor.FromRgb(0x1A3D3A), 1),
            new(lit, ThemeColor.FromRgb(0x3F7A6E), ThemeColor.FromRgb(0x2E6058), 1),
            new(rime, ThemeColor.FromRgb(0xEAF4FF), ThemeColor.FromRgb(0xCFE3F7), 0.85),
            new(cones, ThemeColor.FromRgb(0x6A4329), ThemeColor.FromRgb(0x3E2617), 1),
            new(coneScales, ThemeColor.FromRgb(0x8C5D3B), ThemeColor.FromRgb(0x6A4329), 0.9),
            new(snowShade, ThemeColor.FromRgb(0xB4CCE6), ThemeColor.FromRgb(0x93B3D6), 1),
            new(snow, ThemeColor.FromRgb(0xFFFFFF), ThemeColor.FromRgb(0xE2EEFA), 1),
        };

        var glints = new List<SpriteSpot>();
        foreach (var lump in snow.Take(5))
        {
            var spot = lump[(int)(lump.Count * (0.1 + (0.3 * random.Next())))];
            glints.Add(new SpriteSpot(SceneSprite.Glint, spot + new Vector2(0, 2.5f), 12, 0, 1, 1));
        }

        var shedding = Enumerable.Range(0, 6).Select(i => Near(limb, 200 + (i * 60)) + new Vector2(0, -6)).ToList();
        return new BoughArt(BoughWidth, BoughHeight, BoughPanelTop, new Vector2(590, 104), fills, glints, icicles, shedding);
    }

    // ------------------------------------------------------------ motion

    /// <summary>The nearest speed to <paramref name="radiansPerSecond"/> that turns a whole number of times per loop, so the clock can start again with no jump.</summary>
    public static double WholeTurns(double radiansPerSecond) => StageBars.Turns(radiansPerSecond);

    /// <summary>How a bough sways about its pivot: two slow swings, in degrees, with their speeds in radians a second.</summary>
    public static (double Degrees, double Speed, double Degrees2, double Speed2) BoughSway { get; } =
        (0.6, WholeTurns(Math.Tau / 7.3), 0.25, WholeTurns(Math.Tau / 4.1));

    /// <summary>
    /// A breeze over the piles now and then: its speed in radians a second
    /// (one gust a turn, about every 17 seconds), how far it has moved on per
    /// unit along a pile (radians), and how fast the petals it lifts shiver.
    /// </summary>
    public static (double Speed, double Along, double Shiver) Gust { get; } =
        (WholeTurns(Math.Tau / 17), 0.012, WholeTurns(Math.Tau * 2.2));

    /// <summary>How fast glint <paramref name="index"/> twinkles, in radians a second, and where it starts.</summary>
    public static (double Speed, double Phase) Twinkle(int index) =>
        (WholeTurns(Math.Tau / (2.6 + (2.2 * StageBars.Hash(index, 71)))), Math.Tau * StageBars.Hash(index, 72));

    /// <summary>
    /// Something that falls from a bough now and then (<see cref="Shedding(ThemeScene)"/>):
    /// from <see cref="From"/> (in the bough's frame), once every
    /// <see cref="Period"/> seconds (a whole number of times per loop) from
    /// <see cref="Phase"/>, over <see cref="Active"/> seconds, drifting
    /// <see cref="Drift"/> across and falling <see cref="Fall"/>, turning
    /// <see cref="Spin"/> degrees and fluttering <see cref="Flutter"/>
    /// radians on the way; <see cref="Size"/> across, drawn with
    /// <see cref="Sprite"/> (a soft dot of snow when null).
    /// </summary>
    public readonly record struct Shed(
        Vector2 From,
        double Period,
        double Phase,
        double Active,
        double Drift,
        double Fall,
        double Spin,
        double Flutter,
        double Size,
        SceneSprite? Sprite);

    /// <summary>
    /// What falls from the scene's bough: a petal from a cluster every half
    /// minute or so, drifting away left with the wind, for Japan; a little
    /// snow slipping off the bough, a few soft bits at a time, for Snow.
    /// </summary>
    public static IReadOnlyList<Shed> Shedding(ThemeScene scene)
    {
        if (Bough(scene) is not { Shedding.Count: > 0 } bough)
        {
            return [];
        }

        double H(int index, int salt) => StageBars.Hash(index, 80 + salt);
        var shed = new List<Shed>();
        var from = bough.Shedding;
        if (scene == ThemeScene.Japan)
        {
            for (var i = 0; i < 6; i++)
            {
                var period = SceneWeather.LoopSeconds / Math.Round(26 + (14 * H(i, 1)));
                var sprite = H(i, 9) switch
                {
                    < 0.4 => SceneSprite.PetalPink,
                    < 0.75 => SceneSprite.PetalPale,
                    _ => SceneSprite.PetalWhite,
                };
                shed.Add(new Shed(
                    from[((i * 5) + 2) % from.Count],
                    period,
                    period * H(i, 2),
                    9 + (3 * H(i, 3)),
                    -(180 + (120 * H(i, 4))),
                    480 + (160 * H(i, 5)),
                    (H(i, 6) < 0.5 ? -1 : 1) * (200 + (300 * H(i, 7))),
                    10 + (6 * H(i, 8)),
                    13 + (5 * H(i, 10)),
                    sprite));
            }

            return shed;
        }

        for (var i = 0; i < 6; i++)
        {
            var period = SceneWeather.LoopSeconds / Math.Round(24 + (16 * H(i, 1)));
            var phase = period * H(i, 2);
            for (var k = 0; k < 3; k++)
            {
                // A few bits together, the largest first, a moment apart.
                var spot = from[i % from.Count] + new Vector2((float)((H(i, 10 + k) - 0.5) * 10), (float)(H(i, 20 + k) * 3));
                shed.Add(new Shed(spot, period, (phase - (k * 0.12) + period) % period, 2.6, -(10 + (20 * H(i, 3))), 420, 0, 0, 5.5 - k, null));
            }
        }

        return shed;
    }

    /// <summary>
    /// A rounded layer of snow lying <paramref name="above"/> a line (on its
    /// needles), thickest in its middle and gone at both ends, and the blue
    /// shadow along its underside.
    /// </summary>
    private static (IReadOnlyList<Vector2> Snow, IReadOnlyList<Vector2> Shade) SnowOnLine(List<Vector2> line, double depth, double above, Seeded random)
    {
        var top = new List<Vector2>();
        var bottom = new List<Vector2>();
        var shade = new List<Vector2>();
        var phase = random.Next() * Math.Tau;
        var from = 0.08 + (0.1 * random.Next());
        var to = 0.88 - (0.1 * random.Next());
        for (var i = 0; i < line.Count; i++)
        {
            var t = (double)i / (line.Count - 1);
            if (t < from || t > to)
            {
                continue;
            }

            var u = (t - from) / (to - from);
            var envelope = Math.Pow(Math.Sin(u * Math.PI), 0.55);
            var thick = depth * envelope * (0.82 + (0.18 * Math.Sin((i * 0.45) + phase)));
            var p = line[i] - new Vector2(0, (float)(above * Math.Pow(envelope, 0.3)));
            top.Add(p + new Vector2(0, (float)-thick));
            bottom.Add(p + new Vector2(0, (float)(1.5 * envelope)));
            shade.Add(p + new Vector2(0, (float)(1.5 + (2.5 * envelope))));
        }

        var body = new List<Vector2>(top);
        body.AddRange(Enumerable.Reverse(bottom));
        var band = new List<Vector2>(bottom.Select(p => p + new Vector2(0, -2)));
        band.AddRange(Enumerable.Reverse(shade));
        return (body, band);
    }

    /// <summary>A pine cone hanging from <paramref name="top"/>, and the rows of scales drawn over it.</summary>
    private static (IReadOnlyList<Vector2> Body, List<IReadOnlyList<Vector2>> Scales) Cone(Vector2 top, float length, double tilt)
    {
        var turn = tilt * Math.PI / 180;
        Vector2 At(double across, double down) => top + new Vector2(
            (float)((across * Math.Cos(turn)) - (down * Math.Sin(turn))),
            (float)((across * Math.Sin(turn)) + (down * Math.Cos(turn))));

        var body = new List<Vector2>();
        for (var i = 0; i <= 24; i++)
        {
            var a = Math.PI * i / 24;
            var t = (1 - Math.Cos(a)) / 2;
            var half = length * 0.24 * Math.Pow(Math.Sin(t * Math.PI), 0.7) * (t < 0.5 ? 1 : 1 - ((t - 0.5) * 0.3));
            body.Add(At(-half, t * length));
        }

        for (var i = 24; i >= 0; i--)
        {
            var a = Math.PI * i / 24;
            var t = (1 - Math.Cos(a)) / 2;
            var half = length * 0.24 * Math.Pow(Math.Sin(t * Math.PI), 0.7) * (t < 0.5 ? 1 : 1 - ((t - 0.5) * 0.3));
            body.Add(At(half, t * length));
        }

        var scales = new List<IReadOnlyList<Vector2>>();
        for (var row = 1; row < 6; row++)
        {
            var t = row / 6.0;
            var half = length * 0.24 * Math.Pow(Math.Sin(t * Math.PI), 0.7) * 0.9;
            var offset = row % 2 == 0 ? 0.5 : 0;
            for (var k = -1; k <= 1; k++)
            {
                var c = (k + offset) * half * 0.7;
                if (Math.Abs(c) + 2 > half)
                {
                    continue;
                }

                var y = t * length;
                scales.Add([At(c - 2.6, y), At(c, y + 3.2), At(c + 2.6, y), At(c, y + 1.2)]);
            }
        }

        return (body, scales);
    }

    /// <summary>One needle: a sliver from its root to its tip.</summary>
    private static Vector2[] Needle(Vector2 root, Vector2 tip, double width)
    {
        var along = Vector2.Normalize(tip - root);
        var across = new Vector2(-along.Y, along.X) * (float)(width / 2);
        return [root + across, Vector2.Lerp(root, tip, 0.6f) + (across * 0.8f), tip, Vector2.Lerp(root, tip, 0.6f) - (across * 0.8f), root - across];
    }

    /// <summary>The point of a line nearest across to <paramref name="x"/>.</summary>
    private static Vector2 Near(IReadOnlyList<Vector2> line, float x) => line.MinBy(p => Math.Abs(p.X - x));

    /// <summary>A smooth line through <paramref name="points"/> (Catmull-Rom), about every 3 units.</summary>
    internal static List<Vector2> Smooth(IReadOnlyList<Vector2> points)
    {
        var line = new List<Vector2>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(points.Count - 1, i + 2)];
            var steps = Math.Max(2, (int)(Vector2.Distance(p1, p2) / 3));
            for (var s = 0; s < steps; s++)
            {
                var t = (float)s / steps;
                line.Add(0.5f * ((2 * p1) + ((p2 - p0) * t) + (((2 * p0) - (5 * p1) + (4 * p2) - p3) * t * t) + (((3 * p1) - p0 - (3 * p2) + p3) * t * t * t)));
            }
        }

        line.Add(points[^1]);
        return line;
    }

    /// <summary>A branch's outline along <paramref name="line"/>, narrowing from <paramref name="start"/> to <paramref name="end"/> wide, its bark a little uneven.</summary>
    private static IReadOnlyList<Vector2> Tapered(List<Vector2> line, double start, double end, Seeded random, double rough)
    {
        var left = new List<Vector2>(line.Count);
        var right = new List<Vector2>(line.Count);
        for (var i = 0; i < line.Count; i++)
        {
            var t = (double)i / (line.Count - 1);
            var normal = Normal(line, i);
            var width = (start + ((end - start) * Math.Pow(t, 0.8))) * (1 + (rough * 0.12 * (random.Next() - 0.5)));
            left.Add(line[i] + (normal * (float)(width / 2)));
            right.Add(line[i] - (normal * (float)(width / 2)));
        }

        right.Reverse();
        return [.. left, .. right];
    }

    /// <summary>The lit upper side of a branch: a thin strip along its top.</summary>
    private static IReadOnlyList<Vector2> Highlight(List<Vector2> line, double start, double end)
    {
        var upper = new List<Vector2>(line.Count);
        var lower = new List<Vector2>(line.Count);
        for (var i = 0; i < line.Count; i++)
        {
            var t = (double)i / (line.Count - 1);
            var normal = Normal(line, i);
            if (normal.Y > 0)
            {
                normal = -normal;
            }

            var width = start + ((end - start) * Math.Pow(t, 0.8));
            upper.Add(line[i] + (normal * (float)(width * 0.42)));
            lower.Add(line[i] + (normal * (float)(width * 0.12)));
        }

        lower.Reverse();
        return [.. upper, .. lower];
    }

    private static Vector2 Normal(List<Vector2> line, int i)
    {
        var a = line[Math.Max(0, i - 1)];
        var b = line[Math.Min(line.Count - 1, i + 1)];
        var d = b - a;
        return d.LengthSquared() < 1e-6f ? new Vector2(0, -1) : Vector2.Normalize(new Vector2(-d.Y, d.X));
    }

    private static double Smooth(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>A small, fixed sequence of numbers from 0 to 1 (SplitMix64), the same for the same seed.</summary>
    private sealed class Seeded(int seed)
    {
        private ulong _state = (ulong)seed * 0x9E3779B97F4A7C15UL;

        public double Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }
}
