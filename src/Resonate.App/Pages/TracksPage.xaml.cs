using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Pages;

/// <summary>
/// A list of songs: Liked Songs, a playlist, an album, Local Files or a mix.
/// The whole list loads once (from the stored copy when it is current), so
/// sorting, filtering and shuffling cover every song and happen at once.
/// </summary>
public sealed partial class TracksPage : Page
{
    /// <summary>Below this list width the page uses its compact layout (wider with larger text).</summary>
    private const double CompactWidth = 600;
    private const int CoverSize = 232;
    private const int CompactCoverSize = 128;

    private const string OpenInSpotifyLabel = "Open in Spotify";
    private const string UpGlyph = "";
    private const string DownGlyph = "";

    private readonly AppServices _services = App.Services;
    private readonly CancellationTokenSource _leaving = new();
    private readonly Dictionary<TrackInfo, TrackRow> _rowCache = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherQueueTimer _filterTimer;
    private readonly CoverHero _hero;
    private TrackListSource _source = null!;
    private ListHeader _header = null!;
    private TrackColumns _columns = null!;
    private List<TrackInfo> _all = [];
    private List<TrackInfo> _shown = [];
    private ObservableCollection<TrackRow> _rows = [];
    private Task<FullTrackList>? _fullLoad;
    private bool _complete;
    private bool _itemsHidden;
    private bool? _compact;
    private TrackSort _sort = TrackSort.Default;
    private string _filter = string.Empty;
    private string? _highlightedTrack;
    private int _highlightQueued;
    private int _sourceChangeQueued;
    private int _sourceSongsChanged;
    private bool _reloading;
    private bool _reloadAgain;
    private TrackRow? _dragged;
    private int _dragFrom = -1;

    /// <summary>A move or removal is on its way to Spotify; no other starts until it is done, as the next counts from the positions it gives.</summary>
    private bool _editing;

    public TracksPage()
    {
        InitializeComponent();
        _filterTimer = DispatcherQueue.CreateTimer();
        _filterTimer.Interval = TimeSpan.FromMilliseconds(150);
        _filterTimer.IsRepeating = false;
        _hero = new CoverHero(Hero);
    }

    /// <summary>The list shown is in its own order, unfiltered (so it can play inside its Spotify context and be rearranged).</summary>
    private bool InOwnOrder => _sort.IsDefault && _filter.Length == 0;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _source = e.Parameter as TrackListSource
            ?? TrackListSource.For(e.Parameter as string ?? LikedSongsSource.ListKey, _services);
        _columns = new TrackColumns(album: !_source.IsAlbum, dateAdded: _source.HasDateAdded);
        FitToWidth(TrackList.ActualWidth > 0 ? TrackList.ActualWidth : double.PositiveInfinity);
        AlbumHeading.Visibility = _source.IsAlbum ? Visibility.Collapsed : Visibility.Visible;
        AddedHeading.Visibility = _source.HasDateAdded ? Visibility.Visible : Visibility.Collapsed;

        _sort = TrackSort.Parse(_services.Settings.TrackSorts.GetValueOrDefault(_source.Key));
        if (!SortFields().Contains(_sort.Field))
        {
            _sort = TrackSort.Default;
        }

        BuildSortMenu();
        if (_source.CreatePanel() is { } panel)
        {
            SourcePanel.Child = panel;
            SourcePanel.Visibility = Visibility.Visible;
        }

        // Subscribed only while shown: the timer is not part of the page's
        // tree, so a handler left on it would keep the page alive for good.
        _filterTimer.Tick += OnFilterTick;
        _services.Player.StateChanged += OnPlayerStateChanged;
        _services.Likes.Changed += OnLikesChanged;
        _services.Theme.SizeChanged += OnTextSizeChanged;
        _source.Attach(OnSourceChanged);
        _hero.Attach();
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _source.Detach();
        _services.Player.StateChanged -= OnPlayerStateChanged;
        _services.Likes.Changed -= OnLikesChanged;
        _services.Theme.SizeChanged -= OnTextSizeChanged;
        _filterTimer.Stop();
        _filterTimer.Tick -= OnFilterTick;
        _hero.Detach();
        _leaving.Cancel();
    }

    private void OnFilterTick(DispatcherQueueTimer sender, object args) => ApplyView();

    private async Task LoadAsync()
    {
        var token = _leaving.Token;
        var source = _source;
        ShowHeader(source.CachedHeader);
        _ = LoadHeaderAsync(source, token);

        var full = Task.Run(() => source.LoadAllAsync(token), token);
        _fullLoad = full;

        // A quick answer just appears. A slow one shows the first songs
        // meanwhile, and a spinner only if even those are slow.
        var quick = Task.Delay(120, token);
        if (await Task.WhenAny(full, quick) == quick && !full.IsCompleted)
        {
            _ = ShowPreviewAsync(source, full, token);
            if (await Task.WhenAny(full, Task.Delay(150, token)) != full && _rows.Count == 0)
            {
                LoadingRing.IsActive = true;
            }
        }

        try
        {
            ShowAll(await full);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            UpdateEmpty();
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async Task LoadHeaderAsync(TrackListSource source, CancellationToken token)
    {
        try
        {
            if (await Task.Run(() => source.LoadHeaderAsync(token), token) is { } header)
            {
                ShowHeader(header);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cached header stays; the songs report their own errors.
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ShowPreviewAsync(TrackListSource source, Task<FullTrackList> full, CancellationToken token)
    {
        try
        {
            var preview = await Task.Run(() => source.LoadPreviewAsync(token), token);
            if (preview is { Count: > 0 } && !full.IsCompleted && !_complete)
            {
                LoadingRing.IsActive = false;
                _all = preview.ToList();
                ApplyView();
            }
        }
        catch (Exception)
        {
            // The full list reports any error.
        }
    }

    private void ShowHeader(ListHeader header)
    {
        _header = header;
        KindText.Text = header.Kind;
        TitleText.Text = header.Title.Length > 0 ? header.Title : " ";
        DescriptionText.Text = header.Description ?? string.Empty;
        DescriptionText.Visibility = string.IsNullOrEmpty(header.Description) ? Visibility.Collapsed : Visibility.Visible;

        CoverFrame.Background = Artwork.PlaceholderBrush(header.PlaceholderName);
        CoverGlyph.Glyph = header.Glyph ?? string.Empty;
        CoverGlyph.Visibility = header.Glyph is null ? Visibility.Collapsed : Visibility.Visible;
        if (header.ImageUrl is not null)
        {
            CoverImage.Source = Artwork.FromUrl(header.ImageUrl, CoverSize);
        }

        _hero.Show(Artwork.PlaceholderColors(header.PlaceholderName).From, header.ImageUrl);

        ArtistLinks.Children.Clear();
        foreach (var artist in header.Artists)
        {
            var link = new HyperlinkButton { Content = artist.Name, Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            if (artist.Id is { } id)
            {
                link.Click += (_, _) => App.MainWindow?.Open(TrackActions.ArtistKey(id));
            }

            ArtistLinks.Children.Add(link);
        }

        // An empty panel would still push the details over by the row's spacing.
        ArtistLinks.Visibility = header.Artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenInSpotifyButton.Visibility = _source.SpotifyLink is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_header.Details))
        {
            parts.Add(_header.Details);
        }

        if (_complete && !_itemsHidden && !_source.ShowsOwnTotals)
        {
            parts.Add(ListFormat.CountAndLength(_all));
        }

        // After the artist links, the details continue the same line.
        var details = string.Join(" · ", parts);
        DetailsText.Text = _header.Artists.Count > 0 && details.Length > 0 ? "· " + details : details;
    }

    private void ShowAll(FullTrackList list)
    {
        var previous = _all;
        _complete = true;
        _itemsHidden = list.ItemsHidden;
        _all = list.Tracks.ToList();

        HiddenNotice.Visibility = _itemsHidden ? Visibility.Visible : Visibility.Collapsed;
        ColumnHeadings.Visibility = _itemsHidden ? Visibility.Collapsed : Visibility.Visible;
        FilterBox.IsEnabled = !_itemsHidden;
        SortButton.IsEnabled = !_itemsHidden;
        UpdateDetails();

        // The first songs are already shown in order: add the rest below them, so nothing jumps.
        if (InOwnOrder && previous.Count > 0 && previous.Count <= _all.Count && _rows.Count == previous.Count
            && previous.Select(t => t.Uri).SequenceEqual(_all.Take(previous.Count).Select(t => t.Uri)))
        {
            for (var i = 0; i < previous.Count; i++)
            {
                if (_rowCache.Remove(previous[i], out var row))
                {
                    row.Replace(_all[i]);
                    _rowCache[_all[i]] = row;
                }
            }

            _shown = _all.ToList();
            for (var i = previous.Count; i < _all.Count; i++)
            {
                _rows.Add(RowFor(_all[i], i));
            }

            UpdateEditing();
            UpdateEmpty();
            HighlightPlayingTrack(force: true);
            return;
        }

        ApplyView();
    }

    /// <summary>Sorts and filters every song and shows the result.</summary>
    private void ApplyView()
    {
        _shown = TrackSorter.Apply(_all.Where(t => TrackSorter.Matches(t, _filter)), _sort);
        var rows = new List<TrackRow>(_shown.Count);
        for (var i = 0; i < _shown.Count; i++)
        {
            rows.Add(RowFor(_shown[i], i));
        }

        _rows = new ObservableCollection<TrackRow>(rows);
        TrackList.ItemsSource = _rows;
        UpdateEditing();
        UpdateEmpty();
        UpdateSortIndicators();
        HighlightPlayingTrack(force: true);
    }

    private TrackRow RowFor(TrackInfo track, int index)
    {
        var liked = _source is LikedSongsSource || _services.Likes.IsLiked(track.Uri);
        if (!_rowCache.TryGetValue(track, out var row))
        {
            row = new TrackRow(track, index + 1, _columns, liked);
            _rowCache[track] = row;
        }
        else
        {
            row.IsLiked = liked;
        }

        // Albums number songs by track number while shown in album order.
        row.Number = (_source.IsAlbum && InOwnOrder && track.TrackNumber is { } number ? number : index + 1)
            .ToString(System.Globalization.CultureInfo.CurrentCulture);
        return row;
    }

    private void Renumber()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Number = (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private void UpdateEmpty()
    {
        var empty = _complete && !_itemsHidden && _shown.Count == 0;
        EmptyText.Text = _all.Count == 0 ? _source.EmptyText : "No songs match the filter.";
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Songs can be dragged into a new order in the user's own playlists, shown in their own order.</summary>
    private void UpdateEditing()
    {
        var canDrag = _source.CanEdit && _complete && InOwnOrder;
        TrackList.CanDragItems = canDrag;
        TrackList.CanReorderItems = canDrag;
        TrackList.AllowDrop = canDrag;
    }

    // ---- Lists that change by themselves (Local Files) ----

    /// <summary>Called on any thread, often during a scan; the page follows at most once per frame.</summary>
    private void OnSourceChanged(bool songsChanged)
    {
        if (songsChanged)
        {
            Interlocked.Exchange(ref _sourceSongsChanged, 1);
        }

        if (Interlocked.Exchange(ref _sourceChangeQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ShowSourceChange);
        }
    }

    private void ShowSourceChange()
    {
        Interlocked.Exchange(ref _sourceChangeQueued, 0);
        var songsChanged = Interlocked.Exchange(ref _sourceSongsChanged, 0) == 1;
        if (_leaving.IsCancellationRequested)
        {
            return;
        }

        ShowHeader(_source.CachedHeader);
        UpdateEmpty();
        if (!songsChanged)
        {
            return;
        }

        if (_reloading)
        {
            _reloadAgain = true;
        }
        else
        {
            _ = ReloadAsync();
        }
    }

    /// <summary>Loads the songs again and shows what changed; one load at a time, the last one after the last change.</summary>
    private async Task ReloadAsync()
    {
        var token = _leaving.Token;
        _reloading = true;
        try
        {
            do
            {
                _reloadAgain = false;
                if (_fullLoad is { } first && !first.IsCompleted)
                {
                    // The first load lands first; its own errors are shown by it.
                    try
                    {
                        await first;
                    }
                    catch (Exception)
                    {
                    }
                }

                var list = await Task.Run(() => _source.LoadAllAsync(token), token);
                if (!_complete)
                {
                    ShowAll(list);
                    continue;
                }

                _all = list.Tracks.ToList();
                UpdateDetails();
                SyncRows(TrackSorter.Apply(_all.Where(t => TrackSorter.Matches(t, _filter)), _sort));
                UpdateEmpty();
                HighlightPlayingTrack(force: true);
            }
            while (_reloadAgain && !token.IsCancellationRequested);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The list keeps what it shows; the next change tries again.
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>
    /// Brings the rows in line with <paramref name="shown"/> by adding,
    /// removing and moving only what changed, so the list does not jump or
    /// lose its scroll position.
    /// </summary>
    private void SyncRows(List<TrackInfo> shown)
    {
        _shown = shown;
        var keep = new HashSet<TrackInfo>(shown, ReferenceEqualityComparer.Instance);
        if (shown.Count - _rows.Count(r => keep.Contains(r.Track)) > 500)
        {
            // A first scan's big step: building the list again is quicker.
            ApplyView();
            return;
        }

        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_rows[i].Track))
            {
                _rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < shown.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i].Track, shown[i]))
            {
                continue;
            }

            var from = -1;
            for (var j = i + 1; j < _rows.Count; j++)
            {
                if (ReferenceEquals(_rows[j].Track, shown[i]))
                {
                    from = j;
                    break;
                }
            }

            if (from >= 0)
            {
                _rows.Move(from, i);
            }
            else
            {
                _rows.Insert(i, RowFor(shown[i], i));
            }
        }

        Renumber();
        UpdateEditing();
    }

    // ---- Sorting and filtering ----

    private List<TrackSortField> SortFields()
    {
        var fields = new List<TrackSortField> { TrackSortField.Custom, TrackSortField.Title, TrackSortField.Artist };
        if (!_source.IsAlbum)
        {
            fields.Add(TrackSortField.Album);
        }

        if (_source.HasDateAdded)
        {
            fields.Add(TrackSortField.DateAdded);
        }

        fields.Add(TrackSortField.Duration);
        return fields;
    }

    private string FieldName(TrackSortField field) => field switch
    {
        TrackSortField.Custom => _source.OwnOrderName,
        TrackSortField.Title => "Title",
        TrackSortField.Artist => "Artist",
        TrackSortField.Album => "Album",
        TrackSortField.DateAdded => "Date added",
        TrackSortField.Duration => "Duration",
        _ => field.ToString(),
    };

    private void BuildSortMenu()
    {
        SortMenu.Items.Clear();
        foreach (var field in SortFields())
        {
            var item = new RadioMenuFlyoutItem { Text = FieldName(field), GroupName = "field", IsChecked = _sort.Field == field };
            item.Click += (_, _) => SetSort(new TrackSort(field, field != TrackSortField.Custom && _sort.Descending));
            SortMenu.Items.Add(item);
        }

        if (_sort.Field != TrackSortField.Custom)
        {
            SortMenu.Items.Add(new MenuFlyoutSeparator());
            foreach (var descending in new[] { false, true })
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = descending ? "Descending" : "Ascending",
                    GroupName = "direction",
                    IsChecked = _sort.Descending == descending,
                };
                item.Click += (_, _) => SetSort(_sort with { Descending = descending });
                SortMenu.Items.Add(item);
            }
        }

        SortText.Text = FieldName(_sort.Field);
    }

    private void SetSort(TrackSort sort)
    {
        if (sort == _sort)
        {
            return;
        }

        _sort = sort;
        if (sort.IsDefault)
        {
            _services.Settings.TrackSorts.Remove(_source.Key);
        }
        else
        {
            _services.Settings.TrackSorts[_source.Key] = sort.Serialize();
        }

        _services.SaveSettings();
        BuildSortMenu();
        ApplyView();
    }

    private void UpdateSortIndicators()
    {
        foreach (var (arrow, field) in new[]
        {
            (TitleArrow, TrackSortField.Title),
            (AlbumArrow, TrackSortField.Album),
            (AddedArrow, TrackSortField.DateAdded),
            (DurationArrow, TrackSortField.Duration),
        })
        {
            arrow.Visibility = _sort.Field == field ? Visibility.Visible : Visibility.Collapsed;
            arrow.Glyph = _sort.Descending ? DownGlyph : UpGlyph;
        }
    }

    /// <summary>A click on a heading sorts by it, a second reverses, a third goes back to the list's own order.</summary>
    private void OnHeadingTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_itemsHidden || (sender as FrameworkElement)?.Tag is not string tag || !Enum.TryParse<TrackSortField>(tag, out var field))
        {
            return;
        }

        // Newest first is the useful start for dates.
        var first = field == TrackSortField.DateAdded;
        SetSort(_sort.Field != field
            ? new TrackSort(field, first)
            : _sort.Descending != first ? TrackSort.Default : new TrackSort(field, !first));
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _filter = FilterBox.Text.Trim();
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    // ---- Playing ----

    private void OnTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!IsInsideButton(e.OriginalSource as DependencyObject) && ListEvents.DoubleTapped<TrackRow>(TrackList, e) is { } row)
        {
            _ = PlayAsync(row);
        }
    }

    private void OnTrackListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TrackList.SelectedItem is not TrackRow row)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = PlayAsync(row);
        }
        else if (e.Key == VirtualKey.Delete && _source.CanEdit && _complete && !_editing)
        {
            e.Handled = true;
            _ = RemoveAsync(row);
        }
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => _ = PlayListAsync(shuffle: false);

    private void OnShuffleClick(object sender, RoutedEventArgs e) => _ = PlayListAsync(shuffle: true);

    private Task PlayAsync(TrackRow row)
    {
        var index = _shown.IndexOf(row.Track);
        return index < 0 ? Task.CompletedTask : PlayFromAsync(index, shuffle: null);
    }

    /// <summary>"Play" from the top in the order shown, or "Shuffle" in a truly random order.</summary>
    private Task PlayListAsync(bool shuffle) => PlayFromAsync(-1, shuffle);

    private async Task PlayFromAsync(int index, bool? shuffle)
    {
        var picked = index >= 0 ? _shown[index] : null;

        // A random order needs every song; wait for the rest if only the first
        // ones are here. In order, it plays at once (inside its playlist or
        // album, Spotify has every song; the player is told the list is partial).
        if ((shuffle ?? _services.Player.State.Shuffle) && !_complete && _fullLoad is { } full)
        {
            try
            {
                await full;
            }
            catch (Exception)
            {
                // Play what is there.
            }

            index = picked is null ? -1 : _shown.FindIndex(t => t.Uri == picked.Uri && t.FilePath == picked.FilePath);
        }

        var request = new PlayRequest(_shown.ToList(), index, InOwnOrder ? _source.ContextUri : null, _header.Title)
        {
            Shuffle = shuffle,
            IsPartial = !_complete,
        };
        App.MainWindow?.NoteListPlayed(_source.Key, request.SourceName);
        await _services.Player.PlayAsync(request);
    }

    private void OnTextSizeChanged(object? sender, EventArgs e) =>
        FitToWidth(TrackList.ActualWidth > 0 ? TrackList.ActualWidth : double.PositiveInfinity);

    private void OnTrackListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            FitToWidth(e.NewSize.Width);
        }
    }

    /// <summary>
    /// Fits the page to the list's width: narrow lists (a small window, or
    /// the queue open beside them) drop columns, get a smaller cover, and
    /// put the filter and sort on a row of their own.
    /// </summary>
    private void FitToWidth(double width)
    {
        var text = _services.Theme.TextScale;
        _columns.Fit(width, text);
        AlbumHeadingColumn.Width = _columns.AlbumWidth;
        AddedHeadingColumn.Width = _columns.AddedWidth;

        var compact = width < CompactWidth * text;
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        var cover = compact ? CompactCoverSize : CoverSize;
        CoverColumn.Width = new GridLength(cover);
        CoverFrame.Width = cover;
        CoverFrame.Height = cover;
        CoverShadow.Width = cover;
        CoverShadow.Height = cover;

        // A smaller title, so a long name still fits on its two lines (a style, so it follows the Text size).
        TitleText.Style = (Style)Application.Current.Resources[compact ? "ResonateCompactDisplayTextStyle" : "ResonateDisplayTextStyle"];

        Grid.SetRow(FilterBox, compact ? 1 : 0);
        Grid.SetColumn(FilterBox, compact ? 0 : 4);
        Grid.SetColumnSpan(FilterBox, compact ? 5 : 1);
        Grid.SetRow(SortButton, compact ? 1 : 0);
        SortText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        OpenInSpotifyText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        OpenInSpotifyIcon.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(OpenInSpotifyButton, compact ? OpenInSpotifyLabel : null);
    }

    private void OnOpenInSpotifyClick(object sender, RoutedEventArgs e) => TrackActions.OpenInSpotify(_source.SpotifyLink);

    // ---- Likes, the menu, rearranging ----

    private void OnHeartClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackRow row)
        {
            _ = TrackActions.SetLikedAsync(row.Track, !row.IsLiked);
        }
    }

    private void OnLikesChanged(object? sender, LikeChange change) =>
        DispatcherQueue.TryEnqueue(() => ShowLikeChange(change));

    private void ShowLikeChange(LikeChange change)
    {
        if (_leaving.IsCancellationRequested)
        {
            return;
        }

        if (_source is LikedSongsSource && change.Track is { } track && _complete)
        {
            // Liked Songs follows at once, without loading again.
            if (!change.IsLiked)
            {
                RemoveRows(t => t.Uri == track.Uri);
            }
            else if (!_all.Any(t => t.Uri == track.Uri))
            {
                var added = track with { AddedAt = DateTimeOffset.UtcNow };
                _all.Insert(0, added);
                if (InOwnOrder)
                {
                    _shown.Insert(0, added);
                    _rows.Insert(0, RowFor(added, 0));
                    Renumber();
                }
                else
                {
                    ApplyView();
                }
            }

            UpdateDetails();
            UpdateEmpty();
            return;
        }

        foreach (var row in _rowCache.Values)
        {
            if (change.Uri is null || row.Track.Uri == change.Uri)
            {
                row.IsLiked = _source is LikedSongsSource || _services.Likes.IsLiked(row.Track.Uri);
            }
        }
    }

    private void OnTrackContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TrackRow>(TrackList, args) is not { } row)
        {
            return;
        }

        TrackList.SelectedItem = row;
        var options = new TrackMenuOptions
        {
            Play = () => _ = PlayAsync(row),
            Remove = _source.CanEdit && _complete && !_editing ? () => _ = RemoveAsync(row) : null,
            CurrentPlaylistId = (_source as PlaylistSource)?.PlaylistId,
        };
        TrackActions.ShowMenu(TrackActions.BuildMenu(row.Track, options), TrackList, args);
    }

    private async Task RemoveAsync(TrackRow row)
    {
        if (_editing)
        {
            return;
        }

        _editing = true;
        var before = _all.ToList();
        var track = row.Track;
        RemoveRows(t => t.Uri == track.Uri);
        UpdateDetails();
        UpdateEmpty();
        try
        {
            await Task.Run(() => _source.RemoveAsync(before, track, CancellationToken.None));
            if (track.Uri is { } uri)
            {
                UsePositions(LibraryService.AfterRemove(before, uri));
            }
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            ShowAll(new FullTrackList(before, ItemsHidden: false));
        }
        finally
        {
            _editing = false;
        }
    }

    private void RemoveRows(Func<TrackInfo, bool> match)
    {
        _all.RemoveAll(t => match(t));
        _shown.RemoveAll(t => match(t));
        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (match(_rows[i].Track))
            {
                _rows.RemoveAt(i);
            }
        }

        Renumber();
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = e.Items.Count == 1 && !_editing ? e.Items[0] as TrackRow : null;
        _dragFrom = _dragged is null ? -1 : _rows.IndexOf(_dragged);
        if (_dragged is null)
        {
            e.Cancel = true;
        }
    }

    private async void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var row = _dragged;
        var from = _dragFrom;
        _dragged = null;
        if (row is null || args.DropResult != DataPackageOperation.Move)
        {
            return;
        }

        var to = _rows.IndexOf(row);
        if (to < 0 || to == from)
        {
            return;
        }

        _editing = true;
        var before = _all.ToList();
        _all = _rows.Select(r => r.Track).ToList();
        _shown = _all.ToList();
        Renumber();
        try
        {
            await Task.Run(() => _source.MoveAsync(before, from, to, CancellationToken.None));
            UsePositions(LibraryService.AfterMove(before, from, to));
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
            ShowAll(new FullTrackList(before, ItemsHidden: false));
        }
        finally
        {
            _editing = false;
        }
    }

    /// <summary>
    /// After a move or removal, songs sit at new positions in the playlist;
    /// the next change counts from them. <paramref name="updated"/> holds
    /// the same songs as the page, in the same order.
    /// </summary>
    private void UsePositions(List<TrackInfo> updated)
    {
        if (!updated.Select(t => t.Uri).SequenceEqual(_all.Select(t => t.Uri)))
        {
            return;
        }

        var replaced = new Dictionary<TrackInfo, TrackInfo>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < _all.Count; i++)
        {
            if (!ReferenceEquals(_all[i], updated[i]))
            {
                replaced[_all[i]] = updated[i];
                _all[i] = updated[i];
            }
        }

        // The order shown may be sorted differently by now.
        for (var i = 0; i < _shown.Count; i++)
        {
            if (replaced.TryGetValue(_shown[i], out var now))
            {
                _shown[i] = now;
            }
        }

        foreach (var (old, now) in replaced)
        {
            if (_rowCache.Remove(old, out var row))
            {
                row.Replace(now);
                _rowCache[now] = row;
            }
        }
    }

    // ---- The playing song ----

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _highlightQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _highlightQueued, 0);
                HighlightPlayingTrack(force: false);
            });
        }
    }

    private void HighlightPlayingTrack(bool force)
    {
        var state = _services.Player.State;
        var key = state.TrackUri ?? state.Title;
        if (!force && key == _highlightedTrack)
        {
            return;
        }

        _highlightedTrack = key;
        foreach (var row in _rows)
        {
            row.IsCurrent = state.TrackUri is not null
                ? row.Track.Uri == state.TrackUri
                : state.Title is not null && row.Title == state.Title;
        }
    }

    /// <summary>Double-clicking the heart should like the song, not play it.</summary>
    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null and not ListViewItem)
        {
            if (element is ButtonBase)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }
}
