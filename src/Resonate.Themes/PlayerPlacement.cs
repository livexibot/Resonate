namespace Resonate.Themes;

/// <summary>How much of the player bar fits the width it has.</summary>
public enum PlayerWidthClass
{
    /// <summary>Everything: the song, every control and the volume slider.</summary>
    Full,

    /// <summary>Tighter, without the volume slider (the mouse wheel on the speaker button sets the volume).</summary>
    Compact,

    /// <summary>A shorter bar: the song, previous, play and next over the progress bar, and the queue.</summary>
    Mini,
}

/// <summary>Space on each side, in device-independent pixels (what WinUI calls a Thickness).</summary>
public readonly record struct EdgeInsets(double Left, double Top, double Right, double Bottom)
{
    public static EdgeInsets Zero { get; } = new(0, 0, 0, 0);

    public static EdgeInsets All(double value) => new(value, value, value, value);
}

/// <summary>
/// Where the player's slot sits in the window's shell grid, whose first row
/// holds the panels and whose second row (as tall as the player) holds the
/// player unless it hovers over the page.
/// </summary>
/// <param name="Row">0 to hover over the panels, 1 for the row under them.</param>
/// <param name="StartsAtContent">The slot starts at the page's column instead of the window's left edge.</param>
/// <param name="SpansFollowingColumns">The slot also covers every column after its first (the queue pane's).</param>
/// <param name="AlignBottom">The slot is only as tall as the player and sits at the bottom of its row.</param>
/// <param name="Margin">The slot's margin; negative values reach into the shell's padding, to the window's edges.</param>
/// <param name="SidebarRowSpan">2 when the sidebar runs to the bottom of the window, beside the player.</param>
public readonly record struct PlayerSlot(
    int Row,
    bool StartsAtContent,
    bool SpansFollowingColumns,
    bool AlignBottom,
    EdgeInsets Margin,
    int SidebarRowSpan);

/// <summary>
/// The player's shape and place in each layout, worked out from the look and
/// the user's sidebar switch, so the window, the theme tokens and the tests
/// all use the same numbers. The shell grid has padding of the panel gap on
/// the left, right and bottom, and the same gap between its columns.
/// </summary>
public static class PlayerPlacement
{
    /// <summary>The player bar's height (Full and Compact).</summary>
    public const double BarHeight = 88;

    /// <summary>The Mini player's height: the play button over the progress bar, with a little air.</summary>
    public const double MiniHeight = 72;

    /// <summary>
    /// The widest a hovering player grows: a centred pill on a wide page, as
    /// wide as the full bar needs (see <see cref="FullWidth"/>), so it never
    /// stretches into a strip.
    /// </summary>
    public const double HoveringMaxWidth = 912;

    /// <summary>
    /// The full bar needs this much: 20 padding and 24 spacing on each side,
    /// the controls' 320, 220 for the song, and 272 for the plugin, device,
    /// queue and speaker buttons with the volume slider, plus a little room
    /// for a hovering player's outline.
    /// </summary>
    public const double FullWidth = 912;

    /// <summary>
    /// The compact bar needs this much: 16 padding and 16 spacing on each
    /// side, the controls' 216 (tighter) and 150 on each side.
    /// </summary>
    public const double CompactWidth = 600;

    /// <summary>The room a page leaves under its last row beyond the hovering player itself.</summary>
    public const double HoverClearance = 8;

    /// <summary>The smallest gap around a floating player.</summary>
    private const double FloatingGap = 8;

    /// <summary>The smallest gap around a hovering player: it sits over the page, so it keeps clear of the page's edges.</summary>
    private const double HoveringGap = 12;

    /// <summary>The largest corner of a hovering player whose buttons are not round.</summary>
    private const double HoveringCornerLimit = 44;

    /// <summary>
    /// Which version of the player bar fits <paramref name="width"/> (its
    /// outer width). Half a pixel of slack keeps layout rounding from
    /// tipping a bar that is exactly wide enough into the smaller version.
    /// </summary>
    public static PlayerWidthClass WidthClassFor(double width) =>
        width >= FullWidth - 0.5 ? PlayerWidthClass.Full
        : width >= CompactWidth - 0.5 ? PlayerWidthClass.Compact
        : PlayerWidthClass.Mini;

    /// <summary>The bar's height for a width class.</summary>
    public static double HeightFor(PlayerWidthClass widthClass) =>
        widthClass == PlayerWidthClass.Mini ? MiniHeight : BarHeight;

    /// <summary>The space around the player inside its slot (the ResonatePlayerMargin token).</summary>
    public static EdgeInsets Margin(PlayerLayout layout, double panelGap)
    {
        var gap = Math.Max(panelGap, 0);
        return layout switch
        {
            PlayerLayout.Floating => Around(Math.Max(gap, FloatingGap)),
            PlayerLayout.Hovering => Around(Math.Max(gap, HoveringGap)),
            _ => EdgeInsets.Zero,
        };

        static EdgeInsets Around(double space) => new(space, 0, space, space);
    }

    /// <summary>
    /// The player's outline: a line along the top when docked, the look's
    /// outline all round when floating, and at least a hairline all round when
    /// hovering, so it keeps an edge over the page in looks without shadows.
    /// </summary>
    public static EdgeInsets Outline(PlayerLayout layout, double borderWidth) => layout switch
    {
        PlayerLayout.Floating => EdgeInsets.All(borderWidth),
        PlayerLayout.Hovering => EdgeInsets.All(Math.Max(borderWidth, 1)),
        _ => new EdgeInsets(0, borderWidth, 0, 0),
    };

    /// <summary>
    /// The player's corner radius at <paramref name="height"/>: square when
    /// docked, the panels' corner when floating, and a pill when hovering in
    /// a look with round buttons (otherwise a bit rounder than the panels).
    /// Never more than half the height, which would draw an oval.
    /// </summary>
    public static double Corner(PlayerLayout layout, ButtonShape buttons, double cornerLarge, double height) => layout switch
    {
        PlayerLayout.Floating => Math.Min(Math.Max(cornerLarge, 4), height / 2),
        PlayerLayout.Hovering when buttons == ButtonShape.Round => height / 2,
        PlayerLayout.Hovering => Math.Min(Math.Min(cornerLarge * 1.5, HoveringCornerLimit), height / 2),
        _ => 0,
    };

    /// <summary>How wide the player may grow: a hovering player stays a centred pill.</summary>
    public static double MaxWidth(PlayerLayout layout) =>
        layout == PlayerLayout.Hovering ? HoveringMaxWidth : double.PositiveInfinity;

    /// <summary>
    /// Where the player's slot goes. Docked and floating players sit in the
    /// row under the panels, across the whole window, or (with the sidebar
    /// reaching the bottom) under the page and the queue only; the slot
    /// reaches into the shell's padding so the look's own margins place the
    /// player exactly as before. A hovering player sits over the bottom of
    /// the page's column and leaves the row under the panels empty.
    /// </summary>
    public static PlayerSlot Slot(PlayerLayout layout, double panelGap, bool sidebarFullHeight)
    {
        var gap = Math.Max(panelGap, 0);
        var sidebarRows = sidebarFullHeight ? 2 : 1;
        if (layout == PlayerLayout.Hovering)
        {
            return new PlayerSlot(0, StartsAtContent: true, SpansFollowingColumns: false, AlignBottom: true, EdgeInsets.Zero, sidebarRows);
        }

        if (!sidebarFullHeight)
        {
            return new PlayerSlot(1, StartsAtContent: false, SpansFollowingColumns: true, AlignBottom: false, new EdgeInsets(-gap, gap, -gap, -gap), sidebarRows);
        }

        // Beside the sidebar: the left edge lines up with the page (a floating
        // player's own margin is taken back), the rest reaches the window's edges.
        var left = layout == PlayerLayout.Floating ? -Margin(layout, gap).Left : 0;
        return new PlayerSlot(1, StartsAtContent: true, SpansFollowingColumns: true, AlignBottom: false, new EdgeInsets(left, gap, -gap, -gap), sidebarRows);
    }

    /// <summary>
    /// How much room a page leaves under its last row so it can scroll clear
    /// of a hovering player <paramref name="slotHeight"/> tall (none for the
    /// other layouts, which never cover the page).
    /// </summary>
    public static double PageInset(PlayerLayout layout, double slotHeight) =>
        layout == PlayerLayout.Hovering && slotHeight > 0 ? Math.Ceiling(slotHeight) + HoverClearance : 0;
}
