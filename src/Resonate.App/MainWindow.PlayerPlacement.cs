using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Controls;
using Resonate.App.Pages;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App;

/// <summary>
/// Where the player sits: under the panels or above them across the window,
/// under or above the page beside a sidebar that runs the window's full
/// height, or hovering over the bottom of the page, as a centred pill or in
/// its corner (under the panels instead while the page is too narrow for it,
/// as with Settings open in a small window). The player's slot never leaves
/// the shell grid; only its row, columns, alignment and margin change, so
/// the classic player is never taken out of the window (which would rebuild
/// it). The numbers come from <see cref="PlayerPlacement"/>. Pages leave
/// room under their last row for a hovering player (see <see cref="IPlayerInset"/>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A span past the last column: the slot covers the queue's column while
    /// it is open, and any column added after the page's (the grid clamps it).
    /// </summary>
    private const int ToTheLastColumn = 99;

    private (PlayerLayout Layout, double Gap, bool FullHeight, bool Hovers, double? Width, double? Height, double? X, double? Y)? _placement;

    /// <summary>The cover and the song above the player while it is a column beside the page.</summary>
    private NowPlayingColumn? _sideColumn;
    private double _playerInset = -1;

    /// <summary>Called once, from the constructor.</summary>
    private void SetUpPlayerPlacement()
    {
        // Changed is raised for every colour of a slide too; the placement only follows layout, gaps and the switch.
        _services.Theme.Changed += (_, _) => ApplyPlayerPlacement();
        ContentFrame.Navigated += (_, _) => UpdatePlayerInset(newPage: true);

        // A pane opening beside a narrow page can leave a hovering player too
        // little room there; it moves under the panels until there is room.
        ContentPanel.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width)
            {
                ApplyPlayerPlacement();
            }
        };
    }

    /// <summary>The narrowest the player in use gets, which a hovering player needs over the page.</summary>
    private double NarrowestPlayerWidth => _classicPlayer?.NarrowestWidth ?? PlayerPlacement.HoveringMinWidth;

    /// <summary>
    /// Puts the player's slot where the look and the sidebar switch say (and
    /// the page's width allows, for a hovering player), when any of it changed.
    /// </summary>
    private void ApplyPlayerPlacement()
    {
        var theme = _services.Theme;
        var look = theme.Current;
        var layout = look.PlayerLayout;
        var gap = theme.Palette.PanelGap;

        // A window shape without the page (Window shapes plugin) has the player under everything.
        if (ShapeHidesPanels && (PlayerPlacement.IsAtTop(layout) || PlayerPlacement.IsSide(layout)))
        {
            layout = layout is PlayerLayout.Top or PlayerLayout.Left or PlayerLayout.Right ? PlayerLayout.Docked : PlayerLayout.Floating;
        }

        var hovers = PlayerPlacement.HoversOverPage(layout)
            && !ShapeHidesPanels
            && PlayerPlacement.HoveringFits(ContentPanel.ActualWidth, NarrowestPlayerWidth, gap);
        var fullHeight = theme.SidebarFullHeight && !ShapeHidesPanels;
        var placement = (layout, gap, fullHeight, hovers, look.PlayerWidth, look.PlayerHeight, look.PlayerOffsetX, look.PlayerOffsetY);
        if (_placement == placement)
        {
            return;
        }

        _placement = placement;

        // Settings, Layout, Advanced: moved by the user's own X and Y, wherever it sits.
        PlayerSlot.RenderTransform = look.PlayerOffsetX is null && look.PlayerOffsetY is null
            ? null
            : new Microsoft.UI.Xaml.Media.TranslateTransform { X = look.PlayerOffsetX ?? 0, Y = look.PlayerOffsetY ?? 0 };

        if (PlayerPlacement.IsSide(layout))
        {
            PlaceBesidePage(layout, gap, look.PlayerWidth);
            UpdatePlayerInset();
            return;
        }

        LeaveSide();
        PlayerSlot.Width = look.PlayerWidth ?? double.NaN;
        PlayerSlot.HorizontalAlignment = look.PlayerWidth is null ? HorizontalAlignment.Stretch
            : layout == PlayerLayout.Corner ? HorizontalAlignment.Right
            : layout == PlayerLayout.CornerLeft ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        var slot = PlayerPlacement.Slot(layout, gap, fullHeight, hoveringFits: hovers);
        foreach (var element in new FrameworkElement[] { Sidebar, SidebarElevation, SidebarSplitter })
        {
            Grid.SetRow(element, slot.SidebarRow);
            Grid.SetRowSpan(element, slot.SidebarRowSpan);
        }

        // The page's column, wherever it is (columns may be added before it).
        var page = Grid.GetColumn(ContentPanel);
        Grid.SetRow(PlayerSlot, slot.Row);
        Grid.SetColumn(PlayerSlot, slot.StartsAtContent ? page : 0);
        Grid.SetColumnSpan(PlayerSlot, slot.SpansFollowingColumns ? ToTheLastColumn : 1);
        PlayerSlot.VerticalAlignment = !slot.AlignBottom ? VerticalAlignment.Stretch
            : hovers && PlayerPlacement.HoversAtTop(layout) ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        PlayerSlot.Margin = slot.Margin.ToThickness();
        UpdatePlayerInset();
    }

    /// <summary>
    /// The player as a column on the left or right of the page: the cover and
    /// the song as large as the column allows, the player bar under them. The
    /// page and its shadow make room for it.
    /// </summary>
    private void PlaceBesidePage(PlayerLayout layout, double gap, double? width)
    {
        foreach (var element in new FrameworkElement[] { Sidebar, SidebarElevation, SidebarSplitter })
        {
            Grid.SetRow(element, PlayerPlacement.PanelsRow);
            Grid.SetRowSpan(element, 1);
        }

        var left = PlayerPlacement.IsLeftSide(layout);
        var columnWidth = width ?? PlayerPlacement.SideWidth;
        var inset = layout is PlayerLayout.InsetLeft or PlayerLayout.InsetRight ? Math.Max(gap, 8) : 0;
        Grid.SetRow(PlayerSlot, PlayerPlacement.PanelsRow);
        Grid.SetColumn(PlayerSlot, Grid.GetColumn(ContentPanel));
        Grid.SetColumnSpan(PlayerSlot, 1);
        PlayerSlot.Width = columnWidth;
        PlayerSlot.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PlayerSlot.VerticalAlignment = VerticalAlignment.Stretch;
        PlayerSlot.Margin = new Thickness(inset);
        PlayerSlot.RowSpacing = gap;

        var room = columnWidth + gap + (2 * inset);
        var pageMargin = left ? new Thickness(room, 0, 0, 0) : new Thickness(0, 0, room, 0);
        ContentPanel.Margin = pageMargin;
        ContentElevation.Margin = pageMargin;

        if (_sideColumn is null)
        {
            _sideColumn = new NowPlayingColumn(_services);
            PlayerSlot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            PlayerSlot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var child in PlayerSlot.Children.OfType<FrameworkElement>())
            {
                Grid.SetRow(child, 1);
            }

            PlayerSlot.Children.Insert(0, _sideColumn);
        }
    }

    /// <summary>Back from a column beside the page: the page has its column to itself again.</summary>
    private void LeaveSide()
    {
        ContentPanel.Margin = new Thickness(0);
        ContentElevation.Margin = new Thickness(0);
        PlayerSlot.RowSpacing = 0;
        if (_sideColumn is null)
        {
            return;
        }

        PlayerSlot.Children.Remove(_sideColumn);
        _sideColumn = null;
        PlayerSlot.RowDefinitions.Clear();
        foreach (var child in PlayerSlot.Children.OfType<FrameworkElement>())
        {
            Grid.SetRow(child, 0);
        }
    }

    private void OnPlayerSlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The classic player knows how narrow it gets once it has its display's scale.
        if (_classicPlayer is not null && e.NewSize.Width != e.PreviousSize.Width)
        {
            ApplyPlayerPlacement();
        }

        UpdatePlayerInset();
    }

    /// <summary>
    /// Tells the page on show how much room to leave under its last row, and
    /// lifts the update bar by as much: as much as a hovering player covers,
    /// none otherwise. A new page always hears it; others only when it changes
    /// (the slot's size changes with every resize of the window).
    /// </summary>
    private void UpdatePlayerInset(bool newPage = false)
    {
        var inset = _placement is { Hovers: true } placement ? PlayerPlacement.PageInset(placement.Layout, PlayerSlot.ActualHeight) : 0;
        if (!newPage && Math.Abs(inset - _playerInset) < 0.5)
        {
            return;
        }

        _playerInset = inset;

        // The update bar in the page's corner stays above a hovering player too.
        UpdateBarHost.Margin = new Thickness(16, 16, 16, 16 + inset);
        if (ContentFrame.Content is IPlayerInset page)
        {
            page.SetPlayerInset(inset);
        }
    }

    /// <summary>Whether the player hovers over the page now (for the screenshot tour).</summary>
    internal bool PlayerHovers => _placement?.Hovers == true;

    /// <summary>
    /// For the screenshot tour. A hovering player stays over the page (never
    /// over the sidebar or the queue) with room for all of it, and the page
    /// on show, scrolled to its end, keeps its last row above it. A player
    /// under or above the panels lies wholly in the window, clear of the
    /// page. Null when all is well.
    /// </summary>
    internal string? CheckPlayerPlacement()
    {
        var player = BoundsInShell(PlayerSlot);
        var page = BoundsInShell(ContentPanel);
        if (!PlayerHovers)
        {
            var shell = BoundsInShell(ShellGrid);
            var onTop = _placement is { } placement && PlayerPlacement.IsAtTop(placement.Layout);
            var clear = onTop ? player.Bottom <= page.Top + 0.5 : player.Top >= page.Bottom - 0.5;
            return !clear || player.Left < shell.Left - 0.5 || player.Right > shell.Right + 0.5 || player.Top < shell.Top - 0.5 || player.Bottom > shell.Bottom + 0.5
                ? $"The player ({player}) is not {(onTop ? "above" : "under")} the page ({page}) inside the window ({shell})."
                : null;
        }

        if (player.Left < page.Left - 0.5 || player.Right > page.Right + 0.5 || player.Bottom > page.Bottom + 0.5)
        {
            return $"The hovering player ({player}) reaches outside the page ({page}).";
        }

        if (!PlayerPlacement.HoveringFits(player.Width, NarrowestPlayerWidth, _services.Theme.Palette.PanelGap))
        {
            return $"The hovering player has {player.Width:0} of the {NarrowestPlayerWidth:0} it needs, and its gaps.";
        }

        if (ContentFrame.Content is not IPlayerInset inset)
        {
            return "The page on show leaves no room for the hovering player.";
        }

        var room = BoundsInShell(inset.PlayerSpacer);
        return room.Top > player.Top + 0.5
            ? $"The page's last row ends at {room.Top:0}, under the hovering player's top at {player.Top:0}."
            : null;
    }

    // In the shell's own units, which App size does not change (see ContentScale).
    private Rect BoundsInShell(FrameworkElement element) =>
        element.TransformToVisual(ShellGrid).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
}
