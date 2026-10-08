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
/// pane only reports drags on its edge; the window sets and keeps its width.
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

        Resizer.DragStarted += (_, _) => ResizeStarted?.Invoke(this, EventArgs.Empty);
        Resizer.Dragged += (_, distance) => Resizing?.Invoke(this, distance);
        Resizer.DragCompleted += (_, _) => ResizeCompleted?.Invoke(this, EventArgs.Empty);
        Resizer.ResetRequested += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user starts dragging the pane's edge.</summary>
    public event EventHandler? ResizeStarted;

    /// <summary>Raised while the edge is dragged: how far right the pointer is from where it started.</summary>
    public event EventHandler<double>? Resizing;

    /// <summary>Raised when the user lets go of the edge.</summary>
    public event EventHandler? ResizeCompleted;

    /// <summary>Raised when the edge is double-clicked.</summary>
    public event EventHandler? ResetRequested;

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
