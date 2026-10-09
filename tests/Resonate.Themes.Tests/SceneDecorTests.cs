using System.Numerics;

namespace Resonate.Themes.Tests;

/// <summary>The special looks' decorations: petal piles, snow on the edges, icicles, frost and the boughs.</summary>
public sealed class SceneDecorTests
{
    [Fact]
    public void An_edge_drops_only_along_its_rounded_corners()
    {
        Assert.Equal(0, SceneDecor.EdgeDrop(200, 400, 20));
        Assert.Equal(20, SceneDecor.EdgeDrop(0, 400, 20), 6);
        Assert.Equal(20, SceneDecor.EdgeDrop(400, 400, 20), 6);
        Assert.InRange(SceneDecor.EdgeDrop(10, 400, 20), 0.1, 19.9);
        Assert.Equal(0, SceneDecor.EdgeDrop(5, 400, 0));
        Assert.True(SceneDecor.EdgeSlope(5, 400, 20) < 0);
        Assert.True(SceneDecor.EdgeSlope(395, 400, 20) > 0);
        Assert.Equal(0, SceneDecor.EdgeSlope(200, 400, 20), 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pile_lies_on_its_corner_and_its_petals_come_from_the_right(bool mirrored)
    {
        var pile = SceneDecor.Pile(1, 900, 9, 76, 10, 32, true, mirrored);
        Assert.Equal(pile, SceneDecor.Pile(1, 900, 9, 76, 10, 32, true, mirrored));
        Assert.Equal(32 + SceneDecor.Blossoms(1), pile.Count);
        Assert.InRange(pile.Count(p => p.Sprite == SceneSprite.Blossom), 1, 2);
        Assert.All(pile, p =>
        {
            Assert.InRange(p.X, 0, 9 + 76);
            Assert.InRange(p.Y, -14, 9);
            Assert.True(p.FromY < p.Y - 100);
            Assert.InRange(p.Squash, 0.3, 1);
            Assert.InRange(p.Breeze, 0, 1);

            // A pile on the right end is drawn flipped, so its petals still come from the window's right.
            Assert.True(mirrored ? p.FromX < p.X : p.FromX > p.X);
        });

        // Blossoms lie on top, and petals on top stir.
        Assert.Equal(SceneSprite.Blossom, pile[^1].Sprite);
        Assert.Contains(pile, p => p.Stirs && p.Sprite != SceneSprite.Blossom);
        Assert.Contains(pile, p => !p.Stirs);
    }

    [Fact]
    public void A_pile_keeps_its_petals_whatever_the_length_of_a_long_edge()
    {
        Assert.Equal(SceneDecor.Pile(2, 600, 44, 76, 10, 32, true, true), SceneDecor.Pile(2, 1400, 44, 76, 10, 32, true, true));

        // A short edge still holds the whole pile, within its half.
        var short_ = SceneDecor.Pile(2, 120, 20, 76, 10, 32, true, false);
        Assert.Equal(32 + SceneDecor.Blossoms(2), short_.Count);
        Assert.All(short_, p => Assert.InRange(p.X, 0, 60));
    }

    [Fact]
    public void Petals_land_one_at_a_time_in_turns_and_have_all_gathered_in_time()
    {
        var times = SceneDecor.LandingTimes([5, 3, 4]);
        Assert.Equal([5, 3, 4], times.Select(t => t.Length));
        foreach (var pile in times)
        {
            Assert.All(pile.Take(SceneDecor.SeededPetals), t => Assert.Equal(SceneDecor.Landed, t));
        }

        var falling = times.SelectMany(t => t.Skip(SceneDecor.SeededPetals)).Order().ToList();
        Assert.Equal(SceneDecor.FirstLanding, falling[0]);
        for (var i = 1; i < falling.Count; i++)
        {
            Assert.Equal(SceneDecor.LandingEvery, falling[i] - falling[i - 1], 6);
        }

        // Lowest first in each pile.
        Assert.True(times[0][3] < times[0][4]);

        // Japan's own piles have all gathered before the gathering ends.
        var all = SceneLayout.PileLandings.SelectMany(t => t).ToList();
        Assert.Equal(SceneLayout.Piles.Sum(p => p.Total), all.Count);
        Assert.True(all.Max() <= SceneDecor.GatherSeconds);
    }

    [Fact]
    public void Snow_lies_along_the_edge_and_thins_out_down_its_corners()
    {
        var cap = SceneDecor.Cap(2, 900, 18, 14, 9, 20);
        Assert.Equal(cap.Outline, SceneDecor.Cap(2, 900, 18, 14, 9, 20).Outline);
        Assert.All(cap.Outline, p => Assert.InRange(p.X, 0, 900));
        Assert.All(cap.Crest, p => Assert.InRange(p.Y, -15.5, 18));
        Assert.Contains(cap.Crest, p => p.Y < -10);
        Assert.NotEmpty(cap.Shade);
        Assert.NotEmpty(cap.Glints);

        // The top runs from one corner to the other; the outline comes back underneath.
        Assert.True(cap.Crest[0].X < 10 && cap.Crest[^1].X > 890);
        Assert.Equal(cap.Crest.Count * 2, cap.Outline.Count);

        Assert.InRange(cap.Icicles.Count, 1, 9);
        Assert.All(cap.Icicles, i =>
        {
            Assert.InRange(i.X, 0, 900);
            Assert.InRange(i.Length, 0.5, 20);
            Assert.True(i.GrowAt >= SceneDecor.GatherSeconds * SceneDecor.SnowSettles);
            Assert.True(i.GrowAt + i.GrowFor <= SceneDecor.GatherSeconds);
        });

        Assert.Empty(SceneDecor.Cap(2, 10, 4, 14, 9, 20).Outline);
        Assert.Empty(SceneDecor.Cap(2, 900, 18, 14, 0, 20).Icicles);
    }

    [Fact]
    public void Snow_keeps_what_lies_by_each_corner_while_its_edge_grows()
    {
        var narrow = SceneDecor.Cap(2, 900, 18, 14, 9, 20);
        var wide = SceneDecor.Cap(2, 1300, 18, 14, 9, 20);

        // By the left corner, the same; by the right, the same moved along with it.
        Assert.Equal(narrow.Icicles.Where(i => i.X < 250), wide.Icicles.Where(i => i.X < 250));
        Assert.Equal(
            narrow.Icicles.Where(i => i.X > 650).Select(i => i with { X = i.X + 400 }),
            wide.Icicles.Where(i => i.X > 1050));
        Assert.Equal(narrow.Glints.Where(g => g.X < 250), wide.Glints.Where(g => g.X < 250));
    }

    [Fact]
    public void Icicles_hang_under_the_player_by_its_corners()
    {
        var fringe = SceneDecor.Fringe(4, 900, 44, 8, 13);
        Assert.Equal(fringe, SceneDecor.Fringe(4, 900, 44, 8, 13));
        Assert.InRange(fringe.Count, 4, 8);
        Assert.All(fringe, i =>
        {
            Assert.True(i.X < 250 || i.X > 650);
            Assert.InRange(i.Length, 1, 13);
            Assert.True(i.GrowAt + i.GrowFor <= SceneDecor.GatherSeconds);
        });

        Assert.Equal(fringe.Where(i => i.X < 450), SceneDecor.Fringe(4, 1300, 44, 8, 13).Where(i => i.X < 650));
        Assert.Empty(SceneDecor.Fringe(4, 30, 10, 8, 13));
    }

    [Fact]
    public void An_icicle_narrows_to_its_tip()
    {
        var icicle = new SceneDecor.Icicle(0, 0, 20, 5, 1, 0, 1);
        var outline = SceneDecor.IcicleOutline(icicle);
        Assert.All(outline, p => Assert.InRange(p.Y, -1.5, 20));
        Assert.All(outline, p => Assert.InRange(p.X, -4, 5));
        Assert.Contains(outline, p => p.Y > 19.9);
    }

    [Fact]
    public void Frost_stays_in_its_corner()
    {
        var frost = SceneDecor.Frost(5, 18, SceneLayout.FrostReach);
        Assert.InRange(frost.Count, 30, 2000);
        Assert.Equal(frost.Count, SceneDecor.Frost(5, 18, SceneLayout.FrostReach).Count);
        Assert.All(frost.SelectMany(s => s.Points), p =>
        {
            Assert.InRange(p.X, -2, SceneLayout.FrostReach + 20);
            Assert.InRange(p.Y, -2, SceneLayout.FrostReach + 20);
        });
        Assert.All(frost, s => Assert.InRange(s.Opacity, 0.05, 0.5));
    }

    [Theory]
    [InlineData(ThemeScene.Japan)]
    [InlineData(ThemeScene.Snow)]
    public void A_bough_keeps_clear_of_the_title_bar_buttons(ThemeScene scene)
    {
        var bough = SceneDecor.Bough(scene);
        Assert.NotNull(bough);
        Assert.Same(bough, SceneDecor.Bough(scene));
        Assert.NotEmpty(bough.Fills);
        Assert.NotEmpty(bough.Shedding);

        // Above the page only left of the buttons, at the smallest App size.
        Assert.All(bough.Sprites, s => Assert.False(
            s.Centre.X + (s.Size / 2) > bough.Width - SceneDecor.ButtonRoom && s.Centre.Y - (s.Size / 2) < bough.PanelTop - 6));
        var wood = bough.Fills.SelectMany(f => f.Figures).SelectMany(f => f);
        Assert.All(wood, p => Assert.False(p.X > bough.Width - SceneDecor.ButtonRoom && p.Y < bough.PanelTop - 6));
        Assert.All(bough.Icicles, i => Assert.True(i.GrowAt + i.GrowFor <= SceneDecor.GatherSeconds));
        Assert.Equal(scene == ThemeScene.Snow, bough.Icicles.Count > 0);
        Assert.Null(SceneDecor.Bough(ThemeScene.None));
    }

    [Theory]
    [InlineData(ThemeScene.Japan)]
    [InlineData(ThemeScene.Snow)]
    public void What_falls_from_a_bough_comes_round_a_whole_number_of_times_per_loop(ThemeScene scene)
    {
        var shed = SceneDecor.Shedding(scene);
        Assert.NotEmpty(shed);
        Assert.All(shed, s =>
        {
            AssertWhole(SceneWeather.LoopSeconds / s.Period);
            Assert.InRange(s.Phase, 0, s.Period);
            Assert.True(s.Active < s.Period / 2);
            Assert.True(s.Fall > 0);
            Assert.True(s.Drift < 0);
            Assert.Equal(scene == ThemeScene.Japan, s.Sprite is not null);
        });
        Assert.Empty(SceneDecor.Shedding(ThemeScene.None));
    }

    [Fact]
    public void Everything_that_moves_on_its_own_turns_a_whole_number_of_times_per_loop()
    {
        var (_, speed, _, speed2) = SceneDecor.BoughSway;
        AssertWhole(speed * SceneWeather.LoopSeconds / Math.Tau);
        AssertWhole(speed2 * SceneWeather.LoopSeconds / Math.Tau);
        AssertWhole(SceneDecor.Gust.Speed * SceneWeather.LoopSeconds / Math.Tau);
        AssertWhole(SceneDecor.Gust.Shiver * SceneWeather.LoopSeconds / Math.Tau);
        for (var i = 0; i < 40; i++)
        {
            AssertWhole(SceneDecor.Twinkle(i).Speed * SceneWeather.LoopSeconds / Math.Tau);
        }
    }

    private static void AssertWhole(double turns) => Assert.Equal(Math.Round(turns), turns, 6);
}
