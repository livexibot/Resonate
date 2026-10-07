namespace Resonate.Themes.Tests;

/// <summary>
/// The colour wash that the song cover backdrop shows until the user
/// allows the blurred cover: only the cover's colours, never its picture.
/// </summary>
public sealed class CoverEffectsTests
{
    private const int Size = 40;

    [Fact]
    public void The_wash_is_the_same_every_time()
    {
        var cover = Sunset();
        Assert.Equal(Wash(cover), Wash(cover));
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(7, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 5)]
    public void The_wash_has_the_requested_size_and_is_opaque(int width, int height)
    {
        var wash = ArtworkColors.ColourWash(Sunset(), Size, Size, width, height);

        Assert.Equal(width * height * 4, wash.Length);
        for (var i = 3; i < wash.Length; i += 4)
        {
            Assert.Equal(0xFF, wash[i]);
        }
    }

    [Fact]
    public void Only_the_colours_of_the_cover_count_not_where_they_are()
    {
        var bands = Sunset();
        var wash = Wash(bands);

        Assert.Equal(wash, Wash(Shuffle(bands, seed: 7)));
        Assert.Equal(wash, Wash(Shuffle(bands, seed: 2026)));
        Assert.Equal(wash, Wash(Reverse(bands)));
    }

    [Fact]
    public void Other_colours_give_another_wash() =>
        Assert.NotEqual(Wash(Sunset()), Wash(Image((_, _) => (30, 160, 150))));

    [Fact]
    public void The_wash_is_soft_even_for_a_cover_full_of_hard_edges()
    {
        var cover = Image((x, y) => ((x / 4) + (y / 4)) % 2 == 0 ? (250, 250, 250) : (200, 20, 40));
        var wash = Wash(cover);

        var steepest = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var here = wash[(((y * Size) + x) * 4) + c];
                    if (x + 1 < Size)
                    {
                        steepest = Math.Max(steepest, Math.Abs(here - wash[(((y * Size) + x + 1) * 4) + c]));
                    }

                    if (y + 1 < Size)
                    {
                        steepest = Math.Max(steepest, Math.Abs(here - wash[((((y + 1) * Size) + x) * 4) + c]));
                    }
                }
            }
        }

        Assert.InRange(steepest, 1, 8);
    }

    [Fact]
    public void A_black_and_white_cover_gives_a_grey_wash()
    {
        var wash = Wash(Image((x, y) => (x + y) % 2 == 0 ? (0, 0, 0) : (255, 255, 255)));

        for (var i = 0; i < wash.Length; i += 4)
        {
            Assert.Equal(wash[i], wash[i + 1]);
            Assert.Equal(wash[i], wash[i + 2]);
        }

        Assert.InRange(wash.Where((_, i) => i % 4 == 0).Max(), (byte)0x50, (byte)0x70);
    }

    [Fact]
    public void A_missing_cover_still_gives_a_calm_dark_wash()
    {
        var wash = ArtworkColors.ColourWash([], 0, 0, Size, Size);

        Assert.Equal(Size * Size * 4, wash.Length);
        Assert.Equal(wash, ArtworkColors.ColourWash([], 0, 0, Size, Size));
        for (var i = 0; i < wash.Length; i += 4)
        {
            Assert.Equal(wash[i], wash[i + 2]);
            Assert.InRange(wash[i], (byte)0x04, (byte)0x30);
            Assert.Equal(0xFF, wash[i + 3]);
        }
    }

    [Fact]
    public void The_wash_shows_the_cover_s_lively_colour_and_a_second_hue()
    {
        // Mostly dark grey with a big orange area and some blue.
        var cover = Image((x, y) => x < 20 ? (0x22, 0x22, 0x22) : y < 26 ? (0xF0, 0x80, 0x20) : (0x20, 0x60, 0xF0));
        var (first, second, depth) = ArtworkColors.WashColours(cover, Size, Size);

        Assert.InRange(first.ToHsl().Hue, 15, 45);
        Assert.InRange(second.ToHsl().Hue, 200, 235);
        Assert.True(depth.ToHsl().Lightness < second.ToHsl().Lightness);

        // The first pool sits upper left and the second lower right.
        var wash = Wash(cover);
        Assert.InRange(Pixel(wash, 10, 14).ToHsl().Hue, 15, 45);
        Assert.InRange(Pixel(wash, 30, 26).ToHsl().Hue, 200, 235);
    }

    [Fact]
    public void A_cover_of_one_colour_gets_depth_from_a_deeper_shade_of_it()
    {
        var (first, second, _) = ArtworkColors.WashColours(Image((_, _) => (30, 160, 150)), Size, Size);

        Assert.InRange(Math.Abs(first.ToHsl().Hue - second.ToHsl().Hue), 0, 6);
        Assert.True(second.ToHsl().Lightness < first.ToHsl().Lightness - 0.1);
    }

    [Theory]
    [InlineData(255, 255, 255)]
    [InlineData(255, 240, 60)]
    [InlineData(250, 230, 220)]
    public void Wash_colours_stay_calm_under_a_look_s_tint(int r, int g, int b)
    {
        var (first, second, depth) = ArtworkColors.WashColours(Image((_, _) => (r, g, b)), Size, Size);

        Assert.InRange(first.ToHsl().Lightness, 0, 0.55);
        Assert.InRange(second.ToHsl().Lightness, 0, 0.55);
        Assert.InRange(depth.ToHsl().Lightness, 0, 0.42);
        Assert.InRange(depth.ToHsl().Lightness, 0, first.ToHsl().Lightness);
    }

    [Fact]
    public void A_pale_cover_darkens_to_a_soft_colour_not_a_loud_one()
    {
        // A creamy white with a little red: its second colour (the average) stays nearly grey.
        var cover = Image((x, y) => ((x - 20) * (x - 20)) + ((y - 20) * (y - 20)) < 36 ? (220, 30, 40) : (245, 245, 240));
        var (_, second, _) = ArtworkColors.WashColours(cover, Size, Size);

        Assert.InRange(second.ToHsl().Saturation, 0, 0.15);
    }

    [Fact]
    public void A_short_buffer_or_a_negative_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ArtworkColors.ColourWash(new byte[8], 4, 4, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtworkColors.ColourWash(new byte[64], 4, 4, -1, 4));
    }

    [Fact]
    public void Picking_the_accent_still_finds_the_cover_s_colour()
    {
        // The wash and the accent share the hue picking; the accent is unchanged.
        var accent = ArtworkColors.PickAccent(Sunset(), Size, Size);

        Assert.NotNull(accent);
        Assert.InRange(accent.Value.ToHsl().Hue, 30, 45);
    }

    private static byte[] Wash(byte[] cover) => ArtworkColors.ColourWash(cover, Size, Size, Size, Size);

    /// <summary>A sunset in three bands: yellow, red and purple.</summary>
    private static byte[] Sunset() =>
        Image((_, y) => y < 15 ? (250, 180, 60) : y < 25 ? (230, 80, 90) : (60, 30, 90));

    private static ThemeColor Pixel(byte[] bgra, int x, int y)
    {
        var i = ((y * Size) + x) * 4;
        return new ThemeColor(bgra[i + 3], bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    private static byte[] Shuffle(byte[] bgra, int seed)
    {
        var pixels = (byte[])bgra.Clone();
        var random = new Random(seed);
        for (var i = (pixels.Length / 4) - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            for (var c = 0; c < 4; c++)
            {
                (pixels[(i * 4) + c], pixels[(j * 4) + c]) = (pixels[(j * 4) + c], pixels[(i * 4) + c]);
            }
        }

        return pixels;
    }

    private static byte[] Reverse(byte[] bgra)
    {
        var count = bgra.Length / 4;
        var pixels = new byte[bgra.Length];
        for (var i = 0; i < count; i++)
        {
            Array.Copy(bgra, i * 4, pixels, (count - 1 - i) * 4, 4);
        }

        return pixels;
    }

    private static byte[] Image(Func<int, int, (int R, int G, int B)> pixel)
    {
        var bytes = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = ((y * Size) + x) * 4;
                bytes[i] = (byte)b;
                bytes[i + 1] = (byte)g;
                bytes[i + 2] = (byte)r;
                bytes[i + 3] = 0xFF;
            }
        }

        return bytes;
    }
}
