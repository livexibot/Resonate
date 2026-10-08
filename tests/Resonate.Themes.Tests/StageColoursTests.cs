namespace Resonate.Themes.Tests;

/// <summary>
/// The now-playing stage's colours: the cover's main colours for the clouds,
/// and every colour behind the title kept readable on every preset.
/// </summary>
public sealed class StageColoursTests
{
    private const int Size = 40;

    [Fact]
    public void Two_colour_covers_give_both_colours_most_first()
    {
        // Three quarters red, one quarter blue.
        var cover = Fill((x, _) => x < 30 ? ThemeColor.FromRgb(0xD02020) : ThemeColor.FromRgb(0x2040E0));

        var colours = StageColours.Palette(cover, Size, Size);

        Assert.Equal(StageColours.CloudCount, colours.Count);
        Assert.Equal(ThemeColor.FromRgb(0xD02020), colours[0]);
        Assert.Contains(ThemeColor.FromRgb(0x2040E0), colours);
    }

    [Fact]
    public void The_palette_is_the_same_every_time()
    {
        var cover = Fill((x, y) => new ThemeColor(0xFF, (byte)(x * 6), (byte)(y * 6), (byte)((x + y) * 3)));

        Assert.Equal(StageColours.Palette(cover, Size, Size), StageColours.Palette(cover, Size, Size));
    }

    [Fact]
    public void A_gradient_gives_distinct_colours()
    {
        var cover = Fill((x, y) => new ThemeColor(0xFF, (byte)(x * 6), (byte)(y * 6), (byte)((x + y) * 3)));

        var colours = StageColours.Palette(cover, Size, Size);

        Assert.Equal(StageColours.CloudCount, colours.Distinct().Count());
    }

    [Fact]
    public void A_see_through_or_empty_picture_gives_black()
    {
        var colours = StageColours.Palette(new byte[Size * Size * 4], Size, Size, 3);

        Assert.Equal([ThemeColor.Black, ThemeColor.Black, ThemeColor.Black], colours);
    }

    [Theory]
    [InlineData("midnight")]
    [InlineData("daylight")]
    [InlineData("glass")]
    [InlineData("pure-black")]
    [InlineData("synthwave")]
    [InlineData("paper")]
    public void Text_stays_readable_over_any_cover_colour(string preset)
    {
        var palette = ThemePalette.From(ThemePresets.Find(preset)!);
        uint[] colours = [0xFFFFFF, 0x000000, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFE000, 0x00E0FF, 0x808080, 0xF0C0D0, 0x302010];
        foreach (var rgb in colours)
        {
            var safe = StageColours.ForText(ThemeColor.FromRgb(rgb), palette);

            Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary.Over(safe), safe) >= 7, $"{preset}: main text over {safe}");
            Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary.Over(safe), safe) >= 4.5, $"{preset}: secondary text over {safe}");
        }
    }

    [Fact]
    public void A_colour_that_is_already_readable_is_kept()
    {
        var palette = ThemePalette.From(ThemePresets.Find("midnight")!);
        var deep = ThemeColor.FromRgb(0x101830);

        Assert.Equal(deep, StageColours.ForText(deep, palette));
    }

    [Fact]
    public void Every_pixel_of_a_picture_is_made_readable()
    {
        var palette = ThemePalette.From(ThemePresets.Find("daylight")!);
        var picture = Fill((x, y) => new ThemeColor(0xFF, (byte)(x * 6), (byte)(y * 6), 0x40));

        StageColours.ForText(picture, palette);

        for (var i = 0; i < picture.Length; i += 4)
        {
            Assert.True(StageColours.Readable(new ThemeColor(picture[i + 3], picture[i + 2], picture[i + 1], picture[i]), palette));
        }
    }

    [Fact]
    public void The_away_screen_text_walks_around_a_small_square()
    {
        Assert.Equal((0, 0), StageColours.BurnInShift(0, 6));
        var places = Enumerable.Range(0, 9).Select(m => StageColours.BurnInShift(m, 6)).ToList();
        Assert.Equal(9, places.Distinct().Count());
        Assert.All(places, p => Assert.True(Math.Abs(p.X) <= 6 && Math.Abs(p.Y) <= 6));
        Assert.Equal(StageColours.BurnInShift(3, 6), StageColours.BurnInShift(12, 6));
    }

    private static byte[] Fill(Func<int, int, ThemeColor> colourAt)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var c = colourAt(x, y);
                var i = ((y * Size) + x) * 4;
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = 0xFF;
            }
        }

        return pixels;
    }
}
