using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Resonate.App;

/// <summary>
/// The mini player (Ctrl+M, the button beside the window's own buttons, or
/// the classic player's menu): while it shows, this window is hidden, and
/// anything that needs it (search, a page, Settings) brings it back.
/// </summary>
public sealed partial class MainWindow
{
    private MiniPlayerWindow? _miniPlayer;

    /// <summary>The mini player is showing in place of this window.</summary>
    public bool IsMiniPlayerShown => _miniPlayer is not null;

    /// <summary>What the mini player shows (for the screenshot tour), or null.</summary>
    internal FrameworkElement? MiniPlayerRoot => _miniPlayer?.Root;

    /// <summary>The mini player's size in screen pixels (for the screenshot tour's check), or null.</summary>
    internal global::Windows.Graphics.SizeInt32? MiniPlayerClientSize => _miniPlayer?.AppWindow.ClientSize;

    /// <summary>Hides this window and shows the mini player (only while signed in).</summary>
    public void ShowMiniPlayer()
    {
        if (_miniPlayer is { } shown)
        {
            shown.ShowAndFocus();
            return;
        }

        if (ShellGrid.Visibility != Visibility.Visible)
        {
            return;
        }

        var mini = new MiniPlayerWindow();
        _miniPlayer = mini;
        mini.ShowAndFocus();
        AppWindow.Hide();
    }

    /// <summary>Shows this window again and closes the mini player.</summary>
    public void LeaveMiniPlayer()
    {
        if (_miniPlayer is not { } mini)
        {
            return;
        }

        // Shown before the mini player closes, so the app is never without a window.
        _miniPlayer = null;
        AppWindow.Show();
        Activate();
        mini.CloseForGood();
    }

    private void ToggleMiniPlayer()
    {
        if (_miniPlayer is null)
        {
            ShowMiniPlayer();
        }
        else
        {
            LeaveMiniPlayer();
        }
    }

    private void OnMiniPlayerAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleMiniPlayer();
    }

    private void OnMiniPlayerClick(object sender, RoutedEventArgs e) => ShowMiniPlayer();
}
