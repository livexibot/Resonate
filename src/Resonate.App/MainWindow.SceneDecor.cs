using Microsoft.UI.Xaml;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App;

/// <summary>Where a special look's decorations go (see Controls/SceneDecorLayer).</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Where the panels and the player are, in <paramref name="layer"/>'s
    /// units, for a special look's decorations; null while the page does not
    /// show (signing in, a window shape without it).
    /// </summary>
    internal SceneFrame? SceneFrameFor(FrameworkElement layer)
    {
        if (ShellGrid.Visibility != Visibility.Visible || layer.ActualWidth < 1 || layer.ActualHeight < 1)
        {
            return null;
        }

        SceneBox? Box(FrameworkElement? element)
        {
            if (element is null || element.Visibility != Visibility.Visible || element.ActualWidth < 1 || element.ActualHeight < 1)
            {
                return null;
            }

            try
            {
                var bounds = element.TransformToVisual(layer).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                return new SceneBox(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            catch (Exception)
            {
                // Not in the window (yet, or any more).
                return null;
            }
        }

        if (Box(ContentPanel) is not { } page)
        {
            return null;
        }

        FrameworkElement? pane = QueuePane.Visibility == Visibility.Visible ? QueuePane
            : SettingsPane.Visibility == Visibility.Visible ? SettingsPane
            : LyricsPane.Visibility == Visibility.Visible ? LyricsPane
            : null;

        // The player bar's own surface (not its margins), or the classic player in its place.
        var bar = PlayerBar.Visibility == Visibility.Visible ? PlayerBar.Surface : null;
        FrameworkElement? player = _classicPlayer is { Visibility: Visibility.Visible, Parent: not null } classic ? classic : bar;
        var playerRadius = player is not null && ReferenceEquals(player, bar) ? PlayerBar.SurfaceCorner : 0;

        // Settings and the mini player, beside Windows' own buttons (the margin keeps them clear of those).
        var buttonsLeft = Box(TitleBarButtons) is { } buttons ? buttons.X : layer.ActualWidth - 150;

        return new SceneFrame(
            new SceneBox(0, 0, layer.ActualWidth, layer.ActualHeight),
            Box(Sidebar),
            page,
            Box(pane),
            Box(player),
            ContentPanel.CornerRadius.TopLeft,
            playerRadius,
            ShellGrid.ColumnSpacing,
            _services.Theme.Scale,
            buttonsLeft);
    }
}
