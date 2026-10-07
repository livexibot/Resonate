using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// What sits behind the panels: a plain colour, a gradient, the playing
/// song's cover blurred and drifting slowly, or the Windows material (Mica
/// or acrylic) with the theme's tint over it. Switching between them fades.
/// </summary>
internal sealed partial class BackdropLayer : Grid
{
    private static readonly TimeSpan LayerFade = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan CoverFade = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DriftPeriod = TimeSpan.FromSeconds(28);

    private readonly ThemeService _theme;
    private readonly ArtworkSampler _artwork;
    private readonly Rectangle _solid;
    private readonly Rectangle _gradient;
    private readonly Grid _artworkLayer;
    private readonly Grid _drift;
    private readonly Image[] _covers = [new(), new()];
    private readonly Rectangle _tint;
    private int _front;
    private bool _drifting;

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

        // The tiny blurred cover is stretched over the window; drawing past the
        // edges hides the blur's soft borders while it drifts.
        _artworkLayer = Layer(new Grid { Children = { _drift } });
        _artworkLayer.Background = theme.GetBrush("ResonateBackgroundBrush");
        _tint = Layer(new Rectangle { Fill = theme.GetBrush("ResonateBackdropTintBrush") });

        SizeChanged += (_, _) => UpdateDrift();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed += OnThemeChanged;
        _artwork.Changed += OnArtworkChanged;
        ShowCover(animate: false);
        UpdateLayers();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _artwork.Changed -= OnArtworkChanged;
        StopDrift();
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

    /// <summary>A slow zoom and pan, so the cover behind the panels feels alive. Only while it shows.</summary>
    private void UpdateDrift()
    {
        var visible = _theme.Current.Backdrop == WindowBackdrop.Artwork && ActualWidth > 0;
        var visual = ElementCompositionPreview.GetElementVisual(_drift);
        visual.CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        if (!visible || !_theme.AnimationsEnabled)
        {
            StopDrift();
            visual.Scale = new Vector3(1.18f);
            return;
        }

        if (_drifting)
        {
            return;
        }

        _drifting = true;
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new(0.45f, 0f), new(0.55f, 1f));

        var zoom = compositor.CreateVector3KeyFrameAnimation();
        zoom.InsertKeyFrame(0, new Vector3(1.18f));
        zoom.InsertKeyFrame(1, new Vector3(1.32f), easing);
        zoom.Duration = DriftPeriod;
        zoom.Direction = AnimationDirection.Alternate;
        zoom.IterationBehavior = AnimationIterationBehavior.Forever;

        var turn = compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(0, -2.5f);
        turn.InsertKeyFrame(1, 2.5f, easing);
        turn.Duration = DriftPeriod * 1.7;
        turn.Direction = AnimationDirection.Alternate;
        turn.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation("Scale", zoom);
        visual.StartAnimation("RotationAngleInDegrees", turn);
    }

    private void StopDrift()
    {
        if (!_drifting)
        {
            return;
        }

        _drifting = false;
        var visual = ElementCompositionPreview.GetElementVisual(_drift);
        visual.StopAnimation("Scale");
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
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
