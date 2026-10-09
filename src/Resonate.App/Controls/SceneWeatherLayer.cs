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
/// The farther, smaller part hangs under the player instead
/// (<see cref="MainWindow.WeatherHost"/>), over the panels, so it passes behind
/// the player; it falls from the top of the panels, drawn at the window's
/// scale whatever the App size. While the panels are hidden (signing in),
/// or that place is not to be had, it falls over the whole window; while a
/// switch that keeps the scene lays a picture of the window over it, it
/// falls over everything from where it was (<see cref="KeepInFront"/>).
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
    private readonly ContainerVisual _front;
    private readonly ContainerVisual _behind;
    private readonly CompositionPropertySet _room;
    private readonly CompositionPropertySet _behindRoom;
    private readonly List<(SpriteVisual Sprite, CompositionBrush? Owned, CompositionColorGradientStop[] Stops, SceneWeather.Particle Particle)> _particles = [];
    private MainWindow? _window;
    private ThemeScene _scene;
    private bool _moving;
    private XamlRoot? _xamlRoot;
    private double _rasterization = 1;

    // Where the farther weather belongs (under the player, while the shell it is in shows), where it hangs now
    // (null: not there), whether it is in this layer's own visual instead, and whether a switch keeps it in front.
    private FrameworkElement? _host;
    private UIElement? _shell;
    private long _shellToken;
    private FrameworkElement? _hung;
    private bool _inRoot;
    private bool _lifted;

    public SceneWeatherLayer(ThemeService theme, SceneClock clock)
    {
        _theme = theme;
        _clock = clock;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _front = _compositor.CreateContainerVisual();
        _behind = _compositor.CreateContainerVisual();
        _room = _compositor.CreatePropertySet();
        _room.InsertScalar("W", 0);
        _room.InsertScalar("H", 0);
        _behindRoom = _compositor.CreatePropertySet();
        _behindRoom.InsertScalar("W", 0);
        _behindRoom.InsertScalar("H", 0);

        // Until it has its place under the player, the farther weather falls here too, under the rest.
        _root.Children.InsertAtTop(_behind);
        _root.Children.InsertAtTop(_front);
        _inRoot = true;
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        SizeChanged += (_, e) =>
        {
            _room.InsertScalar("W", (float)e.NewSize.Width);
            _room.InsertScalar("H", (float)e.NewSize.Height);
            FitBehind();
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
        _theme.SizeChanged -= OnAppSizeChanged;
        _theme.SizeChanged += OnAppSizeChanged;
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

        Attach(_window);
        Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _theme.AnimationsChanged -= OnAnimationsChanged;
        _theme.SizeChanged -= OnAppSizeChanged;
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
        Attach(null);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void OnShownChanged(object? sender, EventArgs e) => Refresh();

    private void OnAnimationsChanged(object? sender, EventArgs e) => Refresh();

    private void OnAppSizeChanged(object? sender, EventArgs e) => FitBehind();

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => FitBehind();

    private void OnShellVisibilityChanged(DependencyObject sender, DependencyProperty property) => Place();

    /// <summary>
    /// For a switch that keeps the scene: the picture of the window laid over
    /// it would hide the farther weather (pictures do not show it), so for the
    /// switch's length it falls over everything, from where it was, and then
    /// goes back under the player. A new scene is revealed with the weather in it.
    /// </summary>
    public void KeepInFront(bool inFront)
    {
        if (_lifted != inFront)
        {
            _lifted = inFront;
            Place();
        }
    }

    /// <summary>Follows <paramref name="window"/>'s place for the farther weather, or lets it go.</summary>
    private void Attach(MainWindow? window)
    {
        var host = window?.WeatherHost;
        if (!ReferenceEquals(host, _host))
        {
            if (_host is not null)
            {
                _host.SizeChanged -= OnHostSizeChanged;
            }

            _host = host;
            if (_host is not null)
            {
                _host.SizeChanged += OnHostSizeChanged;
            }
        }

        var shell = window?.WeatherShell;
        if (!ReferenceEquals(shell, _shell))
        {
            _shell?.UnregisterPropertyChangedCallback(VisibilityProperty, _shellToken);
            _shell = shell;
            if (_shell is not null)
            {
                _shellToken = _shell.RegisterPropertyChangedCallback(VisibilityProperty, OnShellVisibilityChanged);
            }
        }

        Place();
    }

    /// <summary>
    /// Hangs the farther weather under the player, or, while that place is
    /// hidden, missing or covered by a switch's picture, under the rest of the
    /// weather in this layer.
    /// </summary>
    private void Place()
    {
        var target = !_lifted && _host is not null && _shell is { Visibility: Visibility.Visible } ? _host : null;
        if (target is not null ? ReferenceEquals(target, _hung) : _inRoot)
        {
            FitBehind();
            return;
        }

        // Out of where it is first: a visual has one place at a time.
        if (_hung is { } old)
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(old, null);
            }
            catch (Exception)
            {
                // Already gone with its window.
            }

            _hung = null;
        }

        if (_inRoot)
        {
            _root.Children.Remove(_behind);
            _inRoot = false;
        }

        if (target is not null)
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(target, _behind);
                _hung = target;
            }
            catch (Exception)
            {
                // Over everything rather than nowhere.
            }
        }

        if (_hung is null)
        {
            try
            {
                _root.Children.InsertAtBottom(_behind);
                _inRoot = true;
            }
            catch (Exception)
            {
                // Still held where it was, on its way out with the window.
            }
        }

        FitBehind();
    }

    /// <summary>
    /// The farther weather's room: under the player, the host's, in the
    /// window's units, drawn at the window's scale (App size enlarges the
    /// host's content); kept in front for a switch, the same room where it
    /// was; otherwise the whole layer.
    /// </summary>
    private void FitBehind()
    {
        var scale = Math.Max(0.1, _theme.Scale);
        if (_hung is { } hung)
        {
            _behind.Offset = Vector3.Zero;
            _behind.Scale = new Vector3((float)(1 / scale), (float)(1 / scale), 1);
            SetRoom(hung.ActualWidth * scale, hung.ActualHeight * scale);
            return;
        }

        if (_lifted && _host is { ActualWidth: > 0 } host && _shell is { Visibility: Visibility.Visible })
        {
            try
            {
                var origin = host.TransformToVisual(this).TransformPoint(default);
                _behind.Offset = new Vector3((float)origin.X, (float)origin.Y, 0);
                _behind.Scale = Vector3.One;
                SetRoom(host.ActualWidth * scale, host.ActualHeight * scale);
                return;
            }
            catch (Exception)
            {
                // Not in this window's tree: the whole layer below.
            }
        }

        _behind.Offset = Vector3.Zero;
        _behind.Scale = Vector3.One;
        SetRoom(ActualWidth, ActualHeight);
    }

    private void SetRoom(double width, double height)
    {
        _behindRoom.InsertScalar("W", (float)width);
        _behindRoom.InsertScalar("H", (float)height);
    }

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

            (particle.Behind ? _behind : _front).Children.InsertAtTop(sprite);
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
                animation.SetReferenceParameter(Room, particle.Behind ? _behindRoom : _room);
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
        _front.Children.RemoveAll();
        _behind.Children.RemoveAll();
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
