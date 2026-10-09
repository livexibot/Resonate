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

    // The back button and the app's name on the left; everything in the title bar ends 6 px above the panels.
    private const double TitleEnd = 160;
    private const double TitleBottom = 38;

    private static SceneFrame Frame(SceneBox? pane = null, SceneBox? player = null, double scale = 1, SceneBox? page = null, SceneBox? sidebar = null) =>
        new(Window, sidebar ?? Sidebar, page ?? Page, pane, player, 18, 44, 12, scale, ButtonsLeft, TitleEnd, TitleBottom);

    [Fact]
    public void Snow_keeps_below_the_title_bars_content_and_rises_freely_elsewhere()
    {
        foreach (var scale in (double[])[0.8, 1, 1.5, 2])
        {
            var frame = Frame(player: Player, scale: scale);
            foreach (var spot in SceneLayout.Caps(frame))
            {
                var cap = SceneDecor.Cap(spot.Seed, spot.Spot.Width, spot.Spot.Radius, spot.Depth, spot.Icicles, spot.IcicleLength, spot.Low);
                foreach (var p in cap.Crest.Concat(cap.Glints))
                {
                    var x = spot.Spot.X + (p.X * spot.Spot.Scale);
                    var y = spot.Spot.Y + (p.Y * spot.Spot.Scale);
                    if (x <= TitleEnd + 8 || x >= ButtonsLeft - 8)
                    {
                        Assert.True(y >= TitleBottom, $"Snow at ({x:0.#}, {y:0.#}) reaches up among the title bar's content at {scale}x");
                    }
                }
            }

            // Between them the page's snow is as deep as ever.
            var page = SceneLayout.Caps(frame).Single(c => c.Seed == 2);
            var free = SceneDecor.Cap(page.Seed, page.Spot.Width, page.Spot.Radius, page.Depth, page.Icicles, page.IcicleLength);
            var middle = free.Crest.Count / 2;
            Assert.Equal(free.Crest[middle], SceneDecor.Cap(page.Seed, page.Spot.Width, page.Spot.Radius, page.Depth, page.Icicles, page.IcicleLength, page.Low).Crest[middle]);
        }

        // The player's snow, far below, rises freely.
        Assert.Empty(SceneLayout.Caps(Frame(player: Player)).Single(c => c.Seed == 3).Low);
    }

    [Fact]
    public void Nothing_lies_over_a_player_at_the_top_or_under_the_title_bars_content()
    {
        // A player docked at the top, under the title bar: the page starts below it.
        var top = new SceneBox(12, 44, 1576, 88);
        var page = Page with { Y = 144, Height = 844 };
        var frame = Frame(player: top, page: page, sidebar: Sidebar with { Y = 144, Height = 844 });
        Assert.Null(SceneLayout.Bough(frame));

        // Its shoulders lie under the back button and Windows' own buttons, so no petals gather there.
        Assert.DoesNotContain(SceneLayout.PileSpots(frame), p => p.Pile is 0 or 1);

        // A short window whose player hovers high up on the page has no bough either.
        var shortPage = Page with { Height = 300 };
        Assert.Null(SceneLayout.Bough(Frame(player: new SceneBox(486, 196, 912, 88), page: shortPage)));
    }

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
    [Fact]
    public void Mercury_drips_from_edges_with_room_below_them_away_from_the_corners_and_never_from_under_the_player()
    {
        // The hovering player's bottom edge has room under it; the panels reach the window's bottom and have none.
        var drips = SceneLayout.Drips(Frame(player: Player));
        Assert.NotEmpty(drips);
        Assert.All(drips, d => Assert.Equal(Player.Bottom, d.Y));
        Assert.All(drips, d => Assert.InRange(d.X, Player.X + 44 + 16, Player.Right - 44 - 16));
        Assert.InRange(drips.Count, 1, 4);

        // A sidebar that ends above a docked player drips onto it; a page edge the player lies over does not drip.
        var docked = new SceneBox(12, 920, 1576, 68);
        var frame = Frame(player: docked, sidebar: Sidebar with { Height = 860 }, page: Page with { Height = 860 });
        var above = SceneLayout.Drips(frame);
        Assert.Contains(above, d => d.Y == 904 && d.X < Sidebar.Right);
        Assert.Contains(above, d => d.Y == 904 && d.X > Page.X);
        Assert.DoesNotContain(above, d => d.Y == docked.Bottom);

        foreach (var drip in drips.Concat(above))
        {
            var turns = drip.Frequency * SceneWeather.LoopSeconds;
            Assert.Equal(Math.Round(turns), turns, 9);
            Assert.InRange(drip.Size, 8, 14);
            Assert.InRange(drip.Phase, 0, 1);
        }

        Assert.Equal(drips, SceneLayout.Drips(Frame(player: Player)));
    }

    [Fact]
    public void A_hud_frames_every_panel_and_the_player_big_enough_to_carry_it()
    {
        var framed = SceneLayout.Framed(Frame(new SceneBox(1200, 44, 388, 944), Player, page: Page with { Width = 892 }));
        Assert.Equal(4, framed.Count);
        Assert.Single(framed, f => f.Player);
        Assert.Equal(Player, framed.Single(f => f.Player).Box);
        Assert.DoesNotContain(SceneLayout.Framed(Frame(player: Player with { Width = 60 })), f => f.Player);
    }
}
