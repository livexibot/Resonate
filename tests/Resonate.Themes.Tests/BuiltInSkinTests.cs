using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

public sealed class BuiltInSkinTests
{
    private static readonly Skin Built = BuiltInSkin.Create();

    /// <summary>webamp's FONT_LOOKUP: where each character sits in text.bmp, as [row, column].</summary>
    private static readonly (char Character, int Row, int Column)[] FontLookup =
    [
        ('a', 0, 0), ('b', 0, 1), ('c', 0, 2), ('d', 0, 3), ('e', 0, 4), ('f', 0, 5), ('g', 0, 6),
        ('h', 0, 7), ('i', 0, 8), ('j', 0, 9), ('k', 0, 10), ('l', 0, 11), ('m', 0, 12), ('n', 0, 13),
        ('o', 0, 14), ('p', 0, 15), ('q', 0, 16), ('r', 0, 17), ('s', 0, 18), ('t', 0, 19), ('u', 0, 20),
        ('v', 0, 21), ('w', 0, 22), ('x', 0, 23), ('y', 0, 24), ('z', 0, 25),
        ('"', 0, 26), ('@', 0, 27), (' ', 0, 30),
        ('0', 1, 0), ('1', 1, 1), ('2', 1, 2), ('3', 1, 3), ('4', 1, 4),
        ('5', 1, 5), ('6', 1, 6), ('7', 1, 7), ('8', 1, 8), ('9', 1, 9),
        ('…', 1, 10),
        ('.', 1, 11), (':', 1, 12), ('(', 1, 13), (')', 1, 14), ('-', 1, 15),
        ('\'', 1, 16), ('!', 1, 17), ('_', 1, 18), ('+', 1, 19), ('\\', 1, 20),
        ('/', 1, 21), ('[', 1, 22), (']', 1, 23), ('^', 1, 24), ('&', 1, 25),
        ('%', 1, 26), (',', 1, 27), ('=', 1, 28), ('$', 1, 29), ('#', 1, 30),
        ('Å', 2, 0), ('Ö', 2, 1), ('Ä', 2, 2), ('?', 2, 3), ('*', 2, 4),
        ('<', 1, 22), ('>', 1, 23), ('{', 1, 22), ('}', 1, 23),
    ];

    /// <summary>Every sprite the main window uses (skin-format section 1), as sheet and rectangle.</summary>
    public static TheoryData<SkinSheet, int, int, int, int> Sprites()
    {
        var data = new TheoryData<SkinSheet, int, int, int, int>
        {
            { SkinSheet.Main, 0, 0, 275, 116 },

            // Title bars, shade bars and their playful variants.
            { SkinSheet.TitleBar, 27, 0, 275, 14 },
            { SkinSheet.TitleBar, 27, 15, 275, 14 },
            { SkinSheet.TitleBar, 27, 57, 275, 14 },
            { SkinSheet.TitleBar, 27, 72, 275, 14 },
            { SkinSheet.TitleBar, 27, 29, 275, 14 },
            { SkinSheet.TitleBar, 27, 42, 275, 14 },

            // Title buttons, normal and pressed.
            { SkinSheet.TitleBar, 0, 0, 9, 9 },
            { SkinSheet.TitleBar, 0, 9, 9, 9 },
            { SkinSheet.TitleBar, 9, 0, 9, 9 },
            { SkinSheet.TitleBar, 9, 9, 9, 9 },
            { SkinSheet.TitleBar, 0, 18, 9, 9 },
            { SkinSheet.TitleBar, 9, 18, 9, 9 },
            { SkinSheet.TitleBar, 18, 0, 9, 9 },
            { SkinSheet.TitleBar, 18, 9, 9, 9 },
            { SkinSheet.TitleBar, 0, 27, 9, 9 },
            { SkinSheet.TitleBar, 9, 27, 9, 9 },

            // Clutter bar, switched off, and each letter lit.
            { SkinSheet.TitleBar, 304, 0, 8, 43 },
            { SkinSheet.TitleBar, 312, 0, 8, 43 },
            { SkinSheet.TitleBar, 304, 47, 8, 8 },
            { SkinSheet.TitleBar, 312, 55, 8, 7 },
            { SkinSheet.TitleBar, 320, 62, 8, 7 },
            { SkinSheet.TitleBar, 328, 69, 8, 8 },
            { SkinSheet.TitleBar, 336, 77, 8, 7 },

            // Shade mode's seek track and its three thumbs.
            { SkinSheet.TitleBar, 0, 36, 17, 7 },
            { SkinSheet.TitleBar, 17, 36, 3, 7 },
            { SkinSheet.TitleBar, 20, 36, 3, 7 },
            { SkinSheet.TitleBar, 23, 36, 3, 7 },

            // Transport, normal and pressed.
            { SkinSheet.CButtons, 0, 0, 23, 18 },
            { SkinSheet.CButtons, 0, 18, 23, 18 },
            { SkinSheet.CButtons, 23, 0, 23, 18 },
            { SkinSheet.CButtons, 23, 18, 23, 18 },
            { SkinSheet.CButtons, 46, 0, 23, 18 },
            { SkinSheet.CButtons, 46, 18, 23, 18 },
            { SkinSheet.CButtons, 69, 0, 23, 18 },
            { SkinSheet.CButtons, 69, 18, 23, 18 },
            { SkinSheet.CButtons, 92, 0, 22, 18 },
            { SkinSheet.CButtons, 92, 18, 22, 18 },
            { SkinSheet.CButtons, 114, 0, 22, 16 },
            { SkinSheet.CButtons, 114, 16, 22, 16 },

            // Lamps.
            { SkinSheet.PlayPaus, 0, 0, 9, 9 },
            { SkinSheet.PlayPaus, 9, 0, 9, 9 },
            { SkinSheet.PlayPaus, 18, 0, 9, 9 },
            { SkinSheet.PlayPaus, 36, 0, 3, 9 },
            { SkinSheet.PlayPaus, 39, 0, 3, 9 },
            { SkinSheet.MonoSter, 0, 0, 29, 12 },
            { SkinSheet.MonoSter, 0, 12, 29, 12 },
            { SkinSheet.MonoSter, 29, 0, 27, 12 },
            { SkinSheet.MonoSter, 29, 12, 27, 12 },

            // Seek bar.
            { SkinSheet.PosBar, 0, 0, 248, 10 },
            { SkinSheet.PosBar, 248, 0, 29, 10 },
            { SkinSheet.PosBar, 278, 0, 29, 10 },

            // Fader caps.
            { SkinSheet.Volume, 15, 422, 14, 11 },
            { SkinSheet.Volume, 0, 422, 14, 11 },
            { SkinSheet.Balance, 15, 422, 14, 11 },
            { SkinSheet.Balance, 0, 422, 14, 11 },
        };

        // Shuffle and repeat in their four states.
        for (var state = 0; state < 4; state++)
        {
            data.Add(SkinSheet.ShufRep, 28, state * 15, 47, 15);
            data.Add(SkinSheet.ShufRep, 0, state * 15, 28, 15);
        }

        // EQ and PL: off, off pressed, on, on pressed.
        foreach (var (x, y) in new[] { (0, 61), (46, 61), (0, 73), (46, 73), (23, 61), (69, 61), (23, 73), (69, 73) })
        {
            data.Add(SkinSheet.ShufRep, x, y, 23, 12);
        }

        // Digits, the blank cell, and nums_ex's minus.
        for (var cell = 0; cell < 11; cell++)
        {
            data.Add(SkinSheet.Numbers, cell * 9, 0, 9, 13);
            data.Add(SkinSheet.NumsEx, cell * 9, 0, 9, 13);
        }

        data.Add(SkinSheet.NumsEx, 99, 0, 9, 13);

        for (var frame = 0; frame < 28; frame++)
        {
            data.Add(SkinSheet.Volume, 0, frame * 15, 68, 13);
            data.Add(SkinSheet.Balance, 9, frame * 15, 38, 13);
        }

        return data;
    }

    [Fact]
    public void Is_the_built_in_skin()
    {
        Assert.True(Built.IsBuiltIn);
        Assert.Equal("Resonate Classic", Built.Name);
        Assert.True(Skin.BuiltIn.IsBuiltIn);
    }

    [Fact]
    public void Every_sheet_is_drawn_at_its_classic_size()
    {
        foreach (var sheet in SkinSheets.All)
        {
            var image = Built.OwnSheet(sheet);
            Assert.NotNull(image);
            Assert.Equal(SkinSheets.ExpectedSize(sheet), (image.Width, image.Height));
        }
    }

    [Fact]
    public void Every_pixel_is_opaque()
    {
        foreach (var sheet in SkinSheets.All)
        {
            Assert.All(Built.Sheet(sheet).Pixels, pixel => Assert.Equal(0xFFu, pixel >> 24));
        }
    }

    [Theory]
    [MemberData(nameof(Sprites))]
    public void Every_sprite_has_art(SkinSheet sheet, int x, int y, int width, int height)
    {
        var image = Built.Sheet(sheet);
        Assert.True(x + width <= image.Width && y + height <= image.Height, "The sprite lies inside its sheet.");
        Assert.True(Colours(image, x, y, width, height) > 1, $"{SkinSheets.FileName(sheet)} at ({x}, {y}) is a flat fill.");
    }

    [Fact]
    public void Every_character_of_the_font_has_a_glyph()
    {
        var text = Built.Sheet(SkinSheet.Text);
        foreach (var (character, row, column) in FontLookup)
        {
            if (character == ' ')
            {
                continue;
            }

            Assert.False(
                Same(text, (column * 5, row * 6), text, (30 * 5, 0), 5, 6),
                $"'{character}' looks like a space.");
        }
    }

    [Fact]
    public void The_space_is_the_text_background_and_the_glyphs_stand_out_from_it()
    {
        var text = Built.Sheet(SkinSheet.Text);
        Assert.Equal(1, Colours(text, 150, 0, 5, 6));

        // Players that fall back to a system font take its colours from here (spec 2.2).
        var background = text[152, 3];
        var brightest = text.Pixels.MaxBy(pixel => Distance(pixel, background));
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.FromRgb(background), ThemeColor.FromRgb(brightest)) >= 7);
    }

    [Fact]
    public void The_digits_have_a_minus_and_numbers_matches_nums_ex()
    {
        var numbers = Built.Sheet(SkinSheet.Numbers);
        var numbersEx = Built.Sheet(SkinSheet.NumsEx);
        Assert.True(Same(numbers, (0, 0), numbersEx, (0, 0), 99, 13));
        Assert.False(Same(numbersEx, (99, 0), numbersEx, (90, 0), 9, 13));
        for (var a = 0; a < 11; a++)
        {
            for (var b = a + 1; b < 12; b++)
            {
                Assert.False(Same(numbersEx, (a * 9, 0), numbersEx, (b * 9, 0), 9, 13), $"Cells {a} and {b} look alike.");
            }
        }
    }

    [Fact]
    public void Pressed_lit_and_active_states_differ()
    {
        var title = Built.Sheet(SkinSheet.TitleBar);
        AssertDiffer(title, (27, 0), (27, 15), 275, 14);
        AssertDiffer(title, (27, 29), (27, 42), 275, 14);
        AssertDiffer(title, (27, 57), (27, 72), 275, 14);
        AssertDiffer(title, (27, 0), (27, 57), 275, 14);
        AssertDiffer(title, (0, 0), (0, 9), 9, 9);
        AssertDiffer(title, (9, 0), (9, 9), 9, 9);
        AssertDiffer(title, (18, 0), (18, 9), 9, 9);
        AssertDiffer(title, (0, 18), (9, 18), 9, 9);
        AssertDiffer(title, (0, 27), (9, 27), 9, 9);
        AssertDiffer(title, (0, 18), (0, 27), 9, 9);
        AssertDiffer(title, (17, 36), (20, 36), 3, 7);
        AssertDiffer(title, (20, 36), (23, 36), 3, 7);
        AssertDiffer(title, (304, 0), (312, 0), 8, 43);
        ReadOnlySpan<int> tops = [3, 11, 18, 25, 33];
        ReadOnlySpan<int> heights = [8, 7, 7, 8, 7];
        for (var i = 0; i < tops.Length; i++)
        {
            AssertDiffer(title, (304, tops[i]), (304 + (8 * i), 44 + tops[i]), 8, heights[i]);
        }

        var buttons = Built.Sheet(SkinSheet.CButtons);
        foreach (var (x, width) in new[] { (0, 23), (23, 23), (46, 23), (69, 23), (92, 22) })
        {
            AssertDiffer(buttons, (x, 0), (x, 18), width, 18);
        }

        AssertDiffer(buttons, (114, 0), (114, 16), 22, 16);

        var lamps = Built.Sheet(SkinSheet.PlayPaus);
        AssertDiffer(lamps, (0, 0), (9, 0), 9, 9);
        AssertDiffer(lamps, (9, 0), (18, 0), 9, 9);
        AssertDiffer(lamps, (0, 0), (18, 0), 9, 9);
        AssertDiffer(lamps, (36, 0), (39, 0), 3, 9);

        var channels = Built.Sheet(SkinSheet.MonoSter);
        AssertDiffer(channels, (0, 0), (0, 12), 29, 12);
        AssertDiffer(channels, (29, 0), (29, 12), 27, 12);

        AssertDiffer(Built.Sheet(SkinSheet.PosBar), (248, 0), (278, 0), 29, 10);
        AssertDiffer(Built.Sheet(SkinSheet.Volume), (0, 422), (15, 422), 14, 11);
        AssertDiffer(Built.Sheet(SkinSheet.Balance), (0, 422), (15, 422), 14, 11);

        var toggles = Built.Sheet(SkinSheet.ShufRep);
        foreach (var (x, width) in new[] { (28, 47), (0, 28) })
        {
            for (var a = 0; a < 4; a++)
            {
                for (var b = a + 1; b < 4; b++)
                {
                    AssertDiffer(toggles, (x, a * 15), (x, b * 15), width, 15);
                }
            }
        }

        foreach (var states in new[] { new[] { (0, 61), (46, 61), (0, 73), (46, 73) }, new[] { (23, 61), (69, 61), (23, 73), (69, 73) } })
        {
            for (var a = 0; a < 4; a++)
            {
                for (var b = a + 1; b < 4; b++)
                {
                    AssertDiffer(toggles, states[a], states[b], 23, 12);
                }
            }
        }
    }

    [Fact]
    public void Every_slider_frame_differs_and_volume_grows_brighter()
    {
        var volume = Built.Sheet(SkinSheet.Volume);
        var balance = Built.Sheet(SkinSheet.Balance);
        for (var a = 0; a < 28; a++)
        {
            for (var b = a + 1; b < 28; b++)
            {
                AssertDiffer(volume, (0, a * 15), (0, b * 15), 68, 13);
                AssertDiffer(balance, (9, a * 15), (9, b * 15), 38, 13);
            }
        }

        for (var frame = 1; frame < 28; frame++)
        {
            Assert.True(Brightness(volume, 0, frame * 15, 68, 13) > Brightness(volume, 0, (frame - 1) * 15, 68, 13));
        }
    }

    [Fact]
    public void Bars_show_their_buttons_as_the_sprites_draw_them()
    {
        // Players draw only the pressed sprites, so the bars carry the resting buttons themselves.
        var title = Built.Sheet(SkinSheet.TitleBar);
        foreach (var bar in new[] { 0, 29 })
        {
            Assert.True(Same(title, (27 + 6, bar + 3), title, (0, 0), 9, 9));
            Assert.True(Same(title, (27 + 244, bar + 3), title, (9, 0), 9, 9));
            Assert.True(Same(title, (27 + 264, bar + 3), title, (18, 0), 9, 9));
        }

        Assert.True(Same(title, (27 + 254, 3), title, (0, 18), 9, 9));
        Assert.True(Same(title, (27 + 254, 29 + 3), title, (0, 27), 9, 9));
        Assert.True(Same(title, (27 + 226, 29 + 4), title, (0, 36), 17, 7));
    }

    [Fact]
    public void Main_shows_the_displays_empty_where_players_leave_them()
    {
        var main = Built.Sheet(SkinSheet.Main);
        var text = Built.Sheet(SkinSheet.Text);
        var numbers = Built.Sheet(SkinSheet.NumsEx);
        var space = text[152, 3];

        // A stopped or blinking clock shows the blank digit cell's dim dots.
        foreach (var x in new[] { 36, 48, 60, 78, 90 })
        {
            Assert.True(Same(main, (x, 26), numbers, (90, 0), 9, 13));
        }

        // The song line and the rates sit on the text's own background.
        Assert.Equal(1, Colours(main, 111, 27, 154, 6));
        Assert.Equal(space, main[111, 27]);
        Assert.Equal(1, Colours(main, 111, 43, 15, 6));
        Assert.Equal(1, Colours(main, 156, 43, 10, 6));
        Assert.Equal(space, main[156, 43]);

        // The visualiser at rest: colour 0 with colour 1 on every other pixel of every other row (spec 7.1).
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 76; x++)
            {
                var dot = y % 2 == 1 && x % 2 == 0 && x < 75;
                Assert.Equal(Built.VisColors[dot ? 1 : 0], main[24 + x, 43 + y]);
            }
        }
    }

    [Fact]
    public void The_shade_bar_has_room_for_the_mini_visualiser_and_clock()
    {
        var title = Built.Sheet(SkinSheet.TitleBar);
        var space = Built.Sheet(SkinSheet.Text)[152, 3];
        foreach (var bar in new[] { 29, 42 })
        {
            Assert.Equal(1, Colours(title, 27 + 79, bar + 5, 38, 5));
            Assert.Equal(Built.VisColors[0], title[27 + 79, bar + 5]);
            foreach (var x in new[] { 128, 134, 139, 147, 152 })
            {
                Assert.Equal(1, Colours(title, 27 + x, bar + 4, 5, 6));
                Assert.Equal(space, title[27 + x, bar + 4]);
            }
        }
    }

    [Fact]
    public void The_visualiser_has_24_distinct_colours()
    {
        var colours = Built.VisColors;
        Assert.Equal(Skin.VisColorCount, colours.Count);
        Assert.All(colours, colour => Assert.Equal(0xFFu, colour >> 24));
        for (var a = 0; a < colours.Count; a++)
        {
            for (var b = a + 1; b < colours.Count; b++)
            {
                Assert.True(Distance(colours[a], colours[b]) >= 12, $"Colours {a} and {b} are too alike.");
            }
        }

        // The bars run hot at the top (magenta, more red than the bottom's deep cyan).
        Assert.True(Red(colours[2]) > Red(colours[17]) + 100);
        Assert.True(Blue(colours[17]) > Red(colours[17]));
    }

    [Fact]
    public void The_playlist_colours_are_readable()
    {
        var playlist = Built.Playlist;
        Assert.Equal("Segoe UI", playlist.Font);
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.FromRgb(playlist.Normal), ThemeColor.FromRgb(playlist.NormalBackground)) >= 4.5);
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.FromRgb(playlist.Current), ThemeColor.FromRgb(playlist.NormalBackground)) >= 4.5);
        Assert.True(ThemeColor.ContrastRatio(ThemeColor.FromRgb(playlist.Normal), ThemeColor.FromRgb(playlist.SelectedBackground)) >= 4.5);
    }

    [Fact]
    public void Nothing_is_green()
    {
        // Resonate must never look like Spotify: no pixel leans green.
        foreach (var sheet in SkinSheets.All)
        {
            Assert.DoesNotContain(Built.Sheet(sheet).Pixels, IsGreen);
        }

        Assert.DoesNotContain(Built.VisColors, IsGreen);
    }

    [Fact]
    public void Drawing_it_twice_gives_the_same_pixels()
    {
        var again = BuiltInSkin.Create();
        foreach (var sheet in SkinSheets.All)
        {
            Assert.Equal(Built.Sheet(sheet).Pixels, again.Sheet(sheet).Pixels);
        }

        Assert.Equal(Built.VisColors, again.VisColors);
        Assert.Equal(Built.Playlist, again.Playlist);
    }

    private static bool IsGreen(uint colour) => Green(colour) > Red(colour) + 32 && Green(colour) > Blue(colour) + 32;

    private static int Red(uint colour) => (int)((colour >> 16) & 0xFF);

    private static int Green(uint colour) => (int)((colour >> 8) & 0xFF);

    private static int Blue(uint colour) => (int)(colour & 0xFF);

    private static int Distance(uint a, uint b) =>
        Math.Abs(Red(a) - Red(b)) + Math.Abs(Green(a) - Green(b)) + Math.Abs(Blue(a) - Blue(b));

    private static int Colours(SkinImage image, int x, int y, int width, int height)
    {
        var seen = new HashSet<uint>();
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                seen.Add(image[column, row]);
            }
        }

        return seen.Count;
    }

    private static long Brightness(SkinImage image, int x, int y, int width, int height)
    {
        long sum = 0;
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                var pixel = image[column, row];
                sum += Red(pixel) + Green(pixel) + Blue(pixel);
            }
        }

        return sum;
    }

    private static bool Same(SkinImage a, (int X, int Y) at, SkinImage b, (int X, int Y) other, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (a[at.X + column, at.Y + row] != b[other.X + column, other.Y + row])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void AssertDiffer(SkinImage image, (int X, int Y) a, (int X, int Y) b, int width, int height) =>
        Assert.False(Same(image, a, image, b, width, height), $"({a.X}, {a.Y}) and ({b.X}, {b.Y}) look the same.");
}
