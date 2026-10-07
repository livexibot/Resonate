using System.Diagnostics;
using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

/// <summary>
/// A skin whose every pixel is different: its colour says which sheet and
/// which pixel of it it is, so a test can tell exactly what was drawn where.
/// The visualiser colours are just as recognisable.
/// </summary>
internal static class CodedSkin
{
    public static Skin Create(params SkinSheet[] without) =>
        Create(new Dictionary<SkinSheet, (int Width, int Height)>(), without);

    /// <summary>Every sheet at its usual size unless <paramref name="sizes"/> says otherwise, apart from those <paramref name="without"/>.</summary>
    public static Skin Create(IReadOnlyDictionary<SkinSheet, (int Width, int Height)> sizes, params SkinSheet[] without)
    {
        var sheets = new Dictionary<SkinSheet, SkinImage>();
        foreach (var sheet in SkinSheets.All)
        {
            if (Array.IndexOf(without, sheet) >= 0)
            {
                continue;
            }

            var (width, height) = sizes.TryGetValue(sheet, out var size) ? size : SkinSheets.ExpectedSize(sheet);
            var image = new SkinImage(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image[x, y] = Code(sheet, x, y);
                }
            }

            sheets[sheet] = image;
        }

        var colours = Enumerable.Range(0, Skin.VisColorCount).Select(VisColour).ToArray();
        return new Skin("Coded", sheets, colours, new PlaylistColors(0xFF00FF00, 0xFFFFFFFF, 0xFF000000, 0xFF0000FF, null));
    }

    /// <summary>The colour of pixel (x, y) of a sheet: the sheet in the top bits, then x, then y.</summary>
    public static uint Code(SkinSheet sheet, int x, int y) =>
        0xFF000000u | ((uint)(sheet + 1) << 18) | ((uint)x << 9) | (uint)y;

    public static uint VisColour(int index) => 0xFFF00000u | (uint)index;

    public static string Describe(uint pixel)
    {
        var tag = (int)((pixel >> 18) & 0x3F);
        return pixel switch
        {
            0xFF000000 => "black",
            _ when (pixel & 0x00FFFF00) == 0x00F00000 => $"viscolor {pixel & 0xFF}",
            _ when tag is >= 1 and <= 12 => $"{(SkinSheet)(tag - 1)} ({(pixel >> 9) & 0x1FF}, {pixel & 0x1FF})",
            _ => $"0x{pixel:X8}",
        };
    }

    /// <summary>Checks that a block of the target is the given block of a sheet.</summary>
    public static void AssertBlock(SkinImage target, SkinSheet sheet, int sourceX, int sourceY, int width, int height, int x, int y)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                AssertPixel(target, x + column, y + row, Code(sheet, sourceX + column, sourceY + row));
            }
        }
    }

    /// <summary>Checks that a block of the target shows main.bmp, as if nothing were drawn there.</summary>
    public static void AssertMain(SkinImage target, int x, int y, int width = 1, int height = 1) =>
        AssertBlock(target, SkinSheet.Main, x, y, width, height, x, y);

    public static void AssertPixel(SkinImage target, int x, int y, uint expected)
    {
        var actual = target[x, y];
        if (actual != expected)
        {
            Assert.Fail($"({x}, {y}) shows {Describe(actual)}, not {Describe(expected)}.");
        }
    }
}

public sealed class ClassicRendererTests
{
    private static readonly Skin Coded = CodedSkin.Create();

    private static readonly ClassicView Playing = new()
    {
        State = ClassicPlayState.Playing,
        Elapsed = TimeSpan.FromSeconds(187),
        Duration = TimeSpan.FromMinutes(4),
        Seek = 187.0 / 240,
        Stereo = true,
        Kbps = "320",
        Khz = "44",
    };

    private static SkinImage Render(ClassicView view, VisualiserFrame? frame = null, Skin? skin = null)
    {
        var target = new SkinImage(ClassicRenderer.Width, view.Shaded ? ClassicRenderer.ShadeHeight : ClassicRenderer.Height);
        ClassicRenderer.Render(skin ?? Coded, view, target, frame);
        return target;
    }

    private static VisualiserFrame Frame(int bars = 19)
    {
        var frame = new VisualiserFrame { BarCount = bars, IsAtRest = false };
        return frame;
    }

    [Fact]
    public void Every_sprite_lies_inside_a_sheet_of_the_usual_size()
    {
        // 153 named sprites and cells, and the font's 3 rows of 31 glyphs.
        Assert.Equal(153 + 93, SkinSprites.All.Count);
        foreach (var sprite in SkinSprites.All)
        {
            var (width, height) = SkinSheets.ExpectedSize(sprite.Sheet);
            Assert.True(sprite.X >= 0 && sprite.Y >= 0 && sprite.Width > 0 && sprite.Height > 0, $"{sprite} is empty.");
            Assert.True(sprite.X + sprite.Width <= width && sprite.Y + sprite.Height <= height, $"{sprite} runs off its {width} x {height} sheet.");
        }
    }

    [Fact]
    public void A_stopped_window_shows_the_skin_with_its_controls_at_rest()
    {
        var image = Render(new ClassicView());

        // main.bmp wherever nothing else goes, the active title bar, the clutter bar.
        CodedSkin.AssertMain(image, 2, 20, 5, 5);
        CodedSkin.AssertMain(image, 268, 108, 7, 8);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 27, 0, 275, 14, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 304, 0, 8, 43, 10, 22);

        // Stopped: the stopped lamp, no work indicator, time, visualiser, kbps or kHz.
        CodedSkin.AssertBlock(image, SkinSheet.PlayPaus, 18, 0, 9, 9, 26, 28);
        CodedSkin.AssertMain(image, 24, 28, 2, 9);
        CodedSkin.AssertMain(image, 36, 26, 63, 13);
        CodedSkin.AssertMain(image, 24, 43, 76, 16);
        CodedSkin.AssertMain(image, 111, 43, 15, 6);
        CodedSkin.AssertMain(image, 156, 43, 10, 6);

        // Both channel lamps unlit; transport, toggles and the seek track as they are.
        CodedSkin.AssertBlock(image, SkinSheet.MonoSter, 29, 12, 27, 12, 212, 41);
        CodedSkin.AssertBlock(image, SkinSheet.MonoSter, 0, 12, 29, 12, 239, 41);
        CodedSkin.AssertBlock(image, SkinSheet.ShufRep, 0, 61, 23, 12, 219, 58);
        CodedSkin.AssertBlock(image, SkinSheet.ShufRep, 23, 61, 23, 12, 242, 58);
        CodedSkin.AssertBlock(image, SkinSheet.PosBar, 0, 0, 248, 10, 16, 72);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 0, 0, 23, 18, 16, 88);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 23, 0, 23, 18, 39, 88);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 46, 0, 23, 18, 62, 88);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 69, 0, 23, 18, 85, 88);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 92, 0, 22, 18, 108, 88);
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, 114, 0, 22, 16, 136, 89);

        // The gap between next and eject, and the strip below the buttons, are main.bmp.
        CodedSkin.AssertMain(image, 130, 88, 6, 18);
        CodedSkin.AssertMain(image, 16, 106, 120, 10);
    }

    [Fact]
    public void An_inactive_window_has_the_inactive_title_bar()
    {
        var image = Render(new ClassicView { WindowActive = false });
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 27, 15, 275, 14, 0, 0);
    }

    [Theory]
    [InlineData(ClassicControl.Options, 0, 9, 6)]
    [InlineData(ClassicControl.Minimize, 9, 9, 244)]
    [InlineData(ClassicControl.Shade, 9, 18, 254)]
    [InlineData(ClassicControl.Close, 18, 9, 264)]
    public void A_pressed_title_button_is_drawn_pressed(ClassicControl button, int sourceX, int sourceY, int x)
    {
        var image = Render(new ClassicView { Pressed = button });
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, sourceX, sourceY, 9, 9, x, 3);

        // Unpressed buttons are the title bar's own art.
        var rest = Render(new ClassicView());
        CodedSkin.AssertBlock(rest, SkinSheet.TitleBar, 27 + x, 3, 9, 9, x, 3);
    }

    [Theory]
    [InlineData(ClassicControl.ClutterOptions, 304, 47, 25, 8)]
    [InlineData(ClassicControl.ClutterAlwaysOnTop, 312, 55, 33, 7)]
    [InlineData(ClassicControl.ClutterInfo, 320, 62, 40, 7)]
    [InlineData(ClassicControl.ClutterDoubleSize, 328, 69, 47, 8)]
    [InlineData(ClassicControl.ClutterVisualiser, 336, 77, 55, 7)]
    public void A_held_clutter_letter_lights(ClassicControl letter, int sourceX, int sourceY, int y, int height)
    {
        var image = Render(new ClassicView { Pressed = letter });
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, sourceX, sourceY, 8, height, 10, y);
    }

    [Fact]
    public void D_stays_lit_while_the_window_is_doubled()
    {
        var image = Render(new ClassicView { DoubleSize = true });
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 328, 69, 8, 8, 10, 47);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 304, 3, 8, 8, 10, 25);
    }

    [Fact]
    public void Playing_lights_the_play_lamp_and_the_work_indicator()
    {
        var image = Render(Playing);
        CodedSkin.AssertBlock(image, SkinSheet.PlayPaus, 1, 0, 8, 9, 27, 28);

        // The indicator's third column covers the lamp's first.
        CodedSkin.AssertBlock(image, SkinSheet.PlayPaus, 36, 0, 3, 9, 24, 28);
    }

    [Fact]
    public void Paused_shows_the_pause_lamp_and_no_indicator()
    {
        var image = Render(Playing with { State = ClassicPlayState.Paused });
        CodedSkin.AssertBlock(image, SkinSheet.PlayPaus, 9, 0, 9, 9, 26, 28);
        CodedSkin.AssertMain(image, 24, 28, 2, 9);
    }

    [Fact]
    public void Working_shows_the_busy_indicator_and_hides_the_lamp()
    {
        var image = Render(Playing with { Working = true });
        CodedSkin.AssertBlock(image, SkinSheet.PlayPaus, 39, 0, 3, 9, 24, 28);
        CodedSkin.AssertMain(image, 27, 28, 8, 9);
    }

    [Fact]
    public void The_time_shows_minutes_and_seconds_with_a_blank_sign()
    {
        var image = Render(Playing);
        AssertTime(image, minus: false, 0, 3, 0, 7);
    }

    [Fact]
    public void Remaining_time_counts_down_with_a_minus()
    {
        var image = Render(Playing with { ShowRemaining = true, Elapsed = TimeSpan.FromSeconds(60.4) });

        // 240 - 60.4 = 179.6 seconds, shown as 2:59.
        AssertTime(image, minus: true, 0, 2, 5, 9);
    }

    [Fact]
    public void Remaining_time_needs_a_length()
    {
        var image = Render(Playing with { ShowRemaining = true, Duration = TimeSpan.Zero });
        AssertTime(image, minus: false, 0, 3, 0, 7);
    }

    [Fact]
    public void From_100_minutes_the_time_shows_hours_and_minutes()
    {
        var image = Render(Playing with { Elapsed = new TimeSpan(1, 45, 30), Duration = TimeSpan.FromHours(3) });
        AssertTime(image, minus: false, 0, 1, 4, 5);

        var under = Render(Playing with { Elapsed = TimeSpan.FromSeconds(5999), Duration = TimeSpan.FromHours(3) });
        AssertTime(under, minus: false, 9, 9, 5, 9);
    }

    [Fact]
    public void A_paused_time_blinks()
    {
        var on = Render(Playing with { State = ClassicPlayState.Paused, BlinkOn = true });
        AssertTime(on, minus: false, 0, 3, 0, 7);

        var off = Render(Playing with { State = ClassicPlayState.Paused, BlinkOn = false });
        CodedSkin.AssertMain(off, 36, 26, 63, 13);

        // Only a paused time blinks.
        var playing = Render(Playing with { BlinkOn = false });
        AssertTime(playing, minus: false, 0, 3, 0, 7);
    }

    [Fact]
    public void A_skin_with_only_numbers_bmp_gets_its_minus_from_the_two()
    {
        var skin = CodedSkin.Create(SkinSheet.NumsEx);
        var image = Render(Playing with { ShowRemaining = true }, skin: skin);

        // The blank cell, with the middle bar of the 2 (20, 6, 5, 1) laid on at (38, 32).
        for (var y = 0; y < 13; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                var minus = y == 6 && x is >= 2 and < 7;
                var expected = minus ? CodedSkin.Code(SkinSheet.Numbers, 20 + x - 2, 6) : CodedSkin.Code(SkinSheet.Numbers, 90 + x, y);
                CodedSkin.AssertPixel(image, 36 + x, 26 + y, expected);
            }
        }

        // 53 seconds left: 00:53.
        CodedSkin.AssertBlock(image, SkinSheet.Numbers, 0, 0, 9, 13, 48, 26);
        CodedSkin.AssertBlock(image, SkinSheet.Numbers, 0, 0, 9, 13, 60, 26);
        CodedSkin.AssertBlock(image, SkinSheet.Numbers, 45, 0, 9, 13, 78, 26);
        CodedSkin.AssertBlock(image, SkinSheet.Numbers, 27, 0, 9, 13, 90, 26);

        var elapsed = Render(Playing, skin: skin);
        CodedSkin.AssertBlock(elapsed, SkinSheet.Numbers, 90, 0, 9, 13, 36, 26);
    }

    [Fact]
    public void The_marquee_writes_in_the_skin_font_and_fills_its_box()
    {
        var image = Render(new ClassicView { Marquee = "Ab 1" });
        CodedSkin.AssertBlock(image, SkinSheet.Text, 0, 0, 5, 6, 111, 27);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 5, 0, 5, 6, 116, 27);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 150, 0, 5, 6, 121, 27);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 5, 6, 5, 6, 126, 27);

        // Padded with spaces to the 31st cell, which is cut to 4 pixels.
        CodedSkin.AssertBlock(image, SkinSheet.Text, 150, 0, 5, 6, 256, 27);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 150, 0, 4, 6, 261, 27);
        CodedSkin.AssertMain(image, 265, 27, 1, 6);
        CodedSkin.AssertMain(image, 111, 26, 154, 1);
        CodedSkin.AssertMain(image, 111, 33, 154, 1);
    }

    [Fact]
    public void A_long_marquee_shows_the_window_its_offset_picks()
    {
        var line = string.Concat(Enumerable.Repeat("0123456789", 4));
        var image = Render(new ClassicView { Marquee = line, MarqueeOffset = 3 });

        // '3' is row 1, column 3; then '4'.
        CodedSkin.AssertBlock(image, SkinSheet.Text, 15, 6, 5, 6, 111, 27);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 20, 6, 5, 6, 116, 27);

        // Offset 38 is "89" then the gap's spaces and stars.
        var wrapped = Render(new ClassicView { Marquee = line, MarqueeOffset = 38 });
        CodedSkin.AssertBlock(wrapped, SkinSheet.Text, 40, 6, 5, 6, 111, 27);
        CodedSkin.AssertBlock(wrapped, SkinSheet.Text, 45, 6, 5, 6, 116, 27);
        CodedSkin.AssertBlock(wrapped, SkinSheet.Text, 150, 0, 5, 6, 121, 27);
        CodedSkin.AssertBlock(wrapped, SkinSheet.Text, 20, 12, 5, 6, 131, 27);
    }

    [Fact]
    public void Kbps_and_khz_show_while_something_plays_as_given()
    {
        var image = Render(Playing with { Kbps = "96" });
        CodedSkin.AssertBlock(image, SkinSheet.Text, 45, 6, 5, 6, 111, 43);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 30, 6, 5, 6, 116, 43);
        CodedSkin.AssertMain(image, 121, 43, 5, 6);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 20, 6, 5, 6, 156, 43);
        CodedSkin.AssertBlock(image, SkinSheet.Text, 20, 6, 5, 6, 161, 43);

        // Longer values are cut at their box.
        var wide = Render(Playing with { Kbps = "1411", Khz = "192" });
        CodedSkin.AssertBlock(wide, SkinSheet.Text, 20, 6, 5, 6, 116, 43);
        CodedSkin.AssertBlock(wide, SkinSheet.Text, 5, 6, 5, 6, 121, 43);
        CodedSkin.AssertMain(wide, 126, 43, 5, 6);
        CodedSkin.AssertMain(wide, 166, 43, 5, 6);
    }

    [Theory]
    [InlineData(true, false, ClassicPlayState.Playing, false, true)]
    [InlineData(false, true, ClassicPlayState.Playing, true, false)]
    [InlineData(true, false, ClassicPlayState.Paused, false, true)]
    [InlineData(true, false, ClassicPlayState.Stopped, false, false)]
    [InlineData(true, true, ClassicPlayState.Playing, false, true)]
    public void The_channel_lamps_follow_the_stream(bool stereo, bool mono, ClassicPlayState state, bool monoLit, bool stereoLit)
    {
        var image = Render(Playing with { Stereo = stereo, Mono = mono, State = state });
        CodedSkin.AssertBlock(image, SkinSheet.MonoSter, 29, monoLit ? 0 : 12, 27, 12, 212, 41);
        CodedSkin.AssertBlock(image, SkinSheet.MonoSter, 0, stereoLit ? 0 : 12, 29, 12, 239, 41);
    }

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(0.5, 14, 26)]
    [InlineData(1.0, 27, 51)]
    public void The_volume_picks_its_frame_and_places_its_thumb(double volume, int frame, int offset)
    {
        var image = Render(new ClassicView { Volume = volume });
        var thumb = 107 + offset;

        // The frame shows everywhere but under the thumb (and in the row above and below it).
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 0, 15 * frame, 68, 1, 107, 57);
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 0, (15 * frame) + 12, 68, 1, 107, 69);
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 0, (15 * frame) + 1, offset, 11, 107, 58);
        CodedSkin.AssertBlock(image, SkinSheet.Volume, offset + 14, (15 * frame) + 1, 68 - offset - 14, 11, thumb + 14, 58);
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 15, 422, 14, 11, thumb, 58);

        var held = Render(new ClassicView { Volume = volume, Pressed = ClassicControl.Volume });
        CodedSkin.AssertBlock(held, SkinSheet.Volume, 0, 422, 14, 11, thumb, 58);
    }

    [Theory]
    [InlineData(-1.0, 27, 0)]
    [InlineData(0.0, 0, 12)]
    [InlineData(0.5, 14, 18)]
    [InlineData(1.0, 27, 24)]
    public void The_balance_picks_its_frame_and_places_its_thumb(double balance, int frame, int offset)
    {
        var image = Render(new ClassicView { Balance = balance });
        CodedSkin.AssertBlock(image, SkinSheet.Balance, 9, 15 * frame, 38, 1, 177, 57);
        CodedSkin.AssertBlock(image, SkinSheet.Balance, 15, 422, 14, 11, 177 + offset, 58);

        var held = Render(new ClassicView { Balance = balance, Pressed = ClassicControl.Balance });
        CodedSkin.AssertBlock(held, SkinSheet.Balance, 0, 422, 14, 11, 177 + offset, 58);
    }

    [Fact]
    public void A_skin_without_balance_bmp_uses_its_volume_bmp()
    {
        var image = Render(new ClassicView { Balance = 1 }, skin: CodedSkin.Create(SkinSheet.Balance));
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 9, 15 * 27, 38, 1, 177, 57);
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 15, 422, 14, 11, 201, 58);
    }

    [Fact]
    public void An_old_volume_sheet_without_thumbs_draws_none()
    {
        var skin = CodedSkin.Create(new Dictionary<SkinSheet, (int, int)> { [SkinSheet.Volume] = (68, 420) });
        var image = Render(new ClassicView { Volume = 0.5 }, skin: skin);

        // Frame 14 everywhere, the thumb's place included.
        CodedSkin.AssertBlock(image, SkinSheet.Volume, 0, 210, 68, 13, 107, 57);
    }

    [Fact]
    public void A_short_sheet_draws_the_part_it_has()
    {
        var skin = CodedSkin.Create(new Dictionary<SkinSheet, (int, int)> { [SkinSheet.PosBar] = (307, 4) });
        var image = Render(Playing, skin: skin);
        CodedSkin.AssertBlock(image, SkinSheet.PosBar, 0, 0, 100, 4, 16, 72);
        CodedSkin.AssertMain(image, 16, 76, 248, 6);
    }

    [Fact]
    public void A_missing_corner_of_main_bmp_is_black()
    {
        var skin = CodedSkin.Create(new Dictionary<SkinSheet, (int, int)> { [SkinSheet.Main] = (200, 100) });
        var image = Render(new ClassicView(), skin: skin);
        CodedSkin.AssertPixel(image, 270, 110, 0xFF000000);
        CodedSkin.AssertPixel(image, 200, 50, 0xFF000000);
        CodedSkin.AssertMain(image, 199, 85);
    }

    [Theory]
    [InlineData(false, false, 0, 61, 23, 61)]
    [InlineData(true, false, 0, 73, 23, 73)]
    [InlineData(false, true, 46, 61, 69, 61)]
    [InlineData(true, true, 46, 73, 69, 73)]
    public void The_window_toggles_show_on_and_pressed(bool on, bool pressed, int eqX, int eqY, int plX, int plY)
    {
        var equalizer = Render(new ClassicView { EqualizerOn = on, Pressed = pressed ? ClassicControl.Equalizer : ClassicControl.None });
        CodedSkin.AssertBlock(equalizer, SkinSheet.ShufRep, eqX, eqY, 23, 12, 219, 58);

        var playlist = Render(new ClassicView { PlaylistOn = on, Pressed = pressed ? ClassicControl.Playlist : ClassicControl.None });
        CodedSkin.AssertBlock(playlist, SkinSheet.ShufRep, plX, plY, 23, 12, 242, 58);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 15)]
    [InlineData(true, false, 30)]
    [InlineData(true, true, 45)]
    public void Shuffle_and_repeat_show_on_and_pressed(bool on, bool pressed, int sourceY)
    {
        var shuffle = Render(new ClassicView { Shuffle = on, Pressed = pressed ? ClassicControl.Shuffle : ClassicControl.None });
        CodedSkin.AssertBlock(shuffle, SkinSheet.ShufRep, 28, sourceY, 46, 15, 164, 89);

        var repeat = Render(new ClassicView { Repeat = on, Pressed = pressed ? ClassicControl.Repeat : ClassicControl.None });
        CodedSkin.AssertBlock(repeat, SkinSheet.ShufRep, 0, sourceY, 28, 15, 210, 89);
    }

    [Fact]
    public void Repeat_covers_the_column_it_shares_with_shuffle()
    {
        var image = Render(new ClassicView { Shuffle = true });
        CodedSkin.AssertBlock(image, SkinSheet.ShufRep, 28 + 45, 30, 1, 15, 209, 89);
        CodedSkin.AssertBlock(image, SkinSheet.ShufRep, 0, 0, 1, 15, 210, 89);
    }

    [Theory]
    [InlineData(ClassicControl.Previous, 0, 23, 18, 16, 88)]
    [InlineData(ClassicControl.Play, 23, 23, 18, 39, 88)]
    [InlineData(ClassicControl.Pause, 46, 23, 18, 62, 88)]
    [InlineData(ClassicControl.Stop, 69, 23, 18, 85, 88)]
    [InlineData(ClassicControl.Next, 92, 22, 18, 108, 88)]
    [InlineData(ClassicControl.Eject, 114, 22, 16, 136, 89)]
    public void A_held_transport_button_is_drawn_pressed(ClassicControl button, int sourceX, int width, int height, int x, int y)
    {
        var image = Render(new ClassicView { Pressed = button });
        CodedSkin.AssertBlock(image, SkinSheet.CButtons, sourceX, height, width, height, x, y);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 109)]
    [InlineData(1.0, 219)]
    [InlineData(1.5, 219)]
    public void The_seek_thumb_follows_the_song(double seek, int offset)
    {
        var image = Render(new ClassicView { Seek = seek });
        CodedSkin.AssertBlock(image, SkinSheet.PosBar, 248, 0, 29, 10, 16 + offset, 72);
        if (offset > 0)
        {
            CodedSkin.AssertBlock(image, SkinSheet.PosBar, 0, 0, offset, 10, 16, 72);
        }

        var held = Render(new ClassicView { Seek = seek, Pressed = ClassicControl.Seek });
        CodedSkin.AssertBlock(held, SkinSheet.PosBar, 278, 0, 29, 10, 16 + offset, 72);
    }

    [Fact]
    public void Without_a_position_the_seek_bar_has_no_thumb()
    {
        var image = Render(Playing with { Seek = null });
        CodedSkin.AssertBlock(image, SkinSheet.PosBar, 0, 0, 248, 10, 16, 72);
    }

    [Fact]
    public void The_visualiser_background_has_winamps_grid()
    {
        var image = Render(Playing);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 76; x++)
            {
                var dot = y % 2 == 1 && x % 2 == 0 && x < 75;
                CodedSkin.AssertPixel(image, 24 + x, 43 + y, CodedSkin.VisColour(dot ? 1 : 0));
            }
        }
    }

    [Fact]
    public void A_frame_at_rest_or_a_spotify_song_leaves_just_the_background()
    {
        var frame = Frame();
        frame.Bars[0] = 15;
        frame.Peaks[0] = 15;
        frame.IsAtRest = true;
        Assert.Equal(Render(Playing).Pixels, Render(Playing, frame).Pixels);
    }

    [Fact]
    public void The_visualiser_is_off_when_stopped_or_switched_off()
    {
        var frame = Frame();
        frame.Bars[0] = 15;
        CodedSkin.AssertMain(Render(Playing with { Visualiser = VisualiserMode.Off }, frame), 24, 43, 76, 16);
        CodedSkin.AssertMain(Render(Playing with { State = ClassicPlayState.Stopped }, frame), 24, 43, 76, 16);

        // Paused keeps the picture it is given.
        var paused = Render(Playing with { State = ClassicPlayState.Paused }, frame);
        CodedSkin.AssertPixel(paused, 24, 43 + 15, CodedSkin.VisColour(17));
    }

    [Fact]
    public void Nineteen_bars_are_three_columns_wide_with_rows_coloured_from_the_top()
    {
        var frame = Frame();
        frame.Bars[0] = 15;
        frame.Bars[1] = 3;
        frame.Peaks[1] = 5;
        frame.Bars[2] = 2.5f;
        frame.Bars[3] = 2.49f;
        frame.Bars[18] = 1;
        var image = Render(Playing, frame);

        // Bar 0: rows 1 to 15 in colours 3 to 17; row 0 stays background; column 3 is the gap.
        for (var x = 0; x < 3; x++)
        {
            CodedSkin.AssertPixel(image, 24 + x, 43, CodedSkin.VisColour(0));
            for (var row = 1; row < 16; row++)
            {
                CodedSkin.AssertPixel(image, 24 + x, 43 + row, CodedSkin.VisColour(2 + row));
            }
        }

        CodedSkin.AssertPixel(image, 24 + 3, 43 + 15, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 24 + 3, 43 + 14, CodedSkin.VisColour(0));

        // Bar 1: three rows, and its cap one row above a bar of height 5 (row 10) in colour 23.
        for (var x = 4; x < 7; x++)
        {
            CodedSkin.AssertPixel(image, 24 + x, 43 + 13, CodedSkin.VisColour(15));
            CodedSkin.AssertPixel(image, 24 + x, 43 + 15, CodedSkin.VisColour(17));
            CodedSkin.AssertPixel(image, 24 + x, 43 + 10, CodedSkin.VisColour(23));
        }

        CodedSkin.AssertPixel(image, 24 + 4, 43 + 12, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 24 + 4, 43 + 11, CodedSkin.VisColour(1));

        // Heights round halves up: 2.5 is three rows, 2.49 two.
        CodedSkin.AssertPixel(image, 24 + 8, 43 + 13, CodedSkin.VisColour(15));
        CodedSkin.AssertPixel(image, 24 + 13, 43 + 13, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 24 + 13, 43 + 14, CodedSkin.VisColour(16));

        // The last bar is columns 72 to 74; column 75 is never drawn on.
        CodedSkin.AssertPixel(image, 24 + 74, 43 + 15, CodedSkin.VisColour(17));
        CodedSkin.AssertPixel(image, 24 + 75, 43 + 15, CodedSkin.VisColour(0));
    }

    [Fact]
    public void Seventy_five_bars_are_one_column_each()
    {
        var frame = Frame(75);
        frame.Bars[10] = 4;
        frame.Bars[74] = 1;
        frame.Peaks[74] = 1;
        var image = Render(Playing, frame);
        CodedSkin.AssertPixel(image, 24 + 10, 43 + 12, CodedSkin.VisColour(14));
        CodedSkin.AssertPixel(image, 24 + 10, 43 + 11, CodedSkin.VisColour(1));
        CodedSkin.AssertPixel(image, 24 + 11, 43 + 15, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 24 + 12, 43 + 15, CodedSkin.VisColour(1));
        CodedSkin.AssertPixel(image, 24 + 74, 43 + 15, CodedSkin.VisColour(17));
        CodedSkin.AssertPixel(image, 24 + 74, 43 + 14, CodedSkin.VisColour(23));
    }

    [Fact]
    public void Silence_on_the_scope_is_a_line_through_row_seven()
    {
        var frame = Frame();
        var image = Render(Playing with { Visualiser = VisualiserMode.Oscilloscope }, frame);
        for (var x = 0; x < 75; x++)
        {
            CodedSkin.AssertPixel(image, 24 + x, 43 + 7, CodedSkin.VisColour(18));
            CodedSkin.AssertPixel(image, 24 + x, 43 + 6, CodedSkin.VisColour(0));
        }

        CodedSkin.AssertPixel(image, 24 + 75, 43 + 7, CodedSkin.VisColour(0));
    }

    [Fact]
    public void The_scope_joins_its_columns_in_colours_by_height()
    {
        var frame = Frame();
        for (var i = 10; i < 20; i++)
        {
            frame.Scope[i] = -1;
        }

        for (var i = 20; i < 30; i++)
        {
            frame.Scope[i] = 1;
        }

        var image = Render(Playing with { Visualiser = VisualiserMode.Oscilloscope }, frame);

        // Up to the top: rows 0 to 7 in the top row's colour (21).
        for (var row = 0; row <= 7; row++)
        {
            CodedSkin.AssertPixel(image, 24 + 10, 43 + row, CodedSkin.VisColour(21));
        }

        CodedSkin.AssertPixel(image, 24 + 11, 43, CodedSkin.VisColour(21));
        CodedSkin.AssertPixel(image, 24 + 11, 43 + 1, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 24 + 12, 43 + 1, CodedSkin.VisColour(1));

        // Down to the bottom: the run starts one row below the last sample (rows 1 to 15, colour 22).
        CodedSkin.AssertPixel(image, 24 + 20, 43, CodedSkin.VisColour(0));
        for (var row = 1; row <= 15; row++)
        {
            CodedSkin.AssertPixel(image, 24 + 20, 43 + row, CodedSkin.VisColour(22));
        }

        // Back to the middle from the bottom: rows 7 to 15, in the middle's colour.
        for (var row = 7; row <= 15; row++)
        {
            CodedSkin.AssertPixel(image, 24 + 30, 43 + row, CodedSkin.VisColour(18));
        }
    }

    [Fact]
    public void Render_visualiser_draws_what_render_draws_there()
    {
        var frame = Frame();
        for (var i = 0; i < 19; i++)
        {
            frame.Bars[i] = i % 16;
            frame.Peaks[i] = (i + 3) % 16;
        }

        for (var i = 0; i < frame.Scope.Length; i++)
        {
            frame.Scope[i] = MathF.Sin(i / 5f);
        }

        foreach (var shaded in new[] { false, true })
        {
            foreach (var mode in new[] { VisualiserMode.Spectrum, VisualiserMode.Oscilloscope, VisualiserMode.Off })
            {
                var window = Render(Playing with { Shaded = shaded, Visualiser = mode }, frame);
                var (left, top, width, height) = ClassicLayout.VisualiserArea(shaded);
                var alone = new SkinImage(width, height);
                ClassicRenderer.RenderVisualiser(Coded, mode, frame, shaded, alone);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        CodedSkin.AssertPixel(alone, x, y, window[left + x, top + y]);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_shaded_window_is_the_shade_bar()
    {
        var image = Render(new ClassicView { Shaded = true });
        Assert.Equal(14, image.Height);

        // The bar's own art around the time (127 to 157) and the seek track (226 to 242).
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 27, 29, 127, 14, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 27 + 158, 29, 68, 14, 158, 0);

        // The unshade button shows while the window is active; the seek track always.
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 0, 27, 9, 9, 254, 3);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 0, 36, 17, 7, 226, 4);

        var inactive = Render(new ClassicView { Shaded = true, WindowActive = false });
        CodedSkin.AssertBlock(inactive, SkinSheet.TitleBar, 27, 42, 127, 14, 0, 0);
        CodedSkin.AssertBlock(inactive, SkinSheet.TitleBar, 27 + 254, 42 + 3, 9, 9, 254, 3);

        var pressed = Render(new ClassicView { Shaded = true, Pressed = ClassicControl.Shade });
        CodedSkin.AssertBlock(pressed, SkinSheet.TitleBar, 9, 27, 9, 9, 254, 3);

        var closing = Render(new ClassicView { Shaded = true, Pressed = ClassicControl.Close });
        CodedSkin.AssertBlock(closing, SkinSheet.TitleBar, 18, 9, 9, 9, 264, 3);
    }

    [Fact]
    public void The_shade_bar_time_is_written_in_text_glyphs()
    {
        // Stopped: five spaces.
        var stopped = Render(new ClassicView { Shaded = true });
        foreach (var x in new[] { 128, 134, 139, 147, 152 })
        {
            CodedSkin.AssertBlock(stopped, SkinSheet.Text, 150, 0, 5, 6, x, 4);
        }

        var playing = Render(Playing with { Shaded = true });
        CodedSkin.AssertBlock(playing, SkinSheet.Text, 150, 0, 5, 6, 128, 4);
        CodedSkin.AssertBlock(playing, SkinSheet.Text, 0, 6, 5, 6, 134, 4);
        CodedSkin.AssertBlock(playing, SkinSheet.Text, 15, 6, 5, 6, 139, 4);
        CodedSkin.AssertBlock(playing, SkinSheet.Text, 0, 6, 5, 6, 147, 4);
        CodedSkin.AssertBlock(playing, SkinSheet.Text, 35, 6, 5, 6, 152, 4);

        // Remaining: a minus first.
        var remaining = Render(Playing with { Shaded = true, ShowRemaining = true });
        CodedSkin.AssertBlock(remaining, SkinSheet.Text, 75, 6, 5, 6, 128, 4);

        // Paused in the dark half of the blink: spaces again, the minus too.
        var blinked = Render(Playing with { Shaded = true, ShowRemaining = true, State = ClassicPlayState.Paused, BlinkOn = false });
        CodedSkin.AssertBlock(blinked, SkinSheet.Text, 150, 0, 5, 6, 128, 4);
        CodedSkin.AssertBlock(blinked, SkinSheet.Text, 150, 0, 5, 6, 152, 4);
    }

    [Theory]
    [InlineData(0.0, 1, 17)]
    [InlineData(0.4, 5, 17)]
    [InlineData(0.5, 7, 20)]
    [InlineData(1.0, 13, 23)]
    public void The_shade_bar_seek_thumb_changes_look_along_the_way(double seek, int offset, int sourceX)
    {
        var image = Render(new ClassicView { Shaded = true, Seek = seek });
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, sourceX, 36, 3, 7, 226 + offset, 4);
    }

    [Fact]
    public void The_shade_bar_visualiser_has_no_grid_and_five_rows()
    {
        var background = Render(Playing with { Shaded = true });
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 38; x++)
            {
                CodedSkin.AssertPixel(background, 79 + x, 5 + y, CodedSkin.VisColour(0));
            }
        }

        var frame = Frame();
        frame.Bars[0] = 15;
        frame.Bars[1] = 15;
        frame.Peaks[0] = 15;
        frame.Bars[17] = 15;
        frame.Bars[18] = 15;
        var image = Render(Playing with { Shaded = true }, frame);

        // Shade bar 0 stands for bar 0: full height, colours 4, 8, 11, 14 and 17 from the top, no cap.
        var colours = new[] { 4, 8, 11, 14, 17 };
        for (var x = 0; x < 3; x++)
        {
            for (var row = 0; row < 5; row++)
            {
                CodedSkin.AssertPixel(image, 79 + x, 5 + row, CodedSkin.VisColour(colours[row]));
            }
        }

        // Shade bar 1 is the mean of bars 1 and 2 (7.5 of 15 rows): three of five rows.
        CodedSkin.AssertPixel(image, 79 + 4, 5 + 1, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 79 + 4, 5 + 2, CodedSkin.VisColour(11));

        // The last bar has one column (36); column 37 stays background.
        CodedSkin.AssertPixel(image, 79 + 36, 5, CodedSkin.VisColour(4));
        CodedSkin.AssertPixel(image, 79 + 37, 5, CodedSkin.VisColour(0));
        CodedSkin.AssertPixel(image, 79 + 35, 5 + 4, CodedSkin.VisColour(0));
    }

    [Fact]
    public void The_shade_bar_scope_is_one_colour_in_five_rows()
    {
        var frame = Frame();
        frame.Scope[20] = 1;
        var image = Render(Playing with { Shaded = true, Visualiser = VisualiserMode.Oscilloscope }, frame);
        for (var x = 0; x < 38; x++)
        {
            CodedSkin.AssertPixel(image, 79 + x, 5 + 2, CodedSkin.VisColour(18));
        }

        // Full scale reaches the bottom row, joined to its neighbours.
        CodedSkin.AssertPixel(image, 79 + 20, 5 + 4, CodedSkin.VisColour(18));
        CodedSkin.AssertPixel(image, 79 + 21, 5 + 3, CodedSkin.VisColour(18));
        CodedSkin.AssertPixel(image, 79 + 22, 5 + 3, CodedSkin.VisColour(0));
    }

    [Fact]
    public void Render_wants_a_picture_the_window_fits()
    {
        Assert.Throws<ArgumentException>(() => ClassicRenderer.Render(Coded, new ClassicView(), new SkinImage(275, 14)));
        Assert.Throws<ArgumentException>(() => ClassicRenderer.Render(Coded, new ClassicView(), new SkinImage(100, 116)));

        // A full-size picture also takes the shade bar, in its top rows.
        var full = new SkinImage(275, 116);
        ClassicRenderer.Render(Coded, new ClassicView { Shaded = true }, full);
        CodedSkin.AssertBlock(full, SkinSheet.TitleBar, 27, 29, 100, 14, 0, 0);
    }

    [Fact]
    public void Rendering_a_busy_window_is_cheap()
    {
        var frame = Frame();
        for (var i = 0; i < 19; i++)
        {
            frame.Bars[i] = 15 - (i % 15);
            frame.Peaks[i] = 15;
        }

        var view = Playing with { Marquee = string.Concat(Enumerable.Repeat("Artist - Title ", 4)), Pressed = ClassicControl.Seek };
        var target = new SkinImage(ClassicRenderer.Width, ClassicRenderer.Height);
        for (var i = 0; i < 50; i++)
        {
            ClassicRenderer.Render(Coded, view with { MarqueeOffset = i }, target, frame);
        }

        const int Runs = 500;
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Runs; i++)
        {
            ClassicRenderer.Render(Coded, view with { MarqueeOffset = i }, target, frame);
        }

        // Well under a millisecond on any machine CI uses; a frame at 165 Hz is 6 ms.
        Assert.InRange(clock.Elapsed.TotalMilliseconds / Runs, 0, 1.0);
    }

    private static void AssertTime(SkinImage image, bool minus, int d0, int d1, int d2, int d3)
    {
        CodedSkin.AssertBlock(image, SkinSheet.NumsEx, minus ? 99 : 90, 0, 9, 13, 36, 26);
        CodedSkin.AssertBlock(image, SkinSheet.NumsEx, 9 * d0, 0, 9, 13, 48, 26);
        CodedSkin.AssertBlock(image, SkinSheet.NumsEx, 9 * d1, 0, 9, 13, 60, 26);
        CodedSkin.AssertBlock(image, SkinSheet.NumsEx, 9 * d2, 0, 9, 13, 78, 26);
        CodedSkin.AssertBlock(image, SkinSheet.NumsEx, 9 * d3, 0, 9, 13, 90, 26);

        // The colon between them is main.bmp's.
        CodedSkin.AssertMain(image, 69, 26, 9, 13);
    }
}

public sealed class PixelFontTests
{
    [Fact]
    public void Every_character_of_webamps_table_has_its_cell()
    {
        string[] rows =
        [
            "abcdefghijklmnopqrstuvwxyz\"@",
            "0123456789\u2026.:()-'!_+\\/[]^&%,=$#",
            "ÅÖÄ?*",
        ];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                Assert.Equal((row, column), PixelFont.Cell(rows[row][column]));
            }
        }

        Assert.Equal((0, 30), PixelFont.Cell(' '));
        Assert.Equal((1, 22), PixelFont.Cell('<'));
        Assert.Equal((1, 23), PixelFont.Cell('>'));
        Assert.Equal((1, 22), PixelFont.Cell('{'));
        Assert.Equal((1, 23), PixelFont.Cell('}'));
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('Z', 'z')]
    [InlineData('é', 'e')]
    [InlineData('Ñ', 'n')]
    [InlineData('Ç', 'c')]
    [InlineData('Ø', 'o')]
    [InlineData('ü', 'u')]
    [InlineData('ÿ', 'y')]
    [InlineData('ß', 's')]
    [InlineData('æ', 'a')]
    [InlineData('å', 'Å')]
    [InlineData('ö', 'Ö')]
    [InlineData('ä', 'Ä')]
    [InlineData('\uFF21', 'a')]
    [InlineData('\uFF41', 'a')]
    [InlineData('\uFF0A', '*')]
    [InlineData('\uFF10', '0')]
    [InlineData('\u3000', ' ')]
    [InlineData('\t', ' ')]
    [InlineData('\u00A0', ' ')]
    [InlineData('\u2019', '\'')]
    [InlineData('`', '\'')]
    [InlineData('\u201C', '"')]
    [InlineData('\u2014', '-')]
    [InlineData('\u2013', '-')]
    [InlineData('~', '-')]
    [InlineData('|', '/')]
    [InlineData(';', ',')]
    public void Other_characters_fold_onto_the_ones_the_font_has(char character, char shown)
    {
        Assert.Equal(PixelFont.Cell(shown), PixelFont.Cell(character));
        Assert.True(PixelFont.Covers(character));
    }

    [Theory]
    [InlineData('あ')]
    [InlineData('東')]
    [InlineData('Ж')]
    public void Characters_the_font_lacks_draw_as_spaces(char character)
    {
        Assert.Equal((0, 30), PixelFont.Cell(character));
        Assert.False(PixelFont.Covers(character));
    }

    [Fact]
    public void Covers_says_whether_a_whole_line_can_be_written()
    {
        Assert.True(PixelFont.Covers("Björk - Jóga (5:05)"));
        Assert.True(PixelFont.Covers("\uFF082026\uFF09\u3000REMIX"));
        Assert.False(PixelFont.Covers("宇多田ヒカル - First Love"));
        Assert.True(PixelFont.Covers(string.Empty));
    }

    [Fact]
    public void Draw_writes_five_pixels_a_character_and_stops_at_its_width()
    {
        var skin = CodedSkin.Create();
        var target = new SkinImage(40, 10);
        var blank = CodedSkin.Code(SkinSheet.Main, 0, 0);
        target.Fill(blank);
        PixelFont.Draw(skin, target, "Hi!", 2, 1, 12);
        CodedSkin.AssertBlock(target, SkinSheet.Text, 35, 0, 5, 6, 2, 1);
        CodedSkin.AssertBlock(target, SkinSheet.Text, 40, 0, 5, 6, 7, 1);
        CodedSkin.AssertBlock(target, SkinSheet.Text, 85, 6, 2, 6, 12, 1);
        CodedSkin.AssertPixel(target, 14, 1, blank);
        CodedSkin.AssertPixel(target, 2, 0, blank);
        CodedSkin.AssertPixel(target, 2, 7, blank);
    }
}
