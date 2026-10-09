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

        var flakes = Enumerable.Range(0, SceneWeather.Count(ThemeScene.Snow)).Select(i => SceneWeather.Get(ThemeScene.Snow, i)).ToList();
        Assert.All(flakes, p => Assert.Equal(0, p.Spin));
        Assert.InRange(flakes.Count(p => p.Size > SceneWeather.FlakeSize * 2), 4, 12);
        Assert.Equal(0, SceneWeather.Count(ThemeScene.None));
    }

    private static void AssertWhole(double turns) => Assert.Equal(Math.Round(turns), turns, 6);
}
