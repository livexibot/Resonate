using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Controls;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Themes;

/// <summary>
/// The animations between two looks. Before the look changes, a picture is
/// taken of the window as it looks now. Most kinds lay the picture over the
/// window, apply the new look underneath, and move the picture out of the
/// way (fade it, split it, fold it into strips or wipe it). Ripple and Grow
/// put it under the window instead and reveal the new look itself through a
/// growing shape, so the new look is live from the first frame and one
/// picture is enough.
/// <para>
/// Each transition follows one value, its progress from 0 to 1, which the
/// compositor animates with the kind's curve (<see cref="ThemeTransitionCatalog"/>).
/// Everything that moves is an expression of it, so the animation runs at the
/// display's refresh rate without the interface thread, can be held at any
/// point for a screenshot, and stops completely when it ends.
/// </para>
/// </summary>
internal sealed class ThemeTransitions
{
    private const int BlindCount = 10;
    private const float GlowWidth = 180;
    private const float EdgeWidth = 3;
    private const string ProgressName = "P";

    // A clip scaled to exactly nothing cannot be inverted; this is far below a pixel.
    private const string RevealScale = "Max(Sqrt(Max(p.P, 0)), 0.0001)";

    private readonly ThemeHost _host;
    private readonly List<(CompositionObject Target, string Property)> _bound = [];
    private Compositor? _compositor;
    private CompositionPropertySet? _progress;
    private RenderTargetBitmap? _picture;
    private Task<bool> _rendering = Task.FromResult(false);
    private bool _covered;
    private int _run;
    private int _play;

    // The transition on screen.
    private TaskCompletionSource? _done;
    private ThemeTransitionSpec _spec;
    private Action<long>? _moving;
    private int _framesSinceStart;
    private bool _watchingFrames;
    private int _batch;
    private bool _frozen;

    public ThemeTransitions(ThemeHost host)
    {
        _host = host;

        // The picture and the shapes are the window's old size: a window
        // resized (maximised, snapped) mid-switch shows the new look at once.
        host.SizeChanged += (_, e) =>
        {
            if (Showing is not null && e.PreviousSize != e.NewSize)
            {
                Clear();
            }
        };
    }

    /// <summary>The kind on screen right now, or null when none plays.</summary>
    public ThemeTransitionKind? Showing { get; private set; }

    /// <summary>Why the last transition that failed could not play (the look then switched at once), for the speed test.</summary>
    public string? Failure { get; private set; }

    private Compositor Compositor => _compositor ??= ElementCompositionPreview.GetElementVisual(_host).Compositor;

    private bool IsOnScreen => _host.Overlay.Children.Count > 0 || _host.Underlay.Children.Count > 0;

    /// <summary>
    /// Takes a picture of the window as it looks now (with any transition
    /// still on screen, so a switch during a switch carries on from where it
    /// was) for the next <see cref="PlayAsync"/>. False if there is nothing
    /// to picture, or a newer switch took over; the caller then just switches.
    /// </summary>
    public async Task<bool> CoverAsync()
    {
        var run = ++_run;
        _covered = false;
        if (_host.XamlRoot is null || _host.ActualWidth < 1 || _host.ActualHeight < 1)
        {
            return false;
        }

        // One picture at a time: a switch that follows quickly waits for the
        // picture before it, which is then never shown.
        await _rendering;
        if (run != _run)
        {
            return false;
        }

        // The same bitmap every time (a picture of a 5K window is about 43 MB),
        // unless it is on screen: drawing a picture into itself is not safe.
        if (_picture is null || IsOnScreen)
        {
            _picture = new RenderTargetBitmap();
        }

        var rendering = RenderAsync(_picture, _host);
        _rendering = rendering;
        var taken = await rendering;
        if (run != _run)
        {
            return false;
        }

        _covered = taken;
        return taken;
    }

    /// <summary>
    /// Ends the transition on screen, puts the picture in place, calls
    /// <paramref name="apply"/> to show the new look and animates from one to
    /// the other, all in this one tick, so no frame shows the new look
    /// uncovered. <paramref name="moving"/> gets the time of the first frame
    /// drawn after the switch. Without a picture, the new look shows at once.
    /// </summary>
    public async Task PlayAsync(ThemeTransitionKind kind, Point? origin, ThemePalette palette, ThemeTransitionSpec spec, Action apply, Action<long> moving)
    {
        if (!_covered)
        {
            Clear();
            apply();
            moving(Stopwatch.GetTimestamp());
            return;
        }

        Clear();
        _covered = false;
        var play = ++_play;
        var size = new Vector2((float)_host.ActualWidth, (float)_host.ActualHeight);
        _progress ??= Compositor.CreatePropertySet();
        _progress.InsertScalar(ProgressName, 0);
        if (kind is not (ThemeTransitionKind.Ripple or ThemeTransitionKind.Grow or ThemeTransitionKind.Split or ThemeTransitionKind.Blinds or ThemeTransitionKind.Wipe or ThemeTransitionKind.Morph))
        {
            kind = ThemeTransitionKind.Fade;
        }

        // Set first, so Clear also tidies up after a failure part of the way.
        Showing = kind;
        try
        {
            try
            {
                spec = Build(kind, size, origin, palette, spec);
            }
            catch (Exception ex)
            {
                // A transition that cannot be built must never keep the new look away.
                Failure = $"{kind}: {ex.Message}";
                Clear();
                apply();
                moving(Stopwatch.GetTimestamp());
                return;
            }

            apply();
            _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _spec = spec;
            _moving = moving;
            _framesSinceStart = 0;
            WatchFrames(true);
            Animate(0, spec);
            await _done.Task;
        }
        finally
        {
            // Unless a newer switch has already cleared this one away and put up its own.
            if (play == _play)
            {
                Clear();
            }
        }
    }

    /// <summary>
    /// For the screenshot tour: holds the transition on screen at
    /// <paramref name="progress"/> until <see cref="Resume"/>. False when none plays.
    /// </summary>
    public bool Freeze(double progress)
    {
        if (_done is null || _progress is null)
        {
            return false;
        }

        _frozen = true;
        _progress.StopAnimation(ProgressName);
        _progress.InsertScalar(ProgressName, (float)Math.Clamp(progress, 0, 1));
        return true;
    }

    /// <summary>Lets a held transition finish from where it was held.</summary>
    public void Resume()
    {
        if (!_frozen || _done is null || _progress is null)
        {
            return;
        }

        _frozen = false;
        _progress.TryGetScalar(ProgressName, out var at);
        Animate(at, _spec with { Duration = _spec.Duration * Math.Max(1 - at, 0.1) });
    }

    /// <summary>
    /// Ends the transition on screen at once: the new look stays, everything
    /// drawn for the transition goes, and nothing is left animating.
    /// </summary>
    public void Clear()
    {
        _frozen = false;
        if (Showing is null && _done is null && _moving is null && _bound.Count == 0)
        {
            return;
        }

        _batch++;
        WatchFrames(false);
        _progress?.StopAnimation(ProgressName);
        foreach (var (target, property) in _bound)
        {
            target.StopAnimation(property);
        }

        _bound.Clear();
        if (Showing is not null)
        {
            ElementCompositionPreview.GetElementVisual(_host.Scene).Clip = null;
            _host.Scene.Background = null;
            var edge = ElementCompositionPreview.GetElementVisual(_host.Edge);
            edge.Clip = null;
            edge.Opacity = 1;
            _host.Edge.Background = null;
            _host.Edge.Visibility = Visibility.Collapsed;
            Empty(_host.Overlay);
            Empty(_host.Underlay);
            Showing = null;
        }

        var moving = _moving;
        _moving = null;
        moving?.Invoke(Stopwatch.GetTimestamp());
        var done = _done;
        _done = null;
        done?.TrySetResult();
    }

    /// <summary>Puts up what the kind animates, bound to the progress; a ripple's duration depends on its reach.</summary>
    private ThemeTransitionSpec Build(ThemeTransitionKind kind, Vector2 size, Point? origin, ThemePalette palette, ThemeTransitionSpec spec)
    {
        switch (kind)
        {
            case ThemeTransitionKind.Ripple:
            case ThemeTransitionKind.Grow:
                return Reveal(kind, size, origin, palette, spec);
            case ThemeTransitionKind.Split:
                Split(size);
                break;
            case ThemeTransitionKind.Blinds:
                Blinds(size);
                break;
            case ThemeTransitionKind.Wipe:
                Wipe(size, palette);
                break;
            default:
                CrossFade(morph: kind == ThemeTransitionKind.Morph);
                break;
        }

        return spec;
    }

    /// <summary>
    /// The old look dissolves into the new one. For Morph only the shapes do,
    /// over the first half, while the colours flow underneath the whole time.
    /// </summary>
    private void CrossFade(bool morph)
    {
        var picture = Picture();
        _host.Overlay.Children.Add(picture);
        Bind(VisualOf(picture), "Opacity", morph ? $"1 - {Smooth("Clamp(p.P * 2, 0, 1)")}" : "1 - p.P");
    }

    /// <summary>
    /// The new look grows over the old one: in a circle from where the user
    /// clicked (Ripple), or in the window's own shape from its middle (Grow),
    /// with a thin edge in the new accent colour. The old look is a picture
    /// underneath; the new one is the window itself, clipped.
    /// </summary>
    private ThemeTransitionSpec Reveal(ThemeTransitionKind kind, Vector2 size, Point? origin, ThemePalette palette, ThemeTransitionSpec spec)
    {
        _host.Underlay.Children.Add(Picture());

        // Wherever the new look has grown it must hide the old one, also while
        // its own backdrop is still fading in.
        _host.Scene.Background = new SolidColorBrush(palette.Background.Opaque.ToColor());
        _host.Edge.Background = new SolidColorBrush(palette.Accent.Opaque.ToColor());
        _host.Edge.Visibility = Visibility.Visible;

        var compositor = Compositor;
        RectangleClip shape, edge;
        Vector2 center;
        float across, down;
        if (kind == ThemeTransitionKind.Ripple)
        {
            center = origin is { } point ? new Vector2((float)point.X, (float)point.Y) : size / 2;
            var reach = (float)ThemeTransitionCatalog.Reach(size.X, size.Y, center.X, center.Y);
            shape = Rounded(compositor, center, reach, reach, reach);
            edge = Rounded(compositor, center, reach + EdgeWidth, reach + EdgeWidth, reach + EdgeWidth);
            (across, down) = (reach, reach);
            spec = spec with { Duration = ThemeTransitionCatalog.RippleDuration(reach) };
        }
        else
        {
            center = size / 2;
            var (corner, margin) = ThemeTransitionCatalog.GrowShape(size.X, size.Y);
            across = center.X + (float)margin;
            down = center.Y + (float)margin;
            shape = Rounded(compositor, center, across, down, (float)corner);
            edge = Rounded(compositor, center, across + EdgeWidth, down + EdgeWidth, (float)corner + EdgeWidth);
        }

        ElementCompositionPreview.GetElementVisual(_host.Scene).Clip = shape;
        var edgeVisual = ElementCompositionPreview.GetElementVisual(_host.Edge);
        edgeVisual.Clip = edge;

        // The shape's area follows the curve (see RevealScale in the catalog).
        Bind(shape, "Scale", $"Vector2({RevealScale}, {RevealScale})");

        // The edge keeps its width while the shape grows: its half-size is
        // always the shape's plus the edge.
        Bind(
            edge,
            "Scale",
            $"Vector2((across * {RevealScale} + edge) / (across + edge), (down * {RevealScale} + edge) / (down + edge))",
            ("across", across),
            ("down", down),
            ("edge", EdgeWidth));

        // It shows once the shape starts to grow, and fades over the last part so the end is calm.
        Bind(edgeVisual, "Opacity", "Clamp(p.P * 25, 0, 1) * (1 - Clamp((p.P - 0.6) / 0.4, 0, 1))");
        return spec;
    }

    /// <summary>The old look splits down the middle and both halves slide out to the sides, dimming on the way.</summary>
    private void Split(Vector2 size)
    {
        var half = size.X / 2;
        var dim = $"1 - 0.7 * {Smooth("Clamp((p.P - 0.4) / 0.6, 0, 1)")}";
        foreach (var (part, direction) in new[] { (new Rect(0, 0, half + 0.5, size.Y), -1f), (new Rect(half - 0.5, 0, half + 0.5, size.Y), 1f) })
        {
            var picture = Picture(part);
            _host.Overlay.Children.Add(picture);
            ElementCompositionPreview.SetIsTranslationEnabled(picture, true);
            var visual = VisualOf(picture);
            Bind(visual, "Translation", "Vector3(distance * p.P, 0, 0)", ("distance", direction * (half + 32)));
            Bind(visual, "Opacity", dim);
        }
    }

    /// <summary>
    /// The old look is cut into strips that fold away one after another, left
    /// to right; each strip eases on its own (StripProgress in the catalog).
    /// </summary>
    private void Blinds(Vector2 size)
    {
        var width = size.X / BlindCount;
        var share = (float)ThemeTransitionCatalog.BlindsShare(BlindCount);
        var folded = Smooth("Clamp((p.P - start) / share, 0, 1)");
        for (var i = 0; i < BlindCount; i++)
        {
            var strip = Picture(new Rect(i * width, 0, width + 1, size.Y));
            _host.Overlay.Children.Add(strip);
            var visual = VisualOf(strip);
            visual.CenterPoint = new Vector3((i + 0.5f) * width, size.Y / 2, 0);
            var start = ("start", (float)(i * ThemeTransitionCatalog.BlindsStagger));
            Bind(visual, "Scale", $"Vector3(1 - {folded}, 1 - 0.08 * {folded}, 1)", start, ("share", share));
            Bind(visual, "Opacity", $"1 - {folded}", start, ("share", share));
        }
    }

    /// <summary>
    /// A glowing edge in the new accent colour sweeps across, leaving the new
    /// look behind it. It comes in from just off the left and leaves just off
    /// the right, so it never appears or vanishes in view.
    /// </summary>
    private void Wipe(Vector2 size, ThemePalette palette)
    {
        var picture = Picture();
        var clip = Compositor.CreateInsetClip();
        VisualOf(picture).Clip = clip;
        _host.Overlay.Children.Add(picture);

        var accent = palette.Accent;
        var glow = new Rectangle
        {
            Width = GlowWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = accent.WithAlpha(0).ToColor(), Offset = 0 },
                    new GradientStop { Color = accent.WithAlpha(0.55).ToColor(), Offset = 0.5 },
                    new GradientStop { Color = accent.WithAlpha(0).ToColor(), Offset = 1 },
                },
            },
        };
        _host.Overlay.Children.Add(glow);
        ElementCompositionPreview.SetIsTranslationEnabled(glow, true);

        // The glow's middle is the wipe's edge.
        Bind(clip, "LeftInset", "Max(p.P * (width + glow) - glow / 2, 0)", ("width", size.X), ("glow", GlowWidth));
        Bind(VisualOf(glow), "Translation", "Vector3(p.P * (width + glow) - glow, 0, 0)", ("width", size.X), ("glow", GlowWidth));
    }

    /// <summary>Animates the progress from <paramref name="from"/> to 1; the transition ends when it gets there, unless held meanwhile.</summary>
    private void Animate(float from, ThemeTransitionSpec spec)
    {
        var compositor = Compositor;
        var curve = spec.Curve;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(
            1,
            1,
            compositor.CreateCubicBezierEasingFunction(new((float)curve.X1, (float)curve.Y1), new((float)curve.X2, (float)curve.Y2)));
        animation.Duration = spec.Duration > TimeSpan.FromMilliseconds(1) ? spec.Duration : TimeSpan.FromMilliseconds(1);

        // Only the progress goes in the batch: the expressions never end by themselves.
        var batchNumber = ++_batch;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _progress!.StartAnimation(ProgressName, animation);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (batchNumber == _batch && !_frozen)
            {
                _done?.TrySetResult();
            }
        };
    }

    /// <summary>Makes <paramref name="property"/> of <paramref name="target"/> follow the progress, until <see cref="Clear"/>.</summary>
    private void Bind(CompositionObject target, string property, string expression, params (string Name, float Value)[] values)
    {
        var animation = Compositor.CreateExpressionAnimation(expression);
        animation.SetReferenceParameter("p", _progress);
        foreach (var (name, value) in values)
        {
            animation.SetScalarParameter(name, value);
        }

        target.StartAnimation(property, animation);
        _bound.Add((target, property));
    }

    /// <summary>Tells who waits when the first frame after the switch is drawn: the first frame lays out the new look, the second shows it moving.</summary>
    private void WatchFrames(bool watch)
    {
        if (watch != _watchingFrames)
        {
            _watchingFrames = watch;
            if (watch)
            {
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (++_framesSinceStart < 2)
        {
            return;
        }

        WatchFrames(false);
        var moving = _moving;
        _moving = null;
        moving?.Invoke(Stopwatch.GetTimestamp());
    }

    private Image Picture(Rect? part = null) => new()
    {
        Source = _picture,
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        Clip = part is { } rect ? new RectangleGeometry { Rect = rect } : null,
    };

    private static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    /// <summary>A rectangle with round corners around <paramref name="center"/>, which it also grows from.</summary>
    private static RectangleClip Rounded(Compositor compositor, Vector2 center, float across, float down, float corner)
    {
        var radius = new Vector2(corner);
        var clip = compositor.CreateRectangleClip(center.X - across, center.Y - down, center.X + across, center.Y + down, radius, radius, radius, radius);
        clip.CenterPoint = center;
        return clip;
    }

    /// <summary>Eases a value from 0 to 1 in and out (smoothstep), inside an expression.</summary>
    private static string Smooth(string x) => $"({x}) * ({x}) * (3 - 2 * ({x}))";

    private static void Empty(Panel panel)
    {
        foreach (var image in panel.Children.OfType<Image>())
        {
            image.Source = null;
        }

        panel.Children.Clear();
    }

    private static async Task<bool> RenderAsync(RenderTargetBitmap picture, UIElement element)
    {
        try
        {
            await picture.RenderAsync(element);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
