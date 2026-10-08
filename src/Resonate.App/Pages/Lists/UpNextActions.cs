using Microsoft.UI.Xaml.Controls;
using Resonate.App.Services;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages.Lists;

/// <summary>
/// "Play next" and "Add to queue" with Up next (a built-in plugin) on: the
/// song joins the order Resonate plays, when Resonate started what plays,
/// and Spotify's own queue otherwise (see <c>PlayerRouter.AddToUpNextAsync</c>).
/// </summary>
public static class UpNextActions
{
    public static bool IsOn => App.Services.BuiltIns.IsOn(BuiltInPlugins.UpNext);

    public static void Add(TrackInfo track, bool playNext)
    {
        _ = App.Services.Player.AddToUpNextAsync(track, playNext);
        App.MainWindow?.ShowMessage(playNext ? $"“{track.Title}” plays next." : $"Added “{track.Title}” to the queue.", InfoBarSeverity.Informational);
    }
}
