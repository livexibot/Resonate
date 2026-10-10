using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Controls;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Pages;

/// <summary>
/// Search as you type: the user's own playlists that match first, at once
/// from the library on this PC (no request to Spotify), then the best match
/// beside the first songs, artists, albums and playlists, with filters for
/// one kind (all its results). Before
/// anything is typed it shows the last searches and what was opened from
/// them (the owner's request, 9 October 2026), kept in the settings.
/// </summary>
public sealed partial class SearchPage : Page
{
    // Long enough that a word typed at a normal pace is one request to Spotify, not one per few letters.
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

    // In "All", the songs beside the top result.
    private const int SongsBesideTop = 4;

    // From this width the top result and the songs sit side by side.
    private const double SideBySide = 860;

    // The user's own playlists shown first.
    private const int OwnPlaylistLimit = 6;

    private readonly AppServices _services = App.Services;
    private readonly List<TrackRow> _allSongs = [];
    private readonly SongRowActions _songActions;
    private CancellationTokenSource? _search;
    private SearchMatches? _results;
    private string _filter = "All";
    private Action? _openTop;
    private (List<SimplifiedPlaylist> Playlists, int Count, QuickSearchIndex Index)? _ownIndex;

    public SearchPage()
    {
        InitializeComponent();
        TopRow.SizeChanged += (_, e) => FitTopRow(e.NewSize.Width);
        _songActions = new SongRowActions(SongList);
    }

    /// <summary>Typed into the box on the next visit (used by the screenshot tour).</summary>
    public static string? PendingQuery { get; set; }

    /// <summary>The query from the last visit, so going back keeps the results.</summary>
    private static string LastQuery { get; set; } = string.Empty;

    public ObservableCollection<TrackRow> Songs { get; } = [];

    public ObservableCollection<CardItem> PlaylistCards { get; } = [];

    public ObservableCollection<CardItem> OwnPlaylistCards { get; } = [];

    public ObservableCollection<CardItem> AlbumCards { get; } = [];

    public ObservableCollection<CardItem> ArtistCards { get; } = [];

    public ObservableCollection<RecentPickItem> RecentPicks { get; } = [];

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
        ShowFilter();

        // Setting the text before the page is shown does not always raise
        // TextChanged, so search for it here.
        _ = SearchAsync(query, TimeSpan.Zero);
        TrackColumns.OptionsChanged += OnColumnOptionsChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _search?.Cancel();
        TrackColumns.OptionsChanged -= OnColumnOptionsChanged;
    }

    /// <summary>Cover size changed in Settings: the songs and recently viewed take it at once.</summary>
    private void OnColumnOptionsChanged(object? sender, EventArgs e)
    {
        foreach (var row in _allSongs)
        {
            row.RefreshCover();
        }

        if (RecentSection.Visibility == Visibility.Visible)
        {
            ShowRecent(true);
        }
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (QueryBox.Text != LastQuery)
        {
            _ = SearchAsync(QueryBox.Text, TypingPause);
        }
    }

    /// <summary>Enter keeps the search among the recent ones.</summary>
    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            RememberQuery();
        }
    }

    private async Task SearchAsync(string text, TimeSpan wait)
    {
        var query = text.Trim();
        LastQuery = text;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        ShowOwnPlaylists(query);

        if (query.Length == 0)
        {
            // A search cancelled above leaves the ring to the newest call, which is this one.
            SearchingRing.IsActive = false;
            Show(null, query);
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
                Show(results, query);
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

    private void Show(SearchMatches? results, string query)
    {
        _results = results;
        _allSongs.Clear();
        PlaylistCards.Clear();
        AlbumCards.Clear();
        ArtistCards.Clear();

        if (results is not null)
        {
            var number = 1;
            foreach (var track in results.Tracks)
            {
                _allSongs.Add(new TrackRow(track, number++, isLiked: _services.Likes.IsLiked(track.Uri)));
            }

            foreach (var artist in results.Artists)
            {
                ArtistCards.Add(new CardItem(artist.Name, "Artist", artist.Uri, artist.Id, ImagePicker.Pick(artist.Images, 300), isPlaylist: false));
            }

            // The user's own, already shown first, are not shown again.
            var own = OwnPlaylistCards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var playlist in results.Playlists.Where(p => !own.Contains(p.Id)))
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

        ShowTop(results, query);
        ShowSections();
    }

    /// <summary>
    /// The user's own playlists that match, ranked as the Summon bar ranks
    /// them, from the library Resonate keeps on this PC: shown as the user
    /// types, before Spotify's results, with no request to Spotify.
    /// </summary>
    private void ShowOwnPlaylists(string query)
    {
        OwnPlaylistCards.Clear();
        if (query.Length > 0 && _services.Library.Snapshot is { Playlists: var playlists } && playlists.Count > 0)
        {
            if (_ownIndex is not { } own || !ReferenceEquals(own.Playlists, playlists) || own.Count != playlists.Count)
            {
                own = (playlists, playlists.Count, QuickSearchIndex.Build(playlists, [], []));
                _ownIndex = own;
            }

            // Liked Songs may be among the matches; only playlists show here.
            foreach (var item in own.Index.SearchLibrary(query, OwnPlaylistLimit + 1))
            {
                if (item.Kind != QuickKind.Playlist || OwnPlaylistCards.Count == OwnPlaylistLimit
                    || playlists.Find(p => p.Id == item.Id) is not { } playlist)
                {
                    continue;
                }

                OwnPlaylistCards.Add(new CardItem(
                    playlist.Name,
                    playlist.Owner?.DisplayName ?? "Playlist",
                    item.Uri ?? "spotify:playlist:" + playlist.Id,
                    playlist.Id,
                    ImagePicker.Pick(playlist.Images, 300),
                    isPlaylist: true));
            }
        }

        ShowSections();
    }

    /// <summary>Which sections show, for the filter chosen; with nothing typed, the recent searches.</summary>
    private void ShowSections()
    {
        var results = _results;
        var searching = results is not null || OwnPlaylistCards.Count > 0;
        FilterBar.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        ShowRecent(!searching);

        // Only the kinds found get a filter; one that found nothing this time goes back to All.
        SongsFilter.Visibility = _allSongs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsFilter.Visibility = ArtistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlbumsFilter.Visibility = AlbumCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsFilter.Visibility = PlaylistCards.Count + OwnPlaylistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (searching && _filter != "All" && FilterButton(_filter).Visibility != Visibility.Visible)
        {
            _filter = "All";
            ShowFilter();
        }

        var all = _filter == "All";
        Songs.Clear();
        foreach (var row in all ? _allSongs.Take(SongsBesideTop) : _allSongs)
        {
            Songs.Add(row);
        }

        var songs = searching && (all || _filter == "Songs") && Songs.Count > 0;
        var top = searching && all && _openTop is not null;
        TopSection.Visibility = top ? Visibility.Visible : Visibility.Collapsed;
        SongsSection.Visibility = songs ? Visibility.Visible : Visibility.Collapsed;
        TopRow.Visibility = top || songs ? Visibility.Visible : Visibility.Collapsed;
        FitTopRow(TopRow.ActualWidth);
        ArtistsSection.Visibility = Shown("Artists", ArtistCards.Count);
        AlbumsSection.Visibility = Shown("Albums", AlbumCards.Count);
        PlaylistsSection.Visibility = Shown("Playlists", PlaylistCards.Count);
        OwnPlaylistsSection.Visibility = Shown("Playlists", OwnPlaylistCards.Count);

        var nothing = results is not null && _allSongs.Count + ArtistCards.Count + PlaylistCards.Count + AlbumCards.Count + OwnPlaylistCards.Count == 0;
        HintText.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;

        Visibility Shown(string kind, int count) =>
            searching && count > 0 && (all || _filter == kind) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The top result and the songs side by side on a wide page, the songs under it on a narrow one (or alone).</summary>
    private void FitTopRow(double width)
    {
        var top = TopSection.Visibility == Visibility.Visible;
        var wide = width >= SideBySide && top;
        Grid.SetColumn(SongsSection, wide ? 1 : 0);
        Grid.SetRow(SongsSection, wide || !top ? 0 : 1);
        Grid.SetColumnSpan(SongsSection, wide ? 1 : 2);
        Grid.SetColumnSpan(TopSection, wide ? 1 : 2);
        TopRow.ColumnDefinitions[1].Width = wide ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        TopSection.MaxWidth = wide ? double.PositiveInfinity : 520;
        TopSection.HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The best match, large: an artist named as typed, else the first song, else the first artist, album or playlist.</summary>
    private void ShowTop(SearchMatches? results, string query)
    {
        _openTop = null;
        if (results is null || RecentSearches.TopKind(query, results) is not { } kind)
        {
            return;
        }

        var round = false;
        string? image;
        switch (kind)
        {
            case RecentSearchKind.Artist:
                var artist = results.Artists[0];
                round = true;
                image = ImagePicker.Pick(artist.Images, 300);
                TopTitle.Text = artist.Name;
                TopKind.Text = "Artist";
                _openTop = () => OpenArtist(ArtistCards[0]);
                break;
            case RecentSearchKind.Song:
                var row = _allSongs[0];
                image = CoverImages.UrlFor(row.Track, 104);
                TopTitle.Text = row.Track.Title;
                TopKind.Text = "Song · " + row.Track.Artists;
                _openTop = () => Play(row);
                break;
            case RecentSearchKind.Album:
                var album = AlbumCards[0];
                image = album.ImageUrl;
                TopTitle.Text = album.Title;
                TopKind.Text = "Album · " + album.Subtitle;
                _openTop = () => OpenAlbum(album);
                break;
            default:
                // Spotify's playlists may all be the user's own, shown above instead.
                if ((PlaylistCards.Count > 0 ? PlaylistCards[0] : OwnPlaylistCards.FirstOrDefault()) is not { } playlist)
                {
                    return;
                }

                image = playlist.ImageUrl;
                TopTitle.Text = playlist.Title;
                TopKind.Text = "Playlist · " + playlist.Subtitle;
                _openTop = () => OpenPlaylist(playlist);
                break;
        }

        TopCoverBox.CornerRadius = round ? new CornerRadius(52) : new CornerRadius(10);
        TopCoverBox.Background = image is null ? Artwork.PlaceholderBrush(TopTitle.Text) : null;
        TopImage.Source = image is null ? null : _services.Covers.Get(image, 104);
        AutomationProperties.SetName(TopCard, TopTitle.Text);
    }

    private void OnTopClick(object sender, RoutedEventArgs e) => _openTop?.Invoke();

    // Checked, not Click, so a chip chosen from the keyboard filters too; checking the chosen one again changes nothing.
    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string filter } && filter != _filter)
        {
            _filter = filter;
            ShowFilter();
            ShowSections();
        }
    }

    private RadioButton FilterButton(string filter) => filter switch
    {
        "Songs" => SongsFilter,
        "Artists" => ArtistsFilter,
        "Albums" => AlbumsFilter,
        "Playlists" => PlaylistsFilter,
        _ => AllFilter,
    };

    /// <summary>The chosen filter is checked: filled with the accent, and selected for screen readers.</summary>
    private void ShowFilter()
    {
        foreach (var button in (RadioButton[])[AllFilter, SongsFilter, ArtistsFilter, AlbumsFilter, PlaylistsFilter])
        {
            button.IsChecked = button.Tag as string == _filter;
        }
    }

    /// <summary>The recent searches as chips (a click searches again, × forgets it) and what was opened from them.</summary>
    private void ShowRecent(bool show)
    {
        var settings = _services.Settings;
        var any = settings.RecentSearches.Count + settings.RecentSearchPicks.Count > 0;
        RecentSection.Visibility = show && any ? Visibility.Visible : Visibility.Collapsed;
        if (!show || !any)
        {
            RecentQueriesHost.Content = null;
            RecentPicks.Clear();
            return;
        }

        var chips = new WrapPanel();
        foreach (var query in settings.RecentSearches)
        {
            chips.Children.Add(Chip(query));
        }

        RecentQueriesHost.Content = settings.RecentSearches.Count > 0 ? chips : null;
        RecentPicks.Clear();
        foreach (var pick in settings.RecentSearchPicks)
        {
            RecentPicks.Add(new RecentPickItem(pick));
        }

        RecentPicksList.Visibility = RecentPicks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button Chip(string query)
    {
        var resources = Application.Current.Resources;
        var forget = new Button
        {
            Style = (Style)resources["ResonateIconButtonStyle"],
            Content = "",
            FontSize = 10,
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(forget, "Remove " + query);
        forget.Click += (_, _) =>
        {
            _services.Settings.RecentSearches.Remove(query);
            _services.SaveSettings();
            ShowRecent(true);
        };

        var label = new TextBlock { Text = query, Style = (Style)resources["ResonateBodyTextStyle"], VerticalAlignment = VerticalAlignment.Center };
        var chip = new Button
        {
            Style = (Style)resources["ResonateSubtleButtonStyle"],
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 4, 4, 4),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, forget } },
        };
        AutomationProperties.SetName(chip, query);
        chip.Click += (_, _) =>
        {
            QueryBox.Text = query;
            QueryBox.SelectionStart = query.Length;
            QueryBox.Focus(FocusState.Programmatic);
        };
        return chip;
    }

    private void OnClearRecentClick(object sender, RoutedEventArgs e)
    {
        _services.Settings.RecentSearches.Clear();
        _services.Settings.RecentSearchPicks.Clear();
        _services.SaveSettings();
        ShowRecent(true);
    }

    private void OnRecentPickClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RecentPickItem { Pick: var pick })
        {
            return;
        }

        Remember(pick);
        switch (pick.Kind)
        {
            case RecentSearchKind.Song:
                var track = pick.ToTrack();
                _ = _services.Player.PlayAsync(new PlayRequest([track], 0, track.AlbumUri, track.Album));
                break;
            case RecentSearchKind.Artist when pick.Id is { } artist:
                App.MainWindow?.Open(TrackActions.ArtistKey(artist));
                break;
            case RecentSearchKind.Album when pick.Id is { } album:
                App.MainWindow?.Open(AlbumSource.Prefix + album);
                break;
            case RecentSearchKind.Playlist when pick.Id is { } playlist:
                App.MainWindow?.Open(playlist);
                break;
        }
    }

    /// <summary>Keeps the query typed now among the recent searches.</summary>
    private void RememberQuery()
    {
        if (RecentSearches.AddQuery(_services.Settings.RecentSearches, QueryBox.Text))
        {
            _services.SaveSettings();
        }
    }

    /// <summary>Keeps what was opened or played, and the search that found it.</summary>
    private void Remember(RecentSearchPick pick)
    {
        RecentSearches.AddQuery(_services.Settings.RecentSearches, QueryBox.Text);
        RecentSearches.AddPick(_services.Settings.RecentSearchPicks, pick);
        _services.SaveSettings();
    }

    private void OnSongDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // A double-click on a song's own buttons is theirs.
        if (!SongRowActions.InButton(e.OriginalSource, SongList) && ListEvents.DoubleTapped<TrackRow>(SongList, e) is { } row)
        {
            Play(row);
        }
    }

    private void OnSongListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && SongList.SelectedItem is TrackRow row && !SongRowActions.InButton(e.OriginalSource, SongList))
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
            Remember(RecentSearchPick.Song(row.Track));
            _ = _services.Player.PlayAsync(new PlayRequest([row.Track], 0, row.Track.AlbumUri, row.Track.Album));
        }
    }

    private void OnSongContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TrackRow>(SongList, args) is { } row)
        {
            SongList.SelectedItem = row;
            TrackActions.ShowMenu(MenuFor(row), SongList, args);
        }
    }

    private MenuFlyout MenuFor(TrackRow row) => TrackActions.BuildMenu(row.Track, new TrackMenuOptions { Play = () => Play(row) });

    // A song's own buttons, under the pointer and on the selected song: Play on its cover, and More.
    private void OnSongPointerEntered(object sender, PointerRoutedEventArgs e) => _songActions.Entered(sender);

    private void OnSongPointerExited(object sender, PointerRoutedEventArgs e) => _songActions.Exited(sender, e);

    private void OnSongPlayClick(object sender, RoutedEventArgs e)
    {
        if (_songActions.PlayClicked(sender) is { } row)
        {
            Play(row);
        }
    }

    private void OnSongMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackRow row } button)
        {
            SongList.SelectedItem = row;
            TrackActions.ShowMenuAt(MenuFor(row), button);
        }
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem card)
        {
            OpenArtist(card);
        }
    }

    private void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem card)
        {
            OpenPlaylist(card);
        }
    }

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem card)
        {
            OpenAlbum(card);
        }
    }

    private void OpenArtist(CardItem card)
    {
        if (card.Id is { } id)
        {
            Remember(new RecentSearchPick(RecentSearchKind.Artist, card.Uri, id, card.Title, card.Subtitle, card.ImageUrl));
            App.MainWindow?.Open(TrackActions.ArtistKey(id));
        }
    }

    private void OpenPlaylist(CardItem card)
    {
        if (card.Id is { } id)
        {
            Remember(new RecentSearchPick(RecentSearchKind.Playlist, card.Uri, id, card.Title, card.Subtitle, card.ImageUrl));
            App.MainWindow?.Open(id);
        }
    }

    private void OpenAlbum(CardItem card)
    {
        if (card.Id is { } id)
        {
            Remember(new RecentSearchPick(RecentSearchKind.Album, card.Uri, id, card.Title, card.Subtitle, card.ImageUrl));
            App.MainWindow?.Open(AlbumSource.Prefix + id);
        }
    }
}
