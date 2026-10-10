namespace Resonate.Themes.Tests;

/// <summary>
/// Grey text, captions, the accent as text, text fields and charts stay
/// readable on every panel, including see-through panels over the lightest
/// part of what shows through them.
/// </summary>
public sealed class ReadableTextTests
{
    // Covers' accents that pass close to a light or a dark page.
    private static readonly uint[] CoverAccents = [0xF2C94C, 0x7FD6C2, 0xFF9EC4, 0x3D5A80];

    public static TheoryData<string> SeeThroughLooks() => new(ThemePresets.All.Where(p => p.PanelOpacity < 1).Select(p => p.Id));

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Captions_and_icons_read_on_every_panel_and_under_the_pointer(string id)
    {
        var look = ThemePresets.Find(id)!;
        var palette = ThemePalette.From(look);
        var page = palette.Surface.Over(palette.Background);
        var hardest = ThemePalette.HardestBackdrop(look.Normalize(), palette.TextPrimary, scenery: true);
        var panels = new[]
        {
            ("page", page),
            ("sidebar", palette.Sidebar.Over(palette.Background)),
            ("lightest page", palette.LightestPage),
            ("lightest sidebar", palette.Sidebar.Over(hardest)),
        };

        foreach (var (name, panel) in panels)
        {
            Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, panel) >= 4.5, $"captions on the {name}: {ThemeColor.ContrastRatio(palette.TextSecondary, panel):0.00}");
            Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, panel) >= 3, $"icons on the {name}: {ThemeColor.ContrastRatio(palette.TextTertiary, panel):0.00}");
        }

        var row = palette.Hover.Over(page);
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, row) >= 3, "icons on a row under the pointer");
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void The_greys_keep_their_order(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var page = palette.Surface.Over(palette.Background);

        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, page) < ThemeColor.ContrastRatio(palette.TextPrimary, page), "secondary is quieter than main text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, page) < ThemeColor.ContrastRatio(palette.TextSecondary, page), "icons are quieter than secondary text");
    }

    [Theory]
    [InlineData("midnight", 0xABACAF, 0x7B7D81)]
    [InlineData("pure-black", 0xABABAB, 0x737373)]
    [InlineData("oled", 0xA4A4A5, 0x6E6E6F)]
    [InlineData("velvet", 0xAFA09A, 0x806F6E)]
    public void Dark_opaque_looks_keep_their_greys(string id, uint secondary, uint tertiary)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);

        Assert.Equal(ThemeColor.FromRgb(secondary), palette.TextSecondary);
        Assert.Equal(ThemeColor.FromRgb(tertiary), palette.TextTertiary);
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Opaque_panels_have_nothing_lighter_behind_them(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        if (palette.Surface.A == 0xFF)
        {
            Assert.Equal(palette.Surface.Over(palette.Background), palette.LightestPage);
        }
    }

    // The lightest panel colour behind text in CI's screenshots of v0.17.0
    // (the sidebar's and the song list's, past single bright specks).
    [Theory]
    [InlineData("glass", 0x4A4A4A)]
    [InlineData("japan", 0x3B323A)]
    [InlineData("snow", 0x354D68)]
    [InlineData("synthwave", 0x653F48)]
    [InlineData("liquid-chrome", 0x303237)]
    [InlineData("cyberpunk", 0x102334)]
    [InlineData("afterhours", 0x211211)]
    public void Grey_text_reads_over_the_lightest_scenery_behind_see_through_panels(string id, uint measured)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var panel = ThemeColor.FromRgb(measured);

        Assert.True(palette.LightestPage.Luminance >= panel.Luminance, $"the stand-in {palette.LightestPage} is lighter than what was measured");
        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, panel) >= 7, "main text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, panel) >= 4.5, $"secondary text: {ThemeColor.ContrastRatio(palette.TextSecondary, panel):0.00}");
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, panel) >= 3, $"icons: {ThemeColor.ContrastRatio(palette.TextTertiary, panel):0.00}");
    }

    [Fact]
    public void Every_scenery_has_a_lightest_part()
    {
        foreach (var scene in Enum.GetValues<ThemeScene>().Where(s => s != ThemeScene.None))
        {
            Assert.NotNull(ThemePalette.SceneryLight(scene));
        }

        Assert.Null(ThemePalette.SceneryLight(ThemeScene.None));
    }

    [Theory]
    [InlineData("midnight", 0xFFFFFF)]
    [InlineData("daylight", 0x000000)]
    public void A_backdrop_of_the_other_lightness_never_pushes_text_into_the_page(string id, uint gradientEnd)
    {
        // A custom look: thin panels over a gradient whose far end is the opposite of the page.
        var look = ThemePresets.Find(id)! with
        {
            Backdrop = WindowBackdrop.Gradient,
            Background2 = ThemeColor.FromRgb(gradientEnd),
            PanelOpacity = 0.3,
        };
        var palette = ThemePalette.From(look);
        var page = palette.Surface.Over(palette.Background);

        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, page) >= 4.5, $"secondary on the page: {ThemeColor.ContrastRatio(palette.TextSecondary, page):0.00}");
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, page) >= 3, $"icons on the page: {ThemeColor.ContrastRatio(palette.TextTertiary, page):0.00}");
        Assert.True(ThemeColor.ContrastRatio(palette.AccentText, page) >= 4.5, $"accent text on the page: {ThemeColor.ContrastRatio(palette.AccentText, page):0.00}");
    }

    [Theory]
    [MemberData(nameof(SeeThroughLooks))]
    public void A_see_through_look_is_judged_over_the_hardest_of_what_shows_through(string id)
    {
        var look = ThemePresets.Find(id)!.Normalize();
        var palette = ThemePalette.From(look);
        var page = palette.Surface.Over(palette.Background);
        var lightest = palette.LightestPage;

        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, lightest) <= ThemeColor.ContrastRatio(palette.TextPrimary, page) + 0.001);
        if (look.Backdrop == WindowBackdrop.Gradient)
        {
            var other = palette.Surface.Over(look.Background2.Opaque);
            Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, lightest) <= ThemeColor.ContrastRatio(palette.TextPrimary, other) + 0.001);
        }
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Text_fields_are_outlined_at_3_to_1(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var page = palette.Surface.Over(palette.Background);

        foreach (var panel in new[] { page, palette.LightestPage })
        {
            Assert.True(ThemeColor.ContrastRatio(palette.FieldBorder.Over(panel), panel) >= 3, $"outline {palette.FieldBorder} over {panel}");
        }
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("terminal")]
    public void A_look_whose_outline_already_reads_keeps_it_on_text_fields(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        Assert.Equal(palette.Border, palette.FieldBorder);
    }

    [Fact]
    public void A_quiet_outline_is_replaced_by_the_text_colour_just_strong_enough()
    {
        var palette = ThemePalette.From(ThemePresets.Midnight);
        Assert.Equal(palette.TextPrimary.WithAlpha(0.40), palette.FieldBorder);
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
    [MemberData(nameof(PresetsAndAccents))]
    public void The_accent_reads_at_3_to_1_on_the_page(string id, uint? accent)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!, accent is { } rgb ? ThemeColor.FromRgb(rgb) : null);
        var page = palette.Surface.Over(palette.Background);

        Assert.True(ThemeColor.ContrastRatio(palette.Accent, page) >= 3, $"{palette.Accent}: {ThemeColor.ContrastRatio(palette.Accent, page):0.00}");
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Every_look_keeps_its_own_accent(string id)
    {
        var look = ThemePresets.Find(id)!;
        Assert.Equal(look.Accent.Opaque, ThemePalette.From(look).Accent);
    }

    [Theory]
    [MemberData(nameof(PresetsAndAccents))]
    public void The_accent_as_text_reads_on_every_panel_it_is_drawn_on(string id, uint? accent)
    {
        var look = ThemePresets.Find(id)!;
        var palette = ThemePalette.From(look, accent is { } rgb ? ThemeColor.FromRgb(rgb) : null);
        var page = palette.Surface.Over(palette.Background);
        var panels = new[]
        {
            ("page", page),
            ("lightest page", palette.LightestPage),
            ("row under the pointer", palette.Hover.Over(page)),
            ("selected row", palette.AccentSoft.Over(page)),
            ("player", palette.Player.Over(palette.Background)),
        };

        var everywhere = true;
        foreach (var (name, panel) in panels)
        {
            Assert.True(ThemeColor.ContrastRatio(palette.AccentText, panel) >= 4.5, $"on the {name}: {ThemeColor.ContrastRatio(palette.AccentText, panel):0.00}");
            everywhere &= ThemeColor.ContrastRatio(palette.Accent, panel) >= 4.5;
        }

        if (everywhere)
        {
            Assert.Equal(palette.Accent, palette.AccentText);
        }
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Past_bars_on_Home_s_charts_read_at_3_to_1_on_the_card(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var card = palette.Hover.Over(palette.Surface.Over(palette.Background));

        Assert.True(ThemeColor.ContrastRatio(palette.ChartQuiet.Over(card), card) >= 3, $"{palette.ChartQuiet.Opacity:0.00}");
    }

    [Theory]
    [MemberData(nameof(PresetsAndAccents))]
    public void Past_bars_are_a_lighter_shade_of_the_accent(string id, uint? accent)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!, accent is { } rgb ? ThemeColor.FromRgb(rgb) : null);

        Assert.Equal((palette.Accent.R, palette.Accent.G, palette.Accent.B), (palette.ChartQuiet.R, palette.ChartQuiet.G, palette.ChartQuiet.B));
        Assert.InRange(palette.ChartQuiet.Opacity, 0.375, 0.855);
    }

    [Theory]
    [InlineData("daylight")]
    [InlineData("paper")]
    [InlineData("sage")]
    [InlineData("bubblegum")]
    public void Light_looks_keep_a_header_glow(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        Assert.True(palette.HeroTint(ThemeColor.FromRgb(0x6C5CF0)).Opacity >= 0.085);
    }

    [Theory]
    [InlineData("japan")]
    [InlineData("snow")]
    [InlineData("synthwave")]
    [InlineData("liquid-chrome")]
    [InlineData("cyberpunk")]
    [InlineData("afterhours")]
    public void Special_looks_keep_their_header_glow(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        Assert.True(palette.HeroTint(ThemeColor.FromRgb(0x6C5CF0)).Opacity >= 0.2);
    }

    [Theory]
    [InlineData(0xFFFF00u)]
    [InlineData(0xFFFFFFu)]
    [InlineData(0x6C5CF0u)]
    [InlineData(0xFF2D55u)]
    public void Liquid_glass_s_header_glow_keeps_grey_text_readable_over_the_brightest_cover(uint cover)
    {
        var palette = ThemePalette.From(ThemePresets.Glass);
        var shown = palette.HeroTint(ThemeColor.FromRgb(cover)).Over(palette.LightestPage);

        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, shown) >= 7, "main text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, shown) >= 4.5, "secondary text");
    }

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void The_selected_row_reads_over_the_lightest_page(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)!);
        var panel = palette.LightestPage;
        var selected = palette.AccentSoft.Over(panel);
        var grey = palette.Pressed.Over(panel);
        foreach (var (text, bar) in new[] { (palette.TextPrimary, 7.0), (palette.TextSecondary, 4.5), (palette.TextTertiary, 2.5) })
        {
            var needed = Math.Min(bar, ThemeColor.ContrastRatio(text, grey));
            Assert.True(ThemeColor.ContrastRatio(text, selected) >= needed, $"{text} over {selected}");
        }
    }

    [Fact]
    public void A_hovering_player_is_nearly_opaque() =>
        Assert.True(ThemePalette.HoveringPlayerOpacity >= 0.97);

    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void Grey_text_on_a_hovering_player_reads_over_anything_scrolling_under_it(string id)
    {
        var palette = ThemePalette.From(ThemePresets.Find(id)! with { PlayerLayout = PlayerLayout.Hovering });
        foreach (var under in new[] { ThemeColor.Black, ThemeColor.White, palette.Accent })
        {
            var seen = palette.Player.Over(under);
            Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, seen) >= 4.5, $"over {under}: {ThemeColor.ContrastRatio(palette.TextSecondary, seen):0.00}");
        }
    }
}
