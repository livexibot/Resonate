using System.Globalization;
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
/// The scenery of a special look (<see cref="ThemeDefinition.Scene"/>),
/// behind the panels: the Japan night, the snowy night, the Synthwave
/// sunset, the Liquid Chrome sea, the Cyberpunk city or the Afterhours
/// skyline. Each scene's shapes are made only while it shows (<c>x:Load</c>)
/// and let go of when another look takes over. Its paths are written by
/// <c>tools/scenes/build_scene_art.py</c>. A few parts move: each carries its
/// motion as its <c>Tag</c> (<see cref="SceneMotion"/>) and a name the scene's
/// own <c>Tag</c> counts, and the compositor moves it with expressions on the
/// scene's clock (<see cref="SceneClock"/>), only while the window shows and
/// Windows' animations are on; otherwise each rests where it is drawn. The
/// weather over the window is <see cref="SceneWeatherLayer"/>.
/// </summary>
public sealed partial class SceneArt : UserControl
{
    // The tags of every scene shown so far and the first thing that went wrong, for CI's tour (CheckMotion).
    private static readonly Dictionary<ThemeScene, string[]> Seen = [];
    private static string? _problem;

    private readonly ThemeService _theme = App.Services.Theme;
    private readonly SceneClock _clock;
    private readonly List<(Visual Visual, SceneMotion.Motion Motion)> _parts = [];
    private MainWindow? _window;
    private FrameworkElement? _art;
    private ThemeScene _shown;
    private bool _moving;
    private bool _loaded;

    internal SceneArt(SceneClock clock)
    {
        _clock = clock;
        InitializeComponent();

        // Only while in the window, so neither the theme nor the window keeps it alive.
        Loaded += (_, _) =>
        {
            _loaded = true;
            _theme.Changed -= OnThemeChanged;
            _theme.Changed += OnThemeChanged;
            _theme.AnimationsChanged -= OnRefresh;
            _theme.AnimationsChanged += OnRefresh;
            if (_window is not null)
            {
                _window.ShownChanged -= OnRefresh;
            }

            _window = App.MainWindow;
            if (_window is not null)
            {
                _window.ShownChanged += OnRefresh;
            }

            Show();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _theme.Changed -= OnThemeChanged;
            _theme.AnimationsChanged -= OnRefresh;
            if (_window is not null)
            {
                _window.ShownChanged -= OnRefresh;
                _window = null;
            }

            Rest();
        };
    }

    /// <summary>
    /// For CI's screenshot tour, after every look has been shown: whether each
    /// scene with moving parts was found with all of them, and whether the
    /// compositor takes every one's expressions (started on a visual nobody
    /// sees). Null when all is well.
    /// </summary>
    internal static string? CheckMotion(Compositor compositor)
    {
        if (_problem is not null)
        {
            return _problem;
        }

        foreach (var scene in (ThemeScene[])[ThemeScene.Synthwave, ThemeScene.LiquidChrome, ThemeScene.Cyberpunk, ThemeScene.Afterhours])
        {
            if (!Seen.TryGetValue(scene, out var tags) || tags.Length == 0)
            {
                return $"{scene}'s scenery never showed its moving parts.";
            }
        }

        var clock = compositor.CreatePropertySet();
        clock.InsertScalar("Time", 1);
        var sprite = compositor.CreateSpriteVisual();
        sprite.Properties.InsertVector3("Translation", Vector3.Zero);
        try
        {
            foreach (var tag in Seen.Values.SelectMany(t => t))
            {
                foreach (var (property, expression) in Expressions(SceneMotion.Parse(tag)))
                {
                    var animation = compositor.CreateExpressionAnimation(expression);
                    animation.SetReferenceParameter(SceneMotion.Clock, clock);
                    sprite.StartAnimation(property, animation);
                    sprite.StopAnimation(property);
                    animation.Dispose();
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
            clock.Dispose();
        }
    }

    private static IEnumerable<(string Property, string Expression)> Expressions(SceneMotion.Motion motion)
    {
        if (motion.Translation is { } translation)
        {
            yield return ("Translation", translation);
        }

        if (motion.Opacity is { } opacity)
        {
            yield return ("Opacity", opacity);
        }

        if (motion.Scale is { } scale)
        {
            yield return ("Scale", scale);
        }

        if (motion.Rotation is { } rotation)
        {
            yield return ("RotationAngleInDegrees", rotation);
        }
    }

    private static void Note(string problem) => _problem ??= problem;

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void OnRefresh(object? sender, EventArgs e) => Refresh();

    private void Show()
    {
        var scene = _theme.Current.Scene;
        if (scene != _shown)
        {
            Rest();
            _parts.Clear();
            if (_art is not null)
            {
                UnloadObject(_art);
                _art = null;
            }

            _shown = scene;
            if (scene != ThemeScene.None)
            {
                // Through its name, as a scene's grid is only made now.
                _art = FindName($"{scene}Art") as FrameworkElement;
                if (_art is null)
                {
                    Note($"{scene}'s scenery was not found.");
                }
                else
                {
                    Collect(scene, _art);
                }
            }
        }

        Refresh();
    }

    /// <summary>The scene's moving parts, each found by its name and given its place to turn round, resting where it is drawn.</summary>
    private void Collect(ThemeScene scene, FrameworkElement art)
    {
        var words = (art.Tag as string)?.Split(' ');
        if (words is not [var prefix, var number] || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            Note($"{scene}'s scenery does not say what moves.");
            return;
        }

        var tags = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var name = $"{prefix}M{i}";
            if (FindName(name) is not FrameworkElement { Tag: string tag } element)
            {
                Note($"{scene}'s {name} was not found.");
                continue;
            }

            SceneMotion.Motion motion;
            try
            {
                motion = SceneMotion.Parse(tag);
            }
            catch (FormatException ex)
            {
                Note($"{scene}'s {name}: {ex.Message}");
                continue;
            }

            var visual = ElementCompositionPreview.GetElementVisual(element);
            if (motion.Translation is not null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            }

            if (motion.Rotation is not null || motion.Scale is not null)
            {
                visual.CenterPoint = new Vector3((float)motion.CentreX, (float)motion.CentreY, 0);
            }

            visual.Opacity = (float)motion.RestOpacity;
            tags.Add(tag);
            _parts.Add((visual, motion));
        }

        Seen[scene] = [.. tags];
    }

    private void Refresh()
    {
        var move = _parts.Count > 0 && _loaded && (_window?.IsShown ?? true) && _theme.AnimationsEnabled;
        if (move && !_moving)
        {
            Move();
        }
        else if (!move && _moving)
        {
            Rest();
        }
    }

    private void Move()
    {
        _moving = true;
        _clock.Want(this, true);
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        try
        {
            foreach (var (visual, motion) in _parts)
            {
                if (motion.Opacity is null)
                {
                    visual.Opacity = (float)motion.BaseOpacity;
                }

                foreach (var (property, expression) in Expressions(motion))
                {
                    var animation = compositor.CreateExpressionAnimation(expression);
                    animation.SetReferenceParameter(SceneMotion.Clock, _clock.Props);
                    visual.StartAnimation(property, animation);
                }
            }
        }
        catch (Exception ex)
        {
            // Still scenery rather than a broken window.
            Note("The scenery could not move: " + ex.Message);
            Rest();
        }
    }

    /// <summary>Every animation ends and each part rests where it is drawn, so nothing redraws for it.</summary>
    private void Rest()
    {
        _clock.Want(this, false);
        _moving = false;
        foreach (var (visual, motion) in _parts)
        {
            if (motion.Translation is not null)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
            }

            if (motion.Scale is not null)
            {
                visual.StopAnimation("Scale");
                visual.Scale = Vector3.One;
            }

            if (motion.Rotation is not null)
            {
                visual.StopAnimation("RotationAngleInDegrees");
                visual.RotationAngleInDegrees = 0;
            }

            visual.StopAnimation("Opacity");
            visual.Opacity = (float)motion.RestOpacity;
        }
    }
}
