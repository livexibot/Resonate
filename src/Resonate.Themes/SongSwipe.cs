namespace Resonate.Themes;

/// <summary>What letting go of a swipe on the playing song does.</summary>
public enum SwipeOutcome
{
    /// <summary>Not far or fast enough: the song springs back.</summary>
    None,

    /// <summary>Swiped to the left: the next song.</summary>
    Next,

    /// <summary>Swiped to the right: the previous song (or this one from its start, as the Previous button does).</summary>
    Previous,
}

/// <summary>
/// The numbers behind swiping the playing song in the player bar to skip,
/// as on Spotify's phone app: the song follows the pointer and fades, and
/// letting go far enough, or flicking it, skips. Distances are in
/// device-independent pixels and speeds in pixels a millisecond.
/// </summary>
public static class SongSwipe
{
    /// <summary>How far the pointer moves sideways before the song follows it; a shorter press is a click.</summary>
    public const double StartDistance = 8;

    /// <summary>A flick at least this fast skips even when it is short.</summary>
    public const double FlickSpeed = 0.5;

    /// <summary>How far a flick must still travel, so a twitch while letting go is not one.</summary>
    public const double FlickDistance = 24;

    /// <summary>The faintest the song gets while it is held away.</summary>
    public const double FaintestOpacity = 0.35;

    /// <summary>How far to drag to skip: a third of the song's area, from 48 to 140 pixels.</summary>
    public static double SkipDistance(double width) => Math.Clamp(width / 3, 48, 140);

    /// <summary>A press that moved this far has become a swipe: far enough, and more sideways than up or down.</summary>
    public static bool Starts(double dx, double dy) => Math.Abs(dx) >= StartDistance && Math.Abs(dx) > Math.Abs(dy);

    /// <summary>A press that moved this far up or down is not a swipe.</summary>
    public static bool IsVertical(double dx, double dy) => Math.Abs(dy) >= StartDistance && Math.Abs(dy) >= Math.Abs(dx);

    /// <summary>
    /// Where the song is drawn for a drag of <paramref name="dx"/>: under
    /// the pointer up to the skip distance, then held back more and more,
    /// never past twice that distance.
    /// </summary>
    public static double Offset(double dx, double width)
    {
        var skip = SkipDistance(width);
        var distance = Math.Abs(dx);
        if (distance <= skip)
        {
            return dx;
        }

        var beyond = distance - skip;
        return Math.Sign(dx) * (skip + (skip * beyond / (beyond + skip)));
    }

    /// <summary>The song's opacity at <paramref name="offset"/>: half at the skip distance, never below <see cref="FaintestOpacity"/>.</summary>
    public static double Opacity(double offset, double width) =>
        Math.Max(FaintestOpacity, 1 - (Math.Abs(offset) / (2 * SkipDistance(width))));

    /// <summary>
    /// What letting go after a drag of <paramref name="dx"/> does, moving at
    /// <paramref name="speed"/> (negative to the left). Far enough skips, and
    /// so does a flick the same way; moving fast back towards the start
    /// changes the user's mind.
    /// </summary>
    public static SwipeOutcome Decide(double dx, double speed, double width)
    {
        var fast = Math.Abs(speed) >= FlickSpeed;
        if (dx == 0 || (fast && Math.Sign(speed) != Math.Sign(dx)))
        {
            return SwipeOutcome.None;
        }

        var far = Math.Abs(dx) >= SkipDistance(width);
        var flick = fast && Math.Abs(dx) >= FlickDistance;
        if (!far && !flick)
        {
            return SwipeOutcome.None;
        }

        return dx < 0 ? SwipeOutcome.Next : SwipeOutcome.Previous;
    }
}

/// <summary>
/// A swipe's speed from the pointer's positions and their times (in
/// microseconds, as Windows stamps them), smoothed over the last few
/// moves. Letting go after holding still is not a flick.
/// </summary>
public sealed class SwipeSpeed
{
    /// <summary>How much the newest move counts against the speed so far.</summary>
    private const double Newest = 0.6;

    /// <summary>A pointer that has not moved for this long (80 ms) is standing still.</summary>
    private const ulong StillAfter = 80_000;

    private double _x;
    private ulong _time;
    private double _speed;

    public void Reset(double x, ulong time)
    {
        _x = x;
        _time = time;
        _speed = 0;
    }

    public void Add(double x, ulong time)
    {
        if (time <= _time)
        {
            // Two reports at once: the later place counts from here on.
            _x = x;
            return;
        }

        var moved = (x - _x) / ((time - _time) / 1000.0);
        _speed = time - _time >= StillAfter ? moved : (Newest * moved) + ((1 - Newest) * _speed);
        _x = x;
        _time = time;
    }

    /// <summary>The speed at <paramref name="time"/>, in pixels a millisecond (negative to the left).</summary>
    public double At(ulong time) => time > _time && time - _time >= StillAfter ? 0 : _speed;
}
