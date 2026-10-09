namespace Resonate.Themes.Tests;

/// <summary>The petals and snowflakes of the special looks' scenery.</summary>
public sealed class SceneWeatherTests
{
    [Theory]
    [InlineData(ThemeScene.Japan)]
    [InlineData(ThemeScene.Snow)]
    public void Everything_moves_a_whole_number_of_times_per_loop(ThemeScene scene)
    {
        for (var i = 0; i < SceneWeather.Count(scene); i++)
        {
            var p = SceneWeather.Get(scene, i);
            AssertWhole(p.Fall * SceneWeather.LoopSeconds);
            AssertWhole(p.SwaySpeed * SceneWeather.LoopSeconds / Math.Tau);
            AssertWhole(p.Spin * SceneWeather.LoopSeconds / 360);
            AssertWhole(p.Flutter * SceneWeather.LoopSeconds / Math.Tau);
            Assert.InRange(p.Start, 0, 1);
            Assert.InRange(p.Opacity, 0.05, 1);
            Assert.True(p.Fall > 0);
            Assert.Equal(p, SceneWeather.Get(scene, i));
        }
    }

    [Fact]
    public void Petals_turn_and_flutter_and_snow_has_a_few_soft_large_flakes()
    {
        var petals = Enumerable.Range(0, SceneWeather.Count(ThemeScene.Japan)).Select(i => SceneWeather.Get(ThemeScene.Japan, i)).ToList();
        Assert.All(petals, p => Assert.NotEqual(0, p.Spin));
        Assert.All(petals, p => Assert.True(p.Flutter > 0));
        Assert.Contains(petals, p => p.Spin < 0);
        Assert.Contains(petals, p => p.Spin > 0);

        // With the wind from the right, where the branch is.
        Assert.All(petals, p => Assert.True(p.Wind < 0));
        Assert.All(petals, p => Assert.True(p.Sprite is SceneSprite.PetalPink or SceneSprite.PetalDeep or SceneSprite.PetalPale or SceneSprite.PetalWhite));

        var flakes = Enumerable.Range(0, SceneWeather.Count(ThemeScene.Snow)).Select(i => SceneWeather.Get(ThemeScene.Snow, i)).ToList();
        var plain = flakes.Where(p => p.Sprite is null).ToList();
        Assert.All(plain, p => Assert.Equal(0, p.Spin));
        Assert.InRange(plain.Count(p => p.Size > SceneWeather.FlakeSize * 2), 4, 12);
        Assert.Equal(0, SceneWeather.Count(ThemeScene.None));
    }

    [Fact]
    public void Snow_has_a_few_crystals_that_turn_slowly()
    {
        var crystals = Enumerable.Range(0, SceneWeather.Count(ThemeScene.Snow))
            .Select(i => SceneWeather.Get(ThemeScene.Snow, i))
            .Where(p => p.Sprite is not null)
            .ToList();
        Assert.InRange(crystals.Count, 6, 14);
        Assert.All(crystals, p => Assert.True(p.Sprite is SceneSprite.Crystal or SceneSprite.CrystalPlate));
        Assert.All(crystals, p => Assert.InRange(Math.Abs(p.Spin), 0.1, 360.0 / 20));
        Assert.All(crystals, p => Assert.InRange(p.Size, SceneWeather.CrystalSize * 0.5, SceneWeather.CrystalSize));
        Assert.Contains(crystals, p => p.Sprite == SceneSprite.Crystal);
        Assert.Contains(crystals, p => p.Sprite == SceneSprite.CrystalPlate);
    }

    [Theory]
    [InlineData(ThemeScene.Japan)]
    [InlineData(ThemeScene.Snow)]
    public void The_smaller_part_of_the_weather_passes_behind_the_player(ThemeScene scene)
    {
        var all = Enumerable.Range(0, SceneWeather.Count(scene)).Select(i => SceneWeather.Get(scene, i)).ToList();
        var behind = all.Where(p => p.Behind).ToList();
        var front = all.Where(p => !p.Behind).ToList();

        // Enough of both that some always cross the player each way.
        Assert.InRange(behind.Count, all.Count * 0.25, all.Count * 0.55);
        Assert.InRange(front.Count, all.Count * 0.45, all.Count * 0.75);

        // Farther away is smaller: of each kind, everything behind is smaller than everything in front.
        foreach (var kind in all.GroupBy(p => p.Sprite is SceneSprite.Crystal or SceneSprite.CrystalPlate ? "crystal" : p.Sprite is null ? "flake" : "petal"))
        {
            var near = kind.Where(p => !p.Behind && !(p.Sprite is null && p.Size > SceneWeather.FlakeSize * 2)).ToList();
            var far = kind.Where(p => p.Behind).ToList();
            if (near.Count > 0 && far.Count > 0)
            {
                Assert.True(far.Max(p => p.Size) <= near.Min(p => p.Size), kind.Key);
            }
        }

        // Japan's farther petals end their fall over the page's middle, where the player floats, and fall more slowly.
        if (scene == ThemeScene.Japan)
        {
            Assert.All(behind, p => Assert.InRange(p.X + p.Wind, SceneWeather.BehindPetalsFrom - 1e-9, SceneWeather.BehindPetalsTo + 1e-9));
            Assert.True(behind.Average(p => p.Fall) < front.Average(p => p.Fall) * 0.8);
            Assert.True(behind.Average(p => p.Opacity) < front.Average(p => p.Opacity));
        }

        // The soft, out-of-focus flakes are the nearest of all.
        Assert.DoesNotContain(behind, p => p.Sprite is null && p.Size > SceneWeather.FlakeSize * 2);
        if (scene == ThemeScene.Snow)
        {
            Assert.Contains(behind, p => p.Sprite is not null);
            Assert.Contains(front, p => p.Sprite is not null);
        }
    }

    private static void AssertWhole(double turns) => Assert.Equal(Math.Round(turns), turns, 6);
}
