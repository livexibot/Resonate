namespace Resonate.Themes.Tests;

public sealed class PlayerSideTests
{
    // The signal pill with only its mark, and what its word (LOSSLESS) adds.
    private const double Mark = 30;
    private const double Word = 70;

    // A hovering bar's hairline on each side.
    private const double Hairline = 2;

    [Fact]
    public void The_full_bar_s_usual_buttons_and_slider_take_what_its_column_kept()
    {
        // Device, lyrics, queue, speaker and slider: the 272 the column used to keep for them.
        var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: false, buttons: 3, signalMark: 0, signalWord: 0, barWidth: 1200, outline: 0);

        Assert.True(fit.Slider);
        Assert.Equal(272, fit.Width);
        Assert.True(PlayerSide.Room(PlayerWidthClass.Full, PlayerPlacement.FullWidth, 0) >= fit.Width);
    }

    [Fact]
    public void A_hovering_bar_leaves_out_the_slider_so_the_whole_pill_shows()
    {
        // The 912 pill: the pill and the buttons need 376 with the slider, it has 282.
        var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: false, buttons: 3, Mark, Word, barWidth: 912, Hairline);

        Assert.False(fit.Slider);
        Assert.True(fit.SignalWord);
        Assert.Equal(Mark + Word + (3 * 36) + (3 * 4) + 4 + 36, fit.Width);
        Assert.Equal(220, fit.SongMinWidth);
    }

    [Fact]
    public void A_wider_bar_keeps_the_slider_beside_the_pill()
    {
        var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: false, buttons: 3, Mark, Word, barWidth: 1008, outline: 0);

        Assert.True(fit.Slider);
        Assert.True(fit.SignalWord);
        Assert.Equal(376, fit.Width);
    }

    [Fact]
    public void The_word_goes_only_once_the_slider_has_gone()
    {
        var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: false, buttons: 4, Mark, signalWord: 100, barWidth: 912, Hairline);

        Assert.False(fit.Slider);
        Assert.False(fit.SignalWord);
        Assert.Equal(Mark + (4 * 36) + (4 * 4) + 4 + 36, fit.Width);
    }

    [Fact]
    public void Above_the_volume_the_slider_stays_and_the_wider_row_counts()
    {
        var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: true, buttons: 3, Mark, Word, barWidth: 912, Hairline);

        Assert.True(fit.Slider);
        Assert.True(fit.SignalWord);
        Assert.Equal(Mark + Word + (3 * 32) + (3 * 6), fit.Width);

        var quiet = PlayerSide.Fit(PlayerWidthClass.Compact, stacked: true, buttons: 1, signalMark: 0, signalWord: 0, barWidth: 700, Hairline);
        Assert.Equal(32 + 4 + 96, quiet.Width);
    }

    [Fact]
    public void Only_what_never_fits_takes_room_from_the_song()
    {
        // Plugins, device, lyrics, queue and the pill's mark above the volume: 182, where the narrowest compact bar has 168.
        var crowded = PlayerSide.Fit(PlayerWidthClass.Compact, stacked: true, buttons: 4, Mark, signalWord: 0, barWidth: 760, Hairline);
        Assert.Equal(182, crowded.Width);
        Assert.Equal(150 - (182 - 168), crowded.SongMinWidth);

        var roomy = PlayerSide.Fit(PlayerWidthClass.Compact, stacked: true, buttons: 3, Mark, signalWord: 0, barWidth: 600, Hairline);
        Assert.Equal(150, roomy.SongMinWidth);
    }

    [Theory]
    [InlineData(PlayerWidthClass.Full, false)]
    [InlineData(PlayerWidthClass.Full, true)]
    [InlineData(PlayerWidthClass.Compact, true)]
    public void Whatever_it_shows_fits_the_bar(PlayerWidthClass widthClass, bool stacked)
    {
        var (from, to) = widthClass == PlayerWidthClass.Full ? (PlayerPlacement.FullWidth, 2400.0) : (PlayerPlacement.CompactWidth, PlayerPlacement.FullWidth - 1);
        for (var width = from; width <= to; width += 7)
        {
            for (var buttons = 1; buttons <= 4; buttons++)
            {
                foreach (var (mark, word) in new[] { (0.0, 0.0), (Mark, 0.0), (Mark, Word), (Mark, 120.0) })
                {
                    var fit = PlayerSide.Fit(widthClass, stacked, buttons, mark, word, width, Hairline);
                    var needed = fit.Width + fit.SongMinWidth + PlayerSide.ControlsMinWidth(widthClass)
                        + (2 * PlayerSide.Padding(widthClass)) + (2 * PlayerSide.ColumnSpacing(widthClass)) + Hairline;
                    Assert.True(needed <= width + 0.5, $"{widthClass} {width}: {fit} needs {needed}");
                }
            }
        }
    }

    [Fact]
    public void A_wider_bar_never_shows_less()
    {
        PlayerSideFit? last = null;
        for (var width = PlayerPlacement.FullWidth; width <= 1600; width += 3)
        {
            var fit = PlayerSide.Fit(PlayerWidthClass.Full, stacked: false, buttons: 4, Mark, Word, width, Hairline);
            if (last is { } before)
            {
                Assert.True(fit.Slider || !before.Slider, $"the slider went at {width}");
                Assert.True(fit.SignalWord || !before.SignalWord, $"the word went at {width}");
            }

            last = fit;
        }

        Assert.True(last is { Slider: true, SignalWord: true });
    }
}
