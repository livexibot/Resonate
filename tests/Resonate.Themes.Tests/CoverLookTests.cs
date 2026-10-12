namespace Resonate.Themes.Tests;

/// <summary>A look that follows the cover takes the cover's colours throughout, not just the accent.</summary>
public sealed class CoverLookTests
{
    private static readonly ThemeColor Blue = ThemeColor.FromHsl(210, 0.8, 0.55);
    private static readonly ThemeColor Teal = ThemeColor.FromHsl(175, 0.7, 0.5);

    [Fact]
    public void Every_coloured_part_takes_the_covers_hue_and_keeps_its_lightness()
    {
        var look = ThemePresets.Synthwave with { AdaptiveAccent = true };

        var followed = CoverLook.Follow(look, Blue, Teal);

        Assert.Equal(Blue, followed.Accent);
        Assert.Equal(Teal, followed.Accent2);
        foreach (var (before, after, hue) in new[]
        {
            (look.Background, followed.Background, 210.0),
            (look.Background2, followed.Background2, 175.0),
            (look.Sidebar, followed.Sidebar, 210.0),
            (look.Surface, followed.Surface, 210.0),
            (look.Player, followed.Player, 210.0),
            (look.Text, followed.Text, 210.0),
        })
        {
            var (_, s0, l0) = before.ToHsl();
            var (h1, s1, l1) = after.ToHsl();
            Assert.Equal(before.A, after.A);
            Assert.Equal(l0, l1, 1);
            if (s0 >= CoverLook.GreyBelow)
            {
                Assert.InRange(Math.Abs(h1 - hue), 0, 6);
                Assert.Equal(s0, s1, 1);
            }
        }

        // A coloured outline follows too.
        Assert.NotNull(look.Border);
        Assert.InRange(Math.Abs(followed.Border!.Value.ToHsl().Hue - 210), 0, 6);
    }

    [Fact]
    public void Greys_stay_grey()
    {
        var grey = new ThemeColor(255, 30, 30, 30);
        Assert.Equal(grey, CoverLook.Turn(grey, 210));
        Assert.Equal(ThemeColor.White, CoverLook.Turn(ThemeColor.White, 120));
    }

    [Fact]
    public void A_cover_of_one_colour_still_gives_two_accents()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 220;
            pixels[i + 1] = 120;
            pixels[i + 2] = 30;
            pixels[i + 3] = 255;
        }

        var accents = ArtworkColors.PickAccents(pixels, size, size);

        Assert.NotNull(accents);
        Assert.NotEqual(accents.Value.Accent, accents.Value.Second);
    }

    [Fact]
    public void A_black_and_white_cover_gives_none()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var v = (byte)(i % 8 == 0 ? 20 : 230);
            pixels[i] = v;
            pixels[i + 1] = v;
            pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }

        Assert.Null(ArtworkColors.PickAccents(pixels, size, size));
    }
    [Fact]
    public void A_black_and_white_cover_turns_a_dark_look_grey_with_a_white_accent()
    {
        var look = ThemePresets.Midnight with { AdaptiveAccent = true };

        var followed = CoverLook.Follow(look, ThemeColor.White, ThemeColor.Black);

        Assert.Equal(ThemeColor.White, followed.Accent);
        foreach (var colour in new[] { followed.Background, followed.Background2, followed.Sidebar, followed.Surface, followed.Player, followed.Text })
        {
            Assert.True(colour.ToHsl().Saturation < CoverLook.GreyBelow);
        }

        // Each part keeps its own lightness.
        Assert.Equal(look.Background.ToHsl().Lightness, followed.Background.ToHsl().Lightness, 2);
        Assert.Equal(look.Text.ToHsl().Lightness, followed.Text.ToHsl().Lightness, 2);
    }

    [Fact]
    public void A_black_and_white_cover_gives_a_light_look_a_black_accent()
    {
        var look = ThemePresets.Daylight with { AdaptiveAccent = true };

        var followed = CoverLook.Follow(look, ThemeColor.White, ThemeColor.Black);

        Assert.Equal(CoverLook.MonochromeDark, followed.Accent);
        Assert.True(followed.Accent.ToHsl().Lightness < 0.15);
    }

    [Fact]
    public void A_monochrome_look_keeps_its_accent_readable()
    {
        foreach (var preset in ThemePresets.All)
        {
            var palette = ThemePalette.From(CoverLook.Monochrome(preset));
            var page = palette.Surface.Over(palette.Background);
            Assert.True(ThemeColor.ContrastRatio(palette.Accent.Opaque, page) >= 3, preset.Name);
        }
    }
}
