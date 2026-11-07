using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Controls;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Lyrics;
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
/// With Line Finder on (a built-in plugin), Lyrics finds songs by a line of
/// their words from the index on this PC instead, asking Spotify nothing,
/// and a song found starts at that line.
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

    // Lyrics are searched on this PC, so a short pause in typing is enough.
    private static readonly TimeSpan LyricsTypingPause = TimeSpan.FromMilliseconds(150);

    // A line starts this much before its time, so its first word is heard whole.
    private static readonly TimeSpan LineLeadIn = TimeSpan.FromSeconds(1);

    // The filter for Line Finder's songs found by a line of their words.
    private const string LyricsKind = "Lyrics";

    // The cover on a song found by a line.
    private const int LineCoverSize = 76;

    private readonly AppServices _services = App.Services;
    private readonly List<TrackRow> _allSongs = [];
    private readonly SongRowActions _songActions;
    private CancellationTokenSource? _search;
    private SearchMatches? _results;
    private string _filter = "All";
    private Action? _openTop;
    private (List<SimplifiedPlaylist> Playlists, int Count, QuickSearchIndex Index)? _ownIndex;

    // What Spotify's results and Line Finder's lines were found for (null: none).
    private string? _resultsFor;
    private IReadOnlyList<LineMatch>? _lines;
    private string? _linesFor;

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

    /// <summary>Lyrics was the filter last time, so the next visit finds lines again.</summary>
    private static bool LyricsChosen { get; set; }

    private bool LineFinderOn => _services.BuiltIns.IsOn(BuiltInPlugins.LineFinder);

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
        _filter = LyricsChosen && LineFinderOn ? LyricsKind : "All";
        QueryBox.Text = query;
        QueryBox.SelectionStart = query.Length;
        QueryBox.Focus(FocusState.Programmatic);
        ShowFilter();
        _services.BuiltIns.Changed += OnPluginChanged;
        _services.LineFinder.ProgressChanged += OnLineFinderProgress;
        ShowLineFinderProgress();

        // Setting the text before the page is shown does not always raise
        // TextChanged, so search for it here.
        _ = SearchAsync(query, TimeSpan.Zero);
        TrackColumns.OptionsChanged += OnColumnOptionsChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _search?.Cancel();
        TrackColumns.OptionsChanged -= OnColumnOptionsChanged;
        _services.BuiltIns.Changed -= OnPluginChanged;
        _services.LineFinder.ProgressChanged -= OnLineFinderProgress;
    }

    /// <summary>Line Finder turned on or off in Settings while the page shows: Lyrics comes or goes.</summary>
    private void OnPluginChanged(object? sender, string id)
    {
        if (id != BuiltInPlugins.LineFinder)
        {
            return;
        }

        if (!LineFinderOn && _filter == LyricsKind)
        {
            ChooseFilter("All");
        }

        ShowLineFinderProgress();
        ShowSections();
    }

    private void OnLineFinderProgress(object? sender, EventArgs e) => ShowLineFinderProgress();

    /// <summary>While Line Finder's index fills: a ring on Lyrics, and how many songs of how many beside it while Lyrics is chosen.</summary>
    private void ShowLineFinderProgress()
    {
        var progress = _services.LineFinder.Progress;
        var building = LineFinderOn && progress.IsBuilding;
        LyricsRing.IsActive = building;
        LyricsRing.Visibility = building ? Visibility.Visible : Visibility.Collapsed;
        LyricsRing.Value = progress.Total > 0 ? (double)progress.Done / progress.Total : 0;
        LyricsCount.Text = building ? $"{progress.Done:N0} / {progress.Total:N0}" : string.Empty;
        LyricsCount.Visibility = building && _filter == LyricsKind ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>Enter keeps the search among the recent ones; on Lyrics it also plays the first song found, from its line.</summary>
    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        RememberQuery();
        if (_filter == LyricsKind)
        {
            e.Handled = true;
            _ = PlayFirstLineAsync();
        }
    }

    /// <summary>The first song found for what is typed now, from its line (searched again at once if typing was faster than the search).</summary>
    private async Task PlayFirstLineAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        if (_linesFor != query)
        {
            await SearchAsync(QueryBox.Text, TimeSpan.Zero);
        }

        if (_filter == LyricsKind && _linesFor == query && _lines is [var first, ..])
        {
            PlayLine(first);
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
            ShowLines(null, query);
            Show(null, query);
            return;
        }

        if (_filter == LyricsKind)
        {
            await SearchLinesAsync(query, wait == TimeSpan.Zero ? wait : LyricsTypingPause, token);
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

    /// <summary>
    /// Line Finder: the songs with a line that fits what was typed, from the
    /// index on this PC (no request to Spotify). Spotify's results for other
    /// words go; they come back from Spotify only when another filter is chosen.
    /// </summary>
    private async Task SearchLinesAsync(string query, TimeSpan wait, CancellationToken token)
    {
        try
        {
            await Task.Delay(wait, token);
            token.ThrowIfCancellationRequested();
            SearchingRing.IsActive = false;
            if (_results is not null && _resultsFor != query)
            {
                Show(null, query);
            }

            var lines = await _services.LineFinder.SearchAsync(query, token);
            if (!token.IsCancellationRequested)
            {
                ShowLines(lines, query);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The songs found by a line, as wide cards: the first one marked, as Enter plays it.</summary>
    private void ShowLines(IReadOnlyList<LineMatch>? lines, string query)
    {
        _lines = lines;
        _linesFor = lines is null ? null : query;
        LyricsSection.Children.Clear();
        if (lines is not null)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                LyricsSection.Children.Add(LineCard(lines[i], first: i == 0));
            }
        }

        ShowSections();
    }

    /// <summary>
    /// One song found by a line: its cover, the line sung large with the words
    /// typed in the accent colour between the lines before and after, then
    /// the song, its artists and when the line comes. Built here, not from a
    /// template, so the line can carry its coloured words.
    /// </summary>
    private Button LineCard(LineMatch match, bool first)
    {
        var resources = Application.Current.Resources;
        var track = match.Track;

        var cover = new Grid
        {
            Width = LineCoverSize,
            Height = LineCoverSize,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(8),
        };
        var image = _services.Covers.Get(CoverImages.UrlFor(track, LineCoverSize), LineCoverSize, out var missing);
        if (image is not null)
        {
            cover.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
        }

        _ = ShowColourTileIfMissingAsync(cover, missing, track.Album.Length > 0 ? track.Album : track.Title);

        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (match.Before is { } before)
        {
            lines.Children.Add(FadedLine(before, resources));
        }

        lines.Children.Add(SungLine(match, resources));
        if (match.After is { } after)
        {
            lines.Children.Add(FadedLine(after, resources));
        }

        Grid.SetColumn(lines, 1);

        var song = new StackPanel
        {
            Spacing = 8,
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        song.Children.Add(new TextBlock
        {
            Text = track.Title,
            Style = (Style)resources["ResonateBodyTextStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
        });
        song.Children.Add(new TextBlock
        {
            Text = track.Artists,
            Style = (Style)resources["ResonateCaptionTextStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
        });
        if (match.At is { } at)
        {
            song.Children.Add(new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                HorizontalAlignment = HorizontalAlignment.Right,
                CornerRadius = new CornerRadius(12),
                Background = (Brush)resources["ResonateSurfaceHoverBrush"],
                Child = new TextBlock
                {
                    Text = Format.Duration(at),
                    Style = (Style)resources["ResonateCaptionTextStyle"],
                    Foreground = (Brush)resources["ResonateTextPrimaryBrush"],
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                },
            });
        }

        Grid.SetColumn(song, 2);

        var card = new Button
        {
            Style = (Style)Resources["LineCardStyle"],
            Content = new Grid
            {
                ColumnSpacing = 20,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(LineCoverSize) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children = { cover, lines, song },
            },
        };
        if (first)
        {
            card.Background = (Brush)resources["ResonateAccentSoftBrush"];
            card.BorderBrush = (Brush)resources["ResonateAccentBrush"];
        }

        AutomationProperties.SetName(card, $"{match.Line}, {track.Title}, {track.Artists}");
        card.Click += (_, _) => PlayLine(match);
        return card;
    }

    /// <summary>The line sung, large, with the words typed in the accent colour.</summary>
    private static TextBlock SungLine(LineMatch match, ResourceDictionary resources)
    {
        var text = new TextBlock { Style = (Style)resources["ResonateSectionTextStyle"], Margin = new Thickness(0, 2, 0, 2) };
        var accent = (Brush)resources["ResonateAccentTextBrush"];
        var line = match.Line;
        var at = 0;
        foreach (var mark in match.Marks)
        {
            if (mark.Start < at || mark.Start + mark.Length > line.Length)
            {
                continue;
            }

            if (mark.Start > at)
            {
                text.Inlines.Add(new Run { Text = line[at..mark.Start] });
            }

            text.Inlines.Add(new Run { Text = line.Substring(mark.Start, mark.Length), Foreground = accent });
            at = mark.Start + mark.Length;
        }

        if (at < line.Length)
        {
            text.Inlines.Add(new Run { Text = line[at..] });
        }

        return text;
    }

    /// <summary>The line before or after, quieter.</summary>
    private static TextBlock FadedLine(string line, ResourceDictionary resources) =>
        new() { Text = line, Style = (Style)resources["ResonateSecondaryTextStyle"], TextWrapping = TextWrapping.NoWrap };

    /// <summary>A colour tile in place of a cover that can not be had (a picture never shows over one while it loads).</summary>
    private static async Task ShowColourTileIfMissingAsync(Grid cover, Task<bool> missing, string name)
    {
        if (await missing)
        {
            cover.Background = Artwork.PlaceholderBrush(name);
        }
    }

    /// <summary>Plays a song found by a line from just before that line (from its start for lyrics without timing), inside its album like any song found.</summary>
    private void PlayLine(LineMatch match)
    {
        var track = match.Track;
        Remember(RecentSearchPick.Song(track));
        var start = match.At is { } at ? (at > LineLeadIn ? at - LineLeadIn : TimeSpan.Zero) : (TimeSpan?)null;
        _ = _services.Player.PlayAsync(new PlayRequest([track], 0, track.AlbumUri, track.Album) { StartPosition = start });
    }

    private void Show(SearchMatches? results, string query)
    {
        _results = results;
        _resultsFor = results is null ? null : query;
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
        var lyrics = _filter == LyricsKind;
        var lineFinder = LineFinderOn;
        var searching = results is not null || OwnPlaylistCards.Count > 0 || (lyrics && _linesFor is not null);

        // With Line Finder on, Lyrics can be chosen before anything is typed, so no search goes to Spotify.
        FilterBar.Visibility = searching || lineFinder ? Visibility.Visible : Visibility.Collapsed;
        LyricsFilter.Visibility = lineFinder ? Visibility.Visible : Visibility.Collapsed;
        ShowRecent(!searching);

        // Only the kinds found get a filter; one that found nothing this time goes back to All.
        SongsFilter.Visibility = _allSongs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArtistsFilter.Visibility = ArtistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlbumsFilter.Visibility = AlbumCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsFilter.Visibility = PlaylistCards.Count + OwnPlaylistCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (searching && _filter is not "All" and not LyricsKind && FilterButton(_filter).Visibility != Visibility.Visible)
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

        LyricsSection.Visibility = lyrics && _lines is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        var nothing = lyrics
            ? _lines is { Count: 0 }
            : results is not null && _allSongs.Count + ArtistCards.Count + PlaylistCards.Count + AlbumCards.Count + OwnPlaylistCards.Count == 0;
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
            ChooseFilter(filter);
        }
    }

    /// <summary>
    /// Shows one kind of result. Lyrics are found on this PC at once; leaving
    /// Lyrics after typing there asks Spotify for the words now in the box
    /// (one search, as typing would have).
    /// </summary>
    private void ChooseFilter(string filter)
    {
        var fromLyrics = _filter == LyricsKind;
        _filter = filter;
        LyricsChosen = filter == LyricsKind;
        ShowFilter();
        ShowLineFinderProgress();
        var query = LastQuery.Trim();
        if (filter == LyricsKind || (fromLyrics && query.Length > 0 && _resultsFor != query))
        {
            _ = SearchAsync(LastQuery, TimeSpan.Zero);
        }

        ShowSections();
    }

    private RadioButton FilterButton(string filter) => filter switch
    {
        "Songs" => SongsFilter,
        "Artists" => ArtistsFilter,
        "Albums" => AlbumsFilter,
        "Playlists" => PlaylistsFilter,
        LyricsKind => LyricsFilter,
        _ => AllFilter,
    };

    /// <summary>The chosen filter is checked: filled with the accent, and selected for screen readers.</summary>
    private void ShowFilter()
    {
        foreach (var button in (RadioButton[])[AllFilter, SongsFilter, ArtistsFilter, AlbumsFilter, PlaylistsFilter, LyricsFilter])
        {
            button.IsChecked = button.Tag as string == _filter;
        }

        // The ring on Lyrics stays visible on the chip's accent fill.
        LyricsRing.Foreground = (Brush)Application.Current.Resources[_filter == LyricsKind ? "ResonateOnAccentBrush" : "ResonateAccentBrush"];
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
