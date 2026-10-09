using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Windows;

namespace Resonate.App;

/// <summary>
/// The screensaver, a built-in plugin (see <see cref="BuiltInPlugins"/>; it
/// was the away screen inside the window until the owner asked for it above
/// every app, 9 October 2026): after a few minutes without the mouse or
/// keyboard (5 by default), while music plays (or always, with its setting),
/// the song, a clock and a visualizer cover the whole display Resonate is
/// on, in a window of their own (<see cref="ScreensaverWindow"/>) with the
/// pointer hidden, and the first touch brings everything back. Never while a
/// full-screen game or video, a presentation or the lock screen has the
/// display, nor while Resonate is in front with a menu, dialog or text field
/// open. While off nothing runs: the idle check (every 5 seconds) runs only
/// while it could show.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan AwayCheckInterval = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer? _awayTimer;
    private AwayScreen? _awayScreen;
    private ScreensaverWindow? _screensaver;
    private bool _awayWatching;
    private bool _awayHadFront;
    private int _awayStateQueued;

    partial void SetUpAwayScreen()
    {
        FollowPlugin(BuiltInPlugins.AwayScreen, WatchAway);
        Closed += (_, _) => HideAway();
        WatchAway();
    }

    /// <summary>Settings changed when the screensaver may show.</summary>
    internal void FollowScreensaver() => UpdateAwayTimer();

    /// <summary>Settings' "Show now": the screensaver at once, to try its look.</summary>
    internal void ShowScreensaverNow() => ShowAway();

    /// <summary>Starts or stops following the player, as the plugin is turned on or off.</summary>
    private void WatchAway()
    {
        var on = _services.BuiltIns.IsOn(BuiltInPlugins.AwayScreen);
        if (on != _awayWatching)
        {
            _awayWatching = on;
            if (on)
            {
                _services.Player.StateChanged += OnAwayPlayerChanged;
            }
            else
            {
                _services.Player.StateChanged -= OnAwayPlayerChanged;
                HideAway();
            }
        }

        UpdateAwayTimer();
    }

    private void OnAwayPlayerChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; look at the newest one once.
        if (Interlocked.Exchange(ref _awayStateQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _awayStateQueued, 0);
                UpdateAwayTimer();
            });
        }
    }

    /// <summary>The idle check runs only while the plugin is on, the screensaver is not up, and music plays (unless it may show any time).</summary>
    private void UpdateAwayTimer()
    {
        var check = _awayWatching && _awayScreen is null
            && (_services.Player.State.IsPlaying || !_services.Settings.ScreensaverOnlyWhilePlaying);
        if (!check)
        {
            _awayTimer?.Stop();
            return;
        }

        if (_awayTimer is null)
        {
            _awayTimer = DispatcherQueue.CreateTimer();
            _awayTimer.Interval = AwayCheckInterval;
            _awayTimer.IsRepeating = true;
            _awayTimer.Tick += (_, _) => CheckAway();
        }

        if (!_awayTimer.IsRunning)
        {
            _awayTimer.Start();
        }
    }

    private void CheckAway()
    {
        var wait = TimeSpan.FromMinutes(BuiltInPluginSettings.ScreensaverAfter(_services.Settings));
        if (UserPresence.IdleTime >= wait && MayShowAway())
        {
            ShowAway();
        }
    }

    /// <summary>
    /// Nothing full screen has the display, and when Resonate is in front,
    /// nothing in it is open or being typed in.
    /// </summary>
    private bool MayShowAway()
    {
        if (UserPresence.IsScreenTaken())
        {
            return false;
        }

        if (!IsShown || !UserPresence.IsForeground(Hwnd) || RootGrid.XamlRoot is not { } root)
        {
            return true;
        }

        // A menu, flyout or dialog is open (a tooltip under a resting pointer does not count). Asked of the
        // automation peer: under Native AOT "is ToolTip" is false for tooltips XAML made (see CLAUDE.md).
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is not { } child
                || FrameworkElementAutomationPeer.CreatePeerForElement(child)?.GetAutomationControlType() != AutomationControlType.ToolTip)
            {
                return false;
            }
        }

        return FocusManager.GetFocusedElement(root) is not (TextBox or PasswordBox or AutoSuggestBox or RichEditBox);
    }

    private void ShowAway()
    {
        if (_awayScreen is not null)
        {
            return;
        }

        var away = new AwayScreen(_services);
        away.WakeRequested += (_, _) => HideAway();
        _awayScreen = away;
        _awayHadFront = IsShown && UserPresence.IsForeground(Hwnd);

        // The whole display Resonate is on, above every app.
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var theme = Content is FrameworkElement content ? content.ActualTheme : ElementTheme.Dark;
        var window = new ScreensaverWindow(away, display.OuterBounds, theme);
        _screensaver = window;
        window.ShowOnTop();
        away.Appear();
        NowPlayingStage.SetCovered(true);
        UpdateAwayTimer();
    }

    /// <summary>Brings everything back: the screensaver fades out quickly (at once with Windows' animations off).</summary>
    private void HideAway()
    {
        if (_awayScreen is not { } away)
        {
            return;
        }

        var window = _screensaver;
        _awayScreen = null;
        _screensaver = null;
        NowPlayingStage.SetCovered(false);
        away.Disappear(() =>
        {
            window?.Close();
            if (_awayHadFront && IsShown)
            {
                // Resonate was in front: it is again.
                Activate();
            }
        });
        UpdateAwayTimer();
    }
}
