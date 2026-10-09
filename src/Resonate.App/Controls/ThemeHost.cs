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
/// </summary>
internal sealed partial class ThemeHost : Grid
{
    public ThemeHost(UIElement content, AppServices services)
    {
        Underlay = new Grid { IsHitTestVisible = false };
        Edge = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        // A special look's scenery sits behind the panels, its decorations and weather over them, all on one clock.
        var clock = new SceneClock(ElementCompositionPreview.GetElementVisual(this).Compositor);
        Scene = new Grid
        {
            Children =
            {
                new BackdropLayer(services.Theme, services.Artwork),
                ClipToSize(new SceneArt()),
                content,
                ClipToSize(new SceneDecorLayer(services.Theme, clock)),
                ClipToSize(new SceneWeatherLayer(services.Theme, clock)),
            },
        };
        Overlay = new Grid { IsHitTestVisible = false };
        Children.Add(Underlay);
        Children.Add(Edge);
        Children.Add(Scene);
        Children.Add(Overlay);

        // Pictures that slide or sweep past the edges are cut off there, so a
        // picture of the window taken meanwhile is still the window's size.
        ClipToSize(Overlay);
    }

    /// <summary>Under everything: the old look, while the new one grows over it.</summary>
    public Grid Underlay { get; }

    /// <summary>Between the old look and the new: the thin accent edge around a growing shape.</summary>
    public Border Edge { get; }

    /// <summary>The backdrop and the content: the live look.</summary>
    public Grid Scene { get; }

    /// <summary>Above everything: the old look, while it moves out of the way.</summary>
    public Grid Overlay { get; }

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
