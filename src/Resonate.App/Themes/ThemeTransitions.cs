using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Themes;

/// <summary>
/// The animations between two looks. Before the look changes, a picture of
/// the window is laid over it; the new look is applied underneath, and the
/// picture is then animated away (faded, split, cut into strips, wiped, or
/// cut open by a growing circle). Everything moves on the compositor, so the
/// animation runs at the display's refresh rate and the window stays usable.
/// </summary>
internal sealed class ThemeTransitions
{
    private const int BlindCount = 10;

    private readonly FrameworkElement _capture;
    private readonly Panel _overlay;
    private RenderTargetBitmap? _before;
    private int _run;

    public ThemeTransitions(FrameworkElement capture, Panel overlay)
    {
        _capture = capture;
        _overlay = overlay;
    }

    /// <summary>
    /// Covers the window with a picture of how it looks now. False if there
    /// is nothing to picture (minimised), so the caller just switches.
    /// </summary>
    public async Task<bool> CoverAsync()
    {
        Clear();
        var run = ++_run;
        if (_capture.XamlRoot is null || _capture.ActualWidth < 1 || _capture.ActualHeight < 1)
        {
            return false;
        }

        var bitmap = new RenderTargetBitmap();
        try
        {
            await bitmap.RenderAsync(_capture);
        }
        catch (Exception)
        {
            return false;
        }

        if (run != _run)
        {
            return false;
        }

        _before = bitmap;
        _overlay.Children.Add(CreateImage(bitmap));
        return true;
    }

    /// <summary>Animates the picture away, showing the new look, then removes it.</summary>
    public async Task RevealAsync(ThemeTransitionKind kind, Point? origin, ThemePalette palette, TimeSpan morphDuration)
    {
        var run = _run;
        if (_before is null)
        {
            return;
        }

        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(_overlay).Compositor;
            var size = new Vector2((float)_overlay.ActualWidth, (float)_overlay.ActualHeight);
            await (kind switch
            {
                ThemeTransitionKind.Ripple => RippleAsync(compositor, size, origin, palette),
                ThemeTransitionKind.Split => SplitAsync(compositor, size),
                ThemeTransitionKind.Blinds => BlindsAsync(compositor, size),
                ThemeTransitionKind.Wipe => WipeAsync(compositor, size, palette),
                _ => FadeAsync(compositor, morphDuration * 0.75),
            });
        }
        finally
        {
            if (run == _run)
            {
                Clear();
            }
        }
    }

    /// <summary>The old look dissolves into the new one (the colours slide underneath).</summary>
    private Task FadeAsync(Compositor compositor, TimeSpan duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_overlay.Children[0]);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, Standard(compositor));
        fade.Duration = duration;
        return RunAsync(compositor, () => visual.StartAnimation("Opacity", fade));
    }

    /// <summary>The new look grows in a circle from where the user clicked, with a thin ring at its edge.</summary>
    private async Task RippleAsync(Compositor compositor, Vector2 size, Point? origin, ThemePalette palette)
    {
        // A second picture, of the new look, revealed through the circle.
        _capture.UpdateLayout();
        var after = new RenderTargetBitmap();
        try
        {
            await after.RenderAsync(_capture);
        }
        catch (Exception)
        {
            await FadeAsync(compositor, TimeSpan.FromMilliseconds(300));
            return;
        }

        var image = CreateImage(after);
        _overlay.Children.Add(image);

        var center = origin is { } point ? new Vector2((float)point.X, (float)point.Y) : size / 2;
        var reach = new[] { Vector2.Zero, new Vector2(size.X, 0), new Vector2(0, size.Y), size }.Max(corner => Vector2.Distance(center, corner));
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(420 + (reach * 0.18), 480, 760));

        var circle = compositor.CreateEllipseGeometry();
        circle.Center = center;
        circle.Radius = Vector2.Zero;
        ElementCompositionPreview.GetElementVisual(image).Clip = compositor.CreateGeometricClip(circle);

        var ringGeometry = compositor.CreateEllipseGeometry();
        ringGeometry.Center = center;
        var ringShape = compositor.CreateSpriteShape(ringGeometry);
        ringShape.StrokeBrush = compositor.CreateColorBrush(palette.Accent.ToColor());
        ringShape.StrokeThickness = 2.5f;
        var ring = compositor.CreateShapeVisual();
        ring.Size = size;
        ring.Shapes.Add(ringShape);
        ElementCompositionPreview.SetElementChildVisual(_overlay, ring);

        var grow = compositor.CreateVector2KeyFrameAnimation();
        grow.InsertKeyFrame(1, new Vector2(reach), Decelerate(compositor));
        grow.Duration = duration;

        var ringFade = compositor.CreateScalarKeyFrameAnimation();
        ringFade.InsertKeyFrame(0, 0.9f);
        ringFade.InsertKeyFrame(1, 0, Standard(compositor));
        ringFade.Duration = duration;

        await RunAsync(compositor, () =>
        {
            circle.StartAnimation("Radius", grow);
            ringGeometry.StartAnimation("Radius", grow);
            ring.StartAnimation("Opacity", ringFade);
        });
    }

    /// <summary>The old look splits down the middle and both halves slide out to the sides.</summary>
    private Task SplitAsync(Compositor compositor, Vector2 size)
    {
        _overlay.Children.Clear();
        var half = size.X / 2;
        var left = CreateImage(_before!, new Rect(0, 0, half + 0.5, size.Y));
        var right = CreateImage(_before!, new Rect(half - 0.5, 0, half + 0.5, size.Y));
        _overlay.Children.Add(left);
        _overlay.Children.Add(right);

        var duration = TimeSpan.FromMilliseconds(680);
        return RunAsync(compositor, () =>
        {
            Slide(left, new Vector3(-(half + 32), 0, 0));
            Slide(right, new Vector3(half + 32, 0, 0));
        });

        void Slide(UIElement element, Vector3 to)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1, to, Emphasized(compositor));
            move.Duration = duration;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0.55f, 1);
            fade.InsertKeyFrame(1, 0.4f);
            fade.Duration = duration;
            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Opacity", fade);
        }
    }

    /// <summary>The old look is cut into strips that fold away one after another, left to right.</summary>
    private Task BlindsAsync(Compositor compositor, Vector2 size)
    {
        _overlay.Children.Clear();
        var strip = size.X / BlindCount;
        var strips = new List<UIElement>(BlindCount);
        for (var i = 0; i < BlindCount; i++)
        {
            var image = CreateImage(_before!, new Rect(i * strip, 0, strip + 1, size.Y));
            _overlay.Children.Add(image);
            strips.Add(image);
        }

        return RunAsync(compositor, () =>
        {
            for (var i = 0; i < strips.Count; i++)
            {
                var visual = ElementCompositionPreview.GetElementVisual(strips[i]);
                visual.CenterPoint = new Vector3((i + 0.5f) * strip, size.Y / 2, 0);
                var delay = TimeSpan.FromMilliseconds(i * 42);
                var duration = TimeSpan.FromMilliseconds(400);

                var fold = compositor.CreateVector3KeyFrameAnimation();
                fold.InsertKeyFrame(1, new Vector3(0, 0.92f, 1), Accelerate(compositor));
                fold.Duration = duration;
                fold.DelayTime = delay;

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1, 0, Accelerate(compositor));
                fade.Duration = duration;
                fade.DelayTime = delay;

                visual.StartAnimation("Scale", fold);
                visual.StartAnimation("Opacity", fade);
            }
        });
    }

    /// <summary>A glowing edge in the new accent colour sweeps across, leaving the new look behind it.</summary>
    private Task WipeAsync(Compositor compositor, Vector2 size, ThemePalette palette)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_overlay.Children[0]);
        var clip = compositor.CreateInsetClip();
        visual.Clip = clip;

        const float GlowWidth = 180;
        var glow = compositor.CreateLinearGradientBrush();
        glow.StartPoint = new Vector2(0, 0.5f);
        glow.EndPoint = new Vector2(1, 0.5f);
        var accent = palette.Accent;
        glow.ColorStops.Add(compositor.CreateColorGradientStop(0, accent.WithAlpha(0).ToColor()));
        glow.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, accent.WithAlpha(0.55).ToColor()));
        glow.ColorStops.Add(compositor.CreateColorGradientStop(1, accent.WithAlpha(0).ToColor()));
        var edge = compositor.CreateSpriteVisual();
        edge.Brush = glow;
        edge.Size = new Vector2(GlowWidth, size.Y);
        edge.Offset = new Vector3(-GlowWidth / 2, 0, 0);
        ElementCompositionPreview.SetElementChildVisual(_overlay, edge);

        var duration = TimeSpan.FromMilliseconds(700);
        var sweep = compositor.CreateScalarKeyFrameAnimation();
        sweep.InsertKeyFrame(1, size.X, Emphasized(compositor));
        sweep.Duration = duration;

        var move = compositor.CreateScalarKeyFrameAnimation();
        move.InsertKeyFrame(1, size.X - (GlowWidth / 2), Emphasized(compositor));
        move.Duration = duration;

        return RunAsync(compositor, () =>
        {
            clip.StartAnimation("LeftInset", sweep);
            edge.StartAnimation("Offset.X", move);
        });
    }

    /// <summary>Removes the pictures and anything drawn for the transition.</summary>
    public void Clear()
    {
        foreach (var image in _overlay.Children.OfType<Image>())
        {
            image.Source = null;
        }

        _overlay.Children.Clear();
        ElementCompositionPreview.SetElementChildVisual(_overlay, null);
        _before = null;
    }

    private static Image CreateImage(ImageSource source, Rect? clip = null) => new()
    {
        Source = source,
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        Clip = clip is { } rect ? new RectangleGeometry { Rect = rect } : null,
    };

    private static Task RunAsync(Compositor compositor, Action start)
    {
        var done = new TaskCompletionSource();
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        start();
        batch.End();
        batch.Completed += (_, _) => done.TrySetResult();
        return done.Task;
    }

    // Fluent motion curves.
    private static CompositionEasingFunction Standard(Compositor c) => c.CreateCubicBezierEasingFunction(new(0.4f, 0f), new(0.2f, 1f));

    private static CompositionEasingFunction Decelerate(Compositor c) => c.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f));

    private static CompositionEasingFunction Accelerate(Compositor c) => c.CreateCubicBezierEasingFunction(new(0.7f, 0f), new(1f, 0.5f));

    private static CompositionEasingFunction Emphasized(Compositor c) => c.CreateCubicBezierEasingFunction(new(0.65f, 0f), new(0.35f, 1f));
}
