using System.Collections.ObjectModel;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.App.Controls;

/// <summary>
/// The queue, in a pane next to the pages: the song playing and the songs
/// that play next. Spotify's queue is read (GET /me/player/queue) when the
/// pane opens and after the song, shuffle or repeat changes, never while the
/// pane is closed; the local files player says what it plays next itself.
/// Spotify does not let other apps reorder or clear its queue, so the pane
/// only shows it.
/// </summary>
public sealed partial class QueuePanel : UserControl
{
    /// <summary>The pane's width next to the pages.</summary>
    public const double PaneWidth = 340;

    private const string SpotifyNote = "Spotify shares about 20 songs ahead, and does not let other apps reorder or clear its queue. Right-click a song for more.";
    private const string LocalNote = "Right-click a song for more.";

    // Spotify shares about 20; the local files player knows the whole rest of its list.
    private const int MaxRows = 100;

    // Spotify's queue catches up with a new song a moment after it starts.
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(600);
    private static readonly Vector3 ClosedOffset = new(24, 0, 0);

    private readonly ObservableCollection<TrackRow> _rows = [];
    private readonly DispatcherQueueTimer _refreshTimer;
    private PlayerRouter? _player;
    private CancellationTokenSource? _loading;
    private PlayerState _shown = PlayerState.Empty;
    private string? _queueKey;
    private object? _artworkKey;
    private TrackInfo? _nowTrack;
    private int _updateQueued;
    private int _version;

    // The songs the rows showed when the pane closed: the rows themselves are let go while it is closed.
    private TrackInfo[] _closedRows = [];

    public QueuePanel()
    {
        InitializeComponent();

        // Closed, the pane waits faded out and a little to the right, so
        // opening slides it in (on the compositor, never delaying a click).
        Opacity = 0;
        Translation = ClosedOffset;
        OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(220) };

        NowImage.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        NowImage.ImageOpened += (_, _) => NowImage.Opacity = 1;

        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = RefreshDelay;
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
    }

    /// <summary>Raised when the pane's close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    public bool IsOpen => _player is not null;

    /// <summary>Shows the queue of <paramref name="player"/> and follows it until <see cref="Close"/>.</summary>
    public void Open(PlayerRouter player)
    {
        if (_player is not null)
        {
            return;
        }

        _player = player;
        player.StateChanged += OnStateChanged;
        player.QueueChanged += OnQueueChanged;

        _queueKey = null;
        UpcomingList.ItemsSource = _rows;
        UpdateRows(_closedRows, _closedRows.Length);
        _closedRows = [];
        Show(player.State, player.ActiveSource);
        _ = RefreshAsync();

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_player is not null)
            {
                Opacity = 1;
                Translation = Vector3.Zero;
            }
        });
    }

    /// <summary>Stops following the player (nothing is read while the pane is closed).</summary>
    public void Close()
    {
        if (_player is { } player)
        {
            player.StateChanged -= OnStateChanged;
            player.QueueChanged -= OnQueueChanged;
        }

        _player = null;
        _refreshTimer.Stop();
        CancelLoading();
        _version++;
        LoadingRing.IsActive = false;
        Opacity = 0;
        Translation = ClosedOffset;

        // Nothing of the list is kept while the pane is closed (its rows, their
        // containers and covers); opening it again shows the same songs at once.
        _dragged = null;
        ReleaseHold();
        _closedRows = [.. _rows.Select(r => r.Track)];
        UpcomingList.ItemsSource = null;
        _rows.Clear();
        _artworkKey = null;
        NowImage.Source = null;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; draw the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            if (_player is { } player)
            {
                Show(player.State, player.ActiveSource);
            }
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_player is not null)
            {
                RefreshSoon();
            }
        });

    private void Show(PlayerState state, PlaybackSource source)
    {
        _shown = state;
        NowTitle.Text = state.Title ?? "Nothing playing";
        NowArtists.Text = state.Artists ?? string.Empty;
        NowDuration.Text = state.Duration > TimeSpan.Zero ? Format.Duration(state.Duration) : string.Empty;
        SourceText.Text = state.SourceName is { Length: > 0 } name ? $"Playing from {name}" : string.Empty;
        SourceText.Visibility = SourceText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteText.Text = _upNextNote ?? DefaultNote(source);
        ShowArtwork(state);

        // What plays next changes with the song, the player, shuffle and repeat.
        var key = string.Join('|', source, state.TrackUri ?? state.Title, state.ContextUri, state.Shuffle, state.Repeat);
        if (key == _queueKey)
        {
            return;
        }

        var first = _queueKey is null;
        _queueKey = key;
        if (!first)
        {
            RefreshSoon();
        }
    }

    private static string DefaultNote(PlaybackSource source) => source == PlaybackSource.LocalFiles ? LocalNote : SpotifyNote;

    private void ShowArtwork(PlayerState state)
    {
        // The cover's address or bytes, or, without one, the album's tile.
        object key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? "tile:" + (state.Album ?? state.Title);
        if (ReferenceEquals(key, _artworkKey) || Equals(key, _artworkKey))
        {
            return;
        }

        _artworkKey = key;
        NowImage.Opacity = 0;

        var name = state.Album ?? state.Title;
        NowCover.Background = name is null
            ? App.Services.Theme.GetBrush("ResonateSurfaceHoverBrush")
            : Artwork.PlaceholderBrush(name);

        if (state.ArtworkUrl is { } url)
        {
            _ = ShowCoverAsync(key, App.Services.Covers.GetReadyAsync(url, 40));
        }
        else if (state.ArtworkBytes is { } bytes)
        {
            _ = ShowCoverAsync(key, CoverImages.FromBytesAsync(bytes, 40));
        }
        else
        {
            NowImage.Source = null;
        }
    }

    private async Task ShowCoverAsync(object key, Task<(ImageSource? Image, bool Loaded)> loading)
    {
        var (image, loaded) = await loading;
        if (!ReferenceEquals(_artworkKey, key))
        {
            return;
        }

        NowImage.Source = image;

        // A picture that already has its pixels may not raise ImageOpened, so it is shown here.
        if (loaded)
        {
            NowImage.Opacity = 1;
        }
    }

    private void RefreshSoon()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>Reads what plays next from the player that plays now.</summary>
    private async Task RefreshAsync()
    {
        if (_player is not { } player)
        {
            return;
        }

        _refreshTimer.Stop();
        CancelLoading();
        var version = ++_version;

        if (TryShowUpNext(player))
        {
            return;
        }

        if (player.ActiveSource == PlaybackSource.LocalFiles)
        {
            _nowTrack = null;
            ShowRows(player.Local.Upcoming, error: null);
            return;
        }

        var loading = new CancellationTokenSource();
        _loading = loading;
        LoadingRing.IsActive = _rows.Count == 0;
        try
        {
            var token = loading.Token;
            var queue = await Task.Run(() => App.Services.Api.GetQueueAsync(token), token);
            if (version != _version)
            {
                return;
            }

            var now = TrackInfo.From(queue.CurrentlyPlaying);
            _nowTrack = now is not null && now.Uri == _shown.TrackUri ? now : null;
            ShowRows(QueuePreview.Upcoming(queue), error: null);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Closed, or a newer read started.
        }
        catch (Exception ex)
        {
            if (version == _version)
            {
                ShowRows([], error: PlayerController.DescribeError(ex));
            }
        }
        finally
        {
            if (version == _version)
            {
                LoadingRing.IsActive = false;
            }

            if (ReferenceEquals(_loading, loading))
            {
                _loading = null;
            }

            loading.Dispose();
        }
    }

    private void ShowRows(IReadOnlyList<TrackInfo> upcoming, string? error)
    {
        var shown = Math.Min(upcoming.Count, MaxRows);
        UpdateRows(upcoming, shown);

        EmptyText.Text = error is not null
            ? $"The queue could not be read. {error}"
            : upcoming.Count == 0
                ? "Nothing is up next. Right-click a song and choose \"Add to queue\"."
                : upcoming.Count > shown
                    ? $"And {Format.SongCount(upcoming.Count - shown)} more."
                    : string.Empty;
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the first <paramref name="count"/> songs. When the songs only
    /// moved up (one finished), the rows that left slide out and the rest
    /// keep their place, instead of the whole list redrawing.
    /// </summary>
    private void UpdateRows(IReadOnlyList<TrackInfo> upcoming, int count)
    {
        var played = 0;
        while (played < _rows.Count && !StartsWith(upcoming, count, played))
        {
            played++;
        }

        if (played == _rows.Count)
        {
            _rows.Clear();
        }
        else
        {
            for (var i = 0; i < played; i++)
            {
                _rows.RemoveAt(0);
            }
        }

        var likes = App.Services.Likes;
        for (var i = 0; i < count; i++)
        {
            if (i < _rows.Count)
            {
                _rows[i].Number = (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
            }
            else
            {
                var track = upcoming[i];
                _rows.Add(new TrackRow(track, i + 1, null, likes.IsLiked(track.Uri)));
            }
        }
    }

    /// <summary>Whether the rows from <paramref name="from"/> on are the first songs of <paramref name="upcoming"/>.</summary>
    private bool StartsWith(IReadOnlyList<TrackInfo> upcoming, int count, int from)
    {
        if (_rows.Count - from > count)
        {
            return false;
        }

        for (var i = from; i < _rows.Count; i++)
        {
            var row = _rows[i].Track;
            var next = upcoming[i - from];
            if (row.Uri != next.Uri || row.Title != next.Title || row.FilePath != next.FilePath)
            {
                return false;
            }
        }

        return true;
    }

    private void CancelLoading()
    {
        if (_loading is { } loading)
        {
            _loading = null;
            loading.Cancel();
        }
    }

    private void OnUpcomingContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TrackRow>(UpcomingList, args) is { } row)
        {
            var menu = TrackActions.BuildMenu(row.Track);
            AddUpNextItems(menu, row);
            TrackActions.ShowMenu(menu, UpcomingList, args);
        }
    }

    private void OnNowContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var state = _shown;
        if (state.Title is null)
        {
            return;
        }

        // The queue's answer has the song's album and artists; without it, what the bar shows.
        var uri = state.TrackUri;
        var isLocal = uri?.StartsWith("spotify:local:", StringComparison.Ordinal) == true;
        var track = _nowTrack is { } known && known.Uri == uri
            ? known
            : new TrackInfo(
                uri,
                state.Title,
                state.Artists ?? string.Empty,
                state.Album ?? string.Empty,
                null,
                state.Duration,
                state.ArtworkUrl,
                state.ArtworkUrl,
                false,
                state.Source == PlaybackSource.Spotify && uri is not null && !isLocal)
            {
                IsLocal = isLocal,
            };
        TrackActions.ShowMenu(TrackActions.BuildMenu(track), NowRow, args);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
