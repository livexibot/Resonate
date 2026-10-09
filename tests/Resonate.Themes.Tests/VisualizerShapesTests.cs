namespace Resonate.Themes.Tests;

/// <summary>
/// The visualizer styles beyond plain bars: where they put things, how they
/// move on their own, and which can show in the player bar.
/// </summary>
public sealed class VisualizerShapesTests
{
    [Fact]
    public void Only_the_styles_around_the_cover_stay_out_of_the_player_bar()
    {
        foreach (var style in Enum.GetValues<VisualizerStyle>())
        {
            var around = style is VisualizerStyle.Radial or VisualizerStyle.Pulse;
            Assert.Equal(around, VisualizerShapes.AroundCover(style));
            Assert.Equal(!around, VisualizerShapes.FitsPlayerBar(style));
        }
    }

    [Fact]
    public void A_look_with_a_cover_style_in_the_player_bar_reads_as_off()
    {
        var look = ThemePresets.Midnight with { PlayerVisualizer = VisualizerStyle.Radial, StageVisualizer = VisualizerStyle.Off };

        var clean = look.Normalize();

        Assert.Equal(VisualizerStyle.Off, clean.PlayerVisualizer);
        Assert.Equal(VisualizerStyle.Bars, clean.StageVisualizer);
    }

    [Theory]
    [InlineData(0, 12, 0)]
    [InlineData(0.05, 12, 0)]
    [InlineData(0.06, 12, 1)]
    [InlineData(0.5, 12, 6)]
    [InlineData(1, 12, 12)]
    [InlineData(2, 12, 12)]
    [InlineData(-1, 12, 0)]
    public void Retro_lights_whole_segments_up_to_the_level(double level, int segments, int lit) =>
        Assert.Equal(lit, VisualizerShapes.RetroLit(level, segments));

    [Fact]
    public void Retro_columns_get_more_segments_when_taller_within_bounds()
    {
        Assert.Equal(VisualizerShapes.RetroMinSegments, VisualizerShapes.RetroSegments(10));
        Assert.Equal(VisualizerShapes.RetroMaxSegments, VisualizerShapes.RetroSegments(1000));
        Assert.True(VisualizerShapes.RetroSegments(132) > VisualizerShapes.RetroSegments(66));
        Assert.InRange(VisualizerShapes.RetroColumns(1200, 64), 8, 48);
        Assert.True(VisualizerShapes.RetroColumns(200, 96) <= 200 / 16);
    }

    [Fact]
    public void A_peak_falls_back_slowly_but_never_below_the_level()
    {
        var peak = VisualizerShapes.Peak(0.9f, 0.2f, 1);
        Assert.Equal(0.9f - VisualizerShapes.PeakFall, peak, 3);
        Assert.Equal(0.7f, VisualizerShapes.Peak(0.5f, 0.7f, 0.016f));
        Assert.Equal(0.3f, VisualizerShapes.Peak(0.9f, 0.3f, 10));
    }

    [Fact]
    public void Embers_rise_and_sway_a_whole_number_of_times_per_loop()
    {
        for (var i = 0; i < 64; i++)
        {
            var ember = VisualizerShapes.Ember(i);
            var rises = ember.Rise * StageBars.LoopSeconds;
            Assert.Equal(Math.Round(rises), rises, 6);
            var sways = ember.DriftSpeed * StageBars.LoopSeconds / Math.Tau;
            Assert.Equal(Math.Round(sways), sways, 6);
            Assert.InRange(ember.X, 0, 1);
            Assert.InRange(ember.Channel, 0, VisualizerShapes.EmberChannels - 1);
            Assert.InRange(ember.Size, 0.7, 1.3);
            Assert.Equal(ember, VisualizerShapes.Ember(i));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_wave_travels_a_whole_number_of_turns_per_loop(bool helix)
    {
        var turns = VisualizerShapes.WaveSpeed(helix) * StageBars.LoopSeconds / Math.Tau;
        Assert.Equal(Math.Round(turns), turns, 6);
        Assert.InRange(VisualizerShapes.WaveDots(1600, helix), 24, StageBars.MaxCount);
        Assert.Equal(24, VisualizerShapes.WaveDots(100, helix));
    }

    [Fact]
    public void Radial_bars_hug_the_cover_lows_at_the_bottom_mirrored()
    {
        const double side = 400;
        const double gap = 6;
        const int count = 64;

        // Bar 0 points straight down from under the middle of the cover's bottom edge.
        var (x, y, degrees) = VisualizerShapes.RadialPlace(0, count, side, gap);
        Assert.Equal(side / 2, x, 6);
        Assert.Equal(side + gap, y, 6);
        Assert.Equal(180, degrees, 6);

        // The bar across points straight up from above the top edge.
        (x, y, degrees) = VisualizerShapes.RadialPlace(count / 2, count, side, gap);
        Assert.Equal(side / 2, x, 6);
        Assert.Equal(-gap, y, 6);
        Assert.Equal(0, degrees, 6);

        // Every foot is outside the cover, on its square edge pushed out by the gap.
        for (var i = 0; i < count; i++)
        {
            (x, y, _) = VisualizerShapes.RadialPlace(i, count, side, gap);
            var outside = Math.Max(Math.Abs(x - (side / 2)), Math.Abs(y - (side / 2)));
            Assert.InRange(outside, (side / 2) + (gap * 0.7), (side / 2) + gap + 0.001);
            Assert.Equal(VisualizerShapes.RadialChannel(i, count), VisualizerShapes.RadialChannel(count - i, count));
            Assert.InRange(VisualizerShapes.RadialChannel(i, count), 0, VisualizerShapes.RadialChannels(count) - 1);
        }
    }

    [Fact]
    public void Pulse_rings_reach_exactly_as_far_as_allowed_when_loudest()
    {
        const double side = 300;
        const double reach = 40;
        for (var ring = 0; ring < VisualizerShapes.PulseRings; ring++)
        {
            var rest = (side / 2) + VisualizerShapes.PulseInset(ring, reach);
            var loudest = rest * (1 + VisualizerShapes.PulseGrowth(ring, side, reach));
            Assert.Equal((side / 2) + reach, loudest, 6);
            Assert.True(VisualizerShapes.PulseInset(ring, reach) < reach);
        }
    }

    [Fact]
    public void Averaged_levels_follow_their_share_of_the_bands()
    {
        var bands = new float[StageBars.UsedBands];
        for (var i = 0; i < bands.Length / 3; i++)
        {
            bands[i] = 15;
        }

        var levels = new float[3];
        StageBars.Average(bands, levels);

        Assert.Equal(1f, levels[0], 3);
        Assert.Equal(0f, levels[1], 3);
        Assert.Equal(0f, levels[2], 3);
    }

    [Fact]
    public void Numbers_are_written_the_same_in_every_language()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("0.5", VisualizerShapes.Number(0.5));
            Assert.Equal("-1234.25", VisualizerShapes.Number(-1234.25));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
