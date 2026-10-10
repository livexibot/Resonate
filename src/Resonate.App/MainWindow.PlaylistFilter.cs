using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App;

/// <summary>
/// The magnifier beside Sort in the sidebar's Playlists heading opens a box
/// in the heading's place that narrows the playlists to those whose name (or
/// owner) holds every word typed, ignoring case and accents, as the Summon bar
/// matches. Enter opens the first one left. While it narrows them, dragging
/// into a new order is off; Esc or the cross clears it, and the whole list
/// comes back in its own order.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<PlaylistNavItem> _filteredPlaylists = [];
    private bool _playlistFilterOpen;
    private bool _playlistsFiltered;
    private bool _playlistFilterQueued;
    private long _playlistsHeaderToken;

    private void OnPlaylistFilterClick(object sender, RoutedEventArgs e) => ShowPlaylistFilter(true);

    private void OnPlaylistFilterCloseClick(object sender, RoutedEventArgs e) => ShowPlaylistFilter(false);

    private void OnPlaylistFilterChanged(object sender, TextChangedEventArgs e) => FilterPlaylists();

    private void OnPlaylistFilterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ShowPlaylistFilter(false);
        }
        else if (e.Key == VirtualKey.Enter && _playlistsFiltered && _filteredPlaylists.Count > 0)
        {
            e.Handled = true;
            Open(_filteredPlaylists[0].Id);
        }
    }

    private void ShowPlaylistFilter(bool open)
    {
        if (open == _playlistFilterOpen)
        {
            return;
        }

        _playlistFilterOpen = open;
        var heading = open ? Visibility.Collapsed : Visibility.Visible;
        PlaylistsTitle.Visibility = heading;
        PlaylistFilterButton.Visibility = heading;
        PlaylistSortButton.Visibility = heading;
        NewPlaylistButton.Visibility = heading;
        PlaylistFilterRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            // Only while it is open: a new library narrows again, and a sidebar
            // too narrow for the heading (or a window shape) closes it.
            Playlists.CollectionChanged += OnPlaylistsChangedWhileFiltered;
            _playlistsHeaderToken = PlaylistsHeader.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnPlaylistsHeaderVisibilityChanged);

            // Once the box is laid out, so it can take the keyboard.
            DispatcherQueue.TryEnqueue(() => PlaylistFilterBox.Focus(FocusState.Programmatic));
            return;
        }

        Playlists.CollectionChanged -= OnPlaylistsChangedWhileFiltered;
        PlaylistsHeader.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _playlistsHeaderToken);
        PlaylistFilterBox.Text = string.Empty;
        FilterPlaylists();
        if (PlaylistsHeader.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => PlaylistFilterButton.Focus(FocusState.Programmatic));
        }
    }

    private void OnPlaylistsHeaderVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (PlaylistsHeader.Visibility != Visibility.Visible)
        {
            // After this callback, which cannot unregister itself.
            DispatcherQueue.TryEnqueue(() => ShowPlaylistFilter(false));
        }
    }

    private void OnPlaylistsChangedWhileFiltered(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The sidebar fills its list one playlist at a time: narrow it once, after.
        if (_playlistFilterQueued)
        {
            return;
        }

        _playlistFilterQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _playlistFilterQueued = false;
            FilterPlaylists();
        });
    }

    /// <summary>The playlists that match what is typed, or all of them, with the open one still selected.</summary>
    private void FilterPlaylists()
    {
        var tokens = _playlistFilterOpen ? QuickSearchIndex.Tokens(PlaylistFilterBox.Text) : [];
        var filtering = tokens.Length > 0;
        PlaylistList.CanReorderItems = !filtering;
        PlaylistList.CanDragItems = !filtering;
        if (filtering)
        {
            var wanted = Playlists.Where(item => Matches(item, tokens)).ToList();
            var kept = new HashSet<PlaylistNavItem>(wanted, ReferenceEqualityComparer.Instance);

            // Rows that still match stay where they are, so typing takes rows away rather than redrawing the list.
            for (var i = _filteredPlaylists.Count - 1; i >= 0; i--)
            {
                if (!kept.Contains(_filteredPlaylists[i]))
                {
                    _filteredPlaylists.RemoveAt(i);
                }
            }

            for (var i = 0; i < wanted.Count; i++)
            {
                if (i < _filteredPlaylists.Count && ReferenceEquals(_filteredPlaylists[i], wanted[i]))
                {
                    continue;
                }

                // A list in a new order (sorted again) moves a row it already shows, so none shows twice.
                var at = IndexOf(wanted[i], i + 1);
                if (at >= 0)
                {
                    _filteredPlaylists.Move(at, i);
                }
                else
                {
                    _filteredPlaylists.Insert(i, wanted[i]);
                }
            }
        }

        var shown = filtering ? _filteredPlaylists : Playlists;
        if (filtering != _playlistsFiltered)
        {
            _playlistsFiltered = filtering;
            PlaylistList.ItemsSource = shown;
        }

        if (!filtering)
        {
            _filteredPlaylists.Clear();
        }

        _syncingSelection = true;
        try
        {
            PlaylistList.SelectedItem = shown.FirstOrDefault(p => p.Id == _currentKey);
        }
        finally
        {
            _syncingSelection = false;
        }

        static bool Matches(PlaylistNavItem item, string[] tokens)
        {
            var name = QuickSearchIndex.Normalize(item.Name);
            var owner = item.Playlist.Owner?.DisplayName;
            return QuickSearchIndex.Score(name, string.IsNullOrEmpty(owner) ? name : name + " " + QuickSearchIndex.Normalize(owner), tokens) > 0;
        }

        int IndexOf(PlaylistNavItem item, int from)
        {
            for (var j = from; j < _filteredPlaylists.Count; j++)
            {
                if (ReferenceEquals(_filteredPlaylists[j], item))
                {
                    return j;
                }
            }

            return -1;
        }
    }
}
