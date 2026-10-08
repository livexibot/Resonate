using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Pages;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App;

/// <summary>
/// Where the player sits: under the panels across the window, under the page
/// beside a sidebar that reaches the bottom, or hovering over the bottom of
/// the page (under the panels instead while the page is too narrow for it,
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

    private (PlayerLayout Layout, double Gap, bool FullHeight, bool Hovers)? _placement;
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
        var layout = theme.Current.PlayerLayout;
        var gap = theme.Palette.PanelGap;
        var hovers = layout == PlayerLayout.Hovering
            && PlayerPlacement.HoveringFits(ContentPanel.ActualWidth, NarrowestPlayerWidth, gap);
        var placement = (layout, gap, theme.SidebarFullHeight, hovers);
        if (_placement == placement)
        {
            return;
        }

        _placement = placement;
        var slot = PlayerPlacement.Slot(layout, gap, theme.SidebarFullHeight, hoveringFits: hovers);
        Grid.SetRowSpan(Sidebar, slot.SidebarRowSpan);
        Grid.SetRowSpan(SidebarElevation, slot.SidebarRowSpan);
        Grid.SetRowSpan(SidebarSplitter, slot.SidebarRowSpan);

        // The page's column, wherever it is (columns may be added before it).
        var page = Grid.GetColumn(ContentPanel);
        Grid.SetRow(PlayerSlot, slot.Row);
        Grid.SetColumn(PlayerSlot, slot.StartsAtContent ? page : 0);
        Grid.SetColumnSpan(PlayerSlot, slot.SpansFollowingColumns ? ToTheLastColumn : 1);
        PlayerSlot.VerticalAlignment = slot.AlignBottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        PlayerSlot.Margin = slot.Margin.ToThickness();
        UpdatePlayerInset();
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
        var inset = _placement?.Hovers == true ? PlayerPlacement.PageInset(PlayerLayout.Hovering, PlayerSlot.ActualHeight) : 0;
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
    /// under the panels lies wholly in the window. Null when all is well.
    /// </summary>
    internal string? CheckPlayerPlacement()
    {
        var player = BoundsInShell(PlayerSlot);
        var page = BoundsInShell(ContentPanel);
        if (!PlayerHovers)
        {
            var shell = BoundsInShell(ShellGrid);
            return player.Top < page.Bottom - 0.5 || player.Left < shell.Left - 0.5 || player.Right > shell.Right + 0.5 || player.Bottom > shell.Bottom + 0.5
                ? $"The player ({player}) is not under the page ({page}) inside the window ({shell})."
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
