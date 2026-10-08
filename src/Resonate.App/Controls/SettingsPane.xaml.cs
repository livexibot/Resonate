using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Resonate.App.Pages;

namespace Resonate.App.Controls;

/// <summary>
/// Settings, in a pane on the right of the window instead of in place of the
/// page, so the page stays in view while settings change. The Settings page
/// is made when the pane opens and let go when it closes, like any page. The
/// window sizes the pane and resizes it with the grip on its left.
/// </summary>
public sealed partial class SettingsPane : UserControl
{
    /// <summary>The pane's width until the user drags it.</summary>
    public const double DefaultWidth = 520;

    /// <summary>The narrowest the pane gets; the rows of buttons in Settings need about this much.</summary>
    public const double MinimumWidth = 440;

    private static readonly Vector3 ClosedOffset = new(24, 0, 0);

    public SettingsPane()
    {
        InitializeComponent();

        // Closed, the pane waits faded out and a little to the right, so
        // opening slides it in (on the compositor, never delaying a click).
        Opacity = 0;
        Translation = ClosedOffset;
        OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(220) };
    }

    /// <summary>Raised when the close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    public bool IsOpen { get; private set; }

    /// <summary>The Settings page while the pane is open.</summary>
    public SettingsPage? Page => SettingsFrame.Content as SettingsPage;

    /// <summary>Makes the Settings page and slides the pane in.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        SettingsFrame.Navigate(typeof(SettingsPage), null, new SuppressNavigationTransitionInfo());
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (IsOpen)
            {
                Opacity = 1;
                Translation = Vector3.Zero;
            }
        });
    }

    /// <summary>Lets go of the Settings page (it saves as it goes), ready to slide in again.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        SettingsFrame.Content = null;
        Opacity = 0;
        Translation = ClosedOffset;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
