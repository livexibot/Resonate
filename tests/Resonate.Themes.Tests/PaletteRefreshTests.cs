namespace Resonate.Themes.Tests;

/// <summary>The glow behind page headers and the accent-tinted selection.</summary>
public sealed class PaletteRefreshTests
{
    // Covers that push the clamp: very bright, white, black, dark blue, a loud red and a plain grey.
    private static readonly uint[] Covers = [0xFFFF00, 0xFFFFFF, 0x000000, 0x102040, 0xFF2D55, 0x808080];

    // Accents a song's cover can give a look that follows the cover.
    private static readonly uint[] CoverAccents = [0xFFD400, 0x1E90FF, 0x2EB8FF, 0xFF2D55, 0x202020, 0xF0F0F0];

    public static TheoryData<string, uint> PresetsAndCovers()
    {
        var data = new TheoryData<string, uint>();
        foreach (var preset in ThemePresets.All)
        {
            foreach (var cover in Covers)
            {
                data.Add(preset.Id, cover);
            }
        }

        return data;
    }

    public static TheoryData<string, uint?> PresetsAndAccents()
    {
        var data = new TheoryData<string, uint?>();
        foreach (var preset in ThemePresets.All)
        {
            data.Add(preset.Id, null);
            foreach (var accent in CoverAccents)
            {
                data.Add(preset.Id, accent);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PresetsAndCovers))]
    public void The_header_glow_keeps_every_text_readable(string id, uint cover)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var shown = palette.HeroTint(ThemeColor.FromRgb(cover)).Over(palette.Surface.Over(palette.Background));

        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, shown) >= 7, "main text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, shown) >= 4.5, "secondary text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, shown) >= 2.5, "captions");
        Assert.True(ThemeColor.ContrastRatio(palette.Accent, shown) >= 2.4, "accent");
    }

    [Theory]
    [MemberData(nameof(PresetsAndCovers))]
    public void The_header_glow_is_the_cover_s_colour_and_never_stronger_than_the_look_allows(string id, uint cover)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var source = ThemeColor.FromRgb(cover);
        var tint = palette.HeroTint(source);

        Assert.Equal((source.R, source.G, source.B), (tint.R, tint.G, tint.B));
        Assert.InRange(tint.Opacity, 0, 0.42 + 0.005);
    }

    [Theory]
    [InlineData("midnight", 0.42)]
    [InlineData("daylight", 0.24)]
    [InlineData("glass", 0.25)]
    [InlineData("pure-black", 0.30)]
    [InlineData("paper", 0.24)]
    public void A_cover_that_only_helps_the_text_glows_at_the_look_s_full_strength(string id, double strength)
    {
        // Black on a dark page and white on a light one only add contrast.
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var cover = palette.IsLight ? ThemeColor.White : ThemeColor.Black;
        Assert.Equal(strength, palette.HeroTint(cover).Opacity, 2);
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void A_lively_cover_shows_behind_the_header_in_every_preset(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        Assert.True(palette.HeroTint(ThemeColor.FromRgb(0xFF2D55)).A > 0);
    }

    [Fact]
    public void A_bright_cover_glows_less_than_a_dark_one_on_a_dark_page()
    {
        var palette = ThemePalette.From(ThemePresets.Midnight);
        Assert.True(palette.HeroTint(ThemeColor.FromRgb(0xFFFF00)).A < palette.HeroTint(ThemeColor.FromRgb(0x102040)).A);
    }

    [Fact]
    public void A_look_whose_text_barely_reads_gets_no_glow()
    {
        // Grey text is nudged to 4.5:1, short of the 7:1 the glow keeps for main text.
        var palette = ThemePalette.From(ThemePresets.Midnight with { Text = ThemeColor.FromRgb(0x6A6C70) });
        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, palette.Surface.Over(palette.Background)) < 7);
        Assert.Equal(0, palette.HeroTint(ThemeColor.FromRgb(0xFF2D55)).A);
    }

    [Theory]
    [MemberData(nameof(PresetsAndAccents))]
    public void The_selected_row_reads_at_least_as_well_as_the_grey_highlight_it_replaces(string id, uint? accent)
    {
        var look = ThemePresets.Find(id)!;
        var palette = ThemePalette.From(look, accent is { } rgb ? ThemeColor.FromRgb(rgb) : null);
        var page = palette.Surface.Over(palette.Background);
        var sidebar = palette.Sidebar.Over(palette.Background);
        (ThemeColor Text, double Bar, string Name)[] texts =
        [
            (palette.TextPrimary, 7, "main text"),
            (palette.TextSecondary, 4.5, "secondary text"),
            (palette.TextTertiary, 2.5, "captions"),
        ];

        foreach (var panel in new[] { page, sidebar })
        {
            var selected = palette.AccentSoft.Over(panel);
            var grey = palette.Pressed.Over(panel);
            foreach (var (text, bar, name) in texts)
            {
                var needed = Math.Min(bar, ThemeColor.ContrastRatio(text, grey));
                Assert.True(ThemeColor.ContrastRatio(text, selected) >= needed, name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PresetsAndAccents))]
    public void The_selected_row_is_a_visible_wash_of_the_accent(string id, uint? accent)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!, accent is { } rgb ? ThemeColor.FromRgb(rgb) : null);

        Assert.Equal((palette.Accent.R, palette.Accent.G, palette.Accent.B), (palette.AccentSoft.R, palette.AccentSoft.G, palette.AccentSoft.B));
        Assert.InRange(palette.AccentSoft.Opacity, 0.05, palette.IsLight ? 0.125 : 0.165);
    }

    [Fact]
    public void Dark_looks_get_a_stronger_wash_than_light_ones()
    {
        Assert.Equal(0.16, ThemePalette.From(ThemePresets.Midnight).AccentSoft.Opacity, 2);
        Assert.Equal(0.12, ThemePalette.From(ThemePresets.Daylight).AccentSoft.Opacity, 2);
    }
}
