using Microsoft.UI.Xaml;
using Resonate.App.Services;

namespace Resonate.App.Controls;

/// <summary>
/// The settings a built-in plugin shows under its switch in Settings,
/// Plugins, while it is on; null for none. Built in code, like the rest of
/// the Plugins section.
/// </summary>
internal static class BuiltInPluginSettings
{
    public static FrameworkElement? Create(string id, AppServices services) => id switch
    {
        // Lyrics
        BuiltInPlugins.Lyrics => null,

        // Home stage
        BuiltInPlugins.HomeStage => null,

        // Away screen
        BuiltInPlugins.AwayScreen => null,

        // Rediscover
        BuiltInPlugins.Rediscover => null,

        // Up next
        BuiltInPlugins.UpNext => null,

        // Artist orbit
        BuiltInPlugins.ArtistOrbit => null,

        // Smart playlists
        BuiltInPlugins.SmartPlaylists => null,

        // Window shapes
        BuiltInPlugins.WindowShapes => null,

        // Summon bar
        BuiltInPlugins.SummonBar => SummonBarSettings.Create(services),

        // Signal path
        BuiltInPlugins.SignalPath => null,

        _ => null,
    };
}
