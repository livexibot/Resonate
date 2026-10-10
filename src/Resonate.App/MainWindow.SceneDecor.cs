using Microsoft.UI.Xaml;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App;

/// <summary>Where a special look's decorations go (see Controls/SceneDecorLayer).</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Where a special look's farther weather is hung (see Controls/SceneWeatherLayer):
    /// over the panels and under the player, in the content's units, while
    /// <see cref="WeatherShell"/> shows.
    /// </summary>
    internal FrameworkElement WeatherHost => WeatherBehindPlayer;

    /// <summary>The panels and the player, hidden while signing in.</summary>
    internal UIElement WeatherShell => ShellGrid;

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

        // A pane lying over the page (see MainWindow.PaneOverlay.cs) leaves only the page's left part in view.
        if (PageCoveredWidth > 0 && ContentPanel.ActualWidth > PageCoveredWidth)
        {
            page = page with { Width = page.Width * VisiblePageWidth / ContentPanel.ActualWidth };
        }

        FrameworkElement? pane = QueuePane.Visibility == Visibility.Visible ? QueuePane
            : SettingsPane.Visibility == Visibility.Visible ? SettingsPane
            : LyricsPane.Visibility == Visibility.Visible ? LyricsPane
            : null;

        // The player's own surface (not its margins, nor the empty row a centred Winamp player leaves), bar or Winamp.
        FrameworkElement? player;
        double playerRadius;
        if (_classicPlayer is { Visibility: Visibility.Visible, Parent: not null } classic)
        {
            player = classic.Frame;
            playerRadius = classic.FrameCorner;
        }
        else
        {
            player = PlayerBar.Visibility == Visibility.Visible ? PlayerBar.Surface : null;
            playerRadius = player is not null ? PlayerBar.SurfaceCorner : 0;
        }

        // Settings and the mini player, beside Windows' own buttons (the margin keeps them clear of those),
        // and the back button and the app's name on the left: nothing reaches up among them.
        var buttons = Box(TitleBarButtons);
        var buttonsLeft = buttons?.X ?? layer.ActualWidth - 150;
        var title = Box(TitleBarStart);
        var titleBottom = Math.Max(buttons?.Bottom ?? 0, title?.Bottom ?? 0);

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
            buttonsLeft,
            title?.Right ?? 0,
            titleBottom);
    }
}
