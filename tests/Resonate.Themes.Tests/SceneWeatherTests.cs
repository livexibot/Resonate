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

    private static void AssertWhole(double turns) => Assert.Equal(Math.Round(turns), turns, 6);
}
