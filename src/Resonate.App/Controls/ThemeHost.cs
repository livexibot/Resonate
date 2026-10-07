using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// Holds the window's content inside the look: the look's backdrop behind
/// it, and above it the layer where switching looks animates. Transitions
/// take pictures of <see cref="Scene"/>, which leaves that layer out.
/// </summary>
internal sealed partial class ThemeHost : Grid
{
    public ThemeHost(UIElement content, AppServices services)
    {
        Scene = new Grid { Children = { new BackdropLayer(services.Theme, services.Artwork), content } };
        Overlay = new Grid { IsHitTestVisible = false };
        Children.Add(Scene);
        Children.Add(Overlay);
    }

    /// <summary>The backdrop and the content.</summary>
    public Grid Scene { get; }

    /// <summary>Where transitions play, above everything.</summary>
    public Grid Overlay { get; }
}
