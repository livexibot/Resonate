namespace Resonate.Themes;

/// <summary>
/// How the screensaver's parts wander in OLED mode (the owner asked for it
/// "mostly black" with "moving elements", 10 October 2026), so nothing
/// stays on one spot of the screen: slow, smooth paths of a few pixels a
/// second, each a figure of two sines whose periods differ, so the path
/// never quite repeats within a minute and nothing jumps. Offsets are in
/// the screen's units, from where each part is laid out: the song's card
/// sits at the bottom left and wanders right and up, the clock at the top
/// left and wanders right and down, and the glow behind them anywhere.
/// </summary>
public static class ScreensaverDrift
{
    /// <summary>How far the card and the clock wander, as shares of the screen's width and height.</summary>
    public const double ReachX = 0.22;

    public const double ReachY = 0.12;

    /// <summary>The card's offset after <paramref name="seconds"/>: right of its place and up from it.</summary>
    public static (double X, double Y) Card(double seconds, double width, double height) =>
        (Half(seconds, 431) * ReachX * width, -Half(seconds, 293) * ReachY * height);

    /// <summary>The clock's offset: right of its place and down from it, out of step with the card.</summary>
    public static (double X, double Y) Clock(double seconds, double width, double height) =>
        (Half(seconds + 157, 557) * ReachX * width, Half(seconds + 71, 367) * ReachY * height);

    /// <summary>The glow's centre, as shares of the screen (0 to 1), drifting over most of it.</summary>
    public static (double X, double Y) Glow(double seconds) =>
        (0.15 + (0.7 * Half(seconds, 263)), 0.2 + (0.6 * Half(seconds + 40, 197)));

    /// <summary>0 to 1 and back once every <paramref name="period"/> seconds, smoothly (a raised cosine).</summary>
    private static double Half(double seconds, double period) =>
        0.5 - (0.5 * Math.Cos(2 * Math.PI * seconds / period));
}
