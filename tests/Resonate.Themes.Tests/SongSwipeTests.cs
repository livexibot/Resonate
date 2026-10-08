namespace Resonate.Themes.Tests;

public sealed class SongSwipeTests
{
    // The player bar's song area at full width: the skip distance is 120.
    private const double Width = 360;

    [Fact]
    public void A_press_that_barely_moves_stays_a_click()
    {
        Assert.False(SongSwipe.Starts(5, 0));
        Assert.False(SongSwipe.IsVertical(5, 0));
        Assert.True(SongSwipe.Starts(-9, 3));
    }

    [Fact]
    public void Moving_up_or_down_is_not_a_swipe()
    {
        Assert.False(SongSwipe.Starts(9, 12));
        Assert.True(SongSwipe.IsVertical(9, 12));
    }

    [Theory]
    [InlineData(90, 48)]
    [InlineData(360, 120)]
    [InlineData(900, 140)]
    public void The_skip_distance_is_a_third_of_the_area_within_limits(double width, double expected) =>
        Assert.Equal(expected, SongSwipe.SkipDistance(width), 6);

    [Fact]
    public void The_song_follows_the_pointer_up_to_the_skip_distance_then_is_held_back()
    {
        Assert.Equal(-60, SongSwipe.Offset(-60, Width), 6);
        Assert.Equal(120, SongSwipe.Offset(120, Width), 6);

        var past = SongSwipe.Offset(240, Width);
        Assert.InRange(past, 120.0, 240.0 - 1);
        Assert.True(SongSwipe.Offset(10_000, Width) < 240);
        Assert.Equal(-SongSwipe.Offset(300, Width), SongSwipe.Offset(-300, Width), 6);
    }

    [Fact]
    public void The_song_fades_as_it_goes_but_stays_visible()
    {
        Assert.Equal(1, SongSwipe.Opacity(0, Width), 6);
        Assert.Equal(0.5, SongSwipe.Opacity(-120, Width), 6);
        Assert.Equal(SongSwipe.FaintestOpacity, SongSwipe.Opacity(239, Width), 6);
    }

    [Fact]
    public void Letting_go_far_enough_skips_that_way()
    {
        Assert.Equal(SwipeOutcome.Next, SongSwipe.Decide(-130, 0, Width));
        Assert.Equal(SwipeOutcome.Previous, SongSwipe.Decide(130, 0, Width));
    }

    [Fact]
    public void Letting_go_short_springs_back()
    {
        Assert.Equal(SwipeOutcome.None, SongSwipe.Decide(-100, 0, Width));
        Assert.Equal(SwipeOutcome.None, SongSwipe.Decide(0, 0, Width));
    }

    [Fact]
    public void A_short_flick_skips_but_a_twitch_does_not()
    {
        Assert.Equal(SwipeOutcome.Next, SongSwipe.Decide(-40, -0.8, Width));
        Assert.Equal(SwipeOutcome.Previous, SongSwipe.Decide(40, 0.8, Width));
        Assert.Equal(SwipeOutcome.None, SongSwipe.Decide(-12, -2, Width));
    }

    [Fact]
    public void Moving_fast_back_towards_the_start_changes_the_users_mind() =>
        Assert.Equal(SwipeOutcome.None, SongSwipe.Decide(-200, 0.9, Width));

    [Fact]
    public void Speed_follows_the_pointer()
    {
        var speed = new SwipeSpeed();
        speed.Reset(0, 1_000_000);
        for (var i = 1; i <= 5; i++)
        {
            // 16 pixels to the left every 16 ms.
            speed.Add(-16 * i, 1_000_000 + ((ulong)i * 16_000));
        }

        Assert.InRange(speed.At(1_080_000), -1.02, -0.95);
    }

    [Fact]
    public void Letting_go_after_holding_still_is_not_a_flick()
    {
        var speed = new SwipeSpeed();
        speed.Reset(0, 1_000_000);
        speed.Add(-40, 1_016_000);
        Assert.True(speed.At(1_020_000) < -1);
        Assert.Equal(0, speed.At(1_200_000));
    }

    [Fact]
    public void Two_reports_at_the_same_moment_do_not_break_the_speed()
    {
        var speed = new SwipeSpeed();
        speed.Reset(0, 1_000_000);
        speed.Add(-10, 1_000_000);
        speed.Add(-26, 1_016_000);

        // One move of 16 pixels in 16 ms, smoothed from standing still.
        Assert.Equal(-0.6, speed.At(1_016_000), 6);
    }
}
