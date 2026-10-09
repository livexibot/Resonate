namespace Resonate.Themes.Tests;

public sealed class ProgressPatternsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5.5)]
    [InlineData(31)]
    [InlineData(200.25)]
    public void Every_line_repeats_after_its_period_so_rolling_never_jumps(double x)
    {
        Assert.Equal(ProgressPatterns.WaveAt(x), ProgressPatterns.WaveAt(x + ProgressPatterns.WaveLength), 6);
        Assert.Equal(ProgressPatterns.UnderAt(x), ProgressPatterns.UnderAt(x + ProgressPatterns.UnderLength), 6);
        Assert.Equal(ProgressPatterns.BeatAt(x), ProgressPatterns.BeatAt(x + ProgressPatterns.BeatLength), 6);
    }

    [Fact]
    public void The_heartbeat_has_one_spike_and_stays_inside_the_bar()
    {
        var heights = Enumerable.Range(0, 560).Select(i => ProgressPatterns.BeatAt(i / 10.0)).ToArray();
        Assert.InRange(heights.Max(), ProgressPatterns.BeatHeight - 0.2, ProgressPatterns.BeatHeight);
        Assert.True(heights.Min() > -4);
        Assert.Equal(0, ProgressPatterns.BeatAt(0), 6);
        Assert.Equal(0, ProgressPatterns.BeatAt(ProgressPatterns.BeatLength * 0.3), 6);
    }

    [Theory]
    [InlineData(ProgressStyle.Wave)]
    [InlineData(ProgressStyle.Liquid)]
    [InlineData(ProgressStyle.Heartbeat)]
    public void A_line_reaches_past_the_width_it_is_asked_for(ProgressStyle style)
    {
        var points = ProgressPatterns.Line(style, 300);
        Assert.Equal(0, points[0].X);
        Assert.True(points[^1].X >= 300);
        Assert.All(points.Zip(points.Skip(1)), pair => Assert.True(pair.Second.X > pair.First.X));
        Assert.Empty(ProgressPatterns.Line(ProgressStyle.Line, 300));
    }

    [Fact]
    public void The_heartbeat_keeps_its_spike_sharp()
    {
        var points = ProgressPatterns.Line(ProgressStyle.Heartbeat, 120);
        Assert.Contains(points, p => Math.Abs(p.Height - ProgressPatterns.BeatHeight) < 1e-9);
    }

    [Theory]
    [InlineData(ProgressStyle.Wave, ProgressStyle.Line)]
    [InlineData(ProgressStyle.Liquid, ProgressStyle.Line)]
    [InlineData(ProgressStyle.Heartbeat, ProgressStyle.Line)]
    [InlineData(ProgressStyle.Shimmer, ProgressStyle.Line)]
    [InlineData(ProgressStyle.Ripple, ProgressStyle.Line)]
    [InlineData(ProgressStyle.Dots, ProgressStyle.Dots)]
    [InlineData(ProgressStyle.Bold, ProgressStyle.Bold)]
    [InlineData(ProgressStyle.Gradient, ProgressStyle.Gradient)]
    public void The_volume_bar_keeps_only_styles_that_stand_still(ProgressStyle style, ProgressStyle volume) =>
        Assert.Equal(volume, ProgressPatterns.ForVolume(style));

    [Fact]
    public void Rolling_styles_roll_at_an_even_pace()
    {
        foreach (var style in Enum.GetValues<ProgressStyle>().Where(ProgressPatterns.Rolls))
        {
            var speed = ProgressPatterns.Period(style) / ProgressPatterns.RollTime(style).TotalSeconds;
            Assert.InRange(speed, 14, 45);
        }
    }
}
