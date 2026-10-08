namespace Resonate.Themes.Tests;

public sealed class CoverColourCacheTests
{
    private static readonly ThemeColor Red = ThemeColor.FromRgb(0xE0303A);
    private static readonly ThemeColor Blue = ThemeColor.FromRgb(0x2050E0);
    private static readonly ThemeColor Green = ThemeColor.FromRgb(0x20B050);

    [Fact]
    public void Remembers_a_colour_by_its_address()
    {
        var cache = new CoverColourCache(4);
        cache.Remember("https://i.scdn.co/image/a", Red);

        Assert.True(cache.TryGet("https://i.scdn.co/image/a", out var colour));
        Assert.Equal(Red, colour);
        Assert.False(cache.TryGet("https://i.scdn.co/image/b", out _));
    }

    [Fact]
    public void Forgets_the_cover_seen_longest_ago_when_full()
    {
        var cache = new CoverColourCache(2);
        cache.Remember("a", Red);
        cache.Remember("b", Blue);

        // Seeing "a" again makes "b" the oldest.
        Assert.True(cache.TryGet("a", out _));
        cache.Remember("c", Green);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Remembering_again_replaces_the_colour_without_growing()
    {
        var cache = new CoverColourCache(2);
        cache.Remember("a", Red);
        cache.Remember("b", Blue);
        cache.Remember("a", Green);
        cache.Remember("c", Red);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out var colour));
        Assert.Equal(Green, colour);
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Needs_room_for_at_least_one_colour() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CoverColourCache(0));

    [Fact]
    public void A_colourful_cover_gives_its_lively_colour()
    {
        // Mostly dark grey with a band of strong red: the red is what the cover reads as.
        var pixels = Solid(ThemeColor.FromRgb(0x202020), 10, 10);
        for (var i = 0; i < 30; i++)
        {
            Paint(pixels, i, ThemeColor.FromRgb(0xD02030));
        }

        var colour = CoverColourCache.HeaderColour(pixels, 10, 10);
        var (hue, saturation, _) = colour.ToHsl();
        Assert.True(hue is < 15 or > 345, $"hue {hue}");
        Assert.True(saturation > 0.5);
    }

    [Fact]
    public void A_black_and_white_cover_gives_its_average_grey()
    {
        var pixels = Solid(ThemeColor.FromRgb(0x404040), 4, 4);
        for (var i = 0; i < 8; i++)
        {
            Paint(pixels, i, ThemeColor.FromRgb(0xC0C0C0));
        }

        Assert.Equal(ThemeColor.FromRgb(0x808080), CoverColourCache.HeaderColour(pixels, 4, 4));
    }

    private static byte[] Solid(ThemeColor colour, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            Paint(pixels, i, colour);
        }

        return pixels;
    }

    private static void Paint(byte[] pixels, int index, ThemeColor colour)
    {
        pixels[index * 4] = colour.B;
        pixels[(index * 4) + 1] = colour.G;
        pixels[(index * 4) + 2] = colour.R;
        pixels[(index * 4) + 3] = 0xFF;
    }
}
