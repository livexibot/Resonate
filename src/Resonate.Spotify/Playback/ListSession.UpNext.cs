using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Up next (a built-in plugin): the user edits what follows in
/// <see cref="Order"/>. Spotify can not be told to reorder its queue, so an
/// edit changes Resonate's order at once and Spotify later gets a new window
/// of it that starts with the current song (<see cref="TakeEdits"/>), the
/// way a shuffled list is handed over. A list Spotify plays in its own
/// context becomes Resonate's own order with the first edit.
/// </summary>
internal sealed partial class ListSession
{
    /// <summary>Songs before the current one that Spotify keeps with an edited order, so "previous" still works.</summary>
    internal const int SongsKeptBehind = 10;

    // Order[Index + 1] up to (not including) this were added with "Add to queue" or "Play next".
    private int _queueEnd;

    /// <summary>The order was edited since Spotify last got a window of it.</summary>
    public bool EditsPending { get; private set; }

    /// <summary>Up next edited this order (so Resonate, not Spotify, starts each pass of "repeat all").</summary>
    public bool Edited { get; private set; }

    /// <summary>The song Spotify plays after the current one in what it has, until it gets the edited order.</summary>
    private TrackInfo? StaleNext { get; set; }

    /// <summary>
    /// Whether what follows can be edited now: not while a song from
    /// elsewhere plays or a new plan waits for the next song, not when the
    /// current song can not be restarted by its address, and not for a
    /// playlist Resonate knows only the first part of.
    /// </summary>
    public bool CanEditUpNext =>
        !StartsWithNextSong && !PlayingOther && !(InContext && IsPartial) && Current is { } current && CanStartByUri(current);

    public UpNextList UpNextList() => InContext
        ? new(Current, Order.Skip(Index + 1).Where(CanStartByUri).ToList(), CanEditUpNext)
        {
            Played = Order.Take(Index).Where(CanStartByUri).ToList(),
            PartlyKnown = IsPartial,
        }
        : new(Current, Order.Skip(Index + 1).ToList(), CanEditUpNext) { Played = Order.Take(Index).ToList() };

    public bool MoveUpNext(UpNextPick song, int to) =>
        Edit((upcoming, queued) => UpNextEdits.IsAt(upcoming, song) && to >= 0 && to < upcoming.Count
            ? UpNextEdits.Move(upcoming, queued, song.Index, to)
            : -1);

    public bool RemoveUpNext(IReadOnlyList<UpNextPick> songs) =>
        Edit((upcoming, queued) => songs.Count > 0 && songs.All(s => UpNextEdits.IsAt(upcoming, s))
            ? UpNextEdits.Remove(upcoming, queued, songs.Select(s => s.Index))
            : -1);

    public bool ClearUpNext() =>
        Edit((upcoming, _) =>
        {
            upcoming.Clear();
            return 0;
        });

    public bool ShuffleUpNext() => Edit((upcoming, _) => UpNextEdits.Shuffle(upcoming, _nextInt));

    /// <summary>Files from the computer can not join: Spotify refuses their addresses in a list.</summary>
    public bool AddToUpNext(TrackInfo track, bool playNext) =>
        CanStartByUri(track) && Edit((upcoming, queued) => UpNextEdits.Insert(upcoming, queued, track, playNext));

    /// <summary>Whether Spotify now plays the song that followed the current one in what it had before the edits.</summary>
    public bool IsStaleNext(string? uri, string? title) =>
        StaleNext is { } song
        && ((uri is not null && song.Uri == uri)
            || (title is not null && string.Equals(song.Title.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>On to the edited order's next song (another pass with "repeat all"); false when nothing follows.</summary>
    public bool AdvanceInEdits(RepeatMode repeat)
    {
        if (Index + 1 >= Order.Count)
        {
            if (repeat != RepeatMode.All || Playable.Count == 0)
            {
                return false;
            }

            AddPass();
        }

        Index++;
        return true;
    }

    /// <summary>
    /// The edited order for Spotify: a window from a few songs before the
    /// current one, starting at it (the caller restarts it where it is).
    /// </summary>
    public StartPlaybackBody TakeEdits()
    {
        EditsPending = false;
        WindowStart = Math.Max(0, Index - SongsKeptBehind);
        WindowEnd = Math.Min(Order.Count, WindowStart + WindowSize);
        if (Index > WindowStart && IsListedTwice(Order.Skip(Index).Take(WindowEnd - Index), Current?.Uri))
        {
            // Spotify refuses to start a list at a song that comes again later in it.
            WithWindowFrom(Index);
        }

        return StartBody();
    }

    /// <summary>Spotify did not take the edited order: it still has what it had, and the edits wait for another try.</summary>
    public void RestoreEdits() => EditsPending = true;

    /// <summary>The edits are given up (the edited order ended where Spotify still had songs).</summary>
    public void DropEdits() => EditsPending = false;

    /// <param name="edit">Changes the upcoming songs (given with how many of them are queued) and returns how many are queued then, or -1 to change nothing.</param>
    private bool Edit(Func<List<TrackInfo>, int, int> edit)
    {
        if (!CanEditUpNext)
        {
            return false;
        }

        var upcoming = InContext
            ? Order.Skip(Index + 1).Where(CanStartByUri).ToList()
            : Order.GetRange(Index + 1, Order.Count - Index - 1);
        var queued = InContext ? 0 : Math.Clamp(_queueEnd - (Index + 1), 0, upcoming.Count);
        var nowQueued = edit(upcoming, queued);
        if (nowQueued < 0)
        {
            return false;
        }

        if (!EditsPending)
        {
            // What Spotify has: its context, or the window it was sent.
            StaleNext = Index + 1 < (InContext ? Order.Count : WindowEnd) ? Order[Index + 1] : null;
            EditsPending = true;
        }

        if (InContext)
        {
            // From now on Resonate's own order, made of the songs Spotify can start by address.
            var current = Order[Index];
            var played = Order.Take(Index).Where(CanStartByUri).ToList();
            Order.Clear();
            Order.AddRange(played);
            Order.Add(current);
            Index = played.Count;
            InContext = false;
        }
        else
        {
            Order.RemoveRange(Index + 1, Order.Count - Index - 1);
        }

        Order.AddRange(upcoming);
        _queueEnd = Index + 1 + nowQueued;
        Edited = true;
        return true;
    }
}
