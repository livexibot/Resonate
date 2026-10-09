namespace Resonate.Themes.Tests;

public sealed class BeatGlowTests
{
    [Fact]
    public void The_bass_comes_from_the_lowest_bands_and_stays_dark_when_quiet()
    {
        var loud = new float[75];
        Array.Fill(loud, 0f);
        for (var i = 0; i < BeatGlow.BassBands; i++)
        {
            loud[i] = BeatGlow.Rows;
        }

        var highOnly = new float[75];
        for (var i = 20; i < 75; i++)
        {
            highOnly[i] = BeatGlow.Rows;
        }

        Assert.Equal(1f, BeatGlow.Bass(loud), 4);
        Assert.Equal(0f, BeatGlow.Bass(highOnly), 4);
        Assert.Equal(0.25f, BeatGlow.Bass(loud.Select(v => v / 2).ToArray()), 4);
        Assert.Equal(0f, BeatGlow.Bass([]), 4);
    }

    [Fact]
    public void The_glow_jumps_up_with_a_beat_and_fades_at_any_refresh_rate()
    {
        Assert.Equal(0.9f, BeatGlow.Follow(0.2f, 0.9f, 0.016f), 4);

        // One second at 60 and at 165 frames a second fades as far.
        var at60 = 1f;
        for (var i = 0; i < 60; i++)
        {
            at60 = BeatGlow.Follow(at60, 0, 1 / 60f);
        }

        var at165 = 1f;
        for (var i = 0; i < 165; i++)
        {
            at165 = BeatGlow.Follow(at165, 0, 1 / 165f);
        }

        Assert.Equal(at60, at165, 3);
        Assert.Equal(0f, at60, 3);
    }

    [Theory]
    [InlineData(40, 100, 40)]
    [InlineData(40, 150, 60)]
    [InlineData(48, 200, 96)]
    [InlineData(40, 130, 50)]
    public void Covers_grow_on_the_cover_size_steps(int usual, int percent, int expected) =>
        Assert.Equal(expected, AppScale.Cover(usual, percent));
}
