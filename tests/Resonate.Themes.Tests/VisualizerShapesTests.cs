namespace Resonate.Themes.Tests;

/// <summary>
/// The visualizer styles beyond plain bars: which are offered, how the
/// retired ones read, where things go, how they move on their own, and how
/// Amount and Size read for each.
/// </summary>
public sealed class VisualizerShapesTests
{
    [Fact]
    public void The_menus_offer_the_kept_and_new_styles_only()
    {
        Assert.Equal(
            [VisualizerStyle.Bars, VisualizerStyle.Mirror, VisualizerStyle.Lines, VisualizerStyle.Pills, VisualizerStyle.Silk, VisualizerStyle.Aurora, VisualizerStyle.Retro, VisualizerStyle.Pulse],
            VisualizerShapes.Offered);
    }

    [Theory]
    [InlineData(VisualizerStyle.Dots, VisualizerStyle.Pills)]
    [InlineData(VisualizerStyle.Wave, VisualizerStyle.Silk)]
    [InlineData(VisualizerStyle.Helix, VisualizerStyle.Silk)]
    [InlineData(VisualizerStyle.Embers, VisualizerStyle.Aurora)]
    [InlineData(VisualizerStyle.Radial, VisualizerStyle.Pulse)]
    [InlineData(VisualizerStyle.Retro, VisualizerStyle.Retro)]
    public void Retired_styles_read_as_their_replacements(VisualizerStyle saved, VisualizerStyle drawn)
    {
        Assert.Equal(drawn, VisualizerShapes.Current(saved));
        Assert.Contains(drawn, VisualizerShapes.Offered);
    }

    [Fact]
    public void A_saved_look_with_a_retired_style_reads_as_its_replacement()
    {
        var look = ThemePresets.Midnight with { StageVisualizer = VisualizerStyle.Embers, PlayerVisualizer = VisualizerStyle.Dots };

        var clean = look.Normalize();

        Assert.Equal(VisualizerStyle.Aurora, clean.StageVisualizer);
        Assert.Equal(VisualizerStyle.Pills, clean.PlayerVisualizer);
    }

    [Fact]
    public void No_preset_uses_a_retired_style()
    {
        foreach (var preset in ThemePresets.All)
        {
            Assert.Contains(preset.StageVisualizer, VisualizerShapes.Offered);
            Assert.True(preset.PlayerVisualizer == VisualizerStyle.Off || VisualizerShapes.Offered.Contains(preset.PlayerVisualizer), preset.Name);
        }
    }

    [Fact]
    public void Only_the_styles_around_the_cover_stay_out_of_the_player_bar()
    {
        foreach (var style in Enum.GetValues<VisualizerStyle>())
        {
            var around = VisualizerShapes.Current(style) == VisualizerStyle.Pulse;
            Assert.Equal(around, VisualizerShapes.AroundCover(style));
            Assert.Equal(!around, VisualizerShapes.FitsPlayerBar(style));
        }
    }

    [Fact]
    public void A_look_with_a_cover_style_in_the_player_bar_reads_as_off()
    {
        var look = ThemePresets.Midnight with { PlayerVisualizer = VisualizerStyle.Pulse, StageVisualizer = VisualizerStyle.Off };

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
    public void Amount_and_size_mean_more_and_larger_in_every_style()
    {
        Assert.True(VisualizerShapes.PillCount(4000, 96) > VisualizerShapes.PillCount(4000, 16));
        Assert.True(VisualizerShapes.SilkColumns(4000, 96).Levels > VisualizerShapes.SilkColumns(4000, 16).Levels);
        Assert.True(VisualizerShapes.AuroraGlows(96) > VisualizerShapes.AuroraGlows(16));
        Assert.True(VisualizerShapes.RetroColumns(4000, 96) > VisualizerShapes.RetroColumns(4000, 16));
        Assert.Equal(2, VisualizerShapes.PulseRingCount(16));
        Assert.Equal(5, VisualizerShapes.PulseRingCount(96));
        Assert.Equal(2, VisualizerShapes.SizeBetween(2, 9, 0.2), 6);
        Assert.Equal(9, VisualizerShapes.SizeBetween(2, 9, 0.9), 6);

        // Never more pills than fit with room to be round.
        Assert.True(VisualizerShapes.PillCount(200, 96) <= 25);
    }

    [Fact]
    public void Silk_columns_run_smoothly_between_levels_and_taper_at_the_ends()
    {
        var (columns, levels) = VisualizerShapes.SilkColumns(1200, 64);
        Assert.True(columns >= levels);
        var lastAt = -1.0;
        for (var c = 0; c < columns; c++)
        {
            var (level, toward) = VisualizerShapes.SilkBlend(c, columns, levels);
            Assert.InRange(level, 0, levels - 2);
            Assert.InRange(toward, 0, 1);
            var at = level + toward;
            Assert.True(at >= lastAt);
            lastAt = at;
        }

        Assert.Equal(levels - 1, lastAt, 6);
        Assert.Equal(0, VisualizerShapes.SilkTaper(0, columns), 6);
        Assert.Equal(1, VisualizerShapes.SilkTaper(columns / 2, columns), 6);
    }

    [Fact]
    public void Aurora_glows_spread_across_and_drift_a_whole_number_of_times_per_loop()
    {
        for (var count = 4; count <= 10; count++)
        {
            var lastX = 0.0;
            for (var i = 0; i < count; i++)
            {
                var glow = VisualizerShapes.AuroraGlow(i, count);
                Assert.InRange(glow.X, 0, 1);
                Assert.True(glow.X > lastX);
                lastX = glow.X;
                var turns = glow.DriftSpeed * StageBars.LoopSeconds / Math.Tau;
                Assert.Equal(Math.Round(turns), turns, 6);
                Assert.Equal(glow, VisualizerShapes.AuroraGlow(i, count));
            }
        }
    }

    [Fact]
    public void Pulse_rings_reach_exactly_as_far_as_allowed_when_loudest()
    {
        const double side = 300;
        const double reach = 40;
        for (var rings = 2; rings <= 5; rings++)
        {
            for (var ring = 0; ring < rings; ring++)
            {
                var rest = (side / 2) + VisualizerShapes.PulseInset(ring, rings, reach);
                var loudest = rest * (1 + VisualizerShapes.PulseGrowth(ring, rings, side, reach));
                Assert.Equal((side / 2) + reach, loudest, 6);
                Assert.True(VisualizerShapes.PulseInset(ring, rings, reach) < reach);
            }
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
