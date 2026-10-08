using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Helpers;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;

namespace Resonate.App;

/// <summary>
/// Covers fetched ahead of a click: resting the pointer on a playlist in the
/// sidebar fetches its cover and its first songs' covers (from the copy of
/// its songs kept on disk, so it costs no Spotify request), and they are
/// there when the page opens.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long the pointer rests on a playlist before its covers are fetched (a pass over the list fetches nothing).</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Songs whose covers are fetched: about two screens of rows.</summary>
    private const int PrefetchSongs = 40;

    private DispatcherQueueTimer? _hoverTimer;
    private PlaylistNavItem? _hovered;
    private string? _prefetched;

    private void OnPlaylistPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var item = ListEvents.ItemOf<PlaylistNavItem>(e.OriginalSource);
        if (ReferenceEquals(item, _hovered))
        {
            return;
        }

        _hovered = item;
        if (_hoverTimer is null)
        {
            _hoverTimer = DispatcherQueue.CreateTimer();
            _hoverTimer.Interval = HoverDelay;
            _hoverTimer.IsRepeating = false;
            _hoverTimer.Tick += (_, _) => PrefetchHovered();
        }

        _hoverTimer.Stop();
        if (item is not null && item.Id != _prefetched)
        {
            _hoverTimer.Start();
        }
    }

    private void OnPlaylistPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = null;
        _hoverTimer?.Stop();
    }

    private void PrefetchHovered()
    {
        if (_hovered is not { } item || _services.Covers.Store is not { } covers)
        {
            return;
        }

        _prefetched = item.Id;
        var header = ImagePicker.Pick(item.Playlist.Images, 300);
        var library = _services.Library;

        // In the order the page will show them (the sort chosen for this playlist).
        var sort = TrackSort.Parse(_services.Settings.TrackSorts.GetValueOrDefault(item.Id));
        _ = Task.Run(() =>
        {
            var songs = library.PeekStoredPlaylistTracks(item.Id) ?? [];
            var shown = sort.IsDefault ? songs.Take(PrefetchSongs) : TrackSorter.Apply(songs, sort).Take(PrefetchSongs);
            covers.Prefetch(shown.Select(t => t.SmallImageUrl).Prepend(header));
        });
    }
}
