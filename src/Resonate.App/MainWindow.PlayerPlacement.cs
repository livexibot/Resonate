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
/// height, hovering over the bottom of the page, as a centred pill or in
/// its corner (under the panels instead while the page is too narrow for it,
/// as with Settings open in a small window), or at the foot of the sidebar.
/// A hovering player set wider than the page reaches over the sidebar and
/// the panes, and no player grows wider than the window. The player's slot never leaves
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

    private (PlayerLayout Layout, double Gap, bool FullHeight, bool Hovers, bool Wide, bool InSidebar, double? Width, double? Height, double? X, double? Y)? _placement;

    /// <summary>The sidebar's padding in MainWindow.xaml; a player at its foot adds to the bottom.</summary>
    private static readonly Thickness SidebarPadding = new(6, 10, 6, 8);

    /// <summary>The room the pane on the right leaves under its content for a wide hovering player over it.</summary>
    private double _paneRoom;

    /// <summary>The cover and the song above the player while it is a column beside the page.</summary>
    private NowPlayingColumn? _sideColumn;

    /// <summary>The column's panel and its shadow, behind the cover, the controls and Up next.</summary>
    private Border? _sideCard;
    private Elevation? _sideShadow;

    /// <summary>What plays next, at the foot of the column beside the page.</summary>
    private SideUpNext? _sideUpNext;

    /// <summary>The grip in the gap between the column beside the page and the page, like the sidebar's.</summary>
    private PaneSplitter? _sideSplitter;
    private double _sideDragStart;
    private bool _sideDragMoved;

    /// <summary>
    /// The narrowest the column beside the page gets with its words: room
    /// for shuffle, previous, play, next and repeat in a row. Dragged
    /// narrower, it snaps to a rail (as the sidebar snaps to its covers).
    /// </summary>
    private const double SideMinWidth = 200;

    /// <summary>The rail: the cover and every control in one line down the middle.</summary>
    private const double SideRailWidth = 96;

    private const double SideMaxWidth = 720;
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
                UpdateCoveredRoom();
            }
        };
        ShellGrid.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width)
            {
                ClampPlayerSlot();
            }
        };

        // Over the panels and the grips between them, so a player reaching over them takes its clicks.
        Canvas.SetZIndex(PlayerSlot, 1);

        // The column beside the page: its panel follows the look, its cover the room above the controls.
        _services.Theme.Changed += (_, _) => StyleSideCard();
        PlayerBar.SizeChanged += (_, _) => FitSideCover();
        SetUpSideSplitter();
    }

    /// <summary>
    /// The column beside the page is dragged wider or narrower by a grip in
    /// the gap, like the sidebar (the owner's request, 10 October 2026):
    /// double-click for the look's width, arrow keys with the keyboard. The
    /// width is the user's own (<see cref="AppSettings.SidePlayerWidth"/>);
    /// narrower than its controls fit in a row, it snaps to a rail.
    /// </summary>
    private void SetUpSideSplitter()
    {
        var grip = new PaneSplitter { Label = "Resize the player", Visibility = Visibility.Collapsed };
        _sideSplitter = grip;
        Grid.SetRow(grip, PlayerPlacement.PanelsRow);
        Canvas.SetZIndex(grip, 2);
        ShellGrid.Children.Add(grip);

        // On the left the grip is on the column's right, so moving it right makes the column wider; on the right the other way round.
        grip.DragStarted += (_, _) =>
        {
            _sideDragStart = PlayerSlot.Width;
            _sideDragMoved = false;
        };
        grip.Dragged += (_, moved) =>
        {
            _sideDragMoved = true;
            LayOutSide(_sideDragStart + (SideOnLeft ? moved : -moved));
        };
        grip.DragCompleted += (_, _) =>
        {
            if (_sideDragMoved)
            {
                KeepSideWidth();
            }
        };
        grip.Stepped += (_, step) =>
        {
            LayOutSide(PlayerSlot.Width + (SideOnLeft ? step : -step));
            KeepSideWidth();
        };
        grip.ResetRequested += (_, _) =>
        {
            _services.Settings.SidePlayerWidth = null;
            _services.SaveSettings();
            LayOutSide();
        };
    }

    private bool SideOnLeft => _placement is { } placement && PlayerPlacement.IsLeftSide(placement.Layout);

    private void KeepSideWidth()
    {
        _services.Settings.SidePlayerWidth = PlayerSlot.Width;
        _services.SaveSettings();
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
        if (ShapeHidesPanels && (PlayerPlacement.IsAtTop(layout) || PlayerPlacement.HoversAtTop(layout) || PlayerPlacement.IsSide(layout) || PlayerPlacement.IsInSidebar(layout)))
        {
            layout = layout is PlayerLayout.Top or PlayerLayout.Left or PlayerLayout.Right ? PlayerLayout.Docked : PlayerLayout.Floating;
        }

        // The sidebar's player needs the sidebar with its names (not covers only) and the player bar (Winamp is wider); otherwise it floats under the panels.
        var inSidebar = PlayerPlacement.IsInSidebar(layout) && _sidebarCompact != true && _classicPlayer is null;
        if (PlayerPlacement.IsInSidebar(layout) && !inSidebar)
        {
            layout = PlayerLayout.Floating;
        }

        // A hovering player set wider than the page (Settings, Player, Advanced) reaches over the sidebar and the panes, however narrow the page.
        var overPage = PlayerPlacement.HoversOverPage(layout) && !ShapeHidesPanels;
        var pageWidth = ContentPanel.ActualWidth;
        var wide = overPage && look.PlayerWidth is { } wanted && pageWidth > 0 && wanted > pageWidth + 0.5;
        var hovers = overPage && (wide || PlayerPlacement.HoveringFits(pageWidth, NarrowestPlayerWidth, gap));
        var fullHeight = theme.SidebarFullHeight && !ShapeHidesPanels;
        var placement = (layout, gap, fullHeight, hovers, wide, inSidebar, look.PlayerWidth, look.PlayerHeight, look.PlayerOffsetX, look.PlayerOffsetY);
        if (_placement == placement)
        {
            return;
        }

        _placement = placement;
        PlayerBar.ShowMode(inSidebar ? PlayerBarMode.Sidebar : PlayerPlacement.IsSide(layout) ? PlayerBarMode.Column : PlayerBarMode.Bar);

        // Settings, Layout, Advanced: moved by the user's own X and Y, wherever it sits.
        PlayerSlot.RenderTransform = look.PlayerOffsetX is null && look.PlayerOffsetY is null
            ? null
            : new Microsoft.UI.Xaml.Media.TranslateTransform { X = look.PlayerOffsetX ?? 0, Y = look.PlayerOffsetY ?? 0 };

        if (PlayerPlacement.IsSide(layout))
        {
            // A width typed under Advanced since the last drag wins over the dragged one.
            if (_lookWidthSeen && _lastLookWidth != look.PlayerWidth && _services.Settings.SidePlayerWidth is not null)
            {
                _services.Settings.SidePlayerWidth = null;
                _services.SaveSettings();
            }

            _lastLookWidth = look.PlayerWidth;
            _lookWidthSeen = true;
            PlaceBesidePage(layout);
            ClampPlayerSlot();
            UpdatePlayerInset();
            return;
        }

        LeaveSide();
        if (inSidebar)
        {
            PlaceInSidebar(gap);
            ClampPlayerSlot();
            UpdatePlayerInset();
            return;
        }

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
        Grid.SetColumn(PlayerSlot, !wide && slot.StartsAtContent ? page : 0);
        Grid.SetColumnSpan(PlayerSlot, wide || slot.SpansFollowingColumns ? ToTheLastColumn : 1);
        PlayerSlot.VerticalAlignment = !slot.AlignBottom ? VerticalAlignment.Stretch
            : hovers && PlayerPlacement.HoversAtTop(layout) ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        PlayerSlot.Margin = slot.Margin.ToThickness();
        ClampPlayerSlot();
        UpdatePlayerInset();
    }

    /// <summary>
    /// The minimal player at the foot of the sidebar: its slot lies over the
    /// sidebar's bottom, inside its padding, and the playlists end above it
    /// (see <see cref="UpdateCoveredRoom"/>). The bar keeps the floating
    /// margin of the look; the slot takes it back, so the bar lines up with
    /// the sidebar's rows.
    /// </summary>
    private void PlaceInSidebar(double gap)
    {
        foreach (var element in new FrameworkElement[] { Sidebar, SidebarElevation, SidebarSplitter })
        {
            Grid.SetRow(element, PlayerPlacement.PanelsRow);
            Grid.SetRowSpan(element, 1);
        }

        var own = PlayerPlacement.Margin(PlayerLayout.Sidebar, gap);
        var border = _services.Theme.Palette.BorderWidth;
        var side = SidebarPadding.Left + border - own.Left;
        Grid.SetRow(PlayerSlot, PlayerPlacement.PanelsRow);
        Grid.SetColumn(PlayerSlot, Grid.GetColumn(Sidebar));
        Grid.SetColumnSpan(PlayerSlot, 1);
        PlayerSlot.Width = double.NaN;
        PlayerSlot.HorizontalAlignment = HorizontalAlignment.Stretch;
        PlayerSlot.VerticalAlignment = VerticalAlignment.Bottom;
        PlayerSlot.Margin = new Thickness(side, 0, side, SidebarPadding.Bottom + border - own.Bottom);
    }

    /// <summary>
    /// A player set wider than the window (Settings, Player, Advanced) keeps
    /// inside it: as wide as the shell's room, its own margins counted.
    /// </summary>
    private void ClampPlayerSlot()
    {
        var room = ShellGrid.ActualWidth - ShellGrid.Padding.Left - ShellGrid.Padding.Right - PlayerSlot.Margin.Left - PlayerSlot.Margin.Right;
        PlayerSlot.MaxWidth = _placement is { InSidebar: true } || room <= 0 ? double.PositiveInfinity : room;
    }

    /// <summary>
    /// The playlists end above a player at the sidebar's foot; they, and the
    /// queue, Settings or lyrics, can scroll clear of a hovering player wide
    /// enough to lie over them.
    /// </summary>
    private void UpdateCoveredRoom()
    {
        var border = _services.Theme.Palette.BorderWidth;
        var bottom = _placement is { InSidebar: true } && PlayerSlot.ActualHeight > 0
            ? Math.Round(PlayerSlot.Margin.Bottom + PlayerSlot.ActualHeight - border + PlayerPlacement.SidebarPlayerGap)
            : SidebarPadding.Bottom;
        if (Math.Abs(Sidebar.Padding.Bottom - bottom) > 0.5)
        {
            Sidebar.Padding = new Thickness(SidebarPadding.Left, SidebarPadding.Top, SidebarPadding.Right, bottom);
        }

        var wide = _placement is { Wide: true } && PlayerSlot.ActualHeight > 0;
        var player = wide ? BoundsInShell(PlayerSlot) : default;
        var reach = Math.Ceiling(PlayerSlot.ActualHeight) + PlayerPlacement.HoverClearance;
        var under = wide && player.Left < BoundsInShell(Sidebar).Right - 0.5 ? reach : 0;
        if (Math.Abs(PlaylistList.Padding.Bottom - under) > 0.5)
        {
            PlaylistList.Padding = new Thickness(0, 0, 0, under);
        }

        var pane = new FrameworkElement[] { QueuePane, SettingsPane, LyricsPane }.FirstOrDefault(p => p.Visibility == Visibility.Visible);
        var paneUnder = wide && pane is not null && player.Right > BoundsInShell(pane).Left + 0.5 ? reach : 0;
        if (Math.Abs(paneUnder - _paneRoom) > 0.5)
        {
            _paneRoom = paneUnder;
            QueuePane.SetPlayerRoom(paneUnder);
            SettingsPane.SetPlayerRoom(paneUnder);
            LyricsPane.SetPlayerRoom(paneUnder);
        }
    }

    /// <summary>
    /// The player as a column on the left or right of the page (redone on
    /// 10 October 2026, when the owner found the cover floating in an empty
    /// panel over a separate bar): one panel, like the page's, with the cover
    /// as wide as the column at the top, the song and its artists under it,
    /// then the progress, the buttons and the volume (the player bar in its
    /// column layout, on the panel), and what plays next filling the rest.
    /// The page and its shadow make room for it.
    /// </summary>
    private void PlaceBesidePage(PlayerLayout layout)
    {
        foreach (var element in new FrameworkElement[] { Sidebar, SidebarElevation, SidebarSplitter })
        {
            Grid.SetRow(element, PlayerPlacement.PanelsRow);
            Grid.SetRowSpan(element, 1);
        }

        var left = PlayerPlacement.IsLeftSide(layout);
        Grid.SetRow(PlayerSlot, PlayerPlacement.PanelsRow);
        Grid.SetColumn(PlayerSlot, Grid.GetColumn(ContentPanel));
        Grid.SetColumnSpan(PlayerSlot, 1);
        PlayerSlot.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PlayerSlot.VerticalAlignment = VerticalAlignment.Stretch;
        PlayerSlot.RowSpacing = 0;

        if (_sideColumn is null)
        {
            _sideColumn = new NowPlayingColumn(_services, beside: true);
            _sideUpNext = new SideUpNext(_services);
            _sideShadow = new Elevation { IsHitTestVisible = false };
            _sideCard = new Border { IsHitTestVisible = false };
            PlayerSlot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlayerSlot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PlayerSlot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            foreach (var child in PlayerSlot.Children.OfType<FrameworkElement>())
            {
                Grid.SetRow(child, 1);
            }

            Grid.SetRowSpan(_sideShadow, 3);
            Grid.SetRowSpan(_sideCard, 3);
            Grid.SetRow(_sideUpNext, 2);
            PlayerSlot.Children.Insert(0, _sideShadow);
            PlayerSlot.Children.Insert(1, _sideCard);
            PlayerSlot.Children.Insert(2, _sideColumn);
            PlayerSlot.Children.Insert(3, _sideUpNext);
            StyleSideCard();
        }

        LayOutSide();
    }

    /// <summary>The look's width under Advanced when the column was last placed, to tell a new one from the dragged width.</summary>
    private double? _lastLookWidth;
    private bool _lookWidthSeen;

    /// <summary>
    /// The column beside the page as wide as <paramref name="wanted"/> (else
    /// as the user dragged it, else the look's width, else the usual), within
    /// what leaves the page its least; the page and its shadow make room, and
    /// the grip sits in the gap between them.
    /// </summary>
    private void LayOutSide(double? wanted = null)
    {
        if (_sideColumn is null || _placement is not { } placement || !PlayerPlacement.IsSide(placement.Layout))
        {
            return;
        }

        var layout = placement.Layout;
        var gap = placement.Gap;
        var left = PlayerPlacement.IsLeftSide(layout);
        var inset = layout is PlayerLayout.InsetLeft or PlayerLayout.InsetRight ? Math.Max(gap, 8) : 0;

        // The page's column holds the page and the column beside it.
        var shell = ShellGrid.ActualWidth - ShellGrid.Padding.Left - ShellGrid.Padding.Right;
        var others = SidebarColumn.Width.Value + ShellGrid.ColumnSpacing
            + (ShellGrid.ColumnDefinitions.Contains(_paneColumn) ? _paneColumn.Width.Value + ShellGrid.ColumnSpacing : 0);
        var most = shell > 0 ? shell - others - PageMinWidth - gap - (2 * inset) : SideMaxWidth;
        var width = Math.Min(
            wanted ?? _services.Settings.SidePlayerWidth ?? _services.Theme.Current.PlayerWidth ?? PlayerPlacement.SideWidth,
            Math.Min(most, SideMaxWidth));
        width = width < SideMinWidth ? SideRailWidth : Math.Round(width);
        PlayerBar.FitColumn(width);
        var rail = width < PlayerBar.ColumnRailBelow;
        _sideColumn.SetRail(rail);
        _sideUpNext?.SetRail(rail);

        PlayerSlot.Width = width;
        PlayerSlot.Margin = new Thickness(inset);
        var room = width + gap + (2 * inset);
        var pageMargin = left ? new Thickness(room, 0, 0, 0) : new Thickness(0, 0, room, 0);
        ContentPanel.Margin = pageMargin;
        ContentElevation.Margin = pageMargin;

        if (_sideSplitter is { } grip)
        {
            // Centred on the gap between the column's panel and the page.
            var middle = inset + width + ((inset + gap) / 2);
            var near = Math.Max(0, middle - (PaneSplitter.GripWidth / 2));
            Grid.SetColumn(grip, Grid.GetColumn(ContentPanel));
            grip.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            grip.Margin = left ? new Thickness(near, 0, 0, 0) : new Thickness(0, 0, near, 0);
            grip.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The column's panel: the player's fill, the look's outline and corners, and the panels' shadow (or the player's glow).</summary>
    private void StyleSideCard()
    {
        if (_sideCard is null || _sideShadow is null)
        {
            return;
        }

        var theme = _services.Theme;
        var palette = theme.Palette;
        var corner = new CornerRadius(palette.CornerLarge);
        _sideCard.Background = theme.GetBrush("ResonatePlayerBrush");
        _sideCard.BorderBrush = theme.GetBrush("ResonateBorderBrush");
        _sideCard.BorderThickness = new Thickness(palette.BorderWidth);
        _sideCard.CornerRadius = corner;
        _sideShadow.CornerRadius = corner;
        _sideShadow.Level = theme.Current.PlayerGlow > 0 ? ElevationLevel.Player : ElevationLevel.Panel;
    }

    /// <summary>The column's cover takes what the controls leave of the column's height.</summary>
    private void FitSideCover()
    {
        if (_sideColumn is not null && PlayerSlot.ActualHeight > 0)
        {
            _sideColumn.FitTo(PlayerSlot.ActualHeight - (_classicPlayer?.ActualHeight ?? PlayerBar.ActualHeight));
        }
    }

    /// <summary>Back from a column beside the page: the page has its column to itself again.</summary>
    private void LeaveSide()
    {
        ContentPanel.Margin = new Thickness(0);
        ContentElevation.Margin = new Thickness(0);
        PlayerSlot.RowSpacing = 0;
        _sideSplitter?.Visibility = Visibility.Collapsed;
        if (_sideColumn is null)
        {
            return;
        }

        foreach (var part in new FrameworkElement?[] { _sideShadow, _sideCard, _sideColumn, _sideUpNext })
        {
            if (part is not null)
            {
                PlayerSlot.Children.Remove(part);
            }
        }

        _sideColumn = null;
        _sideUpNext = null;
        _sideCard = null;
        _sideShadow = null;
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
        FitSideCover();
    }

    /// <summary>
    /// Tells the page on show how much room to leave under its last row, and
    /// lifts the update bar by as much: as much as a hovering player covers,
    /// none otherwise. A new page always hears it; others only when it changes
    /// (the slot's size changes with every resize of the window).
    /// </summary>
    private void UpdatePlayerInset(bool newPage = false)
    {
        UpdateCoveredRoom();
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
