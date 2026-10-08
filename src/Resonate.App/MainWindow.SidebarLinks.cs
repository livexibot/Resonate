using Resonate.App.ViewModels;

namespace Resonate.App;

/// <summary>
/// The links at the top of the sidebar the user can hide (Settings, Layout):
/// Search, Liked Songs, Local Files and DJ. Home always shows. Hidden pages
/// still open from elsewhere (Ctrl+F, the player, the Summon bar).
/// </summary>
public sealed partial class MainWindow
{
    private NavItem[]? _links;

    /// <summary>Every built-in link, in order, kept from before any was hidden (smart playlists come after them).</summary>
    private NavItem[] Links => _links ??=
    [
        .. NavItems.Where(n => n.Key is HomeKey or SearchKey or LikedSongsKey or LocalFilesKey or DjKey),
    ];

    private bool IsLinkShown(string key) => key switch
    {
        HomeKey => true,

        // Local Files has kept its own switch since it came.
        LocalFilesKey => _services.LocalFiles.ShowInSidebar,
        _ => !_services.Settings.HiddenSidebarLinks.Contains(key),
    };

    /// <summary>Adds or removes the links to match the user's choice, keeping their order.</summary>
    internal void ShowSidebarLinks()
    {
        var links = Links;
        _syncingSelection = true;
        try
        {
            var index = 0;
            foreach (var link in links)
            {
                var shown = NavItems.Contains(link);
                if (IsLinkShown(link.Key))
                {
                    if (!shown)
                    {
                        NavItems.Insert(index, link);
                    }

                    index++;
                }
                else if (shown)
                {
                    NavItems.Remove(link);
                }
            }

            NavList.SelectedItem = NavItems.FirstOrDefault(n => n.Key == _currentKey);
        }
        finally
        {
            _syncingSelection = false;
        }

        MoveNavPill(glide: false);
    }
}
