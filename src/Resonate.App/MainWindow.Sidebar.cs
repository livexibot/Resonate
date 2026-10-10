using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.App;

/// <summary>
/// Playing from the sidebar: double-click a playlist to play it, or
/// right-click it for Play, Shuffle play and Copy link.
/// The playlist that plays is drawn in the accent colour with a speaker,
/// the song that plays names the window (on the taskbar and in Alt+Tab),
/// and clicking its title in the player bar opens what it plays from.
/// </summary>
public sealed partial class MainWindow
{
    private const string AppName = "Resonate";

    private int _nowPlayingQueued;
    private string _windowTitle = AppName;

    /// <summary>The list played last from Resonate: its key, and its name as the player shows it.</summary>
    private (string Key, string? Name)? _lastPlayed;

    /// <summary>The playlist the music plays from, and whether it plays, as the sidebar shows them.</summary>
    private (string? Id, bool Playing) _shownNowPlaying;

    private void OnPlaylistDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ListEvents.DoubleTapped<PlaylistNavItem>(PlaylistList, e) is { } item)
        {
            e.Handled = true;
            _ = PlayPlaylistAsync(item, shuffle: null);
        }
    }

    private void OnPlaylistContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<PlaylistNavItem>(PlaylistList, args) is not { } item)
        {
            return;
        }

        var uri = item.Playlist.Uri is { Length: > 0 } known ? known : "spotify:playlist:" + item.Id;
        var menu = new MenuFlyout();
        menu.Items.Add(TrackActions.Item("Play", "", () => _ = PlayPlaylistAsync(item, shuffle: false)));
        menu.Items.Add(TrackActions.Item("Shuffle play", "", () => _ = PlayPlaylistAsync(item, shuffle: true)));
        if (TrackActions.SpotifyWebLink(uri) is { } link)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TrackActions.Item("Copy link", "", () => TrackActions.CopyText(link)));
        }

        TrackActions.ShowMenu(menu, PlaylistList, args);
    }

    /// <summary>
    /// Plays a playlist from the sidebar the way its page would: in the sort
    /// chosen for it, and in a truly random order when shuffled.
    /// </summary>
    /// <param name="shuffle">True for "Shuffle play", false for "Play" in order, null to keep the shuffle setting.</param>
    private async Task PlayPlaylistAsync(PlaylistNavItem item, bool? shuffle)
    {
        var source = TrackListSource.For(item.Id, _services);
        var sort = TrackSort.Parse(_services.Settings.TrackSorts.GetValueOrDefault(item.Id));
        if (sort.IsDefault && !(shuffle ?? _services.Player.Spotify.State.Shuffle))
        {
            // In its own order Spotify plays the playlist itself: it starts at once, with nothing to load.
            NoteListPlayed(item.Id, item.Name);
            await _services.Player.PlayAsync(new PlayRequest([], -1, source.ContextUri, item.Name) { Shuffle = false });
            return;
        }

        FullTrackList list;
        try
        {
            list = await Task.Run(() => source.LoadAllAsync(_lifetime.Token));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            return;
        }

        // Spotify keeps the songs of other people's playlists to itself; those play as a whole.
        var ownOrder = sort.IsDefault || list.ItemsHidden;
        IReadOnlyList<TrackInfo> tracks = ownOrder ? list.Tracks : TrackSorter.Apply(list.Tracks, sort);
        NoteListPlayed(item.Id, item.Name);
        await _services.Player.PlayAsync(new PlayRequest(tracks, -1, ownOrder ? source.ContextUri : null, item.Name) { Shuffle = shuffle });
    }

    /// <summary>On any thread: the player changed; the sidebar and the window's name follow once.</summary>
    private void OnNowPlayingChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _nowPlayingQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Interlocked.Exchange(ref _nowPlayingQueued, 0);
            ShowNowPlaying();
        });
    }

    private void ShowNowPlaying()
    {
        var state = _services.Player.State;
        var title = state is { IsPlaying: true, Title: { } song }
            ? string.IsNullOrEmpty(state.Artists) ? song : $"{song} · {state.Artists}"
            : AppName;
        if (title != _windowTitle)
        {
            _windowTitle = title;
            AppWindow.Title = title;
            if (_miniPlayer is { } mini)
            {
                mini.AppWindow.Title = title;
            }
        }

        var now = (PlayingPlaylistId(state), state.IsPlaying);
        if (now != _shownNowPlaying)
        {
            _shownNowPlaying = now;
            foreach (var item in Playlists)
            {
                MarkNowPlaying(item);
            }
        }
    }

    private void MarkNowPlaying(PlaylistNavItem item)
    {
        item.IsCurrent = item.Id == _shownNowPlaying.Id;
        item.IsPlaying = item.IsCurrent && _shownNowPlaying.Playing;
    }

    /// <summary>Opens the page of what the music plays from (a playlist, album, Liked Songs or Local Files).</summary>
    public void OpenNowPlaying()
    {
        if (NowPlayingKey(_services.Player.State) is { } key)
        {
            // A list opens at the playing song; one already open jumps to it.
            var shown = _currentKey == key ? CurrentPage as Pages.TracksPage : null;
            Pages.TracksPage.OpenAtPlayingSong = true;
            try
            {
                Open(key);
            }
            finally
            {
                Pages.TracksPage.OpenAtPlayingSong = false;
            }

            shown?.RevealPlaying();
        }
    }

    /// <summary>There is a page to open for what the music plays from.</summary>
    public bool CanOpenNowPlaying(PlayerState state) => NowPlayingKey(state) is not null;

    /// <summary>The playlist in the sidebar the music plays from, if any.</summary>
    private string? PlayingPlaylistId(PlayerState state) =>
        state.Source == PlaybackSource.Spotify && NowPlayingKey(state) is { } key && Playlists.Any(p => p.Id == key) ? key : null;

    /// <summary>The navigation key of what the music plays from, if Resonate has a page for it.</summary>
    private string? NowPlayingKey(PlayerState state)
    {
        if (!state.HasTrack)
        {
            return null;
        }

        if (state.Source == PlaybackSource.Spotify && state.ContextUri is { } context)
        {
            if (context.EndsWith(":collection", StringComparison.Ordinal) || context.EndsWith(":collection:tracks", StringComparison.Ordinal))
            {
                return LikedSongsKey;
            }

            var parts = context.Split(':');
            return parts.Length >= 3 && parts[^1].Length > 0
                ? parts[^2] switch
                {
                    "playlist" => parts[^1],
                    "album" => AlbumSource.Prefix + parts[^1],
                    "artist" => ArtistPrefix + parts[^1],
                    _ => null,
                }
                : null;
        }

        // Sorted or truly shuffled, Resonate plays a list's songs as its own
        // list, with no context: it is the list played last, while its name shows.
        if (_lastPlayed is { } last && state.SourceName is { } name && name == last.Name)
        {
            return last.Key;
        }

        return state.Source == PlaybackSource.LocalFiles ? LocalFilesKey : null;
    }
}
