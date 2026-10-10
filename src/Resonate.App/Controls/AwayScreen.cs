using System.Globalization;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;
using Resonate.Windows;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The screensaver, a built-in plugin, in its own window above every app
/// (<see cref="ScreensaverWindow"/>): a large clock at the top left and the
/// song that plays as a card above the visualizer (<see cref="NowPlayingStage"/>),
/// all sized from the screen's height (redone on 10 October 2026, the
/// owner: "make it way more modern"). In OLED mode the screen is black but
/// for a dim glow of the look's accent, and the card, the clock and the
/// glow wander slowly and smoothly (<see cref="ScreensaverDrift"/>).
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

    // OLED mode: the glow's peak, kept low so the screen stays mostly black.
    private const double GlowStrength = 0.22;

    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DimAfter = TimeSpan.FromMinutes(10);

    private readonly AppServices _services;
    private readonly NowPlayingStage _stage;
    private readonly StackPanel _clockPanel;
    private readonly TextBlock _clock;
    private readonly TextBlock _date;
    private readonly DispatcherQueueTimer _poll;
    private readonly bool _oled;
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse? _glow;
    private readonly Helpers.SlowClock? _drift;
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

        // At the user's Text size, read as the screen appears (Settings cannot change it meanwhile).
        var text = services.Theme.TextSize;
        _clock = new TextBlock
        {
            Style = (Style)resources["ResonateDisplayTextStyle"],
            FontWeight = FontWeights.Light,
            CharacterSpacing = -20,
        };
        _date = new TextBlock
        {
            Style = (Style)resources["ResonateEyebrowTextStyle"],
            Margin = new Thickness(6, 0, 0, 0),
        };
        _clockPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Children = { _clock, _date },
        };

        _oled = services.Settings.ScreensaverOled;
        _clockPanel.Visibility = services.Settings.ScreensaverClock ? Visibility.Visible : Visibility.Collapsed;

        // OLED: words a step down from white, which ages the screen less.
        if (_oled)
        {
            _clockPanel.Opacity = 0.8;
            _stage.Body.Opacity = 0.85;
        }

        // A see-through background, so every touch lands here (the stage itself takes none).
        var root = new Grid { Background = services.Theme.GetBrush("ResonateTransparentBrush") };
        if (_oled)
        {
            // A soft, dim glow of the accent wandering behind everything; the rest stays black.
            var accent = services.Theme.Palette.Accent.Opaque.ToColor();
            _glow = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Fill = new Microsoft.UI.Xaml.Media.RadialGradientBrush
                {
                    GradientStops =
                    {
                        new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb((byte)(255 * GlowStrength), accent.R, accent.G, accent.B), Offset = 0 },
                        new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb((byte)(255 * GlowStrength * 0.35), accent.R, accent.G, accent.B), Offset = 0.45 },
                        new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0, accent.R, accent.G, accent.B), Offset = 1 },
                    },
                },
            };
            root.Children.Add(_glow);
            _drift = new Helpers.SlowClock(DispatcherQueue.GetForCurrentThread(), ShowDrift);
        }

        root.Children.Add(_stage);
        root.Children.Add(_clockPanel);
        Content = root;
        SizeChanged += (_, e) => FitToScreen(e.NewSize);

        _poll = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(1);
        _poll.IsRepeating = true;

        PointerMoved += OnPointerMoved;
        Loaded += (_, _) =>
        {
            // With Windows' animations off they move once a minute instead (see ShowTime).
            if (_services.Theme.AnimationsEnabled)
            {
                _drift?.Start();
            }
        };
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
        Unloaded += (_, _) =>
        {
            StopPolling();
            _drift?.Pause();
        };
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

    /// <summary>
    /// Sizes from the screen's height, so the clock and the card look the
    /// same on a laptop and on a 5K screen: a large, thin clock with the
    /// date under it at the top left, and the glow about a screen tall.
    /// </summary>
    private void FitToScreen(Size size)
    {
        var height = Math.Max(size.Height, 1);
        var text = _services.Theme.TextSize;
        var pad = Math.Clamp(height * 0.065, 40, 180);
        _clock.FontSize = AppScale.Font(Math.Clamp(height * (_oled ? 0.12 : 0.15), 72, 340), text);
        _date.FontSize = AppScale.Font(Math.Clamp(height * 0.018, 13, 40), text);
        _clockPanel.Margin = new Thickness(pad, pad * 0.6, pad, pad);
        if (_glow is not null)
        {
            _glow.Width = _glow.Height = height * 1.1;
        }

        ShowDrift(_drift?.Seconds ?? 0);
    }

    /// <summary>OLED: the card, the clock and the glow, where their slow paths have reached.</summary>
    private void ShowDrift(double seconds)
    {
        if (!_oled || ActualWidth <= 0)
        {
            return;
        }

        var (width, height) = (ActualWidth, ActualHeight);
        var (cardX, cardY) = ScreensaverDrift.Card(seconds, width, height);
        _stage.Body.Translation = new Vector3((float)cardX, (float)cardY, 0);
        var (clockX, clockY) = ScreensaverDrift.Clock(seconds, width, height);
        _clockPanel.Translation = new Vector3((float)clockX, (float)clockY, 0);
        if (_glow is not null)
        {
            var (glowX, glowY) = ScreensaverDrift.Glow(seconds);
            _glow.Translation = new Vector3((float)((glowX * width) - (_glow.Width / 2)), (float)((glowY * height) - (_glow.Height / 2)), 0);
        }
    }

    /// <summary>The time, once a minute; out of OLED mode, a small step of the words and the clock away from where they were.</summary>
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
        if (_oled)
        {
            if (_drift is not { IsRunning: true })
            {
                ShowDrift(minute * 60.0);
            }

            return;
        }

        var (x, y) = StageColours.BurnInShift(minute, ShiftRange);
        var shift = new Vector3((float)x, (float)y, 0);
        _stage.Body.Translation = shift;
        _clockPanel.Translation = -shift;
    }
}
