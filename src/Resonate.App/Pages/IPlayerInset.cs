using Microsoft.UI.Xaml;

namespace Resonate.App.Pages;

/// <summary>
/// A page that scrolls, so it can leave room under its last row for a player
/// hovering over the bottom of the page. Each one ends its scrolling content
/// with an empty Border named PlayerSpace; the window sets its height after
/// every page change and whenever the player's size or layout changes. Pages
/// keep no reference to the window or the player, so nothing here can keep
/// a page alive after it is left.
/// </summary>
public interface IPlayerInset
{
    /// <summary>The empty space at the end of the page's scrolling content.</summary>
    FrameworkElement PlayerSpacer { get; }

    /// <summary>Leaves <paramref name="height"/> of room at the end (0 when the player covers nothing).</summary>
    void SetPlayerInset(double height);
}

/// <summary>The one way every page sizes its spacer.</summary>
internal static class PlayerInset
{
    /// <summary>The spacer takes no room, and no spacing in its panel, while the player covers nothing.</summary>
    public static void Apply(FrameworkElement spacer, double height)
    {
        spacer.Height = height;
        spacer.Visibility = height > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
