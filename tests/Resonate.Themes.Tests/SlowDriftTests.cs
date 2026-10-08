using System.Numerics;

namespace Resonate.Themes.Tests;

public sealed class SlowDriftTests
{
    [Fact]
    public void The_easing_starts_and_ends_still_and_passes_the_middle()
    {
        Assert.Equal(0, SlowDrift.Ease(0), 9);
        Assert.Equal(1, SlowDrift.Ease(1), 9);
        Assert.Equal(0.5, SlowDrift.Ease(0.5), 6);
        Assert.True(SlowDrift.Ease(0.05) < 0.01);
        Assert.True(SlowDrift.Ease(0.95) > 0.99);
    }

    [Fact]
    public void The_easing_only_grows()
    {
        var before = 0.0;
        for (var x = 0.0; x <= 1; x += 0.001)
        {
            var eased = SlowDrift.Ease(x);
            Assert.True(eased >= before - 1e-9, $"Ease({x}) went back");
            before = eased;
        }
    }

    [Fact]
    public void The_cover_starts_at_rest_zooms_in_and_comes_back()
    {
        var start = SlowDrift.Cover(0);
        Assert.Equal(SlowDrift.CoverRestZoom, start.Zoom, 5);
        Assert.Equal(-SlowDrift.CoverTurn, start.Angle, 5);

        Assert.Equal(SlowDrift.CoverFarZoom, SlowDrift.Cover(SlowDrift.CoverZoomSeconds).Zoom, 5);
        Assert.Equal(SlowDrift.CoverTurn, SlowDrift.Cover(SlowDrift.CoverTurnSeconds).Angle, 5);
        Assert.Equal(SlowDrift.CoverRestZoom, SlowDrift.Cover(2 * SlowDrift.CoverZoomSeconds).Zoom, 5);
    }

    [Fact]
    public void The_cover_never_shows_its_borders()
    {
        for (var seconds = 0.0; seconds < 400; seconds += 0.25)
        {
            var (zoom, angle) = SlowDrift.Cover(seconds);
            Assert.InRange(zoom, SlowDrift.CoverRestZoom - 1e-4f, SlowDrift.CoverFarZoom + 1e-4f);
            Assert.InRange(angle, -SlowDrift.CoverTurn - 1e-4f, SlowDrift.CoverTurn + 1e-4f);
        }
    }

    [Fact]
    public void A_cloud_follows_its_loop_and_ends_where_it_began()
    {
        const double Loop = 47;
        Assert.Equal(Vector2.Zero, SlowDrift.CloudOffset(0, Loop));
        AssertNear(new Vector2(1, -0.6f), SlowDrift.CloudOffset(Loop / 4, Loop));
        AssertNear(new Vector2(0.3f, 1), SlowDrift.CloudOffset(Loop / 2, Loop));
        AssertNear(new Vector2(-1, 0.4f), SlowDrift.CloudOffset(Loop * 3 / 4, Loop));
        AssertNear(Vector2.Zero, SlowDrift.CloudOffset(Loop - 1e-9, Loop));
        AssertNear(SlowDrift.CloudOffset(3, Loop), SlowDrift.CloudOffset(Loop + 3, Loop));
    }

    [Fact]
    public void A_cloud_breathes_in_and_out_once_a_loop()
    {
        const double Breath = 31;
        Assert.Equal(1, SlowDrift.CloudScale(0, Breath), 5);
        Assert.Equal(SlowDrift.CloudBreath, SlowDrift.CloudScale(Breath / 2, Breath), 5);
        Assert.Equal(1, SlowDrift.CloudScale(Breath - 1e-9, Breath), 4);
        Assert.Equal(SlowDrift.CloudScale(7, Breath), SlowDrift.CloudScale(Breath + 7, Breath), 5);
    }

    [Fact]
    public void Between_two_frames_the_cover_moves_about_a_pixel_and_a_cloud_a_few_on_a_5K_display()
    {
        // The widest the owner's window gets, in screen pixels, and the stage's clouds at that width.
        const double Width = 5120;
        const double Height = 2160;
        var frame = 1.0 / SlowDrift.FramesPerSecond;
        var corner = Math.Sqrt((Width * Width / 4) + (Height * Height / 4));
        var mostCover = 0.0;
        var mostCloud = 0.0;
        for (var seconds = 0.0; seconds < 120; seconds += frame)
        {
            var (zoom, angle) = SlowDrift.Cover(seconds);
            var (nextZoom, nextAngle) = SlowDrift.Cover(seconds + frame);
            var zoomMove = corner / SlowDrift.CoverRestZoom * Math.Abs(nextZoom - zoom);
            var turnMove = corner * Math.Abs(nextAngle - angle) * Math.PI / 180;
            mostCover = Math.Max(mostCover, zoomMove + turnMove);

            var reach = new Vector2((float)(Width * 0.1), (float)(Height * 0.1));
            var step = (SlowDrift.CloudOffset(seconds + frame, 47) - SlowDrift.CloudOffset(seconds, 47)) * reach;
            mostCloud = Math.Max(mostCloud, step.Length());
        }

        // The clouds are soft all the way across (no edge to see jump).
        Assert.InRange(mostCover, 0, 1.5);
        Assert.InRange(mostCloud, 0, 4);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}");
}
