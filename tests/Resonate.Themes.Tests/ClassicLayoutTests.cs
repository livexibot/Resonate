using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

public sealed class ClassicLayoutTests
{
    [Theory]
    [InlineData(10, 7, ClassicControl.Options)]
    [InlineData(248, 7, ClassicControl.Minimize)]
    [InlineData(258, 7, ClassicControl.Shade)]
    [InlineData(268, 7, ClassicControl.Close)]
    [InlineData(100, 5, ClassicControl.TitleBar)]
    [InlineData(0, 0, ClassicControl.TitleBar)]
    [InlineData(274, 13, ClassicControl.TitleBar)]
    [InlineData(13, 28, ClassicControl.ClutterOptions)]
    [InlineData(13, 36, ClassicControl.ClutterAlwaysOnTop)]
    [InlineData(13, 43, ClassicControl.ClutterInfo)]
    [InlineData(13, 50, ClassicControl.ClutterDoubleSize)]
    [InlineData(13, 58, ClassicControl.ClutterVisualiser)]
    [InlineData(36, 26, ClassicControl.Time)]
    [InlineData(98, 38, ClassicControl.Time)]
    [InlineData(50, 50, ClassicControl.Visualiser)]
    [InlineData(150, 24, ClassicControl.Marquee)]
    [InlineData(264, 35, ClassicControl.Marquee)]
    [InlineData(120, 62, ClassicControl.Volume)]
    [InlineData(190, 62, ClassicControl.Balance)]
    [InlineData(225, 62, ClassicControl.Equalizer)]
    [InlineData(250, 62, ClassicControl.Playlist)]
    [InlineData(16, 72, ClassicControl.Seek)]
    [InlineData(263, 81, ClassicControl.Seek)]
    [InlineData(16, 88, ClassicControl.Previous)]
    [InlineData(45, 95, ClassicControl.Play)]
    [InlineData(70, 95, ClassicControl.Pause)]
    [InlineData(90, 95, ClassicControl.Stop)]
    [InlineData(129, 105, ClassicControl.Next)]
    [InlineData(140, 95, ClassicControl.Eject)]
    [InlineData(180, 95, ClassicControl.Shuffle)]
    [InlineData(209, 95, ClassicControl.Shuffle)]
    [InlineData(210, 95, ClassicControl.Repeat)]
    [InlineData(237, 103, ClassicControl.Repeat)]
    [InlineData(258, 98, ClassicControl.About)]
    public void Every_control_of_the_main_window_answers_to_the_pointer(int x, int y, ClassicControl control) =>
        Assert.Equal(control, ClassicLayout.HitTest(x, y, shaded: false));

    [Theory]
    [InlineData(2, 20)]
    [InlineData(30, 30)]
    [InlineData(130, 95)]
    [InlineData(140, 88)]
    [InlineData(15, 88)]
    [InlineData(200, 47)]
    [InlineData(270, 110)]
    [InlineData(-1, 5)]
    [InlineData(275, 5)]
    [InlineData(5, 116)]
    public void Elsewhere_is_nothing(int x, int y) =>
        Assert.Equal(ClassicControl.None, ClassicLayout.HitTest(x, y, shaded: false));

    [Theory]
    [InlineData(10, 7, ClassicControl.Options)]
    [InlineData(248, 7, ClassicControl.Minimize)]
    [InlineData(258, 7, ClassicControl.Shade)]
    [InlineData(268, 7, ClassicControl.Close)]
    [InlineData(90, 7, ClassicControl.Visualiser)]
    [InlineData(140, 6, ClassicControl.Time)]
    [InlineData(172, 7, ClassicControl.Previous)]
    [InlineData(180, 7, ClassicControl.Play)]
    [InlineData(190, 7, ClassicControl.Pause)]
    [InlineData(199, 7, ClassicControl.Stop)]
    [InlineData(208, 7, ClassicControl.Next)]
    [InlineData(219, 7, ClassicControl.Eject)]
    [InlineData(233, 7, ClassicControl.Seek)]
    [InlineData(50, 7, ClassicControl.TitleBar)]
    [InlineData(172, 0, ClassicControl.TitleBar)]
    [InlineData(274, 13, ClassicControl.TitleBar)]
    [InlineData(5, 14, ClassicControl.None)]
    [InlineData(-1, 5, ClassicControl.None)]
    public void The_shade_bar_answers_to_the_pointer(int x, int y, ClassicControl control) =>
        Assert.Equal(control, ClassicLayout.HitTest(x, y, shaded: true));

    [Fact]
    public void The_visualiser_sits_where_winamp_put_it()
    {
        Assert.Equal((24, 43, 76, 16), ClassicLayout.VisualiserArea(false));
        Assert.Equal((79, 5, 38, 5), ClassicLayout.VisualiserArea(true));
    }

    [Theory]
    [InlineData(ClassicControl.Volume, 0.0, false, 107, 14)]
    [InlineData(ClassicControl.Volume, 0.5, false, 133, 14)]
    [InlineData(ClassicControl.Volume, 0.49, false, 132, 14)]
    [InlineData(ClassicControl.Volume, 1.0, false, 158, 14)]
    [InlineData(ClassicControl.Volume, 2.0, false, 158, 14)]
    [InlineData(ClassicControl.Balance, -1.0, false, 177, 14)]
    [InlineData(ClassicControl.Balance, -0.5, false, 183, 14)]
    [InlineData(ClassicControl.Balance, 0.0, false, 189, 14)]
    [InlineData(ClassicControl.Balance, 0.04, false, 189, 14)]
    [InlineData(ClassicControl.Balance, -0.05, false, 188, 14)]
    [InlineData(ClassicControl.Balance, 1.0, false, 201, 14)]
    [InlineData(ClassicControl.Seek, 0.0, false, 16, 29)]
    [InlineData(ClassicControl.Seek, 0.5, false, 125, 29)]
    [InlineData(ClassicControl.Seek, 1.0, false, 235, 29)]
    [InlineData(ClassicControl.Seek, 0.0, true, 227, 3)]
    [InlineData(ClassicControl.Seek, 0.5, true, 233, 3)]
    [InlineData(ClassicControl.Seek, 1.0, true, 239, 3)]
    public void Thumbs_sit_where_winamp_put_them(ClassicControl slider, double value, bool shaded, int left, int width) =>
        Assert.Equal((left, width), ClassicLayout.SliderThumb(slider, value, shaded));

    [Theory]
    [InlineData(ClassicControl.Volume, false, 107, 51, 1.0)]
    [InlineData(ClassicControl.Balance, false, 177, 24, 2.0)]
    [InlineData(ClassicControl.Seek, false, 16, 219, 1.0)]
    [InlineData(ClassicControl.Seek, true, 227, 12, 1.0)]
    public void Every_thumb_place_and_its_value_agree(ClassicControl slider, bool shaded, int first, int travel, double range)
    {
        // A thumb put at each pixel stays there.
        for (var place = first; place <= first + travel; place++)
        {
            var value = ClassicLayout.SliderValue(slider, place, shaded);
            Assert.Equal(place, ClassicLayout.SliderThumb(slider, value, shaded).Left);
        }

        // And a value comes back within one pixel's step.
        var step = range / travel;
        var lowest = slider == ClassicControl.Balance ? -1.0 : 0.0;
        for (var value = lowest; value <= 1.0; value += 0.01)
        {
            var left = ClassicLayout.SliderThumb(slider, value, shaded).Left;
            var back = ClassicLayout.SliderValue(slider, left, shaded);
            Assert.InRange(Math.Abs(back - value), 0, step);
        }
    }

    [Fact]
    public void Slider_values_stay_on_their_tracks()
    {
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Volume, 0, false));
        Assert.Equal(1, ClassicLayout.SliderValue(ClassicControl.Volume, 400, false));
        Assert.Equal(0.5, ClassicLayout.SliderValue(ClassicControl.Volume, 107 + 25.5, false), 6);
        Assert.Equal(-1, ClassicLayout.SliderValue(ClassicControl.Balance, 0, false));
        Assert.Equal(1, ClassicLayout.SliderValue(ClassicControl.Balance, 400, false));
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Seek, -40, false));
        Assert.Equal(1, ClassicLayout.SliderValue(ClassicControl.Seek, 400, false));
        Assert.Equal(0.5, ClassicLayout.SliderValue(ClassicControl.Seek, 16 + 109.5, false), 6);
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Seek, double.NaN, false));
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Seek, 0, true));
        Assert.Equal(1, ClassicLayout.SliderValue(ClassicControl.Seek, 260, true));
    }

    [Fact]
    public void Balance_snaps_to_the_centre_when_close()
    {
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Balance, 189.5, false));
        Assert.Equal(0, ClassicLayout.SliderValue(ClassicControl.Balance, 188.2, false));
        Assert.Equal(1.0 / 12, ClassicLayout.SliderValue(ClassicControl.Balance, 190, false), 6);
        Assert.Equal(-1.0 / 12, ClassicLayout.SliderValue(ClassicControl.Balance, 188, false), 6);
    }

    [Fact]
    public void Odd_values_draw_the_thumb_at_the_start()
    {
        Assert.Equal(107, ClassicLayout.SliderThumb(ClassicControl.Volume, double.NaN, false).Left);
        Assert.Equal(189, ClassicLayout.SliderThumb(ClassicControl.Balance, double.PositiveInfinity, false).Left);
        Assert.Equal(16, ClassicLayout.SliderThumb(ClassicControl.Seek, -3, false).Left);
    }

    [Theory]
    [InlineData(ClassicControl.Play)]
    [InlineData(ClassicControl.None)]
    [InlineData(ClassicControl.Marquee)]
    public void Only_sliders_have_values(ClassicControl control)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLayout.SliderValue(control, 100, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLayout.SliderThumb(control, 0.5, false));
    }
}
