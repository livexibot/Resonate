using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Pages;

/// <summary>A playlist, or Liked Songs: header, Play, and the songs (loaded a page at a time while scrolling).</summary>
public sealed partial class PlaylistPage : Page
{
    private readonly AppServices _services = App.Services;
    private readonly CancellationTokenSource _leaving = new();
    private string _key = MainWindow.LikedSongsKey;
    private string? _contextUri;
    private string? _spotifyLink;
    private bool _hasMore;
    private bool _loadingMore;
    private int _nextOffset;
    private ScrollViewer? _scroller;
    private string? _highlightedTrack;
    private int _highlightQueued;

    public PlaylistPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    private bool IsLikedSongs => _key == MainWindow.LikedSongsKey;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _key = e.Parameter as string ?? MainWindow.LikedSongsKey;
        _services.Player.StateChanged += OnPlayerStateChanged;
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _services.Player.StateChanged -= OnPlayerStateChanged;
        _leaving.Cancel();
        if (_scroller is not null)
        {
            _scroller.ViewChanged -= OnScrolled;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroller = VisualTree.FindDescendant<ScrollViewer>(TrackList);
        if (_scroller is not null)
        {
            _scroller.ViewChanged += OnScrolled;
        }
    }

    private async Task LoadAsync()
    {
        var token = _leaving.Token;
        var library = _services.Library;
        ShowHeaderFromCache();

        var slow = Task.Delay(150, token);
        var loading = IsLikedSongs
            ? Task.Run(() => LoadLikedSongsAsync(library, token), token)
            : Task.Run(() => LoadPlaylistAsync(library, token), token);

        // Only show a spinner if Spotify is slow; a fast answer should just appear.
        if (await Task.WhenAny(loading, slow) == slow && !loading.IsCompleted)
        {
            LoadingRing.IsActive = true;
        }

        try
        {
            var (details, page) = await loading;
            if (details is not null)
            {
                ShowHeader(details);
            }

            AddPage(page);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private static async Task<(PlaylistDetails? Details, TrackListPage Page)> LoadLikedSongsAsync(LibraryService library, CancellationToken token) =>
        (null, await library.GetLikedSongsAsync(0, token));

    private async Task<(PlaylistDetails? Details, TrackListPage Page)> LoadPlaylistAsync(LibraryService library, CancellationToken token)
    {
        var details = await library.GetPlaylistAsync(_key, token);
        return (details, details.FirstPage);
    }

    private void ShowHeaderFromCache()
    {
        if (IsLikedSongs)
        {
            KindText.Text = "COLLECTION";
            TitleText.Text = "Liked Songs";
            CoverFrame.Background = Artwork.PlaceholderBrush("Liked Songs");
            CoverGlyph.Visibility = Visibility.Visible;
            var userId = _services.Library.Snapshot?.User?.Id;
            _contextUri = userId is null ? null : $"spotify:user:{userId}:collection";
            _spotifyLink = "spotify:collection:tracks";
            return;
        }

        var cached = _services.Library.Snapshot?.Playlists.FirstOrDefault(p => p.Id == _key);
        KindText.Text = "PLAYLIST";
        CoverFrame.Background = Artwork.PlaceholderBrush(cached?.Name ?? _key);
        if (cached is not null)
        {
            TitleText.Text = cached.Name;
            _contextUri = cached.Uri;
            _spotifyLink = cached.Uri;
            CoverImage.Source = Artwork.FromUrl(ImagePicker.Pick(cached.Images, 300), 184);
            DetailsText.Text = Format.SongCount(cached.ItemCount);
        }
    }

    private void ShowHeader(PlaylistDetails details)
    {
        TitleText.Text = details.Name;
        _contextUri = details.Uri;
        _spotifyLink = details.Uri;
        CoverFrame.Background = Artwork.PlaceholderBrush(details.Name);
        if (details.ImageUrl is not null)
        {
            CoverImage.Source = Artwork.FromUrl(details.ImageUrl, 184);
        }

        if (details.Description is { Length: > 0 } description)
        {
            DescriptionText.Text = description;
            DescriptionText.Visibility = Visibility.Visible;
        }

        var count = details.FirstPage.ItemsHidden ? null : Format.SongCount(details.FirstPage.Total);
        DetailsText.Text = string.Join(" · ", new[] { details.Owner, count }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private void AddPage(TrackListPage page)
    {
        if (page.ItemsHidden)
        {
            HiddenNotice.Visibility = Visibility.Visible;
            ColumnHeadings.Visibility = Visibility.Collapsed;
            _hasMore = false;
            return;
        }

        if (IsLikedSongs && Tracks.Count == 0)
        {
            DetailsText.Text = Format.SongCount(page.Total);
        }

        foreach (var track in page.Tracks)
        {
            Tracks.Add(new TrackRow(track, Tracks.Count + 1));
        }

        _hasMore = page.HasMore;
        _nextOffset = page.NextOffset;
        HighlightPlayingTrack();

        // If the songs do not fill the screen yet, scrolling will never ask for more.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, LoadMoreIfNearEnd);
    }

    private void OnScrolled(object? sender, ScrollViewerViewChangedEventArgs e) => LoadMoreIfNearEnd();

    private void LoadMoreIfNearEnd()
    {
        if (!_hasMore || _loadingMore || _scroller is null)
        {
            return;
        }

        if (_scroller.VerticalOffset >= _scroller.ScrollableHeight - (_scroller.ViewportHeight * 2))
        {
            _ = LoadMoreAsync();
        }
    }

    private async Task LoadMoreAsync()
    {
        _loadingMore = true;
        var token = _leaving.Token;
        var offset = _nextOffset;
        try
        {
            var library = _services.Library;
            var page = IsLikedSongs
                ? await Task.Run(() => library.GetLikedSongsAsync(offset, token), token)
                : await Task.Run(() => library.GetPlaylistTracksAsync(_key, offset, token), token);
            AddPage(page);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hasMore = false;
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private void OnTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TrackRow row)
        {
            Play(row);
        }
    }

    private void OnTrackListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && TrackList.SelectedItem is TrackRow row)
        {
            e.Handled = true;
            Play(row);
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        var first = Tracks.FirstOrDefault(t => t.Track.IsPlayable);
        if (first is not null)
        {
            Play(first);
        }
        else if (_contextUri is not null)
        {
            _ = _services.Player.PlayContextAsync(_contextUri);
        }
    }

    private void Play(TrackRow row)
    {
        if (!row.Track.IsPlayable)
        {
            return;
        }

        // Spotify may refuse Liked Songs as a context; the loaded songs are the fallback.
        var fallback = IsLikedSongs ? Tracks.Select(t => t.Track.Uri).OfType<string>().ToList() : null;
        _ = _services.Player.PlayTrackAsync(row.Track, _contextUri, fallback);
    }

    private void OnOpenInSpotifyClick(object sender, RoutedEventArgs e)
    {
        if (_spotifyLink is not null && Uri.TryCreate(_spotifyLink, UriKind.Absolute, out var uri))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _highlightQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _highlightQueued, 0);
                HighlightPlayingTrack();
            });
        }
    }

    private void HighlightPlayingTrack()
    {
        var state = _services.Player.State;
        var key = state.TrackUri ?? state.Title;
        if (key == _highlightedTrack && Tracks.Count > 0 && Tracks.Any(t => t.IsCurrent))
        {
            return;
        }

        _highlightedTrack = key;
        foreach (var row in Tracks)
        {
            row.IsCurrent = state.TrackUri is not null
                ? row.Track.Uri == state.TrackUri
                : state.Title is not null && row.Title == state.Title;
        }
    }
}
