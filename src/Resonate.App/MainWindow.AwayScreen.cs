using Microsoft.UI.Dispatching;
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
/// The away screen, a built-in plugin (see <see cref="BuiltInPlugins"/>):
/// after a few minutes without the mouse or keyboard (5 by default) while
/// music plays, Resonate shows the song, a clock and the next song over the
/// whole window (<see cref="AwayScreen"/>), and the first touch brings
/// everything back. It only takes over while Resonate is the window in
/// front and shown, signed in, with no menu, dialog or text field open, and
/// never while a full-screen game or video, a presentation or the lock
/// screen has the display. While off, or while nothing plays, nothing runs:
/// the idle check (every 5 seconds) runs only while it could show.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan AwayCheckInterval = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer? _awayTimer;
    private AwayScreen? _awayScreen;
    private bool _awayWatching;
    private int _awayStateQueued;

    partial void SetUpAwayScreen()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.AwayScreen)
            {
                WatchAway();
            }
        };
        WatchAway();
    }

    /// <summary>Starts or stops following the player and the window, as the plugin is turned on or off.</summary>
    private void WatchAway()
    {
        var on = _services.BuiltIns.IsOn(BuiltInPlugins.AwayScreen);
        if (on != _awayWatching)
        {
            _awayWatching = on;
            if (on)
            {
                _services.Player.StateChanged += OnAwayPlayerChanged;
                ShownChanged += OnAwayShownChanged;
            }
            else
            {
                _services.Player.StateChanged -= OnAwayPlayerChanged;
                ShownChanged -= OnAwayShownChanged;
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

    private void OnAwayShownChanged(object? sender, EventArgs e)
    {
        if (!IsShown)
        {
            HideAway();
        }

        UpdateAwayTimer();
    }

    /// <summary>The idle check runs only while the plugin is on, music plays, the window shows and the screen is not up yet.</summary>
    private void UpdateAwayTimer()
    {
        var check = _awayWatching && _awayScreen is null && IsShown && _services.Player.State.IsPlaying;
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
        var wait = TimeSpan.FromMinutes(StageSettings.AwayAfter(_services.Settings));
        if (UserPresence.IdleTime >= wait && MayShowAway())
        {
            ShowAway();
        }
    }

    /// <summary>Resonate is in front, shown and signed in, nothing is open or being typed in, and nothing full screen has the display.</summary>
    private bool MayShowAway()
    {
        if (!IsShown || ShellGrid.Visibility != Visibility.Visible || RootGrid.XamlRoot is not { } root || !UserPresence.IsForeground(Hwnd))
        {
            return false;
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

        return FocusManager.GetFocusedElement(root) is not (TextBox or PasswordBox or AutoSuggestBox or RichEditBox)
            && !UserPresence.IsScreenTaken();
    }

    private void ShowAway()
    {
        if (_awayScreen is not null || Content is not ThemeHost host)
        {
            return;
        }

        var away = new AwayScreen(_services);
        away.WakeRequested += (_, _) => HideAway();
        _awayScreen = away;
        // Over everything, a special look's decorations included.
        host.Children.Add(away);
        away.Appear();
        NowPlayingStage.SetCovered(true);
        UpdateAwayTimer();
    }

    /// <summary>Brings the window back: the away screen fades out quickly (at once with Windows' animations off).</summary>
    private void HideAway()
    {
        if (_awayScreen is not { } away)
        {
            return;
        }

        _awayScreen = null;
        NowPlayingStage.SetCovered(false);
        away.Disappear(() =>
        {
            if (Content is ThemeHost host)
            {
                host.Children.Remove(away);
            }
        });
        UpdateAwayTimer();
    }
}
