namespace Resonate.Themes;

/// <summary>
/// How the now-playing stage (the top of Home, and the away screen) places
/// its cover and words: side by side whenever the stage is wider than tall,
/// or the cover above the words on a narrow, tall stage. The words are
/// measured first and the cover takes only the room they leave, so the play
/// and skip buttons are never cut and the visualizer keeps the room under
/// them. Sizes are in the stage's own units, inside its padding.
/// </summary>
public static class StageLayout
{
    /// <summary>The smallest the cover is drawn.</summary>
    public const double MinCover = 96;

    /// <summary>The largest the cover is drawn above the words.</summary>
    public const double StackedCoverMax = 240;

    /// <summary>The space between the cover and the words under it.</summary>
    public const double StackedGap = 28;

    /// <summary>From this width the cover always sits beside the words.</summary>
    public const double WideWidth = 600;

    /// <summary>A narrower stage keeps the cover beside the words from this width while it is at least <see cref="LandscapeRatio"/> times as wide as tall.</summary>
    public const double LandscapeWidth = 480;

    public const double LandscapeRatio = 1.2;

    /// <summary>The cover's share of the width beside the words (its visualizers reach 40 px out, short of the words 48 px away).</summary>
    public const double SideCoverShare = 0.42;

    /// <summary>Whether the cover sits beside the words on a stage this large inside its padding.</summary>
    public static bool SideBySide(double width, double height) =>
        width >= WideWidth || (width >= LandscapeWidth && width >= height * LandscapeRatio);

    /// <summary>The cover beside the words: as tall as the stage allows, at most <see cref="SideCoverShare"/> of its width and <paramref name="max"/>.</summary>
    public static double SideCover(double width, double height, double max) =>
        Math.Max(MinCover, Math.Floor(Math.Min(max, Math.Min(height, width * SideCoverShare))));

    /// <summary>
    /// The cover above words that need <paramref name="words"/> of the
    /// height: what they leave, up to <see cref="StackedCoverMax"/> and the
    /// stage's width. Null when even <see cref="MinCover"/> would not leave
    /// them room; the cover then goes beside them at that size.
    /// </summary>
    public static double? StackedCover(double width, double height, double words)
    {
        var room = Math.Floor(height - words - StackedGap);
        if (room < MinCover)
        {
            return null;
        }

        return Math.Min(room, Math.Max(MinCover, Math.Min(StackedCoverMax, Math.Floor(width))));
    }

    /// <summary>
    /// The song's title size at the usual Text size: the compact size above
    /// the words or beside the smallest cover, larger beside a large cover.
    /// The away screen's are larger.
    /// </summary>
    public static double TitleSize(bool away, bool sideBySide, double cover)
    {
        var compact = !sideBySide || cover <= MinCover;
        if (away)
        {
            return compact ? 44 : 64;
        }

        return compact ? 32 : cover >= 360 ? 52 : 40;
    }
}
