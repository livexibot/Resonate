using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// What sits behind the panels: a plain colour, a gradient, the playing
/// song's cover drifting slowly (blurred once the user switches that on,
/// otherwise a soft wash of its colours), or the Windows material (Mica or
/// acrylic) with the theme's tint over it. Switching between them fades.
/// </summary>
internal sealed partial class BackdropLayer : Grid
{
    private static readonly TimeSpan LayerFade = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan CoverFade = TimeSpan.FromMilliseconds(900);

    private readonly ThemeService _theme;
    private readonly ArtworkSampler _artwork;
    private readonly Rectangle _solid;
    private readonly Rectangle _gradient;
    private readonly Grid _artworkLayer;
    private readonly Grid _drift;
    private readonly Image[] _covers = [new(), new()];
    private readonly Rectangle _tint;
    private readonly SlowClock _driftClock;
    private int _front;
    private bool _playing;
    private bool _windowShown = true;
    private int _playerUpdateQueued;
    private MainWindow? _window;

    public BackdropLayer(ThemeService theme, ArtworkSampler artwork)
    {
        _theme = theme;
        _artwork = artwork;
        IsHitTestVisible = false;

        _solid = Layer(new Rectangle { Fill = theme.GetBrush("ResonateBackgroundBrush") });
        _gradient = Layer(new Rectangle { Fill = theme.GetBrush("ResonateBackgroundGradientBrush") });

        _drift = new Grid();
        foreach (var cover in _covers)
        {
            cover.Stretch = Stretch.UniformToFill;
            cover.HorizontalAlignment = HorizontalAlignment.Center;
            cover.VerticalAlignment = VerticalAlignment.Center;
            _drift.Children.Add(cover);
        }

        // The tiny blurred cover (or wash) is stretched over the window and drawn
        // larger than it, so its soft borders never show while it drifts.
        ElementCompositionPreview.GetElementVisual(_drift).Scale = new Vector3(SlowDrift.CoverRestZoom);
        _driftClock = new SlowClock(DispatcherQueue.GetForCurrentThread(), ShowDrift);
        _artworkLayer = Layer(new Grid { Children = { _drift } });
        _artworkLayer.Background = theme.GetBrush("ResonateBackgroundBrush");
        _tint = Layer(new Rectangle { Fill = theme.GetBrush("ResonateBackdropTintBrush") });

        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed += OnThemeChanged;
        _artwork.Changed += OnArtworkChanged;
        App.Services.Player.StateChanged += OnPlayerStateChanged;
        _playing = App.Services.Player.State.IsPlaying;
        _window = App.MainWindow;
        if (_window is not null)
        {
            _window.ShownChanged += OnWindowShownChanged;
            _windowShown = _window.IsShown;
        }

        ShowCover(animate: false);
        UpdateLayers();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _artwork.Changed -= OnArtworkChanged;
        App.Services.Player.StateChanged -= OnPlayerStateChanged;
        if (_window is not null)
        {
            _window.ShownChanged -= OnWindowShownChanged;
            _window = null;
        }

        _driftClock.Pause();
    }

    /// <summary>On any thread; the drift follows the newest state once.</summary>
    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _playerUpdateQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _playerUpdateQueued, 0);
                var playing = App.Services.Player.State.IsPlaying;
                if (playing != _playing)
                {
                    _playing = playing;
                    UpdateDrift();
                }
            });
        }
    }

    private void OnWindowShownChanged(object? sender, EventArgs e)
    {
        _windowShown = _window?.IsShown ?? true;
        UpdateDrift();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The zoomed cover stops at the window's edges, also in pictures of the
        // window (taken for switching animations and screenshots).
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        UpdateDrift();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateLayers();

    private void OnArtworkChanged(object? sender, EventArgs e) => ShowCover(animate: _theme.AnimationsEnabled);

    private void UpdateLayers()
    {
        var backdrop = _theme.Current.Backdrop;
        var material = backdrop is WindowBackdrop.Mica or WindowBackdrop.Acrylic;
        _solid.Opacity = backdrop == WindowBackdrop.Solid ? 1 : 0;
        _gradient.Opacity = backdrop == WindowBackdrop.Gradient ? 1 : 0;
        _artworkLayer.Opacity = backdrop == WindowBackdrop.Artwork ? 1 : 0;
        _tint.Opacity = material || backdrop == WindowBackdrop.Artwork ? 1 : 0;
        UpdateDrift();
    }

    /// <summary>The new cover fades in over the old one.</summary>
    private void ShowCover(bool animate)
    {
        var source = _artwork.Blurred;
        var current = _covers[_front];
        if (ReferenceEquals(current.Source, source))
        {
            return;
        }

        _front = 1 - _front;
        var next = _covers[_front];
        next.Source = source;
        Canvas.SetZIndex(next, 1);
        Canvas.SetZIndex(current, 0);

        var visual = ElementCompositionPreview.GetElementVisual(next);
        if (!animate || source is null || current.Source is null)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
            return;
        }

        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = CoverFade;
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// A slow zoom and turn, so the cover behind the panels feels alive. Only
    /// while it shows, and paused while nothing plays or the window is
    /// minimised. It is shown <see cref="SlowDrift.FramesPerSecond"/> times a
    /// second (<see cref="SlowClock"/>): a moving picture behind everything
    /// redraws the whole window each time.
    /// </summary>
    private void UpdateDrift()
    {
        var shown = _theme.Current.Backdrop == WindowBackdrop.Artwork && ActualWidth > 0;
        var visual = ElementCompositionPreview.GetElementVisual(_drift);
        visual.CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        if (!shown)
        {
            // Held where it is, so nothing jumps while the layer fades out.
            _driftClock.Pause();
            return;
        }

        if (!_theme.AnimationsEnabled)
        {
            _driftClock.Reset();
            visual.Scale = new Vector3(SlowDrift.CoverRestZoom);
            visual.RotationAngleInDegrees = 0;
            return;
        }

        if (_playing && _windowShown)
        {
            _driftClock.Start();
            ShowDrift(_driftClock.Seconds);
        }
        else
        {
            // Still, but in its place: the drift picks up from here without a jump.
            _driftClock.Pause();
            ShowDrift(_driftClock.Seconds);
        }
    }

    private void ShowDrift(double seconds)
    {
        var (zoom, angle) = SlowDrift.Cover(seconds);
        var visual = ElementCompositionPreview.GetElementVisual(_drift);
        visual.Scale = new Vector3(zoom);
        visual.RotationAngleInDegrees = angle;
    }

    private T Layer<T>(T element)
        where T : FrameworkElement
    {
        element.OpacityTransition = new ScalarTransition { Duration = LayerFade };
        element.Opacity = 0;
        Children.Add(element);
        return element;
    }
}
