using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Resonate.App.Helpers;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The weather of a special look, drifting over the whole window: sakura
/// petals for Japan, snow and a few crystals for Snow (<see cref="SceneWeather"/>).
/// Sprites the compositor moves with expressions from the scene's clock
/// (<see cref="SceneClock"/>, which ticks at a capped rate), so nothing else
/// runs on the interface thread. It moves only while the window shows and
/// Windows' animations are on; otherwise every animation stops and nothing
/// shows. Never in the way of a click.
/// </summary>
internal sealed partial class SceneWeatherLayer : Grid
{
    private const string Clock = "c";
    private const string Room = "r";

    private readonly ThemeService _theme;
    private readonly SceneClock _clock;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionPropertySet _room;
    private readonly List<(SpriteVisual Sprite, CompositionBrush? Owned, CompositionColorGradientStop[] Stops, SceneWeather.Particle Particle)> _particles = [];
    private MainWindow? _window;
    private ThemeScene _scene;
    private bool _moving;
    private XamlRoot? _xamlRoot;
    private double _rasterization = 1;

    public SceneWeatherLayer(ThemeService theme, SceneClock clock)
    {
        _theme = theme;
        _clock = clock;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _room = _compositor.CreatePropertySet();
        _room.InsertScalar("W", 0);
        _room.InsertScalar("H", 0);
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        SizeChanged += (_, e) =>
        {
            _room.InsertScalar("W", (float)e.NewSize.Width);
            _room.InsertScalar("H", (float)e.NewSize.Height);
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>For CI's screenshot tour: starts both scenes' motion on visuals nobody sees, so a mistake in an expression shows. Null when all is well.</summary>
    public static string? CheckMotion(Compositor compositor)
    {
        var clock = compositor.CreatePropertySet();
        clock.InsertScalar("Time", 1);
        clock.InsertScalar("Gather", 1);
        var room = compositor.CreatePropertySet();
        room.InsertScalar("W", 800);
        room.InsertScalar("H", 600);
        var sprite = compositor.CreateSpriteVisual();
        try
        {
            foreach (var scene in (ThemeScene[])[ThemeScene.Japan, ThemeScene.Snow])
            {
                for (var i = 0; i < SceneWeather.Count(scene); i++)
                {
                    foreach (var (property, expression) in Motion(SceneWeather.Get(scene, i)))
                    {
                        var animation = compositor.CreateExpressionAnimation(expression);
                        animation.SetReferenceParameter(Clock, clock);
                        animation.SetReferenceParameter(Room, room);
                        sprite.StartAnimation(property, animation);
                        sprite.StopAnimation(property);
                    }
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
            room.Dispose();
            clock.Dispose();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can come twice in a row; each handler is held once.
        _theme.Changed -= OnThemeChanged;
        _theme.Changed += OnThemeChanged;
        _theme.AnimationsChanged -= OnAnimationsChanged;
        _theme.AnimationsChanged += OnAnimationsChanged;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnRootChanged;
        }

        _xamlRoot = XamlRoot;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed += OnRootChanged;
        }

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
        _theme.AnimationsChanged -= OnAnimationsChanged;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnRootChanged;
            _xamlRoot = null;
        }

        if (_window is not null)
        {
            _window.ShownChanged -= OnShownChanged;
            _window = null;
        }

        Stop();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void OnShownChanged(object? sender, EventArgs e) => Refresh();

    private void OnAnimationsChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>On a display with another scale, petals and crystals take pictures at its sharpness.</summary>
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale == _rasterization)
        {
            return;
        }

        _rasterization = sender.RasterizationScale;
        foreach (var (sprite, _, _, particle) in _particles)
        {
            if (particle.Sprite is { } picture)
            {
                sprite.Brush = SceneSpriteBrushes.Get(_compositor, picture, particle.Size * _rasterization);
            }
        }
    }

    /// <summary>The look's weather: made afresh when the scene changes, then moving or resting as the window allows.</summary>
    private void Show()
    {
        var scene = _theme.Current.Scene;
        if (scene != _scene)
        {
            Stop();
            Clear();
            _scene = scene;
            try
            {
                Make();
            }
            catch (Exception)
            {
                // No weather rather than a broken window.
                Clear();
            }
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
        // Pictures sharp at the display's scale; the weather is drawn at the window's size, whatever the App size.
        var pixels = XamlRoot?.RasterizationScale ?? 1;
        _rasterization = pixels;
        for (var i = 0; i < SceneWeather.Count(_scene); i++)
        {
            var particle = SceneWeather.Get(_scene, i);
            var size = (float)particle.Size;
            var sprite = _compositor.CreateSpriteVisual();
            sprite.Size = new Vector2(size);
            sprite.CenterPoint = new Vector3(size / 2, size / 2, 0);
            sprite.Opacity = 0;
            if (particle.Sprite is { } picture)
            {
                // Petals and crystals are pictures, shared by every sprite that shows one.
                sprite.Brush = SceneSpriteBrushes.Get(_compositor, picture, size * pixels);
                _particles.Add((sprite, null, [], particle));
            }
            else
            {
                var brush = _compositor.CreateRadialGradientBrush();
                var stops = FlakeStops(particle);
                foreach (var stop in stops)
                {
                    brush.ColorStops.Add(stop);
                }

                sprite.Brush = brush;
                _particles.Add((sprite, brush, stops, particle));
            }

            _root.Children.InsertAtTop(sprite);
        }
    }

    /// <summary>A flake's soft white: crisp when small, out of focus when large.</summary>
    private CompositionColorGradientStop[] FlakeStops(SceneWeather.Particle particle)
    {
        var soft = particle.Size > SceneWeather.FlakeSize * 2;
        return
        [
            _compositor.CreateColorGradientStop(0, ThemeColor.White.ToColor()),
            _compositor.CreateColorGradientStop(soft ? 0.2f : 0.55f, ThemeColor.FromRgb(0xEAF4FF).WithAlpha(soft ? 0.7 : 0.95).ToColor()),
            _compositor.CreateColorGradientStop(1, ThemeColor.FromRgb(0xEAF4FF).WithAlpha(0).ToColor()),
        ];
    }

    /// <summary>Each particle's motion: where it is, how bright, and for a petal or a crystal how it turns and tumbles.</summary>
    private static IEnumerable<(string Property, string Expression)> Motion(SceneWeather.Particle p)
    {
        static string N(double value) => VisualizerShapes.Number(value);
        var size = N(p.Size);
        var half = N(p.Size / 2);

        // How far through its fall, 0 at the top to 1 below the bottom.
        var fall = $"Mod({Clock}.Time * {N(p.Fall)} + {N(p.Start)}, 1)";
        yield return ("Offset", $"Vector3({N(p.X)} * {Room}.W + {N(p.Wind)} * {fall} * {Room}.W + {N(p.Sway)} * Sin({Clock}.Time * {N(p.SwaySpeed)} + {N(p.SwayPhase)}) - {half}, -{size} + {fall} * ({Room}.H + 2 * {size}) - {half}, 0)");
        yield return ("Opacity", $"{N(p.Opacity)} * Clamp({fall} * 12, 0, 1) * Clamp((1 - {fall}) * 12, 0, 1)");
        if (p.Spin != 0)
        {
            yield return ("RotationAngleInDegrees", $"{N(p.Start * 360)} + {Clock}.Time * {N(p.Spin)}");
        }

        if (p.Flutter != 0)
        {
            // A petal flutters to nearly edge on; a crystal only tilts.
            var least = p.Sprite is SceneSprite.Crystal or SceneSprite.CrystalPlate ? 0.6 : 0.3;
            yield return ("Scale", $"Vector3({N(least)} + {N(1 - least)} * Abs(Cos({Clock}.Time * {N(p.Flutter)} + {N(p.SwayPhase)})), 1, 1)");
        }
    }

    private void Start()
    {
        _clock.Want(this, true);
        foreach (var (sprite, _, _, particle) in _particles)
        {
            foreach (var (property, expression) in Motion(particle))
            {
                var animation = _compositor.CreateExpressionAnimation(expression);
                animation.SetReferenceParameter(Clock, _clock.Props);
                animation.SetReferenceParameter(Room, _room);
                sprite.StartAnimation(property, animation);
            }
        }

        _moving = true;
    }

    /// <summary>Every animation ends and the weather hides, so nothing redraws while the window is away.</summary>
    private void Stop()
    {
        _clock.Want(this, false);
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
        foreach (var (sprite, owned, stops, _) in _particles)
        {
            // Shared picture brushes stay; a flake's own gradient goes with it.
            sprite.Brush = null;
            foreach (var stop in stops)
            {
                stop.Dispose();
            }

            owned?.Dispose();
            sprite.Dispose();
        }

        _particles.Clear();
    }
}
