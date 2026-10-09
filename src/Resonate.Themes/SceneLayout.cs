namespace Resonate.Themes;

/// <summary>A rectangle in the window, in the window's units.</summary>
public readonly record struct SceneBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Overlaps(SceneBox other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>
/// Where the window's panels and its player are, for the special looks'
/// decorations (<see cref="SceneLayout"/>): boxes in the window's units, the
/// panels' and the player's corner radius and the gap between panels in the
/// content's units, <see cref="Scale"/> from one to the other (App size),
/// and where the title bar's buttons begin, in the window's units.
/// </summary>
public sealed record SceneFrame(
    SceneBox Window,
    SceneBox? Sidebar,
    SceneBox Page,
    SceneBox? Pane,
    SceneBox? Player,
    double PanelRadius,
    double PlayerRadius,
    double Gap,
    double Scale,
    double ButtonsLeft);

/// <summary>
/// Where a special look's decorations go in the window (see
/// <see cref="SceneDecor"/>), worked out from <see cref="SceneFrame"/>.
/// Positions are in the window's units; widths and radii in the content's,
/// drawn <see cref="Spot.Scale"/> times larger.
/// </summary>
public static class SceneLayout
{
    /// <summary>
    /// A decoration on an edge: its left end at (<see cref="X"/>,
    /// <see cref="Y"/>), or its right end when <see cref="Mirrored"/> (the
    /// app draws it flipped), <see cref="Width"/> long with corners of
    /// <see cref="Radius"/>.
    /// </summary>
    public readonly record struct Spot(double X, double Y, double Width, double Radius, double Scale, bool Mirrored = false);

    /// <summary>A petal pile's place on the window (see <see cref="Piles"/>), by the pile it is.</summary>
    public readonly record struct PileSpot(int Pile, Spot Spot);

    /// <summary>
    /// A snowy edge's place: <see cref="Seed"/> and the snow's depth, its
    /// icicles and their longest length, in the content's units.
    /// </summary>
    public readonly record struct CapSpot(int Seed, Spot Spot, double Depth, int Icicles, double IcicleLength);

    /// <summary>Frost in a corner at (<see cref="X"/>, <see cref="Y"/>), drawn as a top left corner flipped as asked.</summary>
    public readonly record struct FrostSpot(int Seed, double X, double Y, bool FlipX, bool FlipY, double Radius, double Scale);

    /// <summary>
    /// The branch: its right edge at <see cref="Right"/> (where it is cut off),
    /// its <see cref="SceneDecor.BoughArt.PanelTop"/> on the page's top
    /// edge at <see cref="Top"/>, drawn <see cref="Scale"/> times larger.
    /// </summary>
    public readonly record struct BoughSpot(double Right, double Top, double Scale);

    /// <summary>
    /// The petal piles of Japan, in a fixed order (their landing times are
    /// worked out for all of them, whichever show): one on each of the
    /// player's shoulders and a small one on the page's top edge under the
    /// branch.
    /// </summary>
    public static IReadOnlyList<PileKind> Piles { get; } =
    [
        new(1, Length: 76, Height: 10, Count: 32, Blossoms: true),
        new(2, Length: 76, Height: 10, Count: 32, Blossoms: true),
        new(3, Length: 80, Height: 8, Count: 14, Blossoms: false),
    ];

    /// <summary>What a pile is made of (see <see cref="SceneDecor.Pile"/>).</summary>
    public readonly record struct PileKind(int Seed, double Length, double Height, int Count, bool Blossoms)
    {
        /// <summary>How many petals and blossoms it holds, whatever its edge.</summary>
        public int Total => Count + (Blossoms ? SceneDecor.Blossoms(Seed) : 0);
    }

    /// <summary>When each pile's petals land (see <see cref="SceneDecor.LandingTimes"/>), the same wherever the piles are.</summary>
    public static double[][] PileLandings { get; } = SceneDecor.LandingTimes([.. Piles.Select(p => p.Total)]);

    /// <summary>How long the page's own pile is, in the content's units, and how far its left end lies from the branch's right edge.</summary>
    public const double PagePileWidth = 160;

    public const double PagePileFromRight = 400;

    /// <summary>How far frost reaches from its corner, in the content's units.</summary>
    public const double FrostReach = 120;

    /// <summary>The branch's place, or null when the page is too small to carry it.</summary>
    public static BoughSpot? Bough(SceneFrame frame)
    {
        var s = Math.Max(0.1, frame.Scale);
        var page = frame.Page;
        if (page.Width / s < 300 || page.Height / s < 240)
        {
            return null;
        }

        // Smaller on a narrow page, so it never reaches across all of it.
        var scale = s * Math.Clamp(page.Width / s / 900, 0.55, 1);

        // It comes in from beyond the window's edge, or from behind whatever lies right of the page.
        var right = NothingRightOf(frame) ? frame.Window.Right : page.Right + (frame.Gap * s / 2);

        // Never so small that what reaches above the page gets under the title bar's buttons; rather none than one wider than the page.
        var buttons = right - frame.ButtonsLeft;
        if (buttons > 0)
        {
            scale = Math.Max(scale, buttons / SceneDecor.ButtonRoom);
        }

        return scale * SceneDecor.BoughWidth > page.Width * 1.05 ? null : new BoughSpot(right, page.Y, scale);
    }

    /// <summary>Where Japan's piles show: on the player's shoulders when it is wide enough, and on the page under the branch.</summary>
    public static IReadOnlyList<PileSpot> PileSpots(SceneFrame frame)
    {
        var s = Math.Max(0.1, frame.Scale);
        var spots = new List<PileSpot>(3);
        if (frame.Player is { } player && player.Width / s >= 140 && player.Height / s >= 24)
        {
            var width = player.Width / s;
            spots.Add(new PileSpot(0, new Spot(player.X, player.Y, width, frame.PlayerRadius, s)));
            spots.Add(new PileSpot(1, new Spot(player.Right, player.Y, width, frame.PlayerRadius, s, Mirrored: true)));
        }

        if (Bough(frame) is { } bough)
        {
            var x = bough.Right - (PagePileFromRight * bough.Scale);
            if (x >= frame.Page.X + (24 * s) && !Covered(frame, new SceneBox(x, frame.Page.Y - (20 * s), PagePileWidth * s, 30 * s)))
            {
                spots.Add(new PileSpot(2, new Spot(x, frame.Page.Y, PagePileWidth, 0, s)));
            }
        }

        return spots;
    }

    /// <summary>Snow's edges: the sidebar's, the page's, the side pane's and the player's tops, wherever there is room.</summary>
    public static IReadOnlyList<CapSpot> Caps(SceneFrame frame)
    {
        var s = Math.Max(0.1, frame.Scale);
        var caps = new List<CapSpot>(4);
        void Add(int seed, SceneBox? box, double radius, double depth, int icicles, double length)
        {
            if (box is { } b && b.Width / s >= 60 && b.Height / s >= 24)
            {
                caps.Add(new CapSpot(seed, new Spot(b.X, b.Y, b.Width / s, radius, s), depth, icicles, length));
            }
        }

        Add(1, frame.Sidebar, frame.PanelRadius, 12, 4, 10);
        Add(2, frame.Page, frame.PanelRadius, 14, 9, 20);
        Add(7, frame.Pane, frame.PanelRadius, 12, 5, 12);
        Add(3, frame.Player, frame.PlayerRadius, 10, 4, 8);
        return caps;
    }

    /// <summary>Icicles under the player's bottom edge, or null without a player.</summary>
    public static Spot? Fringe(SceneFrame frame)
    {
        var s = Math.Max(0.1, frame.Scale);
        return frame.Player is { } player && player.Width / s >= 140 && player.Height / s >= 24
            ? new Spot(player.X, player.Bottom, player.Width / s, frame.PlayerRadius, s)
            : null;
    }

    /// <summary>Frost in the sidebar's bottom left corner and the page's bottom right, where nothing covers it.</summary>
    public static IReadOnlyList<FrostSpot> Frost(SceneFrame frame)
    {
        var s = Math.Max(0.1, frame.Scale);
        var reach = FrostReach * s;
        var frost = new List<FrostSpot>(2);
        if (frame.Sidebar is { } sidebar && sidebar.Width >= reach && sidebar.Height >= reach * 2
            && !Covered(frame, new SceneBox(sidebar.X, sidebar.Bottom - reach, reach, reach)))
        {
            frost.Add(new FrostSpot(5, sidebar.X, sidebar.Bottom, false, true, frame.PanelRadius, s));
        }

        var page = frame.Page;
        if (page.Width >= reach * 2 && page.Height >= reach * 2
            && !Covered(frame, new SceneBox(page.Right - reach, page.Bottom - reach, reach, reach)))
        {
            frost.Add(new FrostSpot(6, page.Right, page.Bottom, true, true, frame.PanelRadius, s));
        }

        return frost;
    }

    /// <summary>Whether the player lies over <paramref name="box"/>.</summary>
    private static bool Covered(SceneFrame frame, SceneBox box) => frame.Player is { } player && player.Overlaps(box);

    /// <summary>Whether the page runs to the window's right edge: no side pane, nor a player, beside it.</summary>
    private static bool NothingRightOf(SceneFrame frame)
    {
        var page = frame.Page;
        bool Beside(SceneBox? box) => box is { } b && b.X >= page.Right - 1 && b.Y < page.Bottom && b.Bottom > page.Y;
        return !Beside(frame.Pane) && !Beside(frame.Player);
    }
}
