using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Where you left off: the plan is kept on disk as a <see cref="KeptList"/>
/// and made again after a restart (<see cref="Restore"/>), so Play, Next,
/// Previous and Up next go on through the list. Spotify has none of a
/// restored plan until Resonate sends it (<see cref="Unsent"/>).
/// </summary>
internal sealed partial class ListSession
{
    /// <summary>Songs kept after the playing one: far more than Spotify gets at once, so the list goes on for hours after a restart.</summary>
    internal const int KeptAhead = 300;

    // The plan a waiting one becomes, made once for keeping (see ToKept).
    private ListSession? _settled;

    /// <summary>Made again after a restart, and Spotify has none of it yet: Play (or Next, or Previous) sends it.</summary>
    public bool Unsent { get; set; }

    /// <summary>What to keep of this plan around the playing song.</summary>
    /// <param name="spotifyShuffle">Spotify's own shuffle, as last known.</param>
    public KeptList ToKept(bool? spotifyShuffle)
    {
        if (StartsWithNextSong)
        {
            // Shuffle was switched while paused: kept as the plan it becomes once the music plays.
            _settled ??= Current is { } current && CanStartByUri(current) ? Restarted(current, Shuffle, ContextUri) : null;
            if (_settled is { } settled)
            {
                return settled.ToKept(spotifyShuffle);
            }
        }

        var index = Math.Clamp(Index, 0, Math.Max(0, Order.Count - 1));
        var start = Math.Clamp(index - SongsKeptBehind, 0, Order.Count);
        if (!InContext)
        {
            // From where Spotify's window starts, too: the Spotify app may still hold it after a restart.
            start = Math.Min(start, Math.Clamp(WindowStart, 0, Order.Count));
        }

        var end = Math.Min(Order.Count, Math.Max(index + 1 + KeptAhead, InContext ? 0 : WindowEnd));
        return new KeptList
        {
            Songs = Order.GetRange(start, end - start),
            Index = index - start,
            WindowStart = InContext ? 0 : Math.Clamp(WindowStart - start, 0, end - start),
            WindowEnd = InContext ? 0 : Math.Clamp(WindowEnd - start, 0, end - start),
            Shuffle = Shuffle,
            InContext = InContext,
            ContextUri = ContextUri,
            SourceName = SourceName,
            Complete = start == 0 && end == Order.Count && !IsPartial,
            Edited = Edited,
            SpotifyShuffle = spotifyShuffle,
        };
    }

    /// <summary>
    /// The plan <paramref name="kept"/> describes, waiting to be sent, or null
    /// when it can not be one. Of a part of a list, the part is the whole
    /// plan: Spotify plays its context itself (the list's own order), and
    /// otherwise the part plays to its end (and loops with "repeat all").
    /// </summary>
    public static ListSession? Restore(KeptList kept, Func<int, int>? nextInt)
    {
        var songs = kept.Songs;
        if (kept.Index < 0 || kept.Index >= songs.Count || !CanStartByUri(songs[kept.Index]))
        {
            return null;
        }

        var inContext = kept.InContext && kept.ContextUri is not null;

        // A list of songs may hold the end of one "repeat all" pass and the start of the next.
        var playable = inContext
            ? songs.Where(CanStartByUri).ToList()
            : songs.Where(CanStartByUri).DistinctBy(t => t.Uri).ToList();
        var order = inContext ? songs.ToList() : songs.Where(CanStartByUri).ToList();
        var index = Mapped(kept.Index);

        // Only a list Spotify plays in its own order keeps its context: a shuffled part
        // switched back to the list's own order could not show what Spotify then plays.
        var session = new ListSession(songs, playable, inContext ? kept.ContextUri : null, kept.SourceName, kept.Shuffle, inContext, order, index, nextInt)
        {
            Picked = true,
            IsPartial = !kept.Complete,
            Unsent = true,
        };
        session.Edited = kept.Edited;
        if (!inContext)
        {
            // What Spotify had last, which the Spotify app may still hold.
            var start = Mapped(kept.WindowStart);
            var end = Math.Max(start, Mapped(kept.WindowEnd));
            if (index >= start && index < end)
            {
                session.WindowStart = start;
                session.WindowEnd = end;
            }
            else
            {
                session.WithWindowBehind();
            }
        }

        return session;

        // Where a place among the kept songs is in the order (a list of songs holds only those Spotify can start).
        int Mapped(int at) => inContext ? at : songs.Take(Math.Clamp(at, 0, songs.Count)).Count(CanStartByUri);
    }

    /// <summary>
    /// Under the caller's lock: ready to start at the current song (a new
    /// window from a few songs before it, so Previous works too), and no
    /// longer waiting to be sent.
    /// </summary>
    public ListSession Sending()
    {
        Unsent = false;
        if (!InContext)
        {
            WithWindowBehind();
        }

        return this;
    }

    /// <summary>A window from a few songs before the current one (so "previous" works), starting at it when it comes again later in the window.</summary>
    private ListSession WithWindowBehind()
    {
        WindowStart = Math.Max(0, Index - SongsKeptBehind);
        WindowEnd = Math.Min(Order.Count, WindowStart + WindowSize);
        if (Index > WindowStart && IsListedTwice(Order.Skip(Index).Take(WindowEnd - Index), Current?.Uri))
        {
            // Spotify refuses to start a list at a song that comes again later in it.
            WithWindowFrom(Index);
        }

        return this;
    }
}
