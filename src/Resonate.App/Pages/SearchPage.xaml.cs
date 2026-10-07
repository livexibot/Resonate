using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Pages;

/// <summary>Search as you type: songs (double-click to play), artists, playlists and albums.</summary>
public sealed partial class SearchPage : Page
{
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(250);

    private readonly AppServices _services = App.Services;
    private CancellationTokenSource? _search;

    public SearchPage()
    {
        InitializeComponent();
    }

    /// <summary>Typed into the box on the next visit (used by the screenshot tour).</summary>
    public static string? PendingQuery { get; set; }

    /// <summary>The query from the last visit, so going back keeps the results.</summary>
    private static string LastQuery { get; set; } = string.Empty;

    public ObservableCollection<TrackRow> Songs { get; } = [];

    public ObservableCollection<CardItem> PlaylistCards { get; } = [];

    public ObservableCollection<CardItem> AlbumCards { get; } = [];

    public ObservableCollection<CardItem> ArtistCards { get; } = [];

    /// <summary>Moves the keyboard to the search box when the search page is showing.</summary>
    public static void FocusSearchBox(Frame frame)
    {
        if (frame.Content is SearchPage page)
        {
            page.QueryBox.Focus(FocusState.Keyboard);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var query = PendingQuery ?? LastQuery;
        PendingQuery = null;
        QueryBox.Text = query;
        QueryBox.SelectionStart = query.Length;
        QueryBox.Focus(FocusState.Programmatic);

        // Setting the text before the page is shown does not always raise
        // TextChanged, so search for it here.
        _ = SearchAsync(query, TimeSpan.Zero);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _search?.Cancel();

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (QueryBox.Text != LastQuery)
        {
            _ = SearchAsync(QueryBox.Text, TypingPause);
        }
    }

    private async Task SearchAsync(string text, TimeSpan wait)
    {
        var query = text.Trim();
        LastQuery = text;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;

        if (query.Length == 0)
        {
            // A search cancelled above leaves the ring to the newest call, which is this one.
            SearchingRing.IsActive = false;
            Show(null);
            return;
        }

        try
        {
            // Wait for a pause in typing so each key press does not cost a request.
            await Task.Delay(wait, token);

            // A newer call may have run before this one resumed; the ring is its now.
            token.ThrowIfCancellationRequested();
            SearchingRing.IsActive = Songs.Count == 0;
            var library = _services.Library;
            var results = await Task.Run(() => library.SearchAsync(query, token), token);
            if (!token.IsCancellationRequested)
            {
                Show(results);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SearchingRing.IsActive = false;
            }
        }
    }

    private void Show(SearchMatches? results)
    {
        Songs.Clear();
        PlaylistCards.Clear();
        AlbumCards.Clear();
        ArtistCards.Clear();

        if (results is not null)
        {
            var number = 1;
            foreach (var track in results.Tracks)
            {
                Songs.Add(new TrackRow(track, number++, isLiked: _services.Likes.IsLiked(track.Uri)));
            }

            foreach (var artist in results.Artists)
            {
                ArtistCards.Add(new CardItem(artist.Name, "Artist", artist.Uri, artist.Id, ImagePicker.Pick(artist.Images, 300), isPlaylist: false));
            }

            foreach (var playlist in results.Playlists)
            {
                PlaylistCards.Add(new CardItem(
                    playlist.Name,
                    playlist.Owner?.DisplayName ?? "Playlist",
                    playlist.Uri,
                    playlist.Id,
                    ImagePicker.Pick(playlist.Images, 300),
                    isPlaylist: true));
            }

            foreach (var album in results.Albums)
            {
                if (album.Uri is null)
                {
                    continue;
                }

                AlbumCards.Add(new CardItem(
                    album.Name,
                    string.Join(", ", album.Artists?.Select(a => a.Name) ?? []),
                    album.Uri,
                    album.Id,
                    ImagePicker.Pick(album.Images, 300),
                    isPlaylist: false));
            }
        }

        HintText.Text = "Search for songs, artists, albums and playlists. Double-click a song to play it.";
        HintText.Visibility = results is null ? Visibility.Visible : Visibility.Collapsed;
        SongsSection.Visibility = Songs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsSection.Visibility = ArtistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsSection.Visibility = PlaylistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlbumsSection.Visibility = AlbumCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results is not null && Songs.Count + ArtistCards.Count + PlaylistCards.Count + AlbumCards.Count == 0)
        {
            HintText.Text = "Nothing found. Try other words.";
            HintText.Visibility = Visibility.Visible;
        }
    }

    private void OnSongDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ListEvents.DoubleTapped<TrackRow>(SongList, e) is { } row)
        {
            Play(row);
        }
    }

    private void OnSongListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && SongList.SelectedItem is TrackRow row)
        {
            e.Handled = true;
            Play(row);
        }
    }

    private void Play(TrackRow row)
    {
        // Play the song inside its album, so the album continues after it.
        if (row.Track.IsPlayable)
        {
            _ = _services.Player.PlayAsync(new PlayRequest([row.Track], 0, row.Track.AlbumUri, row.Track.Album));
        }
    }

    private void OnSongContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TrackRow>(SongList, args) is { } row)
        {
            SongList.SelectedItem = row;
            TrackActions.ShowMenu(TrackActions.BuildMenu(row.Track, new TrackMenuOptions { Play = () => Play(row) }), SongList, args);
        }
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem { Id: { } id })
        {
            App.MainWindow?.Open(TrackActions.ArtistKey(id));
        }
    }

    private void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem { Id: { } id })
        {
            App.MainWindow?.Open(id);
        }
    }

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem { Id: { } id })
        {
            App.MainWindow?.Open(AlbumSource.Prefix + id);
        }
    }
}
