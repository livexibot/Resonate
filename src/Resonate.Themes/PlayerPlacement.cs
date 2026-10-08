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
/// Where the player's slot sits in the window's shell grid, whose middle row
/// holds the panels, with a row above and one below them (each as tall as
/// the player, or empty) for a player on top or underneath. A player that
/// hovers over the page sits in the panels' row.
/// </summary>
/// <param name="Row">
/// <see cref="PlayerPlacement.TopRow"/>, <see cref="PlayerPlacement.PanelsRow"/>
/// (hovering over the page) or <see cref="PlayerPlacement.BottomRow"/>.
/// </param>
/// <param name="StartsAtContent">The slot starts at the page's column instead of the window's left edge.</param>
/// <param name="SpansFollowingColumns">The slot also covers every column after its first (the queue pane's).</param>
/// <param name="AlignBottom">The slot is only as tall as the player and sits at the bottom of its row.</param>
/// <param name="Margin">The slot's margin; negative values reach into the shell's padding, to the window's edges.</param>
/// <param name="SidebarRow">The sidebar's first row: the top row when it reaches the top beside a player there.</param>
/// <param name="SidebarRowSpan">2 when the sidebar runs to the window's edge beside the player.</param>
public readonly record struct PlayerSlot(
    int Row,
    bool StartsAtContent,
    bool SpansFollowingColumns,
    bool AlignBottom,
    EdgeInsets Margin,
    int SidebarRow,
    int SidebarRowSpan);

/// <summary>
/// The player's shape and place in each layout, worked out from the look and
/// the user's sidebar switch, so the window, the theme tokens and the tests
/// all use the same numbers. The shell grid has padding of the panel gap on
/// the left, right and bottom, and the same gap between its columns.
/// </summary>
public static class PlayerPlacement
{
    /// <summary>The shell's row above the panels, for a player along the top.</summary>
    public const int TopRow = 0;

    /// <summary>The shell's row of the panels, where a hovering player sits over the page.</summary>
    public const int PanelsRow = 1;

    /// <summary>The shell's row under the panels.</summary>
    public const int BottomRow = 2;

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

    /// <summary>
    /// The narrowest a hovering player bar gets: the mini bar's cover, its
    /// previous, play and next over the progress bar, the queue button and
    /// the start of the song's name. Over a page narrower than this and its
    /// gaps (Settings or the queue open in a small window), the player sits
    /// under the panels instead (see <see cref="HoveringFits"/>).
    /// </summary>
    public const double HoveringMinWidth = 360;

    /// <summary>
    /// The widest a player in the corner grows: the mini bar (narrower than
    /// <see cref="CompactWidth"/>), so it stays a small pill beside the page's
    /// content.
    /// </summary>
    public const double CornerWidth = 440;

    /// <summary>The room a page leaves under its last row beyond the hovering player itself.</summary>
    public const double HoverClearance = 8;

    /// <summary>The smallest gap around a floating player.</summary>
    private const double FloatingGap = 8;

    /// <summary>The smallest gap around a hovering player: it sits over the page, so it keeps clear of the page's edges.</summary>
    private const double HoveringGap = 12;

    /// <summary>The largest corner of a hovering player whose buttons are not round.</summary>
    private const double HoveringCornerLimit = 44;

    /// <summary>Whether the player sits along the top of the window, under the title bar.</summary>
    public static bool IsAtTop(PlayerLayout layout) => layout is PlayerLayout.Top or PlayerLayout.FloatingTop;

    /// <summary>Whether the player hovers over the page (in the middle or in its corner), which scrolls on under it.</summary>
    public static bool HoversOverPage(PlayerLayout layout) => layout is PlayerLayout.Hovering or PlayerLayout.Corner;

    /// <summary>Whether the player is a card of its own (corners, outline all round and the look's shadow) rather than a bar along an edge.</summary>
    public static bool Floats(PlayerLayout layout) => layout is not (PlayerLayout.Docked or PlayerLayout.Top);

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

            // Right under the title bar, where the panels start otherwise; the slot keeps the gap below it.
            PlayerLayout.FloatingTop => new EdgeInsets(Math.Max(gap, FloatingGap), 0, Math.Max(gap, FloatingGap), 0),
            PlayerLayout.Hovering or PlayerLayout.Corner => Around(Math.Max(gap, HoveringGap)),
            _ => EdgeInsets.Zero,
        };

        static EdgeInsets Around(double space) => new(space, 0, space, space);
    }

    /// <summary>
    /// The player's outline: a line along the edge that faces the panels when
    /// docked (its top at the bottom, its bottom at the top), the look's
    /// outline all round when floating, and at least a hairline all round when
    /// hovering, so it keeps an edge over the page in looks without shadows.
    /// </summary>
    public static EdgeInsets Outline(PlayerLayout layout, double borderWidth) => layout switch
    {
        PlayerLayout.Floating or PlayerLayout.FloatingTop => EdgeInsets.All(borderWidth),
        PlayerLayout.Hovering or PlayerLayout.Corner => EdgeInsets.All(Math.Max(borderWidth, 1)),
        PlayerLayout.Top => new EdgeInsets(0, 0, 0, borderWidth),
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
        PlayerLayout.Floating or PlayerLayout.FloatingTop => Math.Min(Math.Max(cornerLarge, 4), height / 2),
        PlayerLayout.Hovering or PlayerLayout.Corner when buttons == ButtonShape.Round => height / 2,
        PlayerLayout.Hovering or PlayerLayout.Corner => Math.Min(Math.Min(cornerLarge * 1.5, HoveringCornerLimit), height / 2),
        _ => 0,
    };

    /// <summary>How wide the player may grow: a hovering player stays a centred pill, one in the corner a small one.</summary>
    public static double MaxWidth(PlayerLayout layout) => layout switch
    {
        PlayerLayout.Hovering => HoveringMaxWidth,
        PlayerLayout.Corner => CornerWidth,
        _ => double.PositiveInfinity,
    };

    /// <summary>
    /// Whether a hovering player at least <paramref name="playerWidth"/> wide
    /// fits over a page <paramref name="pageWidth"/> wide, with its gap on
    /// each side. A page not measured yet (0 wide) counts as fitting.
    /// </summary>
    public static bool HoveringFits(double pageWidth, double playerWidth, double panelGap)
    {
        var side = Margin(PlayerLayout.Hovering, panelGap).Left;
        return pageWidth <= 0 || pageWidth + 0.5 >= playerWidth + (2 * side);
    }

    /// <summary>
    /// Where the player's slot goes. Docked and floating players sit in the
    /// row under the panels (or above them, for the layouts along the top),
    /// across the whole window, or (with the sidebar reaching the window's
    /// edge) beside the sidebar, over the page and the queue only; the slot
    /// reaches into the shell's padding so the look's own margins place the
    /// player exactly as before. A hovering player sits over the bottom of
    /// the page's column (in the middle or in its corner) and leaves the rows
    /// above and under the panels empty; when it does not fit there
    /// (<paramref name="hoveringFits"/>), it keeps its shape in the row under
    /// the panels, like a floating player.
    /// </summary>
    public static PlayerSlot Slot(PlayerLayout layout, double panelGap, bool sidebarFullHeight, bool hoveringFits = true)
    {
        var gap = Math.Max(panelGap, 0);
        var sidebarRows = sidebarFullHeight ? 2 : 1;
        if (HoversOverPage(layout) && hoveringFits)
        {
            return new PlayerSlot(PanelsRow, StartsAtContent: true, SpansFollowingColumns: false, AlignBottom: true, EdgeInsets.Zero, PanelsRow, sidebarRows);
        }

        // The shell has no padding at the top (the title bar is there), so a
        // player along the top starts right under it, with the gap below.
        var top = IsAtTop(layout);
        var row = top ? TopRow : BottomRow;
        var (above, below) = top ? (0, gap) : (gap, -gap);
        if (!sidebarFullHeight)
        {
            return new PlayerSlot(row, StartsAtContent: false, SpansFollowingColumns: true, AlignBottom: false, new EdgeInsets(-gap, above, -gap, below), PanelsRow, 1);
        }

        // Beside the sidebar: the left edge lines up with the page (a floating
        // or hovering player's own margin is taken back), the rest reaches the
        // window's edges.
        var left = Floats(layout) ? -Margin(layout, gap).Left : 0;
        return new PlayerSlot(row, StartsAtContent: true, SpansFollowingColumns: true, AlignBottom: false, new EdgeInsets(left, above, -gap, below), top ? TopRow : PanelsRow, 2);
    }

    /// <summary>
    /// How much room a page leaves under its last row so it can scroll clear
    /// of a hovering player <paramref name="slotHeight"/> tall (none for the
    /// other layouts, which never cover the page).
    /// </summary>
    public static double PageInset(PlayerLayout layout, double slotHeight) =>
        HoversOverPage(layout) && slotHeight > 0 ? Math.Ceiling(slotHeight) + HoverClearance : 0;
}
