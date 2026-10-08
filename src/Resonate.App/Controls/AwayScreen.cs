using System.Globalization;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;
using Resonate.Themes;
using Resonate.Windows;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The away screen, a built-in plugin: over the whole window, the song that
/// plays (<see cref="NowPlayingStage"/>), a large clock and the next song.
/// The first touch of the mouse or keyboard asks for it to go
/// (<see cref="WakeRequested"/>), and is not passed on: a click lands here,
/// not on the button underneath, and no shortcut runs; media keys pass.
/// The words move a few pixels every minute, so nothing burns into an OLED
/// screen, and after ten minutes paused only the clock stays bright. It
/// checks once a second (only while shown) for input it cannot see, such as
/// over the title bar.
/// </summary>
internal sealed partial class AwayScreen : UserControl
{
    private const double ShiftRange = 6;
    private const double DimmedOpacity = 0.15;

    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DimAfter = TimeSpan.FromMinutes(10);

    private readonly AppServices _services;
    private readonly NowPlayingStage _stage;
    private readonly StackPanel _clockPanel;
    private readonly TextBlock _clock;
    private readonly TextBlock _date;
    private readonly DispatcherQueueTimer _poll;
    private uint? _shownTick;
    private long _minute = -1;
    private DateTimeOffset? _pausedSince;
    private bool _woken;

    public AwayScreen(AppServices services)
    {
        _services = services;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Opacity = 0;
        var resources = Application.Current.Resources;

        _stage = new NowPlayingStage(services, StageKind.Away);
        _clock = new TextBlock
        {
            Style = (Style)resources["ResonateDisplayTextStyle"],
            FontSize = 88,
            FontWeight = FontWeights.Light,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _date = new TextBlock
        {
            Style = (Style)resources["ResonateEyebrowTextStyle"],
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _clockPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 72, 0),
            IsHitTestVisible = false,
            Children = { _clock, _date },
        };

        // A see-through background, so every touch lands here (the stage itself takes none).
        Content = new Grid { Background = services.Theme.GetBrush("ResonateTransparentBrush"), Children = { _stage, _clockPanel } };

        _poll = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(1);
        _poll.IsRepeating = true;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerInput;
        PointerReleased += OnPointerHandled;
        PointerWheelChanged += OnPointerInput;
        Tapped += (_, e) => e.Handled = true;
        DoubleTapped += (_, e) => e.Handled = true;
        RightTapped += (_, e) => e.Handled = true;
        PreviewKeyDown += OnKey;
        ProcessKeyboardAccelerators += (_, e) =>
        {
            // No shortcut runs while away: the key only wakes the window.
            e.Handled = true;
            Wake();
        };
        Unloaded += (_, _) => StopPolling();
    }

    /// <summary>Raised once, on the first touch of the mouse or keyboard.</summary>
    public event EventHandler? WakeRequested;

    /// <summary>Fades in (unless Windows' animations are off) and takes the keyboard, so no key reaches the page behind.</summary>
    public void Appear()
    {
        _shownTick = UserPresence.LastInputTick;
        ShowTime(force: true);

        // Only while shown: a timer's handler that holds this screen would keep it (and its stage) for good.
        _poll.Tick -= OnPoll;
        _poll.Tick += OnPoll;
        _poll.Start();
        OpacityTransition = _services.Theme.AnimationsEnabled ? new ScalarTransition { Duration = FadeIn } : null;

        // Once it is in the window, so the fade plays and the keyboard can be taken.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_woken)
            {
                Opacity = 1;
                Focus(FocusState.Programmatic);
            }
        });
    }

    /// <summary>Fades out quickly and calls <paramref name="gone"/>; it keeps catching the pointer until then, so a click's release lands here too.</summary>
    public void Disappear(Action gone)
    {
        StopPolling();
        if (!_services.Theme.AnimationsEnabled)
        {
            gone();
            return;
        }

        OpacityTransition = new ScalarTransition { Duration = FadeOut };
        Opacity = 0;
        var done = DispatcherQueue.CreateTimer();
        done.Interval = FadeOut;
        done.IsRepeating = false;
        TypedEventHandler<DispatcherQueueTimer, object>? finished = null;
        finished = (_, _) =>
        {
            done.Stop();
            done.Tick -= finished;
            gone();
        };
        done.Tick += finished;
        done.Start();
    }

    private void StopPolling()
    {
        _poll.Stop();
        _poll.Tick -= OnPoll;
    }

    private void OnPoll(DispatcherQueueTimer sender, object args) => Check();

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // A pointer event Windows makes up (the screen appearing under a resting mouse) is not a touch.
        if (UserPresence.LastInputTick != _shownTick)
        {
            Wake();
        }
    }

    private void OnPointerInput(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        Wake();
    }

    private void OnPointerHandled(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        // Volume and media keys keep working (from 0xAD, volume mute, to 0xB7, launch app 2).
        var key = (int)e.Key;
        if (key is < 0xAD or > 0xB7)
        {
            e.Handled = true;
        }

        Wake();
    }

    private void Wake()
    {
        if (!_woken)
        {
            _woken = true;
            WakeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Once a second while shown: input elsewhere (the title bar, another window), the clock, and dimming while paused.</summary>
    private void Check()
    {
        if (UserPresence.LastInputTick != _shownTick)
        {
            Wake();
            return;
        }

        ShowTime(force: false);
        var now = DateTimeOffset.UtcNow;
        if (_services.Player.State.IsPlaying)
        {
            _pausedSince = null;
        }
        else
        {
            _pausedSince ??= now;
        }

        var opacity = _pausedSince is { } since && now - since >= DimAfter ? DimmedOpacity : 1;
        if (opacity != _stage.Opacity)
        {
            _stage.OpacityTransition = _services.Theme.AnimationsEnabled ? new ScalarTransition { Duration = TimeSpan.FromSeconds(1) } : null;
            _stage.Opacity = opacity;
        }
    }

    /// <summary>The time, once a minute, and a small step of the words and the clock away from where they were.</summary>
    private void ShowTime(bool force)
    {
        var now = DateTime.Now;
        var minute = now.Ticks / TimeSpan.TicksPerMinute;
        if (minute == _minute && !force)
        {
            return;
        }

        _minute = minute;
        _clock.Text = now.ToString("t", CultureInfo.CurrentCulture);
        _date.Text = now.ToString("dddd d MMMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        var (x, y) = StageColours.BurnInShift(minute, ShiftRange);
        var shift = new Vector3((float)x, (float)y, 0);
        _stage.Body.Translation = shift;
        _clockPanel.Translation = -shift;
    }
}
