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
/// the page. The player's slot never leaves the shell grid; only its row,
/// columns, alignment and margin change, so the classic player is never
/// taken out of the window (which would rebuild it). The numbers come from
/// <see cref="PlayerPlacement"/>. Pages leave room under their last row for a
/// hovering player (see <see cref="IPlayerInset"/>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A span past the last column: the slot covers the queue's column while
    /// it is open, and any column added after the page's (the grid clamps it).
    /// </summary>
    private const int ToTheLastColumn = 99;

    private (PlayerLayout Layout, double Gap, bool FullHeight)? _placement;
    private double _playerInset = -1;

    /// <summary>Called once, from the constructor.</summary>
    private void SetUpPlayerPlacement()
    {
        // Changed is raised for every colour of a slide too; the placement only follows layout, gaps and the switch.
        _services.Theme.Changed += (_, _) => ApplyPlayerPlacement();
        ContentFrame.Navigated += (_, _) => UpdatePlayerInset(newPage: true);
    }

    /// <summary>Puts the player's slot where the look and the sidebar switch say, when either changed.</summary>
    private void ApplyPlayerPlacement()
    {
        var theme = _services.Theme;
        var placement = (theme.Current.PlayerLayout, theme.Palette.PanelGap, theme.SidebarFullHeight);
        if (_placement == placement)
        {
            return;
        }

        _placement = placement;
        var slot = PlayerPlacement.Slot(placement.PlayerLayout, placement.PanelGap, placement.SidebarFullHeight);
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

    private void OnPlayerSlotSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePlayerInset();

    /// <summary>
    /// Tells the page on show how much room to leave under its last row: as
    /// much as a hovering player covers, none otherwise. A new page always
    /// hears it; others only when it changes (the slot's size changes with
    /// every resize of the window).
    /// </summary>
    private void UpdatePlayerInset(bool newPage = false)
    {
        var inset = PlayerPlacement.PageInset(_services.Theme.Current.PlayerLayout, PlayerSlot.ActualHeight);
        if (!newPage && Math.Abs(inset - _playerInset) < 0.5)
        {
            return;
        }

        _playerInset = inset;
        if (ContentFrame.Content is IPlayerInset page)
        {
            page.SetPlayerInset(inset);
        }
    }

    /// <summary>
    /// For the screenshot tour, while the player hovers: whether it stays over
    /// the page (never over the sidebar or the queue) and the page on show,
    /// scrolled to its end, keeps its last row above it. Null when all is well.
    /// </summary>
    internal string? CheckHoveringPlayer()
    {
        var player = BoundsInWindow(PlayerSlot);
        var page = BoundsInWindow(ContentPanel);
        if (player.Left < page.Left - 0.5 || player.Right > page.Right + 0.5 || player.Bottom > page.Bottom + 0.5)
        {
            return $"The hovering player ({player}) reaches outside the page ({page}).";
        }

        if (ContentFrame.Content is not IPlayerInset inset)
        {
            return "The page on show leaves no room for the hovering player.";
        }

        var room = BoundsInWindow(inset.PlayerSpacer);
        return room.Top > player.Top + 0.5
            ? $"The page's last row ends at {room.Top:0}, under the hovering player's top at {player.Top:0}."
            : null;
    }

    private static Rect BoundsInWindow(FrameworkElement element) =>
        element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
}
