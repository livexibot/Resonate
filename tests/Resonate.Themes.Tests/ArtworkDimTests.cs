namespace Resonate.Themes.Tests;

public sealed class ArtworkDimTests
{
    [Fact]
    public void A_bright_cover_is_dimmed_until_white_text_on_its_panels_reads()
    {
        var pixels = Fill(4, 4, 0xF0, 0xE0, 0x40); // a bright yellow
        var factor = ArtworkColors.DimForWhiteText(pixels, 4, 4, panelOpacity: 0.1);

        Assert.True(factor < 1);
        var colour = new ThemeColor(0xFF, pixels[2], pixels[1], pixels[0]);
        var panel = ThemeColor.White.WithAlpha(0.1).Over(colour);
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.White, panel.Opaque) >= 4.4, $"contrast {ThemeColor.ContrastRatio(ThemeColor.White, panel.Opaque):0.00}");
    }

    [Fact]
    public void A_dark_cover_is_left_as_it_is()
    {
        var pixels = Fill(4, 4, 0x20, 0x18, 0x30);
        var before = (byte[])pixels.Clone();

        Assert.Equal(1, ArtworkColors.DimForWhiteText(pixels, 4, 4, panelOpacity: 0.1));
        Assert.Equal(before, pixels);
    }

    private static byte[] Fill(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0xFF;
        }

        return pixels;
    }
}
