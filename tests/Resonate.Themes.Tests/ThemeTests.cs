namespace Resonate.Themes.Tests;

public sealed class ThemeColorTests
{
    [Theory]
    [InlineData("#8B7CFF", 0xFF, 0x8B, 0x7C, 0xFF)]
    [InlineData("8b7cff", 0xFF, 0x8B, 0x7C, 0xFF)]
    [InlineData("#80FFFFFF", 0x80, 0xFF, 0xFF, 0xFF)]
    [InlineData("#F0A", 0xFF, 0xFF, 0x00, 0xAA)]
    public void Parses_hex_colours(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(ThemeColor.TryParse(text, out var color));
        Assert.Equal(new ThemeColor(a, r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void Rejects_anything_else(string? text) => Assert.False(ThemeColor.TryParse(text, out _));

    [Fact]
    public void Writes_opaque_colours_without_alpha()
    {
        Assert.Equal("#8B7CFF", ThemeColor.FromRgb(0x8B7CFF).ToString());
        Assert.Equal("#29FFFFFF", ThemeColor.White.WithAlpha(0.16).ToString());
    }

    [Fact]
    public void Contrast_matches_the_WCAG_formula()
    {
        Assert.Equal(21, ThemeColor.ContrastRatio(ThemeColor.Black, ThemeColor.White), 1);
        Assert.Equal(1, ThemeColor.ContrastRatio(ThemeColor.White, ThemeColor.White), 3);
    }

    [Fact]
    public void Hsl_round_trips()
    {
        var color = ThemeColor.FromRgb(0xC4532D);
        var (h, s, l) = color.ToHsl();
        var back = ThemeColor.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - color.R), 0, 1);
        Assert.InRange(Math.Abs(back.G - color.G), 0, 1);
        Assert.InRange(Math.Abs(back.B - color.B), 0, 1);
    }

    [Fact]
    public void A_translucent_colour_over_another_blends()
    {
        var blended = ThemeColor.White.WithAlpha(0.5).Over(ThemeColor.Black);
        Assert.InRange(blended.R, 126, 129);
        Assert.Equal(0xFF, blended.A);
    }
}

public sealed class PresetTests
{
    [Fact]
    public void There_are_twenty_presets_in_four_groups_with_unique_ids_and_names()
    {
        Assert.Equal(20, ThemePresets.All.Count);
        Assert.Equal(ThemePresets.Dark.Count + ThemePresets.Light.Count + ThemePresets.Black.Count + ThemePresets.Special.Count, ThemePresets.All.Count);
        Assert.All(ThemePresets.Special, p => Assert.NotEqual(ThemeScene.None, p.Scene));
        Assert.All(ThemePresets.Dark.Concat(ThemePresets.Light).Concat(ThemePresets.Black), p => Assert.Equal(ThemeScene.None, p.Scene));
        Assert.Equal(ThemePresets.All.Count, ThemePresets.All.Select(p => p.Id).Distinct().Count());
        Assert.Equal(ThemePresets.All.Count, ThemePresets.All.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void Every_preset_has_the_player_at_the_bottom() =>
        Assert.All(ThemePresets.All, p => Assert.True(
            p.PlayerLayout is PlayerLayout.Docked or PlayerLayout.Floating or PlayerLayout.Hovering or PlayerLayout.Corner or PlayerLayout.CornerLeft,
            $"{p.Name} has the player at {p.PlayerLayout}"));

    [Fact]
    public void The_presets_use_only_fonts_that_come_with_Resonate_or_Windows() =>
        Assert.All(
            ThemePresets.All.SelectMany(p => new[] { p.DisplayFont, p.TextFont }).Where(f => !f.StartsWith("Segoe", StringComparison.Ordinal) && !f.StartsWith("Sitka", StringComparison.Ordinal) && f != "Bahnschrift"),
            font => Assert.NotNull(BundledFonts.Find(font)));

    [Fact]
    public void The_ids_the_first_release_saved_still_work()
    {
        Assert.NotNull(ThemePresets.Find("midnight"));
        Assert.NotNull(ThemePresets.Find("pure-black"));
        Assert.NotNull(ThemePresets.Find("daylight"));
    }

    [Fact]
    public void Presets_are_already_normal() =>
        Assert.All(ThemePresets.All, p => Assert.Equal(p, p.Normalize()));

    [Fact]
    public void Each_preset_differs_from_every_other_in_more_than_colour()
    {
        foreach (var a in ThemePresets.All)
        {
            foreach (var b in ThemePresets.All.Where(b => b != a))
            {
                var differences = new[]
                {
                    a.Backdrop != b.Backdrop,
                    a.PlayerLayout != b.PlayerLayout,
                    a.Progress != b.Progress,
                    a.PlayButton != b.PlayButton,
                    a.Cover != b.Cover,
                    a.Shadow != b.Shadow,
                    a.Buttons != b.Buttons,
                    a.DisplayFont != b.DisplayFont,
                    Math.Abs(a.CornerRadius - b.CornerRadius) >= 4,
                }.Count(d => d);
                Assert.True(differences >= 2, $"{a.Name} and {b.Name} are too alike.");
            }
        }
    }

    [Fact]
    public void The_presets_cover_light_dark_and_true_black()
    {
        var palettes = ThemePresets.All.Select(p => ThemePalette.From(p)).ToList();
        Assert.Contains(palettes, p => p.IsLight);
        Assert.Contains(palettes, p => !p.IsLight);
        Assert.Contains(ThemePresets.All, p => p.Background == ThemeColor.Black && p.Surface == ThemeColor.Black);
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Every_preset_is_readable(string id)
    {
        var preset = ThemePresets.Find(id)!;
        var palette = ThemePalette.From(preset);
        var surface = palette.Surface.Over(palette.Background);

        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, surface) >= 7, "main text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextSecondary, surface) >= 4.5, "secondary text");
        Assert.True(ThemeColor.ContrastRatio(palette.TextTertiary, surface) >= 2.5, "captions");
        Assert.True(ThemeColor.ContrastRatio(palette.OnAccent, palette.Accent) >= 4.5, "text on the accent");
        Assert.True(ThemeColor.ContrastRatio(palette.Accent, surface) >= 2.4, "accent on the page");
    }

    public static TheoryData<string> PresetIds() => new(ThemePresets.All.Select(p => p.Id));
}

public sealed class PaletteTests
{
    [Fact]
    public void Unreadable_choices_are_nudged_until_they_read()
    {
        var look = ThemePresets.Midnight with { Text = ThemeColor.FromRgb(0x1C1E24), Accent = ThemeColor.FromRgb(0x1A1C22) };
        var palette = ThemePalette.From(look);
        var surface = palette.Surface.Over(palette.Background);

        Assert.True(ThemeColor.ContrastRatio(palette.TextPrimary, surface) >= 4.5);
        Assert.True(ThemeColor.ContrastRatio(palette.Accent, surface) >= 2.4);
    }

    [Fact]
    public void Glass_panels_are_translucent_and_the_mode_follows_what_shows()
    {
        var palette = ThemePalette.From(ThemePresets.Glass);
        Assert.InRange(palette.Surface.A, 1, 60);
        Assert.False(palette.IsLight);
    }

    [Fact]
    public void An_adaptive_accent_replaces_the_look_s_accent()
    {
        var cover = ThemeColor.FromRgb(0x2EB8FF);
        Assert.Equal(cover, ThemePalette.From(ThemePresets.Midnight, cover).Accent);
    }

    [Theory]
    [InlineData(ButtonShape.Round, 999)]
    [InlineData(ButtonShape.Square, 3)]
    public void Button_shape_sets_the_button_corners(ButtonShape shape, double expected) =>
        Assert.Equal(expected, ThemePalette.From(ThemePresets.Midnight with { Buttons = shape, CornerRadius = 20 }).CornerButton);

    [Fact]
    public void Outline_and_plain_play_buttons_have_no_fill()
    {
        Assert.Equal(0, ThemePalette.From(ThemePresets.Midnight with { PlayButton = PlayButtonStyle.Outline }).PlayButtonBackground.A);
        Assert.Equal(0, ThemePalette.From(ThemePresets.Midnight with { PlayButton = PlayButtonStyle.Plain }).PlayButtonBackground.A);
    }

    [Fact]
    public void No_shadow_style_draws_nothing() =>
        Assert.False(ThemePalette.From(ThemePresets.Midnight with { Shadow = ShadowStyle.None }).PanelShadow.IsVisible);

    [Fact]
    public void Bases_give_light_dark_and_black_looks()
    {
        Assert.True(ThemePalette.From(ThemePresets.Synthwave.WithBase(ThemeBase.Light)).IsLight);
        Assert.False(ThemePalette.From(ThemePresets.Daylight.WithBase(ThemeBase.Dark)).IsLight);
        var black = ThemePresets.Daylight.WithBase(ThemeBase.Black);
        Assert.Equal(ThemeColor.Black, black.Surface);
        Assert.Equal(ThemePresets.Daylight.Accent, black.Accent);
    }
}

public sealed class ThemeJsonTests
{
    [Theory]
    [MemberData(nameof(PresetTests.PresetIds), MemberType = typeof(PresetTests))]
    public void A_look_survives_being_copied_as_text(string id)
    {
        var look = ThemePresets.Find(id)!;
        Assert.Equal(look, ThemeJson.Import(ThemeJson.Export(look)));
    }

    [Fact]
    public void Shared_text_is_readable()
    {
        var text = ThemeJson.Export(ThemePresets.Paper);
        Assert.Contains("\"resonateLook\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"accent\": \"#C4532D\"", text, StringComparison.Ordinal);
        Assert.Contains("\"progress\": \"Wave\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("{}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"name\": \"no colours\"}")]
    [InlineData("{\"resonateLook\": 1, \"look\": {\"accent\": \"not a colour\"}}")]
    [InlineData("{\"resonateLook\": 1, \"look\": {\"accent\": \"#FFFFFF\", \"progress\": \"Sparkles\"}}")]
    public void Anything_that_is_not_a_look_is_refused(string? text) => Assert.Null(ThemeJson.Import(text));

    [Fact]
    public void Extreme_values_are_clamped()
    {
        const string Text = """
            { "resonateLook": 1, "look": {
              "name": "  Loud\u0007 ",
              "accent": "#FF0000",
              "cornerRadius": 5000,
              "panelOpacity": -3,
              "borderWidth": 1e9,
              "displayFont": "",
              "playerWidth": -5,
              "playerHeight": 1e9,
              "playerOffsetX": -1e9
            } }
            """;
        var look = ThemeJson.Import(Text)!;
        Assert.Equal("Loud", look.Name);
        Assert.Equal(ThemeDefinition.MaxCornerRadius, look.CornerRadius);
        Assert.Equal(0, look.PanelOpacity);
        Assert.Equal(ThemeDefinition.MaxBorderWidth, look.BorderWidth);
        Assert.Equal(ThemeDefinition.DefaultDisplayFont, look.DisplayFont);
        Assert.Equal(ThemeDefinition.MinPlayerWidth, look.PlayerWidth);
        Assert.Equal(ThemeDefinition.MaxPlayerHeight, look.PlayerHeight);
        Assert.Equal(-ThemeDefinition.MaxPlayerOffset, look.PlayerOffsetX);
        Assert.Null(look.PlayerOffsetY);
    }

    [Fact]
    public void A_bare_look_without_the_envelope_is_accepted()
    {
        var look = ThemeJson.Import("""{ "name": "Bare", "accent": "#00FF88", "background": "#101010" }""");
        Assert.NotNull(look);
        Assert.Equal(ThemeColor.FromRgb(0x00FF88), look.Accent);
    }

    [Fact]
    public void Very_long_text_is_refused() =>
        Assert.Null(ThemeJson.Import("{\"accent\":\"#FFFFFF\",\"name\":\"" + new string('x', ThemeJson.MaxLength) + "\"}"));
}

public sealed class ThemeLibraryTests
{
    [Fact]
    public void Starts_on_the_saved_choice_or_the_default()
    {
        Assert.Equal("paper", new ThemeLibrary("paper", null, null).Active.Id);
        Assert.Equal("midnight", new ThemeLibrary("gone", null, null).Active.Id);
        Assert.Equal("midnight", new ThemeLibrary(null, null, null).Active.Id);
        Assert.Equal("midnight", new ThemeLibrary(ThemeLibrary.CustomId, null, null).Active.Id);
    }

    [Fact]
    public void Editing_a_preset_makes_a_custom_copy_and_leaves_the_preset_alone()
    {
        var library = new ThemeLibrary("synthwave", null, null);
        var edited = library.Edit(t => t with { CornerRadius = 20 });

        Assert.Equal(ThemeLibrary.CustomId, library.ActiveId);
        Assert.Equal("Synthwave (custom)", edited.Name);
        Assert.Equal(20, library.Active.CornerRadius);
        Assert.Equal(4, ThemePresets.Synthwave.CornerRadius);

        library.Edit(t => t with { BorderWidth = 2 });
        Assert.Equal(20, library.Active.CornerRadius);
        Assert.Equal(2, library.Active.BorderWidth);
        Assert.Equal("Synthwave (custom)", library.Active.Name);
    }

    [Fact]
    public void Saving_names_the_look_and_editing_it_changes_it_in_place()
    {
        var library = new ThemeLibrary("midnight", null, null);
        library.Edit(t => t with { Accent = ThemeColor.FromRgb(0x00C2A8) });
        var saved = library.SaveAs("Teal night");

        Assert.Equal("look-teal-night", saved.Id);
        Assert.Equal(saved.Id, library.ActiveId);
        Assert.Null(library.Custom);

        library.Edit(t => t with { CornerRadius = 4 });
        Assert.Single(library.Saved);
        Assert.Equal("Teal night", library.Active.Name);
        Assert.Equal(4, library.Saved[0].CornerRadius);
    }

    [Fact]
    public void Pasting_a_look_keeps_the_unsaved_custom_look()
    {
        var library = new ThemeLibrary("midnight", null, null);
        library.Edit(t => t with { CornerRadius = 3 });
        library.Add(ThemePresets.Paper);

        Assert.NotNull(library.Custom);
        Assert.Equal(3, library.Custom.CornerRadius);
    }

    [Fact]
    public void Names_and_ids_stay_unique()
    {
        var library = new ThemeLibrary("midnight", null, null);
        var first = library.SaveAs("Mine");
        var second = library.SaveAs("Mine");
        var preset = library.Add(ThemePresets.Paper);

        Assert.Equal("Mine 2", second.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Paper 2", preset.Name);
        Assert.False(ThemePresets.IsPreset(preset.Id));
    }

    [Fact]
    public void Deleting_the_look_in_use_falls_back_to_the_default()
    {
        var library = new ThemeLibrary("midnight", null, null);
        var saved = library.SaveAs("Short lived");
        library.Delete(saved.Id);

        Assert.Empty(library.Saved);
        Assert.Equal("midnight", library.ActiveId);
    }

    [Fact]
    public void Saved_looks_that_clash_with_presets_or_each_other_are_dropped()
    {
        var library = new ThemeLibrary(
            "look-a",
            null,
            [ThemePresets.Midnight, ThemePresets.Paper with { Id = "look-a", Name = "A" }, ThemePresets.Glass with { Id = "look-a", Name = "Again" }]);

        Assert.Single(library.Saved);
        Assert.Equal("A", library.Active.Name);
    }

    [Fact]
    public void Renaming_keeps_names_unique()
    {
        var library = new ThemeLibrary("midnight", null, null);
        var a = library.SaveAs("A");
        library.SaveAs("B");
        library.Rename(a.Id, "B");
        Assert.Equal("B 2", library.Find(a.Id)!.Name);

        // Changing only the capitals is not a clash with itself.
        library.Rename(a.Id, "b 2");
        Assert.Equal("b 2", library.Find(a.Id)!.Name);
    }

    [Fact]
    public void The_custom_look_can_be_thrown_away()
    {
        var library = new ThemeLibrary("paper", null, null);
        library.Edit(look => look with { CornerRadius = 20 });
        library.Delete(ThemeLibrary.CustomId);

        Assert.Null(library.Custom);
        Assert.Equal("midnight", library.ActiveId);
    }
}

public sealed class ArtworkColorsTests
{
    [Fact]
    public void Picks_the_colour_that_dominates_a_cover()
    {
        // Mostly dark grey with a big orange area and a little blue.
        var pixels = Image(32, 32, (x, y) => x < 20 ? (0x22, 0x22, 0x22) : y < 26 ? (0xF0, 0x80, 0x20) : (0x20, 0x60, 0xF0));
        var accent = ArtworkColors.PickAccent(pixels, 32, 32);

        Assert.NotNull(accent);
        var (hue, saturation, _) = accent.Value.ToHsl();
        Assert.InRange(hue, 15, 40);
        Assert.True(saturation >= 0.55);
    }

    [Fact]
    public void A_black_and_white_cover_has_no_accent()
    {
        var pixels = Image(16, 16, (x, y) => (x + y) % 2 == 0 ? (0, 0, 0) : (255, 255, 255));
        Assert.Null(ArtworkColors.PickAccent(pixels, 16, 16));
    }

    [Fact]
    public void Blurring_smooths_a_hard_edge_and_keeps_flat_areas()
    {
        var pixels = Image(16, 4, (x, _) => x < 8 ? (0, 0, 0) : (255, 255, 255));
        ArtworkColors.Blur(pixels, 16, 4, radius: 2);

        int Red(int x) => pixels[(x * 4) + 2];
        Assert.Equal(0, Red(0));
        Assert.Equal(255, Red(15));
        Assert.InRange(Red(7), 60, 200);
        Assert.True(Red(6) < Red(7) && Red(7) < Red(8) && Red(8) < Red(9));
    }

    [Fact]
    public void A_missing_cover_becomes_its_tile_gradient()
    {
        var from = ThemeColor.FromRgb(0xFF0000);
        var to = ThemeColor.FromRgb(0x0000FF);
        var pixels = ArtworkColors.Gradient(from, to, 8);

        Assert.Equal(8 * 8 * 4, pixels.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, pixels[..4]);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, pixels[^4..]);
        Assert.NotNull(ArtworkColors.PickAccent(pixels, 8, 8));
    }

    [Fact]
    public void Averages_the_cover()
    {
        var pixels = Image(2, 1, (x, _) => x == 0 ? (0, 0, 0) : (200, 100, 50));
        Assert.Equal(new ThemeColor(0xFF, 100, 50, 25), ArtworkColors.Average(pixels, 2, 1));
    }

    [Fact]
    public void A_short_buffer_is_refused() =>
        Assert.Throws<ArgumentException>(() => ArtworkColors.Average(new byte[8], 4, 4));

    private static byte[] Image(int width, int height, Func<int, int, (int R, int G, int B)> pixel)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = ((y * width) + x) * 4;
                bytes[i] = (byte)b;
                bytes[i + 1] = (byte)g;
                bytes[i + 2] = (byte)r;
                bytes[i + 3] = 0xFF;
            }
        }

        return bytes;
    }
}
