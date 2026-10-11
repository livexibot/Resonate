using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// Home's visualizer full screen (the owner's request, 11 October 2026:
/// "exactly the same as the normal version" and not too big): Home's own
/// stage filling the screen, drawn a little larger than on the page only on
/// a tall screen (<see cref="ScaleFor"/>). Esc or the stage's button goes
/// back, Space plays or pauses.
/// </summary>
internal sealed partial class FullScreenView : UserControl
{
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(220);

    private readonly AppServices _services;
    private readonly ScaleBox _box = new();

    public FullScreenView(AppServices services)
    {
        _services = services;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Opacity = 0;
        _box.Children.Add(new NowPlayingStage(services, StageKind.Home, fullView: true));
        Content = _box;
        SizeChanged += (_, e) => _box.Factor = ScaleFor(e.NewSize.Height);
        PreviewKeyDown += OnKey;
    }

    /// <summary>Raised when Esc asks to go back.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// How much larger than on the page: none up to 1300 high (in the
    /// window's units), then growing with the screen to at most 1.35 times,
    /// so a 5K screen seen from a distance reads well without the cover and
    /// words looking blown up.
    /// </summary>
    public static double ScaleFor(double height) => Math.Clamp(height / 1300, 1, 1.35);

    /// <summary>Fades in (at once with Windows' animations off) and takes the keyboard.</summary>
    public void Appear()
    {
        OpacityTransition = _services.Theme.AnimationsEnabled ? new ScalarTransition { Duration = Fade } : null;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Opacity = 1;
            Focus(FocusState.Programmatic);
        });
    }

    /// <summary>Fades out and calls <paramref name="gone"/>.</summary>
    public void Disappear(Action gone)
    {
        if (!_services.Theme.AnimationsEnabled)
        {
            gone();
            return;
        }

        Opacity = 0;
        var done = DispatcherQueue.CreateTimer();
        done.Interval = Fade;
        done.IsRepeating = false;
        global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Dispatching.DispatcherQueueTimer, object>? finished = null;
        finished = (_, _) =>
        {
            done.Stop();
            done.Tick -= finished;
            gone();
        };
        done.Tick += finished;
        done.Start();
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == global::Windows.System.VirtualKey.Space && !e.KeyStatus.WasKeyDown)
        {
            e.Handled = true;
            _ = _services.Player.TogglePlayPauseAsync();
        }
    }
}
