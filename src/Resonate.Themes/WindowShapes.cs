namespace Resonate.Themes;

/// <summary>How the window arranges itself with the Window shapes plugin on (see <see cref="WindowShapes"/>).</summary>
public enum WindowShape
{
    /// <summary>The sidebar, the page and the queue: the usual window.</summary>
    Full,

    /// <summary>The sidebar folds into a rail of icons and playlist covers; the page and the player stay.</summary>
    Compact,

    /// <summary>Tall and narrow (snapped to a third or a quarter of the screen): a big cover over the player.</summary>
    Column,

    /// <summary>Short: one line with the player, which can stay above other windows.</summary>
    Strip,
}

/// <summary>
/// Which shape the window takes at a size, and the size each shape takes when
/// it is picked from the menu. Sizes are the inside of the window (without
/// its frame) in device-independent pixels.
/// </summary>
public static class WindowShapes
{
    /// <summary>Shorter than this, the window is a strip.</summary>
    public const double StripMaxHeight = 200;

    /// <summary>Narrower than this, the window is a column.</summary>
    public const double ColumnMaxWidth = 640;

    /// <summary>Up to this wide, a window clearly taller than wide is a column too (a third of a wide screen).</summary>
    public const double TallColumnMaxWidth = 1280;

    /// <summary>A window this many times taller than wide is tall.</summary>
    public const double TallRatio = 1.2;

    /// <summary>Narrower than this, the sidebar folds into a rail.</summary>
    public const double CompactMaxWidth = 1100;

    /// <summary>The smallest window while the plugin is on: room for the mini player bar.</summary>
    public const double MinimumWidth = 360;

    /// <summary>The shortest window while the plugin is on; a strip then grows to fit its player.</summary>
    public const double MinimumHeight = 64;

    /// <summary>The rail that takes the sidebar's place in <see cref="WindowShape.Compact"/>.</summary>
    public const double RailWidth = 76;

    /// <summary>The usual window, for <see cref="WindowShape.Full"/> picked before any full size is known.</summary>
    public static (double Width, double Height) DefaultFullSize { get; } = (1280, 820);

    public static WindowShape For(double width, double height)
    {
        if (height < StripMaxHeight)
        {
            return WindowShape.Strip;
        }

        if (width < ColumnMaxWidth || (width < TallColumnMaxWidth && height > width * TallRatio))
        {
            return WindowShape.Column;
        }

        return width < CompactMaxWidth ? WindowShape.Compact : WindowShape.Full;
    }

    /// <summary>
    /// The size the window takes when <paramref name="shape"/> is picked from
    /// the menu, within <paramref name="screen"/> (the screen's work area). A
    /// strip's height is a start: the window then fits its player.
    /// </summary>
    /// <param name="fullSize">The last size the window had in <see cref="WindowShape.Full"/>, if any.</param>
    public static (double Width, double Height) PickedSize(
        WindowShape shape,
        (double Width, double Height) screen,
        (double Width, double Height)? fullSize = null)
    {
        var (width, height) = shape switch
        {
            WindowShape.Compact => (980, 720),
            WindowShape.Column => (420, 780),
            WindowShape.Strip => (760, 120),
            _ => fullSize ?? DefaultFullSize,
        };

        return (Math.Min(width, screen.Width), Math.Min(height, screen.Height));
    }
}
