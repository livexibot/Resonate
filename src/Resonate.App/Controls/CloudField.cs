using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Themes;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Controls;

/// <summary>
/// The now-playing stage's background: five soft clouds in the cover's
/// colours that drift slowly, drawn and moved by the compositor (sprites,
/// nothing per frame on the interface thread). Each cloud is its colour
/// shown through one shared, dithered mask (<see cref="CloudMask"/>), not a
/// radial gradient: on large dark areas a gradient shows rings, because the
/// screen has too few shades between two dark colours. New colours flow in
/// over a second. The drift only runs while <see cref="SetRunning"/> says
/// so, and stands still otherwise, so a paused song, a hidden window or a
/// stage scrolled away costs nothing.
/// </summary>
internal sealed partial class CloudField : Grid
{
    private static readonly TimeSpan ColourChange = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Appear = TimeSpan.FromMilliseconds(400);
    private static readonly string[] Animated = ["Offset", "Scale"];

    // How much of the clouds' colour stays while they stand still.
    private const float RestingOpacity = 0.8f;

    // Where each cloud sits (0 to 1 across and down), its size against the
    // field's longer side, and how long its drift and its breathing take
    // (whole seconds that share no factor, so the pattern never repeats).
    private static readonly (float X, float Y, float Size, int Drift, int Breath)[] Clouds =
    [
        (0.22f, 0.32f, 0.95f, 47, 31),
        (0.78f, 0.28f, 0.80f, 53, 37),
        (0.62f, 0.82f, 0.75f, 59, 41),
        (0.12f, 0.86f, 0.62f, 61, 43),
        (0.92f, 0.70f, 0.58f, 67, 29),
    ];

    // The mask, decoded once for every field: read while loading, and set once it is done.
    private static Task<LoadedImageSurface?>? _maskLoading;

    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionSurfaceBrush _mask;
    private readonly SpriteVisual[] _sprites = new SpriteVisual[Clouds.Length];
    private readonly CompositionColorBrush[] _tints = new CompositionColorBrush[Clouds.Length];
    private readonly List<AnimationController> _drift = [];
    private readonly DispatcherQueueTimer _resize;
    private Vector2 _size;
    private Vector2 _builtFor;
    private bool _running;
    private bool _animate;
    private bool _maskReady;

    public CloudField()
    {
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();

        // Nothing shows until the mask is ready, and then the clouds fade in.
        _root.Opacity = 0;
        _shape = _compositor.CreateRoundedRectangleGeometry();
        _root.Clip = _compositor.CreateGeometricClip(_shape);
        _mask = _compositor.CreateSurfaceBrush();
        _mask.Stretch = CompositionStretch.Fill;
        _mask.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;

        for (var i = 0; i < Clouds.Length; i++)
        {
            _tints[i] = _compositor.CreateColorBrush(default);
            var brush = _compositor.CreateMaskBrush();
            brush.Source = _tints[i];
            brush.Mask = _mask;
            var sprite = _compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            _sprites[i] = sprite;
            _root.Children.InsertAtTop(sprite);
        }

        ElementCompositionPreview.SetElementChildVisual(this, _root);
        _ = UseMaskAsync();

        // A window being dragged larger resizes the field every frame; the drift is laid out again once it settles.
        _resize = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _resize.Interval = TimeSpan.FromMilliseconds(150);
        _resize.IsRepeating = false;
        _resize.Tick += (_, _) => Build();
        SizeChanged += OnSizeChanged;
        Unloaded += (_, _) =>
        {
            _resize.Stop();
            StopDrift();
            _builtFor = default;
        };
        Loaded += (_, _) => Build();
    }

    /// <summary>Why the cloud mask could not be loaded; null while it loads or once it has.</summary>
    public static string? MaskError { get; private set; }

    /// <summary>For CI's screenshot tour: loads the mask if no field has yet, and says why it failed (null once it is ready).</summary>
    public static async Task<string?> CheckMaskAsync() =>
        await LoadMask() is null ? MaskError ?? "It did not load." : null;

    /// <summary>The field's rounded corners, matching the card it fills (0 for square).</summary>
    public float CornerRadiusValue
    {
        get => _shape.CornerRadius.X;
        set => _shape.CornerRadius = new Vector2(value, value);
    }

    /// <summary>
    /// Paints the clouds in <paramref name="colours"/> (one per cloud, cycled
    /// if fewer) at <paramref name="strength"/> (0 to 1), flowing over a
    /// second when <paramref name="animate"/>.
    /// </summary>
    public void SetColours(IReadOnlyList<ThemeColor> colours, double strength, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < Clouds.Length; i++)
        {
            // As strong in the middle as the gradients these clouds once were; the mask fades them out.
            Paint(_tints[i], colours[i % colours.Count].WithAlpha(0.9 * strength), animate);
        }
    }

    /// <summary>
    /// Whether the clouds drift (<paramref name="running"/>) and whether
    /// any change may animate (<paramref name="animate"/>: Windows' animations
    /// are on). A stopped field keeps its place, and a little of its colour fades.
    /// </summary>
    public void SetRunning(bool running, bool animate)
    {
        _animate = animate;
        running &= animate;
        if (running == _running)
        {
            return;
        }

        _running = running;
        if (running && _drift.Count == 0)
        {
            Build();
        }

        foreach (var controller in _drift)
        {
            if (running)
            {
                controller.Resume();
            }
            else
            {
                controller.Pause();
            }
        }

        Fade(Settle);
    }

    private static Task<LoadedImageSurface?> LoadMask() => _maskLoading ??= LoadMaskAsync();

    private static async Task<LoadedImageSurface?> LoadMaskAsync()
    {
        try
        {
            var png = await Task.Run(() => CloudMask.Png());
            var stream = await ImageStreams.FromBytesAsync(png);
            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            var loaded = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.LoadCompleted += (_, e) =>
            {
                stream.Dispose();
                loaded.TrySetResult(e.Status);
            };

            var status = await loaded.Task;
            if (status != LoadedImageSourceLoadStatus.Success)
            {
                MaskError = $"Windows could not decode it ({status}).";
                return null;
            }

            MaskError = null;
            return surface;
        }
        catch (Exception ex)
        {
            MaskError = ex.Message;
            return null;
        }
    }

    private async Task UseMaskAsync()
    {
        var surface = await LoadMask();
        if (surface is null)
        {
            // No clouds at all rather than hard-edged ones; the stage's own colour shows.
            return;
        }

        _mask.Surface = surface;
        _maskReady = true;
        Fade(Appear);
    }

    /// <summary>Full while drifting, a little less while still, nothing before the mask is ready.</summary>
    private void Fade(TimeSpan duration)
    {
        var opacity = !_maskReady ? 0f : _running ? 1f : RestingOpacity;
        if (!_animate)
        {
            _root.StopAnimation("Opacity");
            _root.Opacity = opacity;
            return;
        }

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, opacity);
        fade.Duration = duration;
        _root.StartAnimation("Opacity", fade);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        _shape.Size = _size;
        if (_drift.Count == 0 || _builtFor == default)
        {
            Build();
        }
        else
        {
            _resize.Stop();
            _resize.Start();
        }
    }

    /// <summary>Places the clouds for the field's size, and starts their drift while running.</summary>
    private void Build()
    {
        if (_size.X <= 0 || _size.Y <= 0 || !IsLoaded)
        {
            return;
        }

        StopDrift();
        _builtFor = _size;
        var side = Math.Max(_size.X, _size.Y);
        var linear = _compositor.CreateLinearEasingFunction();
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0), new Vector2(0.55f, 1));
        for (var i = 0; i < Clouds.Length; i++)
        {
            var (x, y, size, drift, breath) = Clouds[i];
            var sprite = _sprites[i];
            var diameter = side * size;
            sprite.Size = new Vector2(diameter, diameter);
            sprite.CenterPoint = new Vector3(diameter / 2, diameter / 2, 0);
            var home = new Vector3((_size.X * x) - (diameter / 2), (_size.Y * y) - (diameter / 2), 0);
            sprite.Offset = home;
            sprite.Scale = Vector3.One;
            if (!_running)
            {
                // Still: the drift starts from here when the music does.
                continue;
            }

            // A slow loop around the cloud's place, a tenth of the field each way.
            var reachX = _size.X * 0.1f;
            var reachY = _size.Y * 0.1f;
            var path = _compositor.CreateVector3KeyFrameAnimation();
            path.InsertKeyFrame(0, home, linear);
            path.InsertKeyFrame(0.25f, home + new Vector3(reachX, -reachY * 0.6f, 0), ease);
            path.InsertKeyFrame(0.5f, home + new Vector3(reachX * 0.3f, reachY, 0), ease);
            path.InsertKeyFrame(0.75f, home + new Vector3(-reachX, reachY * 0.4f, 0), ease);
            path.InsertKeyFrame(1, home, ease);
            path.Duration = TimeSpan.FromSeconds(drift);
            path.IterationBehavior = AnimationIterationBehavior.Forever;
            sprite.StartAnimation("Offset", path);

            var grow = _compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, Vector3.One, linear);
            grow.InsertKeyFrame(0.5f, new Vector3(1.18f, 1.18f, 1), ease);
            grow.InsertKeyFrame(1, Vector3.One, ease);
            grow.Duration = TimeSpan.FromSeconds(breath);
            grow.IterationBehavior = AnimationIterationBehavior.Forever;
            sprite.StartAnimation("Scale", grow);

            foreach (var property in Animated)
            {
                if (sprite.TryGetAnimationController(property) is { } controller)
                {
                    _drift.Add(controller);
                }
            }
        }
    }

    private void StopDrift()
    {
        _drift.Clear();
        foreach (var sprite in _sprites)
        {
            sprite.StopAnimation("Offset");
            sprite.StopAnimation("Scale");
        }
    }

    private void Paint(CompositionColorBrush tint, ThemeColor colour, bool animate)
    {
        var target = colour.ToColor();
        if (!animate || !_animate)
        {
            tint.StopAnimation("Color");
            tint.Color = target;
            return;
        }

        var flow = _compositor.CreateColorKeyFrameAnimation();
        flow.InsertKeyFrame(1, target);
        flow.Duration = ColourChange;
        tint.StartAnimation("Color", flow);
    }
}
