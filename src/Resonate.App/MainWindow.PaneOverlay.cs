using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.Themes;

namespace Resonate.App;

/// <summary>
/// A window too small for the sidebar, the page and the pane on the right
/// (Settings, the queue or the lyrics) side by side: the page keeps its
/// least width and runs on under the pane, which lies over its right part
/// with an outline and the panels' shadow, so the page's left part stays
/// readable instead of squeezed to slivers. When everything fits, nothing
/// changes. The hovering player, the update bar and the page's messages keep
/// to the part of the page in view.
/// </summary>
public sealed partial class MainWindow
{
    private bool _paneOverPage;
    private bool _pageSqueezed;
    private double _pageCovered;

    /// <summary>How much of the page the pane covers (with the gap before it), in the content's units; 0 when it lies beside the page.</summary>
    private double PageCoveredWidth => _paneOverPage ? _paneColumn.Width.Value + ShellGrid.ColumnSpacing : 0;

    /// <summary>The part of the page in view.</summary>
    private double VisiblePageWidth => Math.Max(0, ContentPanel.ActualWidth - PageCoveredWidth);

    /// <summary>Called by LayOutPanes with whether the page, beside the pane, would have less than its least width.</summary>
    private void ShowPaneOverPage(bool squeezed)
    {
        _pageSqueezed = squeezed;
        UpdatePaneOverPage();
    }

    /// <summary>
    /// Also from ApplyPlayerPlacement, since the window shape and the look's
    /// player decide it too: a window shape without the page shows the pane
    /// across the window instead (see PlacePanesAsSheet), and a player in a
    /// column on the page's right keeps to the page's own column, so a page
    /// running on under the pane would run under that player too.
    /// </summary>
    private bool PaneMayLieOverPage =>
        !ShapeHidesPanels
        && !(PlayerPlacement.IsSide(_services.Theme.Current.PlayerLayout) && !PlayerPlacement.IsLeftSide(_services.Theme.Current.PlayerLayout));

    private void UpdatePaneOverPage()
    {
        var over = _pageSqueezed && PaneMayLieOverPage;
        if (over != _paneOverPage)
        {
            _paneOverPage = over;

            // The panes come after the page, so they lie over it.
            Grid.SetColumnSpan(ContentPanel, over ? 2 : 1);
            Grid.SetColumnSpan(ContentElevation, over ? 2 : 1);
            var shown = over ? Visibility.Visible : Visibility.Collapsed;
            PaneBacking.Visibility = shown;
            PaneEdge.Visibility = shown;

            // The pane is as narrow as it goes, so its grip has nothing to give.
            RightSplitter.IsHitTestVisible = !over;
            RightSplitter.IsTabStop = !over;
        }

        var covered = PageCoveredWidth;
        if (Math.Abs(covered - _pageCovered) < 0.5)
        {
            return;
        }

        _pageCovered = covered;
        MessageBar.Margin = new Thickness(16, 16, 16 + covered, 16);
        UpdateBarHost.Margin = new Thickness(16, 16, 16 + covered, 16 + Math.Max(_playerInset, 0));
        FitUpdateBar();
    }

    /// <summary>The update bar stays inside the page in view (16 px from each edge, inside the page's outline).</summary>
    private void FitUpdateBar()
    {
        var outline = ContentPanel.BorderThickness.Left + ContentPanel.BorderThickness.Right;
        UpdateBar.Width = Math.Clamp(ContentPanel.ActualWidth - outline - 32 - PageCoveredWidth, 0, UpdateBarWidth);
    }

    /// <summary>
    /// For the screenshot tour: with a pane open beside it, the page keeps at
    /// least its least width (the pane lies over it when the window is too
    /// small for both). Null when all is well.
    /// </summary>
    internal string? CheckPageBesidePane()
    {
        var open = QueuePane.IsOpen || SettingsPane.IsOpen || LyricsPane.IsOpen;
        if (!open || !PaneMayLieOverPage)
        {
            return null;
        }

        if (ContentPanel.ActualWidth < PageMinWidth - 0.5)
        {
            return $"The page is {ContentPanel.ActualWidth:0} wide beside the pane, less than its least {PageMinWidth:0}.";
        }

        return _paneOverPage && PaneEdge.Visibility != Visibility.Visible
            ? "The pane lies over the page without its outline."
            : null;
    }
}
