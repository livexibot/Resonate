using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// Holds the window's content inside the look: the look's backdrop behind
/// it, and the layers where switching looks animates (see ThemeTransitions).
/// Those layers are empty, and cost nothing, while no transition plays.
/// A special look's decorations and weather lie over all of it
/// (<see cref="Decor"/>), outside the picture a switch takes, since much of
/// them is drawn by the compositor where pictures do not reach: they stay
/// as they are through a switch that keeps the scene, and grow in with the
/// new look when it changes. The farther part of the weather hangs under
/// the player instead, inside the scene (see SceneWeatherLayer).
/// </summary>
internal sealed partial class ThemeHost : Grid
{
    public ThemeHost(UIElement content, AppServices services)
    {
        Underlay = new Grid { IsHitTestVisible = false };
        Edge = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

        // A special look's scenery sits behind the panels, its decorations and weather over everything, all on one clock.
        var clock = new SceneClock(ElementCompositionPreview.GetElementVisual(this).Compositor);
        _weather = new SceneWeatherLayer(services.Theme, clock);
        Scene = new Grid
        {
            Children =
            {
                new BackdropLayer(services.Theme, services.Artwork),
                ClipToSize(new SceneArt()),
                content,
            },
        };
        Overlay = new Grid { IsHitTestVisible = false };
        Pictured = new Grid { Children = { Underlay, Edge, Scene, Overlay } };
        Decor = new Grid
        {
            IsHitTestVisible = false,
            Children =
            {
                ClipToSize(new SceneDecorLayer(services.Theme, clock)),
                ClipToSize(_weather),
            },
        };
        Children.Add(Pictured);
        Children.Add(Decor);

        // Pictures that slide or sweep past the edges are cut off there, so a
        // picture of the window taken meanwhile is still the window's size.
        ClipToSize(Overlay);
    }

    private readonly SceneWeatherLayer _weather;

    /// <summary>What a picture of the window for a switch shows: everything but <see cref="Decor"/>.</summary>
    public Grid Pictured { get; }

    /// <summary>Over everything: a special look's decorations and weather.</summary>
    public Grid Decor { get; }

    /// <summary>Under everything: the old look, while the new one grows over it.</summary>
    public Grid Underlay { get; }

    /// <summary>Between the old look and the new: the thin accent edge around a growing shape.</summary>
    public Border Edge { get; }

    /// <summary>The backdrop and the content: the live look.</summary>
    public Grid Scene { get; }

    /// <summary>Above everything: the old look, while it moves out of the way.</summary>
    public Grid Overlay { get; }

    /// <summary>
    /// While a switch that keeps the scene plays, the weather that passes
    /// behind the player falls over everything instead, since the old look's
    /// picture, which leaves it out, stands wherever the new look has not
    /// reached yet (see SceneWeatherLayer.KeepInFront).
    /// </summary>
    public void KeepWeatherInFront(bool inFront) => _weather.KeepInFront(inFront);

    /// <summary>
    /// Cuts <paramref name="element"/> off at its own edges. Anything past the
    /// window's edges (the snowy sky is wider than most windows, the moon and
    /// the branch reach over the top) would make a picture of the window
    /// larger than the window, and a switching animation would show that
    /// picture squeezed into it.
    /// </summary>
    private static T ClipToSize<T>(T element)
        where T : FrameworkElement
    {
        element.SizeChanged += (_, e) => element.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        return element;
    }
}
