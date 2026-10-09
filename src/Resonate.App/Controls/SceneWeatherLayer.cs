using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The weather of a special look, drifting over the whole window: sakura
/// petals for Japan, snow for Snow (<see cref="SceneWeather"/>). Soft sprites
/// the compositor moves from one clock with expressions, so nothing runs on
/// the interface thread. It moves only while the window shows and Windows'
/// animations are on; otherwise every animation stops and nothing shows.
/// Never in the way of a click.
/// </summary>
internal sealed partial class SceneWeatherLayer : Grid
{
    private const string P = "p";

    private readonly ThemeService _theme;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionPropertySet _props;
    private readonly List<(SpriteVisual Sprite, CompositionRadialGradientBrush Brush, CompositionColorGradientStop[] Stops, SceneWeather.Particle Particle)> _particles = [];
    private AnimationController? _clock;
    private MainWindow? _window;
    private ThemeScene _scene;
    private bool _moving;

    public SceneWeatherLayer(ThemeService theme)
    {
        _theme = theme;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _props = _compositor.CreatePropertySet();
        _props.InsertScalar("Time", 0);
        _props.InsertScalar("W", 0);
        _props.InsertScalar("H", 0);
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        SizeChanged += (_, e) =>
        {
            _props.InsertScalar("W", (float)e.NewSize.Width);
            _props.InsertScalar("H", (float)e.NewSize.Height);
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>For CI's screenshot tour: starts both scenes' motion on visuals nobody sees, so a mistake in an expression shows. Null when all is well.</summary>
    public static string? CheckMotion(Compositor compositor)
    {
        var props = compositor.CreatePropertySet();
        props.InsertScalar("Time", 1);
        props.InsertScalar("W", 800);
        props.InsertScalar("H", 600);
        var sprite = compositor.CreateSpriteVisual();
        try
        {
            foreach (var scene in (ThemeScene[])[ThemeScene.Japan, ThemeScene.Snow])
            {
                var particle = SceneWeather.Get(scene, 0);
                foreach (var (property, expression) in Motion(particle, scene))
                {
                    var animation = compositor.CreateExpressionAnimation(expression);
                    animation.SetReferenceParameter(P, props);
                    sprite.StartAnimation(property, animation);
                    sprite.StopAnimation(property);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            sprite.Dispose();
            props.Dispose();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can come twice in a row; each handler is held once.
        _theme.Changed -= OnThemeChanged;
        _theme.Changed += OnThemeChanged;
        _window = App.MainWindow;
        if (_window is not null)
        {
            _window.ShownChanged -= OnShownChanged;
            _window.ShownChanged += OnShownChanged;
        }

        Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        if (_window is not null)
        {
            _window.ShownChanged -= OnShownChanged;
            _window = null;
        }

        Stop();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void OnShownChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>The look's weather: made afresh when the scene changes, then moving or resting as the window allows.</summary>
    private void Show()
    {
        var scene = _theme.Current.Scene;
        if (scene != _scene)
        {
            Stop();
            Clear();
            _scene = scene;
            Make();
        }

        Refresh();
    }

    private void Refresh()
    {
        var move = _particles.Count > 0 && (_window?.IsShown ?? true) && _theme.AnimationsEnabled;
        if (move && !_moving)
        {
            Start();
        }
        else if (!move && _moving)
        {
            Stop();
        }
    }

    private void Make()
    {
        for (var i = 0; i < SceneWeather.Count(_scene); i++)
        {
            var particle = SceneWeather.Get(_scene, i);
            var brush = _compositor.CreateRadialGradientBrush();
            var stops = Stops(_scene, particle);
            foreach (var stop in stops)
            {
                brush.ColorStops.Add(stop);
            }

            var sprite = _compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            var size = (float)particle.Size;

            // A petal is an oval; a flake is round.
            sprite.Size = _scene == ThemeScene.Japan ? new Vector2(size, size * 0.66f) : new Vector2(size);
            sprite.CenterPoint = new Vector3(sprite.Size / 2, 0);
            sprite.Opacity = 0;
            _root.Children.InsertAtTop(sprite);
            _particles.Add((sprite, brush, stops, particle));
        }
    }

    /// <summary>A petal's blush, pale at its tip and deeper at its heart; a flake's soft white.</summary>
    private CompositionColorGradientStop[] Stops(ThemeScene scene, SceneWeather.Particle particle)
    {
        if (scene == ThemeScene.Japan)
        {
            var pinks = (ThemeColor[])[ThemeColor.FromRgb(0xF7B6CB), ThemeColor.FromRgb(0xF29CB8), ThemeColor.FromRgb(0xFBD3E0)];
            var pink = pinks[(int)(particle.Start * pinks.Length) % pinks.Length];
            return
            [
                _compositor.CreateColorGradientStop(0, pink.Mix(ThemeColor.FromRgb(0xE0688F), 0.35).ToColor()),
                _compositor.CreateColorGradientStop(0.7f, pink.ToColor()),
                _compositor.CreateColorGradientStop(1, pink.WithAlpha(0).ToColor()),
            ];
        }

        // The large, soft flakes look out of focus; the small ones crisp.
        var soft = particle.Size > SceneWeather.FlakeSize * 2;
        return
        [
            _compositor.CreateColorGradientStop(0, ThemeColor.White.ToColor()),
            _compositor.CreateColorGradientStop(soft ? 0.2f : 0.55f, ThemeColor.FromRgb(0xEAF4FF).WithAlpha(soft ? 0.7 : 0.95).ToColor()),
            _compositor.CreateColorGradientStop(1, ThemeColor.FromRgb(0xEAF4FF).WithAlpha(0).ToColor()),
        ];
    }

    /// <summary>Each particle's motion: where it is, how bright, and for a petal how it turns and flutters.</summary>
    private static IEnumerable<(string Property, string Expression)> Motion(SceneWeather.Particle p, ThemeScene scene)
    {
        static string N(double value) => VisualizerShapes.Number(value);
        var size = N(p.Size);

        // How far through its fall, 0 at the top to 1 below the bottom.
        var fall = $"Mod({P}.Time * {N(p.Fall)} + {N(p.Start)}, 1)";
        yield return ("Offset", $"Vector3({N(p.X)} * {P}.W + {N(p.Wind)} * {fall} * {P}.W + {N(p.Sway)} * Sin({P}.Time * {N(p.SwaySpeed)} + {N(p.SwayPhase)}), -{size} + {fall} * ({P}.H + 2 * {size}), 0)");
        yield return ("Opacity", $"{N(p.Opacity)} * Clamp({fall} * 12, 0, 1) * Clamp((1 - {fall}) * 12, 0, 1)");
        if (scene == ThemeScene.Japan)
        {
            yield return ("RotationAngleInDegrees", $"{N(p.Start * 360)} + {P}.Time * {N(p.Spin)}");
            yield return ("Scale", $"Vector3(0.3 + 0.7 * Abs(Cos({P}.Time * {N(p.Flutter)} + {N(p.SwayPhase)})), 1, 1)");
        }
    }

    private void Start()
    {
        if (_clock is null)
        {
            var clock = _compositor.CreateScalarKeyFrameAnimation();
            clock.InsertKeyFrame(0, 0, _compositor.CreateLinearEasingFunction());
            clock.InsertKeyFrame(1, (float)SceneWeather.LoopSeconds, _compositor.CreateLinearEasingFunction());
            clock.Duration = TimeSpan.FromSeconds(SceneWeather.LoopSeconds);
            clock.IterationBehavior = AnimationIterationBehavior.Forever;
            _props.StartAnimation("Time", clock);
            _clock = _props.TryGetAnimationController("Time");
        }
        else
        {
            _clock.Resume();
        }

        foreach (var (sprite, _, _, particle) in _particles)
        {
            foreach (var (property, expression) in Motion(particle, _scene))
            {
                var animation = _compositor.CreateExpressionAnimation(expression);
                animation.SetReferenceParameter(P, _props);
                sprite.StartAnimation(property, animation);
            }
        }

        _moving = true;
    }

    /// <summary>Every animation ends and the weather hides, so nothing redraws while the window is away.</summary>
    private void Stop()
    {
        _clock?.Pause();
        foreach (var (sprite, _, _, _) in _particles)
        {
            sprite.StopAnimation("Offset");
            sprite.StopAnimation("Opacity");
            sprite.StopAnimation("RotationAngleInDegrees");
            sprite.StopAnimation("Scale");
            sprite.Opacity = 0;
        }

        _moving = false;
    }

    private void Clear()
    {
        _root.Children.RemoveAll();
        foreach (var (sprite, brush, stops, _) in _particles)
        {
            sprite.Brush = null;
            foreach (var stop in stops)
            {
                stop.Dispose();
            }

            brush.Dispose();
            sprite.Dispose();
        }

        _particles.Clear();
    }
}
