using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;

namespace Resonate.App;

/// <summary>
/// Smart playlists in the window (a built-in plugin, off until turned on in
/// Settings, Plugins): each one under DJ in the sidebar with a spark, then
/// "New smart playlist". Their pages are song lists
/// (<see cref="SmartPlaylistSource"/>, "smart:&lt;id&gt;").
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The sidebar's "New smart playlist": not a page, it makes one and opens it.</summary>
    private const string NewSmartPlaylistKey = "smart-new";

    private SmartPlaylistService? _smartPlaylists;

    /// <summary>The smart playlists and their syncing to Spotify.</summary>
    internal SmartPlaylistService? SmartPlaylists => _smartPlaylists;

    partial void SetUpSmartPlaylists()
    {
        var smart = new SmartPlaylistService(_services);
        _smartPlaylists = smart;
        smart.ListChanged += (_, _) => ShowSmartPlaylistsInSidebar();
        smart.Deleted += (_, id) => LeaveSmartPages(SmartPlaylistSource.Prefix + id);
        smart.Message += (_, message) => ShowMessage(message, InfoBarSeverity.Warning);
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.SmartPlaylists)
            {
                ApplySmartPlaylistsSwitch();
            }
        };
        ApplySmartPlaylistsSwitch();
    }

    private void ApplySmartPlaylistsSwitch()
    {
        if (_smartPlaylists is not { } smart)
        {
            return;
        }

        if (smart.IsOn)
        {
            smart.Start();
        }
        else
        {
            smart.Stop();
            LeaveSmartPages(SmartPlaylistSource.Prefix);
        }

        ShowSmartPlaylistsInSidebar();
    }

    /// <summary>The sidebar's smart playlists, after the other links, while the plugin is on (and its settings keep them there).</summary>
    internal void ShowSmartPlaylistsInSidebar()
    {
        if (_smartPlaylists is not { } smart)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            for (var i = NavItems.Count - 1; i >= 0; i--)
            {
                if (IsSmartKey(NavItems[i].Key))
                {
                    NavItems.RemoveAt(i);
                }
            }

            if (smart.IsOn && _services.Settings.SmartPlaylistsInSidebar)
            {
                foreach (var playlist in smart.All)
                {
                    NavItems.Add(new NavItem(SmartPlaylistSource.Prefix + playlist.Id, SmartPlaylistSource.Glyph, playlist.Name));
                }
            }

            if (smart.IsOn && _services.Settings.SmartPlaylistsNewLink)
            {
                NavItems.Add(new NavItem(NewSmartPlaylistKey, "", "New smart playlist"));
            }

            NavList.SelectedItem = NavItems.FirstOrDefault(n => n.Key == _currentKey);
        }
        finally
        {
            _syncingSelection = false;
        }

        MoveNavPill(glide: false);
    }

    /// <summary>"New smart playlist" in the sidebar: makes one from all of Liked Songs and opens it, where the starters wait.</summary>
    private void OpenNewSmartPlaylist()
    {
        if (_smartPlaylists is { IsOn: true } smart)
        {
            Open(SmartPlaylistSource.Prefix + smart.Create().Id);
        }
        else
        {
            SelectNav(_currentKey ?? HomeKey);
        }
    }

    /// <summary>Closes the pages whose keys start with <paramref name="keyPrefix"/> (one deleted smart playlist, or all of them) and forgets them in Back.</summary>
    private void LeaveSmartPages(string keyPrefix)
    {
        if (_currentKey?.StartsWith(keyPrefix, StringComparison.Ordinal) == true)
        {
            Open(HomeKey);
        }

        _history.RemoveAll(k => k.StartsWith(keyPrefix, StringComparison.Ordinal));
        _forward.RemoveAll(k => k.StartsWith(keyPrefix, StringComparison.Ordinal));
        BackButton.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleBarPassthrough();
    }

    private static bool IsSmartKey(string key) =>
        key == NewSmartPlaylistKey || key.StartsWith(SmartPlaylistSource.Prefix, StringComparison.Ordinal);
}
