using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Resonate.App.Pages.Lists;

/// <summary>Extra entries a list adds to a song's menu.</summary>
public sealed record TrackMenuOptions
{
    /// <summary>Plays the song in its list ("Play").</summary>
    public Action? Play { get; init; }

    /// <summary>Removes the song from the list shown ("Remove from this playlist").</summary>
    public Action? Remove { get; init; }

    /// <summary>The playlist shown, left out of "Add to playlist".</summary>
    public string? CurrentPlaylistId { get; init; }
}

/// <summary>
/// What can be done with a song, the same everywhere it appears: the right
/// click menu, the heart, "Add to playlist" and "New playlist".
/// </summary>
public static class TrackActions
{
    /// <summary>The right-click (or menu key) menu for a song.</summary>
    public static MenuFlyout BuildMenu(TrackInfo track, TrackMenuOptions? options = null)
    {
        options ??= new TrackMenuOptions();
        var services = App.Services;
        var menu = new MenuFlyout();

        if (options.Play is { } play && (track.IsPlayable || track.IsLocal || track.FilePath is not null))
        {
            menu.Items.Add(Item("Play", "", play));
        }

        if (track.FilePath is not null || track.IsPlayable)
        {
            if (UpNextActions.IsOn)
            {
                menu.Items.Add(Item("Play next", "\uE893", () => UpNextActions.Add(track, playNext: true)));
            }

            menu.Items.Add(Item("Add to queue", "", () => AddToQueue(track)));
        }

        if (ViewModels.TrackRow.CanLike(track))
        {
            var liked = services.Likes.IsLiked(track.Uri);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(liked ? "Remove from Liked Songs" : "Save to Liked Songs", liked ? "" : "", () => _ = SetLikedAsync(track, !liked)));
        }

        if (track.Uri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true
            || track.Uri?.StartsWith("spotify:episode:", StringComparison.Ordinal) == true)
        {
            menu.Items.Add(AddToPlaylistMenu([track], options.CurrentPlaylistId));
        }

        if (options.Remove is { } remove)
        {
            menu.Items.Add(Item("Remove from this playlist", "", remove));
        }

        var goTo = new List<MenuFlyoutItemBase>();
        if (track.AlbumId is { } albumId)
        {
            goTo.Add(Item("Go to album", "", () => App.MainWindow?.Open(AlbumSource.Prefix + albumId)));
        }

        var artists = track.ArtistRefs.Where(a => a.Id is not null).ToList();
        if (artists.Count == 1)
        {
            goTo.Add(Item("Go to artist", "", () => App.MainWindow?.Open(ArtistKey(artists[0].Id!))));
        }
        else if (artists.Count > 1)
        {
            var sub = new MenuFlyoutSubItem { Text = "Go to artist", Icon = Icon("") };
            foreach (var artist in artists)
            {
                sub.Items.Add(Item(artist.Name, null, () => App.MainWindow?.Open(ArtistKey(artist.Id!))));
            }

            goTo.Add(sub);
        }

        if (track.FilePath is { } path)
        {
            goTo.Add(Item("Show in folder", "", () => ShowInFolder(path)));
        }

        if (goTo.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var item in goTo)
            {
                menu.Items.Add(item);
            }
        }

        if (SpotifyWebLink(track.Uri) is { } link)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Copy song link", "", () => CopyText(link)));
        }

        return menu;
    }

    /// <summary>Opens <paramref name="menu"/> where the user right-clicked (or at the element, from the keyboard).</summary>
    public static void ShowMenu(MenuFlyout menu, UIElement target, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(target, out var point))
        {
            menu.ShowAt(target, new FlyoutShowOptions { Position = point });
        }
        else
        {
            menu.ShowAt(target as FrameworkElement ?? throw new ArgumentException("A menu needs an element to open at.", nameof(target)));
        }

        args.Handled = true;
    }

    /// <summary>Opens <paramref name="menu"/> under a song row's More button.</summary>
    public static void ShowMenuAt(MenuFlyout menu, FrameworkElement button) =>
        menu.ShowAt(button, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });

    /// <summary>"Add to playlist": a new playlist, then every playlist the user may change.</summary>
    public static MenuFlyoutSubItem AddToPlaylistMenu(IReadOnlyList<TrackInfo> tracks, string? exceptPlaylistId = null)
    {
        var services = App.Services;
        var sub = new MenuFlyoutSubItem { Text = "Add to playlist", Icon = Icon("") };
        sub.Items.Add(Item("New playlist…", "", () => _ = AddToNewPlaylistAsync(tracks)));

        var playlists = services.Library.Snapshot?.Playlists
            .Where(p => p.Id != exceptPlaylistId && services.Library.CanListSongs(p))
            .ToList() ?? [];
        if (playlists.Count > 0)
        {
            sub.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var playlist in playlists)
        {
            sub.Items.Add(Item(playlist.Name, null, () => _ = AddToPlaylistAsync(tracks, playlist.Id, playlist.Name)));
        }

        return sub;
    }

    public static void AddToQueue(TrackInfo track)
    {
        if (UpNextActions.IsOn)
        {
            UpNextActions.Add(track, playNext: false);
            return;
        }

        _ = App.Services.Player.AddToQueueAsync(track);
        App.MainWindow?.ShowMessage($"Added “{track.Title}” to the queue.", InfoBarSeverity.Informational);
    }

    /// <summary>Likes or unlikes a song; the heart changes at once and changes back if Spotify refuses.</summary>
    public static async Task SetLikedAsync(TrackInfo track, bool liked)
    {
        try
        {
            await Task.Run(() => App.Services.Likes.SetLikedAsync(track, liked, CancellationToken.None));
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
    }

    public static async Task AddToPlaylistAsync(IReadOnlyList<TrackInfo> tracks, string playlistId, string playlistName)
    {
        var uris = tracks.Select(t => t.Uri).OfType<string>().Where(u => !u.StartsWith("spotify:local:", StringComparison.Ordinal)).ToList();
        if (uris.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Run(() => App.Services.Library.AddToPlaylistAsync(playlistId, uris, CancellationToken.None));
            var what = tracks.Count == 1 ? $"“{tracks[0].Title}”" : Helpers.Format.SongCount(uris.Count);
            App.MainWindow?.ShowMessage($"Added {what} to {playlistName}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
    }

    /// <summary>Asks for a name and creates a private playlist; null when the user cancelled or it failed.</summary>
    public static async Task<SimplifiedPlaylist?> CreatePlaylistAsync()
    {
        // The dialog shows in the full window, so from the mini player it comes back first.
        App.MainWindow?.LeaveMiniPlayer();
        if (App.MainWindow?.Content?.XamlRoot is not { } root)
        {
            return null;
        }

        var name = new TextBox { PlaceholderText = "My playlist", MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "New playlist",
            Content = name,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        name.Loaded += (_, _) => name.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(name.Text) ? "My playlist" : name.Text.Trim();
        try
        {
            return await Task.Run(() => App.Services.Library.CreatePlaylistAsync(title, CancellationToken.None));
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            return null;
        }
    }

    public static string ArtistKey(string artistId) => "artist:" + artistId;

    /// <summary>The open.spotify.com address of a song, list or artist, for sharing.</summary>
    public static string? SpotifyWebLink(string? uri)
    {
        if (uri is null)
        {
            return null;
        }

        if (uri.EndsWith(":collection", StringComparison.Ordinal) || uri.EndsWith(":collection:tracks", StringComparison.Ordinal))
        {
            return "https://open.spotify.com/collection/tracks";
        }

        var parts = uri.Split(':');
        return parts is ["spotify", "track" or "episode" or "album" or "playlist" or "artist", { Length: > 0 } id]
            ? $"https://open.spotify.com/{parts[1]}/{id}"
            : null;
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
            App.MainWindow?.ShowMessage("Link copied.", InfoBarSeverity.Informational);
        }
        catch (COMException)
        {
            // Another app is holding the clipboard. This runs from a menu's Click, so it must not throw.
            App.MainWindow?.ShowMessage("The clipboard is busy. Try again in a moment.", InfoBarSeverity.Warning);
        }
    }

    private static async Task AddToNewPlaylistAsync(IReadOnlyList<TrackInfo> tracks)
    {
        if (await CreatePlaylistAsync() is { } playlist)
        {
            await AddToPlaylistAsync(tracks, playlist.Id, playlist.Name);
        }
    }

    private static void ShowInFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.MainWindow?.ShowMessage("Windows could not open the folder.", InfoBarSeverity.Warning);
        }
    }

    /// <summary>A menu item with an icon (a glyph of the icon font) that runs <paramref name="action"/>.</summary>
    public static MenuFlyoutItem Item(string text, string? glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph is not null)
        {
            item.Icon = Icon(glyph);
        }

        item.Click += (_, _) => action();
        return item;
    }

    // FontIcon's default font is the system icon font, the same as ResonateIconFont.
    private static FontIcon Icon(string glyph) => new() { Glyph = glyph };
}
