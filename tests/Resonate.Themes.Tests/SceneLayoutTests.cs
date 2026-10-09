namespace Resonate.Themes.Tests;

/// <summary>Where the special looks' decorations go in the window.</summary>
public sealed class SceneLayoutTests
{
    // A 1600 x 1000 window: the sidebar, the page and a hovering player over the page's bottom.
    private static readonly SceneBox Window = new(0, 0, 1600, 1000);
    private static readonly SceneBox Sidebar = new(12, 44, 272, 944);
    private static readonly SceneBox Page = new(296, 44, 1292, 944);
    private static readonly SceneBox Player = new(486, 868, 912, 88);

    // The title bar's buttons: settings, the mini player and Windows' own three.
    private const double ButtonsLeft = 1600 - 226;

    private static SceneFrame Frame(SceneBox? pane = null, SceneBox? player = null, double scale = 1, SceneBox? page = null) =>
        new(Window, Sidebar, page ?? Page, pane, player, 18, 44, 12, scale, ButtonsLeft);

    [Fact]
    public void The_bough_comes_in_from_beyond_the_window_or_from_behind_the_side_pane()
    {
        var bough = SceneLayout.Bough(Frame(player: Player));
        Assert.NotNull(bough);
        Assert.Equal(1600, bough.Value.Right);
        Assert.Equal(44, bough.Value.Top);
        Assert.Equal(1, bough.Value.Scale);

        var pane = new SceneBox(1240, 44, 348, 944);
        var narrowed = Frame(pane, Player, page: Page with { Width = 932 });
        Assert.Equal(296 + 932 + 6, SceneLayout.Bough(narrowed)!.Value.Right);
    }

    [Fact]
    public void The_bough_shrinks_on_a_narrow_page_and_goes_on_a_tiny_one()
    {
        // Beside a side pane (the title bar's buttons are over the pane).
        var pane = new SceneBox(720, 44, 868, 944);
        Assert.Equal(0.55, SceneLayout.Bough(Frame(pane, page: Page with { Width = 400 }))!.Value.Scale, 6);
        Assert.Null(SceneLayout.Bough(Frame(pane, page: Page with { Width = 250 })));
        Assert.Null(SceneLayout.Bough(Frame(page: Page with { Height = 200 })));

        // At twice the App size it is twice as large.
        var large = Frame(scale: 2, page: Page with { Width = 2000 });
        Assert.Equal(2, SceneLayout.Bough(large)!.Value.Scale, 6);
    }

    [Fact]
    public void The_bough_is_never_so_small_that_it_reaches_under_the_title_bar_buttons()
    {
        // At the smallest App size the buttons keep their size, so the bough does too.
        var small = SceneLayout.Bough(Frame(scale: 0.8, page: Page with { Width = 1292 }))!.Value;
        Assert.Equal(226 / SceneDecor.ButtonRoom, small.Scale, 6);
        Assert.True((1600 - ButtonsLeft) / small.Scale <= SceneDecor.ButtonRoom + 1e-9);

        // A page too narrow for that has none.
        Assert.Null(SceneLayout.Bough(Frame(page: Page with { X = 1148, Width = 440 })));
    }

    [Fact]
    public void Petals_gather_on_the_players_shoulders_and_on_the_page_under_the_branch()
    {
        var spots = SceneLayout.PileSpots(Frame(player: Player));
        Assert.Equal([0, 1, 2], spots.Select(s => s.Pile));
        Assert.Equal(new SceneLayout.Spot(486, 868, 912, 44, 1), spots[0].Spot);
        Assert.Equal(new SceneLayout.Spot(1398, 868, 912, 44, 1, Mirrored: true), spots[1].Spot);
        Assert.Equal(1600 - SceneLayout.PagePileFromRight, spots[2].Spot.X);
        Assert.Equal(44, spots[2].Spot.Y);

        // No player, no shoulders; a narrow player has none either.
        Assert.Equal([2], SceneLayout.PileSpots(Frame()).Select(s => s.Pile));
        Assert.Equal([2], SceneLayout.PileSpots(Frame(player: Player with { Width = 100 })).Select(s => s.Pile));
    }

    [Fact]
    public void Snow_settles_on_every_panel_and_the_player_and_icicles_hang_under_it()
    {
        var pane = new SceneBox(1240, 44, 348, 944);
        var caps = SceneLayout.Caps(Frame(pane, Player));
        Assert.Equal([1, 2, 7, 3], caps.Select(c => c.Seed));
        Assert.All(caps, c => Assert.True(c.Depth > 0 && c.Icicles > 0));
        Assert.Equal(912, caps[3].Spot.Width);
        Assert.Equal(44, caps[3].Spot.Radius);

        var fringe = SceneLayout.Fringe(Frame(player: Player));
        Assert.Equal(new SceneLayout.Spot(486, 956, 912, 44, 1), fringe);
        Assert.Null(SceneLayout.Fringe(Frame()));

        // In the content's units at any App size.
        Assert.Equal(456, SceneLayout.Caps(Frame(player: Player, scale: 2))[^1].Spot.Width);
    }

    [Fact]
    public void Frost_grows_only_in_corners_nothing_covers()
    {
        var frost = SceneLayout.Frost(Frame(player: Player));
        Assert.Equal([5, 6], frost.Select(f => f.Seed));
        Assert.Equal((12.0, 988.0, false, true), (frost[0].X, frost[0].Y, frost[0].FlipX, frost[0].FlipY));
        Assert.Equal((1588.0, 988.0, true, true), (frost[1].X, frost[1].Y, frost[1].FlipX, frost[1].FlipY));

        // A player over the page's bottom right corner keeps it clear.
        var wide = Player with { X = 300, Width = 1280 };
        Assert.Equal([5], SceneLayout.Frost(Frame(player: wide)).Select(f => f.Seed));
    }
}
