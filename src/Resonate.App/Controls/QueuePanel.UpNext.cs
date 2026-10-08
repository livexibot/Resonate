using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Pages.Lists;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Resonate.App.Controls;

/// <summary>
/// Up next, a built-in plugin: while it is on and Resonate knows the list
/// that plays, the queue shows Resonate's own order (no request to Spotify)
/// and can be edited: songs dragged into a new order, removed with the
/// button on the row, the menu or Delete, shuffled, cleared, and saved as a
/// playlist. The player shows each edit at once and hands Spotify the new
/// order a moment later (<see cref="PlayerController"/>). While it is off,
/// the queue looks and behaves as before.
/// </summary>
public sealed partial class QueuePanel
{
    private const string WaitNote = "You can edit the queue again when the next song starts.";
    private const string PartNote = "This list started before all its songs had loaded, so its queue can't be edited. Play it again to edit it.";
    private const string OutsideNote = "Started outside Resonate, so this queue can't be edited. Play a list in Resonate to edit it.";

    private bool _upNextOn;
    private bool _upNextEditable;
    private string? _upNextNote;
    private TrackRow? _dragged;
    private int _dragFrom = -1;

    // The player told to hold the order while a song is dragged (released even if the pane closes meanwhile).
    private PlayerRouter? _holding;
    private bool _refreshAfterDrag;

    /// <summary>Turns Up next on or off (see <c>MainWindow.UpNext.cs</c>).</summary>
    public void SetUpNext(bool on)
    {
        if (_upNextOn == on)
        {
            return;
        }

        _upNextOn = on;
        if (on)
        {
            UpcomingList.DragItemsStarting += OnUpNextDragStarting;
            UpcomingList.DragItemsCompleted += OnUpNextDragCompleted;
            UpcomingList.KeyDown += OnUpNextKeyDown;
        }
        else
        {
            UpcomingList.DragItemsStarting -= OnUpNextDragStarting;
            UpcomingList.DragItemsCompleted -= OnUpNextDragCompleted;
            UpcomingList.KeyDown -= OnUpNextKeyDown;
            _dragged = null;
            ReleaseHold();

            ShowUpNextState(editable: false, note: null, canSave: false);
        }

        if (_player is not null)
        {
            _queueKey = null;
            _ = RefreshAsync();
        }
    }

    /// <summary>Called by <see cref="RefreshAsync"/>: shows Resonate's order when Up next is on and knows it. False to read the queue as before.</summary>
    private bool TryShowUpNext(PlayerRouter player)
    {
        if (!_upNextOn)
        {
            return false;
        }

        if (_dragged is not null)
        {
            // Not under the user's pointer; once the song is dropped.
            _refreshAfterDrag = true;
            return true;
        }

        var source = player.ActiveSource;
        var canSave = source == PlaybackSource.Spotify && _shown.HasTrack;
        if (player.UpNext is not { } list)
        {
            ShowUpNextState(editable: false, source == PlaybackSource.Spotify ? OutsideNote : null, canSave);
            return false;
        }

        _nowTrack = source == PlaybackSource.Spotify ? list.Current : null;
        LoadingRing.IsActive = false;
        ShowRows(list.Upcoming, error: null);
        ShowUpNextState(list.CanEdit, list.CanEdit ? null : list.PartlyKnown ? PartNote : WaitNote, canSave);
        return true;
    }

    private void ShowUpNextState(bool editable, string? note, bool canSave)
    {
        _upNextEditable = editable;
        _upNextNote = note;
        if (_player is not null)
        {
            ShowNote(note);
        }

        UpcomingList.CanDragItems = editable;
        UpcomingList.CanReorderItems = editable;
        UpcomingList.AllowDrop = editable;
        var selection = editable ? ListViewSelectionMode.Extended : ListViewSelectionMode.None;
        if (UpcomingList.SelectionMode != selection)
        {
            UpcomingList.SelectionMode = selection;
        }

        UpNextShuffleButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        UpNextClearButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        UpNextSaveButton.Visibility = canSave ? Visibility.Visible : Visibility.Collapsed;
        UpNextTools.Visibility = editable || canSave ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Remove from the queue" in a song's menu, while it can be edited.</summary>
    private void AddUpNextItems(MenuFlyout menu, TrackRow row)
    {
        if (_upNextOn && _upNextEditable)
        {
            menu.Items.Insert(0, TrackActions.Item("Remove from the queue", "", () => Remove([row])));
            menu.Items.Insert(1, new MenuFlyoutSeparator());
        }
    }

    private void Remove(IReadOnlyList<TrackRow> rows)
    {
        if (_player is not { } player)
        {
            return;
        }

        var picks = rows
            .Select(r => new UpNextPick(_rows.IndexOf(r), r.Track))
            .Where(p => p.Index >= 0)
            .ToList();
        if (picks.Count == 0)
        {
            return;
        }

        if (player.RemoveUpNext(picks))
        {
            foreach (var pick in picks.OrderByDescending(p => p.Index))
            {
                _rows.RemoveAt(pick.Index);
            }
        }

        // The rows now match what plays next (or, when the list moved on meanwhile, are read again).
        _ = RefreshAsync();
    }

    private void OnUpNextDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = _upNextEditable && e.Items.Count == 1 ? e.Items[0] as TrackRow : null;
        _dragFrom = _dragged is null ? -1 : _rows.IndexOf(_dragged);
        if (_dragged is null || _dragFrom < 0)
        {
            _dragged = null;
            e.Cancel = true;
            return;
        }

        // Spotify gets the order only once the song is dropped.
        _holding = _player;
        _holding?.HoldUpNext(true);
    }

    private void ReleaseHold()
    {
        _holding?.HoldUpNext(false);
        _holding = null;
    }

    private void OnUpNextDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var row = _dragged;
        var from = _dragFrom;
        _dragged = null;
        if (row is null || _player is not { } player)
        {
            ReleaseHold();
            return;
        }

        var to = _rows.IndexOf(row);
        var moved = args.DropResult == DataPackageOperation.Move && to >= 0 && to != from;
        if (moved && !player.MoveUpNext(new UpNextPick(from, row.Track), to))
        {
            // The list moved on meanwhile: show it as it is.
            _refreshAfterDrag = true;
        }

        ReleaseHold();
        if (moved || _refreshAfterDrag)
        {
            _refreshAfterDrag = false;
            _ = RefreshAsync();
        }
    }

    private void OnUpNextKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete || !_upNextEditable)
        {
            return;
        }

        var rows = UpcomingList.SelectedItems.OfType<TrackRow>().ToList();
        if (rows.Count > 0)
        {
            Remove(rows);
            e.Handled = true;
        }
    }

    private void OnUpNextRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_upNextOn && _upNextEditable)
        {
            ShowRemoveButton(sender, true);
        }
    }

    private void OnUpNextRowPointerExited(object sender, PointerRoutedEventArgs e) => ShowRemoveButton(sender, false);

    /// <summary>The row's remove button takes the place of its length while the pointer is on it.</summary>
    private static void ShowRemoveButton(object row, bool show)
    {
        if (row is Grid { Children.Count: 4 } grid && grid.Children[3] is Button remove && grid.Children[2] is TextBlock length)
        {
            remove.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            length.Opacity = show ? 0 : 1;
        }
    }

    private void OnUpNextRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackRow row })
        {
            Remove([row]);
        }
    }

    private void OnUpNextShuffleClick(object sender, RoutedEventArgs e)
    {
        if (_player?.ShuffleUpNext() == true)
        {
            _ = RefreshAsync();
        }
    }

    private void OnUpNextClearClick(object sender, RoutedEventArgs e)
    {
        if (_player?.ClearUpNext() == true)
        {
            _rows.Clear();
            _ = RefreshAsync();
        }
    }

    private void OnUpNextSaveClick(object sender, RoutedEventArgs e)
    {
        var list = _player?.UpNext;
        IEnumerable<TrackInfo?> next = list is not null
            ? [list.Current, .. list.Upcoming]
            : [_nowTrack, .. _rows.Select(r => r.Track)];
        var menu = new MenuFlyout();
        menu.Items.Add(TrackActions.Item("Now playing and next up", null, () => _ = SaveAsync(next)));
        if (list is { Played.Count: > 0 })
        {
            IEnumerable<TrackInfo?> whole = [.. list.Played, list.Current, .. list.Upcoming];
            menu.Items.Add(TrackActions.Item("The whole list", null, () => _ = SaveAsync(whole)));
        }

        menu.ShowAt(UpNextSaveButton);
    }

    /// <summary>Asks for a name and saves the Spotify songs among <paramref name="songs"/> as a new playlist, in this order.</summary>
    private static async Task SaveAsync(IEnumerable<TrackInfo?> songs)
    {
        var tracks = songs
            .OfType<TrackInfo>()
            .Where(t => t.Uri is { } uri && (uri.StartsWith("spotify:track:", StringComparison.Ordinal) || uri.StartsWith("spotify:episode:", StringComparison.Ordinal)))
            .Distinct(ReferenceEqualityComparer.Instance)
            .OfType<TrackInfo>()
            .ToList();
        if (tracks.Count == 0)
        {
            App.MainWindow?.ShowMessage("There are no Spotify songs to save.", InfoBarSeverity.Informational);
            return;
        }

        if (await TrackActions.CreatePlaylistAsync() is { } playlist)
        {
            await TrackActions.AddToPlaylistAsync(tracks, playlist.Id, playlist.Name);
        }
    }
}
