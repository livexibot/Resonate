using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

public sealed class MarqueeTests
{
    [Fact]
    public void A_song_reads_artist_title_and_length()
    {
        Assert.Equal("Daft Punk - One More Time (5:20)", Marquee.Line("Daft Punk", "One More Time", new TimeSpan(0, 5, 20)));
        Assert.Equal("A - B (0:07)", Marquee.Line("A", "B", TimeSpan.FromSeconds(7)));
        Assert.Equal("A - B (12:00)", Marquee.Line("A", "B", TimeSpan.FromMinutes(12)));
    }

    [Fact]
    public void An_hour_or_more_reads_hours_minutes_and_seconds()
    {
        Assert.Equal("A - Mix (59:59)", Marquee.Line("A", "Mix", new TimeSpan(0, 59, 59)));
        Assert.Equal("A - Mix (1:00:00)", Marquee.Line("A", "Mix", TimeSpan.FromHours(1)));
        Assert.Equal("A - Mix (1:02:03)", Marquee.Line("A", "Mix", new TimeSpan(1, 2, 3)));
        Assert.Equal("A - Mix (12:00:05)", Marquee.Line("A", "Mix", new TimeSpan(12, 0, 5)));
    }

    [Fact]
    public void Lengths_count_whole_seconds()
    {
        Assert.Equal("A - B (3:45)", Marquee.Line("A", "B", TimeSpan.FromSeconds(225.9)));
        Assert.Equal("A - B", Marquee.Line("A", "B", TimeSpan.FromSeconds(0.5)));
    }

    [Fact]
    public void Missing_parts_are_left_out()
    {
        Assert.Equal("Intro (0:42)", Marquee.Line(null, "Intro", TimeSpan.FromSeconds(42)));
        Assert.Equal("Intro (0:42)", Marquee.Line("  ", "Intro", TimeSpan.FromSeconds(42)));
        Assert.Equal("Artist (1:00)", Marquee.Line("Artist", string.Empty, TimeSpan.FromMinutes(1)));
        Assert.Equal("A - T", Marquee.Line(" A ", " T ", TimeSpan.Zero));
        Assert.Equal("A - T", Marquee.Line("A", "T", TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void Nothing_playing_is_an_empty_line()
    {
        Assert.Equal(string.Empty, Marquee.Line(null, null, TimeSpan.FromMinutes(3)));
        Assert.Equal(string.Empty, Marquee.Line(string.Empty, " ", TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    [InlineData(200, true)]
    public void Lines_of_31_characters_or_more_scroll(int length, bool scrolls) =>
        Assert.Equal(scrolls, Marquee.Scrolls(new string('x', length)));

    [Fact]
    public void A_short_line_is_padded_and_never_moves()
    {
        var expected = "Hello".PadRight(Marquee.Cells);
        Assert.Equal(expected, Marquee.Window("Hello", 0));
        Assert.Equal(expected, Marquee.Window("Hello", 7));
        Assert.Equal(expected, Marquee.Window("Hello", -3));
        Assert.Equal(new string(' ', Marquee.Cells), Marquee.Window(string.Empty, 4));
    }

    [Fact]
    public void A_long_line_runs_through_the_gap_into_its_start()
    {
        var line = string.Concat(Enumerable.Repeat("0123456789", 4));
        Assert.Equal(line[..31], Marquee.Window(line, 0));
        Assert.Equal(line[10..] + " ", Marquee.Window(line, 10));
        Assert.Equal("  ***  " + line[..24], Marquee.Window(line, 40));
        Assert.Equal("***  " + line[..26], Marquee.Window(line, 42));

        // One lap is the line and the gap; offsets wrap either way.
        Assert.Equal(Marquee.Window(line, 0), Marquee.Window(line, 47));
        Assert.Equal(Marquee.Window(line, 5), Marquee.Window(line, 5 + (47 * 3)));
        Assert.Equal(" " + line[..30], Marquee.Window(line, -1));
        Assert.Equal(Marquee.Window(line, int.MaxValue % 47), Marquee.Window(line, int.MaxValue));
        Assert.Equal(Marquee.Window(line, (int.MinValue % 47) + 47), Marquee.Window(line, int.MinValue));
    }

    [Fact]
    public void Every_window_is_one_marquee_wide()
    {
        var line = "Boards of Canada - Roygbiv (2:31) and then some more";
        for (var offset = -60; offset < 120; offset++)
        {
            Assert.Equal(Marquee.Cells, Marquee.Window(line, offset).Length);
        }

        Assert.Equal(31, Marquee.Cells);
        Assert.Equal("  ***  ", Marquee.Gap);
        Assert.Equal(TimeSpan.FromMilliseconds(220), Marquee.Step);
    }
}
