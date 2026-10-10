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

    [Theory]
    [InlineData(0xF0, 0xE0, 0x40)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x90, 0xC8, 0xF0)]
    public void A_bright_cover_is_dimmed_until_grey_text_on_liquid_glass_reads(byte r, byte g, byte b)
    {
        var glass = ThemePalette.From(ThemePresets.Glass);
        var pixels = Fill(4, 4, r, g, b);
        ArtworkColors.DimForWhiteText(pixels, 4, 4, glass.Surface.Opacity);

        var panel = glass.Surface.Over(new ThemeColor(0xFF, pixels[2], pixels[1], pixels[0]));
        Assert.True(ThemeColor.ContrastRatio(glass.TextSecondary, panel) >= 4.5, $"secondary text {ThemeColor.ContrastRatio(glass.TextSecondary, panel):0.00} over {panel}");
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.White, panel) >= 7, $"white {ThemeColor.ContrastRatio(ThemeColor.White, panel):0.00}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.15)]
    public void The_brightest_cover_leaves_the_glass_panel_no_lighter_than_74_grey(double opacity)
    {
        var panel = ThemeColor.White.WithAlpha(opacity).Over(ArtworkColors.BrightestCover(opacity));
        Assert.True(panel.R <= 0x4A, $"{panel}");
    }

    [Fact]
    public void A_cover_is_never_dimmed_to_black_however_much_white_the_panels_add() =>
        Assert.True(ArtworkColors.BrightestCover(0.5).Luminance >= 0.02);

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
