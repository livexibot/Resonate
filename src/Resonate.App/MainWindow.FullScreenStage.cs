using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Resonate.App.Controls;

namespace Resonate.App;

/// <summary>
/// Home's visualizer full screen (the button on Home's stage): Home's own
/// stage in a full-screen window of its own on the display Resonate is on
/// (<see cref="FullScreenView"/>). The page's stage rests meanwhile, and the
/// screensaver does not come over it.
/// </summary>
public sealed partial class MainWindow
{
    private FullScreenView? _fullScreenView;
    private ScreensaverWindow? _fullScreenWindow;

    internal void ShowVisualizerFullScreen()
    {
        if (_fullScreenView is not null)
        {
            return;
        }

        var view = new FullScreenView(_services);
        view.CloseRequested += (_, _) => HideVisualizerFullScreen();
        _fullScreenView = view;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var theme = Content is FrameworkElement content ? content.ActualTheme : ElementTheme.Dark;
        var window = new ScreensaverWindow(view, display.OuterBounds, theme, screensaver: false);
        window.Closed += (_, _) =>
        {
            // Closed another way (Alt+F4): the page's stage moves again.
            if (ReferenceEquals(_fullScreenWindow, window))
            {
                _fullScreenView = null;
                _fullScreenWindow = null;
                NowPlayingStage.SetCovered(false);
            }
        };
        _fullScreenWindow = window;
        window.ShowOnTop();
        view.Appear();
        NowPlayingStage.SetCovered(true);
    }

    internal void HideVisualizerFullScreen()
    {
        if (_fullScreenView is not { } view)
        {
            return;
        }

        var window = _fullScreenWindow;
        _fullScreenView = null;
        _fullScreenWindow = null;
        NowPlayingStage.SetCovered(false);
        view.Disappear(() =>
        {
            window?.Close();
            if (IsShown)
            {
                Activate();
            }
        });
    }
}
