using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

/// <summary>The mini player's equalizer and playlist windows, and how the three windows stack.</summary>
public sealed class ClassicStackTests
{
    private static readonly Skin Coded = CodedSkin.Create();

    // The equalizer

    [Theory]
    [InlineData(100, 5, EqualizerControl.TitleBar, -1)]
    [InlineData(256, 5, EqualizerControl.Shade, -1)]
    [InlineData(266, 5, EqualizerControl.Close, -1)]
    [InlineData(20, 22, EqualizerControl.On, -1)]
    [InlineData(50, 22, EqualizerControl.Auto, -1)]
    [InlineData(230, 22, EqualizerControl.Presets, -1)]
    [InlineData(120, 25, EqualizerControl.Graph, -1)]
    [InlineData(25, 60, EqualizerControl.Preamp, -1)]
    [InlineData(80, 40, EqualizerControl.Band, 0)]
    [InlineData(244, 100, EqualizerControl.Band, 9)]
    [InlineData(5, 60, EqualizerControl.None, -1)]
    [InlineData(275, 60, EqualizerControl.None, -1)]
    public void The_equalizer_finds_its_controls(int x, int y, EqualizerControl control, int band)
    {
        Assert.Equal((control, band), EqualizerLayout.HitTest(x, y, shaded: false));
    }

    [Theory]
    [InlineData(80, 6, EqualizerControl.ShadeVolume)]
    [InlineData(180, 6, EqualizerControl.ShadeBalance)]
    [InlineData(30, 6, EqualizerControl.TitleBar)]
    [InlineData(266, 6, EqualizerControl.Close)]
    [InlineData(100, 20, EqualizerControl.None)]
    public void The_rolled_up_equalizer_has_its_mini_sliders(int x, int y, EqualizerControl control)
    {
        Assert.Equal(control, EqualizerLayout.HitTest(x, y, shaded: true).Control);
    }

    [Theory]
    [InlineData(12, 38)]
    [InlineData(0, 64)]
    [InlineData(-12, 90)]
    public void A_gain_puts_the_thumb_on_its_travel(double gain, int thumbTop)
    {
        Assert.Equal(thumbTop, EqualizerLayout.ThumbTop(gain));
        Assert.Equal(gain, EqualizerLayout.GainAt(thumbTop), 3);
    }

    [Fact]
    public void Near_the_middle_a_slider_snaps_to_zero()
    {
        Assert.Equal(0, EqualizerLayout.GainAt(64.2));
        Assert.NotEqual(0, EqualizerLayout.GainAt(62));
        Assert.Equal(12, EqualizerLayout.GainAt(-50));
        Assert.Equal(-12, EqualizerLayout.GainAt(500));
    }

    [Fact]
    public void The_equalizer_window_draws_its_background_title_and_switches()
    {
        var image = RenderEqualizer(new EqualizerView { On = true });
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 134, 264, 14, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 69, 119, 26, 12, 14, 18);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 36, 119, 32, 12, 40, 18);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 224, 164, 44, 12, 217, 18);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 116, 9, 9, 264, 3);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 108, 275, 8, 0, 108);
    }

    [Fact]
    public void An_inactive_equalizer_has_the_inactive_title()
    {
        var image = RenderEqualizer(new EqualizerView { WindowActive = false });
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 149, 264, 14, 0, 0);
    }

    [Fact]
    public void Each_slider_shows_its_gain_and_a_held_one_its_pressed_thumb()
    {
        var bands = new double[10];
        bands[0] = 12;
        bands[9] = -12;
        var image = RenderEqualizer(new EqualizerView { Bands = bands, Pressed = EqualizerControl.Band, PressedBand = 9 });

        // Top: the last frame of the second row; bottom: the first frame; the middle one for 0 dB.
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 13 + (13 * 15), 164 + 65, 1, 63, 78, 38);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 13, 164, 1, 63, 78 + (18 * 9), 38);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 164, 11, 11, 79, 38);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 176, 11, 11, 79 + (18 * 9), 90);
        CodedSkin.AssertBlock(image, SkinSheet.EqMain, 0, 164, 11, 11, 79 + 18, 64);
    }

    [Fact]
    public void The_graph_draws_a_line_through_flat_bands_in_the_row_colour()
    {
        var image = RenderEqualizer(new EqualizerView());
        var middle = 17 + 9;
        for (var x = 88; x <= 88 + 108; x++)
        {
            CodedSkin.AssertPixel(image, x, middle, CodedSkin.Code(SkinSheet.EqMain, 115, 294 + 9));
        }
    }

    [Fact]
    public void The_rolled_up_equalizer_shows_its_bar_and_mini_thumbs()
    {
        var image = RenderEqualizer(new EqualizerView { Shaded = true, Volume = 1, Balance = 0 }, shaded: true);
        CodedSkin.AssertBlock(image, SkinSheet.EqEx, 0, 0, 61, 4, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.EqEx, 7, 30, 3, 7, 61 + 94, 4);
        CodedSkin.AssertBlock(image, SkinSheet.EqEx, 14, 30, 3, 7, 164 + 20, 4);
        CodedSkin.AssertBlock(image, SkinSheet.EqEx, 11, 38, 9, 9, 264, 3);
    }

    [Fact]
    public void The_equalizer_needs_a_picture_large_enough()
    {
        Assert.Throws<ArgumentException>(() => EqualizerRenderer.Render(Coded, new EqualizerView(), new SkinImage(275, 14)));
        EqualizerRenderer.Render(Coded, new EqualizerView { Shaded = true }, new SkinImage(275, 14));
    }

    [Fact]
    public void Equal_views_compare_equal_by_their_gains()
    {
        Assert.Equal(new EqualizerView { Bands = [1, 2] }, new EqualizerView { Bands = [1, 2] });
        Assert.NotEqual(new EqualizerView { Bands = [1, 2] }, new EqualizerView { Bands = [1, 3] });
    }

    [Fact]
    public void Ten_sliders_share_six_bands()
    {
        Assert.Equal([0, 1, 2, 2, 3, 4, 4, 5, 5, 5], Enumerable.Range(0, 10).Select(EqualizerSliders.BandFor));
        Assert.Equal([1, 2, 3, 3, 4, 5, 5, 6, 6, 6], EqualizerSliders.FromBands([1, 2, 3, 4, 5, 6]));
        Assert.Equal(new double[10], EqualizerSliders.FromBands([]));
        Assert.Equal(5, EqualizerSliders.BandFor(42));
    }

    // The playlist

    [Theory]
    [InlineData(0, 116)]
    [InlineData(130, 116)]
    [InlineData(131, 145)]
    [InlineData(232, 232)]
    [InlineData(5000, PlaylistLayout.MaxHeight)]
    public void The_playlist_grows_in_whole_steps(int asked, int height)
    {
        Assert.Equal(height, PlaylistLayout.SnapHeight(asked));
    }

    [Fact]
    public void The_list_fills_the_frame_and_holds_whole_rows()
    {
        Assert.Equal((12, 20, 243, 58), PlaylistLayout.ListArea(116));
        Assert.Equal(4, PlaylistLayout.VisibleRows(116));
        Assert.Equal(13, PlaylistLayout.VisibleRows(232));
        Assert.Equal(0, PlaylistLayout.RowAt(20, 116));
        Assert.Equal(1, PlaylistLayout.RowAt(33, 116));
        Assert.Equal(-1, PlaylistLayout.RowAt(19, 116));
        Assert.Equal(-1, PlaylistLayout.RowAt(78, 116));
    }

    [Theory]
    [InlineData(100, 5, PlaylistControl.TitleBar)]
    [InlineData(266, 5, PlaylistControl.Close)]
    [InlineData(256, 5, PlaylistControl.Shade)]
    [InlineData(100, 40, PlaylistControl.List)]
    [InlineData(262, 40, PlaylistControl.Scroll)]
    [InlineData(5, 40, PlaylistControl.None)]
    [InlineData(20, 90, PlaylistControl.Add)]
    [InlineData(50, 90, PlaylistControl.Remove)]
    [InlineData(80, 90, PlaylistControl.Select)]
    [InlineData(110, 90, PlaylistControl.Misc)]
    [InlineData(240, 90, PlaylistControl.ListOptions)]
    [InlineData(130, 102, PlaylistControl.Previous)]
    [InlineData(141, 102, PlaylistControl.Play)]
    [InlineData(178, 102, PlaylistControl.Eject)]
    [InlineData(270, 110, PlaylistControl.Grip)]
    public void The_playlist_finds_its_controls(int x, int y, PlaylistControl control)
    {
        Assert.Equal(control, PlaylistLayout.HitTest(x, y, 116, shaded: false));
    }

    [Fact]
    public void The_rolled_up_playlist_is_a_bar_with_two_buttons()
    {
        Assert.Equal(PlaylistControl.TitleBar, PlaylistLayout.HitTest(100, 5, 232, shaded: true));
        Assert.Equal(PlaylistControl.Close, PlaylistLayout.HitTest(266, 5, 232, shaded: true));
        Assert.Equal(PlaylistControl.None, PlaylistLayout.HitTest(100, 20, 232, shaded: true));
    }

    [Fact]
    public void Scrolling_picks_the_first_row_and_back()
    {
        Assert.Equal(0, PlaylistLayout.FirstRow(1, 3, 4));
        Assert.Equal(6, PlaylistLayout.FirstRow(1, 10, 4));
        Assert.Equal(3, PlaylistLayout.FirstRow(0.5, 10, 4));
        Assert.Equal(0.5, PlaylistLayout.ScrollOf(3, 10, 4));
        Assert.Equal(0, PlaylistLayout.ScrollOf(3, 4, 4));
        Assert.Equal(0, PlaylistLayout.ScrollAt(116, 20));
        Assert.Equal(1, PlaylistLayout.ScrollAt(116, 60));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(187, "3:07")]
    [InlineData(3786, "1:03:06")]
    [InlineData(-5, "0:00")]
    public void Lengths_are_written_as_winamp_did(int seconds, string text)
    {
        Assert.Equal(text, PlaylistLayout.FormatTime(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void The_playlist_frame_has_its_corners_sides_and_the_skins_background()
    {
        var image = RenderPlaylist(new PlaylistView { Height = 145 });
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 0, 0, 25, 20, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 153, 0, 25, 20, 250, 0);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 0, 42, 12, 29, 0, 20);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 31, 42, 5, 29, 255, 20);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 0, 72, 125, 38, 0, 107);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 126, 72, 150, 38, 125, 107);
        CodedSkin.AssertPixel(image, 100, 60, 0xFF000000);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 52, 53, 8, 18, 260, 20);
    }

    [Fact]
    public void The_scroll_handle_moves_down_the_bar()
    {
        var image = RenderPlaylist(new PlaylistView { Height = 116, Scroll = 1, Pressed = PlaylistControl.Scroll });
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 61, 53, 8, 18, 260, 60);
    }

    [Fact]
    public void The_rolled_up_playlist_shows_its_bar()
    {
        var image = RenderPlaylist(new PlaylistView { Shaded = true, WindowActive = false }, shaded: true);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 72, 42, 5, 14, 0, 0);
        CodedSkin.AssertBlock(image, SkinSheet.PlEdit, 99, 57, 50, 14, 225, 0);
    }

    // The stack and the screen

    [Fact]
    public void The_windows_stack_under_the_main_one()
    {
        Assert.Equal(new ClassicStackLayout(116, 116, 0, 116, 0), ClassicStack.Arrange(new ClassicStackState(false, false, false, false, false, 232)));
        var all = ClassicStack.Arrange(new ClassicStackState(false, true, false, true, false, 240));
        Assert.Equal(new ClassicStackLayout(116, 116, 116, 232, 232), all);
        Assert.Equal(464, all.Height);
        var rolled = ClassicStack.Arrange(new ClassicStackState(true, true, true, true, true, 240));
        Assert.Equal(new ClassicStackLayout(14, 14, 14, 28, 14), rolled);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(-8, 0)]
    [InlineData(11, 11)]
    [InlineData(1500, 1500)]
    [InlineData(1625, 1630)]
    [InlineData(1640, 1630)]
    public void A_window_dragged_near_an_edge_snaps_to_it(int position, int snapped)
    {
        Assert.Equal(snapped, WindowSnap.Snap(position, 290, 0, 1920, 10));
    }

    [Fact]
    public void A_held_A_lights_and_stays_lit_while_on_top()
    {
        var image = new SkinImage(ClassicRenderer.Width, ClassicRenderer.Height);
        ClassicRenderer.Render(Coded, new ClassicView { AlwaysOnTop = true }, image);
        CodedSkin.AssertBlock(image, SkinSheet.TitleBar, 312, 55, 8, 7, 10, 33);
    }

    [Fact]
    public void Every_equalizer_and_playlist_sprite_of_the_built_in_skin_has_art()
    {
        var built = BuiltInSkin.Create();
        foreach (var sprite in StackSprites.All)
        {
            var sheet = built.Sheet(sprite.Sheet);
            Assert.True(sprite.X + sprite.Width <= sheet.Width && sprite.Y + sprite.Height <= sheet.Height, $"{sprite} lies inside its sheet.");
            var colours = new HashSet<uint>();
            for (var y = sprite.Y; y < sprite.Y + sprite.Height; y++)
            {
                for (var x = sprite.X; x < sprite.X + sprite.Width; x++)
                {
                    colours.Add(sheet[x, y]);
                }
            }

            Assert.True(colours.Count > 1, $"{sprite} is a flat fill.");
        }
    }

    [Fact]
    public void The_built_in_skin_draws_both_windows_without_falling_back()
    {
        var built = BuiltInSkin.Create();
        EqualizerRenderer.Render(built, new EqualizerView { On = true, Bands = [3, -3, 6, 0, 0, 0, 0, 12, -12, 1] }, new SkinImage(275, 116));
        PlaylistRenderer.Render(built, new PlaylistView { Height = 290, RunningTime = "3:07/1:03:06", TrackTime = "1:23" }, new SkinImage(275, 290));
        PlaylistRenderer.Render(built, new PlaylistView { Shaded = true, ShadeTitle = "1. Artist - Title", ShadeTime = "3:07" }, new SkinImage(275, 14));
    }

    private static SkinImage RenderEqualizer(EqualizerView view, bool shaded = false)
    {
        var image = new SkinImage(EqualizerLayout.Width, shaded ? EqualizerLayout.ShadeHeight : EqualizerLayout.Height);
        EqualizerRenderer.Render(Coded, view, image);
        return image;
    }

    private static SkinImage RenderPlaylist(PlaylistView view, bool shaded = false)
    {
        var image = new SkinImage(PlaylistLayout.Width, shaded ? PlaylistLayout.ShadeHeight : PlaylistLayout.SnapHeight(view.Height));
        PlaylistRenderer.Render(Coded, view, image);
        return image;
    }
}
