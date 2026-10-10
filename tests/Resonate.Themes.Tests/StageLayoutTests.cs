namespace Resonate.Themes.Tests;

/// <summary>
/// Where the now-playing stage puts its cover: beside the words on any
/// stage wider than tall, above them only on a narrow, tall one, and never
/// so large that the words (and the play button at their end) are cut.
/// </summary>
public sealed class StageLayoutTests
{
    // The words of the stage at the usual Text size with a one-line title: about 210 px, the play button last.
    private const double Words = 210;

    [Theory]
    [InlineData(600, 2000)]
    [InlineData(1400, 700)]
    [InlineData(572, 375)] // The CI tour's window (1008 x 711): the button was cut there.
    [InlineData(480, 400)]
    public void The_cover_sits_beside_the_words_on_a_wide_or_landscape_stage(double width, double height) =>
        Assert.True(StageLayout.SideBySide(width, height));

    [Theory]
    [InlineData(572, 600)]
    [InlineData(470, 300)]
    [InlineData(360, 800)]
    public void The_cover_goes_above_the_words_only_on_a_narrow_stage_that_is_not_much_wider_than_tall(double width, double height) =>
        Assert.False(StageLayout.SideBySide(width, height));

    [Fact]
    public void Beside_the_words_the_cover_takes_its_share_of_the_width_within_the_height_and_the_largest_size()
    {
        Assert.Equal(240, StageLayout.SideCover(572, 375, 440));
        Assert.Equal(300, StageLayout.SideCover(1400, 300, 440));
        Assert.Equal(440, StageLayout.SideCover(3700, 1400, 440));
        Assert.Equal(StageLayout.MinCover, StageLayout.SideCover(150, 60, 440));
    }

    [Fact]
    public void Above_the_words_the_cover_takes_only_the_room_they_leave()
    {
        // 572 x 600: 600 - 210 - 28 leaves 362, more than the largest cover.
        Assert.Equal<double?>(StageLayout.StackedCoverMax, StageLayout.StackedCover(572, 600, Words));

        // 420 x 380: 380 - 210 - 28 leaves 142, so the words keep all they need.
        var cover = StageLayout.StackedCover(420, 380, Words);
        Assert.Equal<double?>(142, cover);
        Assert.True(cover + StageLayout.StackedGap + Words <= 380);

        // Never wider than the stage.
        Assert.Equal<double?>(200, StageLayout.StackedCover(200, 900, Words));
    }

    [Theory]
    [InlineData(380, Words)]
    [InlineData(330, Words)]
    [InlineData(375, 300)] // Larger Text size: taller words.
    public void Above_the_words_the_cover_is_never_cut_below_its_smallest_size(double height, double words)
    {
        var cover = StageLayout.StackedCover(420, height, words);
        if (cover is { } size)
        {
            Assert.InRange(size, StageLayout.MinCover, StageLayout.StackedCoverMax);
            Assert.True(size + StageLayout.StackedGap + words <= height);
        }
        else
        {
            // Too little room even for the smallest cover: it goes beside the words instead.
            Assert.True(height - words - StageLayout.StackedGap < StageLayout.MinCover);
        }
    }

    [Fact]
    public void The_title_is_compact_above_the_words_or_beside_the_smallest_cover()
    {
        Assert.Equal(32, StageLayout.TitleSize(away: false, sideBySide: false, cover: 240));
        Assert.Equal(32, StageLayout.TitleSize(away: false, sideBySide: true, cover: StageLayout.MinCover));
        Assert.Equal(40, StageLayout.TitleSize(away: false, sideBySide: true, cover: 240));
        Assert.Equal(52, StageLayout.TitleSize(away: false, sideBySide: true, cover: 400));
        Assert.Equal(44, StageLayout.TitleSize(away: true, sideBySide: false, cover: 240));
        Assert.Equal(64, StageLayout.TitleSize(away: true, sideBySide: true, cover: 400));
    }
}
