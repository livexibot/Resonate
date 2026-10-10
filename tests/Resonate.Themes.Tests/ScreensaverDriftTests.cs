using Resonate.Themes;
using Xunit;

namespace Resonate.Themes.Tests;

public sealed class ScreensaverDriftTests
{
    [Fact]
    public void The_card_and_the_clock_stay_on_their_side_of_the_screen_and_move_slowly()
    {
        const double width = 4096;
        const double height = 1728;
        for (var second = 0.0; second < 3600; second += 0.5)
        {
            var (cardX, cardY) = ScreensaverDrift.Card(second, width, height);
            Assert.InRange(cardX, 0, ScreensaverDrift.ReachX * width);
            Assert.InRange(cardY, -ScreensaverDrift.ReachY * height, 0);

            var (clockX, clockY) = ScreensaverDrift.Clock(second, width, height);
            Assert.InRange(clockX, 0, ScreensaverDrift.ReachX * width);
            Assert.InRange(clockY, 0, ScreensaverDrift.ReachY * height);

            // A frame at 30 a second moves a few pixels at most: smooth, never a jump.
            var (nextX, nextY) = ScreensaverDrift.Card(second + (1.0 / 30), width, height);
            Assert.True(Math.Abs(nextX - cardX) < 1 && Math.Abs(nextY - cardY) < 1);

            var (glowX, glowY) = ScreensaverDrift.Glow(second);
            Assert.InRange(glowX, 0.15, 0.85);
            Assert.InRange(glowY, 0.2, 0.8);
        }
    }

    [Fact]
    public void Over_an_hour_the_card_visits_most_of_its_reach()
    {
        var xs = Enumerable.Range(0, 3600).Select(s => ScreensaverDrift.Card(s, 1000, 1000).X).ToList();
        Assert.True(xs.Max() - xs.Min() > 0.9 * ScreensaverDrift.ReachX * 1000);
    }
}
