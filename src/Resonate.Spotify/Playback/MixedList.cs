using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// A list that mixes Spotify's songs with music files on this computer (the
/// local songs a Spotify playlist holds, found in Local Files). Spotify can
/// not play the files and the local files player can not play Spotify's
/// songs, so the list plays in stretches: each run of Spotify songs goes to
/// Spotify as one list, each run of files to the local files player, and as
/// one stretch ends (<see cref="PlayRequest.HandsOver"/>) the next starts on
/// the other player. Resonate keeps the order, shuffle and repeat of the
/// whole list. Not thread-safe: <see cref="PlayerRouter"/> uses it under its lock.
/// </summary>
internal sealed class MixedList
{
    private readonly Func<int, int>? _nextInt;

    // Order[Index + 1] up to (not including) this were added with "Add to queue" or "Play next".
    private int _queueEnd;

    /// <param name="songs">The list in its own order: songs Spotify can start by address, and files (<see cref="TrackInfo.FilePath"/>).</param>
    /// <param name="start">The song to start with, or -1 for the start (anywhere, when shuffled).</param>
    public MixedList(IReadOnlyList<TrackInfo> songs, int start, bool shuffle, RepeatMode repeat, string? sourceName, Func<int, int>? nextInt)
    {
        All = songs;
        Shuffle = shuffle;
        Repeat = repeat;
        SourceName = sourceName;
        _nextInt = nextInt;
        if (shuffle)
        {
            Order = TrueShuffle.ShuffleAfter(songs, start, nextInt);
            Index = 0;
        }
        else
        {
            Order = songs.ToList();
            Index = Math.Clamp(start, 0, songs.Count - 1);
        }

        _queueEnd = Index + 1;
    }

    /// <summary>The list in its own order.</summary>
    public IReadOnlyList<TrackInfo> All { get; }

    /// <summary>The songs in the order they play.</summary>
    public List<TrackInfo> Order { get; private set; }

    /// <summary>Where in <see cref="Order"/> the playing song is.</summary>
    public int Index { get; private set; }

    public bool Shuffle { get; private set; }

    public RepeatMode Repeat { get; set; }

    public string? SourceName { get; }

    /// <summary>The stretch that plays now (what its player was given); null between stretches and once the list ended.</summary>
    public PlayRequest? Stretch { get; private set; }

    /// <summary>The stretch's player has taken it (it was started), so what that player says is about it.</summary>
    public bool StretchStarted { get; set; }

    /// <summary>Where the playing stretch starts in <see cref="Order"/>.</summary>
    public int StretchStart { get; private set; }

    /// <summary>Where the next stretch starts in <see cref="Order"/>: after the playing one, or right after the current song once the order changed.</summary>
    public int StretchEnd { get; private set; }

    /// <summary>The last song played to its end with repeat off: the list waits at its start for Play.</summary>
    public bool Finished { get; private set; }

    public TrackInfo Current => Order[Index];

    /// <summary>The songs after the current one, in the order they play.</summary>
    public IReadOnlyList<TrackInfo> Upcoming => Order.Skip(Index + 1).ToList();

    /// <summary>A song the local files player plays.</summary>
    public static bool IsFile(TrackInfo song) => song.FilePath is not null;

    /// <summary>What plays next, as the Up next plugin shows it (not editable: the stretches are already with their players).</summary>
    public UpNextList UpNextList() =>
        new(Current, Upcoming, CanEdit: false) { Played = Order.Take(Index).ToList(), Mixed = true };

    /// <summary>Makes the stretch that holds <paramref name="index"/> the one that plays, from that song; returns what its player gets.</summary>
    public PlayRequest StartStretchAt(int index)
    {
        Finished = false;
        Index = index;
        var file = IsFile(Order[index]);
        var start = index;
        while (start > 0 && IsFile(Order[start - 1]) == file)
        {
            start--;
        }

        var end = index + 1;
        while (end < Order.Count && IsFile(Order[end]) == file)
        {
            end++;
        }

        StretchStart = start;
        StretchEnd = end;
        _queueEnd = Math.Max(_queueEnd, Index + 1);
        StretchStarted = false;
        Stretch = new PlayRequest(Order.GetRange(start, end - start), index - start, ContextUri: null, SourceName)
        {
            Shuffle = false,
            HandsOver = true,
        };
        return Stretch;
    }

    /// <summary>
    /// The playing stretch ended: the next one, or null when the list ended
    /// (with "repeat all" it starts again, in a new random order when shuffled).
    /// </summary>
    public PlayRequest? NextStretch()
    {
        if (StretchEnd < Order.Count)
        {
            return StartStretchAt(StretchEnd);
        }

        if (Repeat == RepeatMode.All)
        {
            NewPass();
            return StartStretchAt(0);
        }

        Stretch = null;
        Finished = true;
        return null;
    }

    /// <summary>The stretch before the playing one, from its last song; null at the list's start (unless "repeat all").</summary>
    public PlayRequest? PreviousStretch()
    {
        if (StretchStart > 0)
        {
            return StartStretchAt(StretchStart - 1);
        }

        return Repeat == RepeatMode.All ? StartStretchAt(Order.Count - 1) : null;
    }

    /// <summary>Play after the list ended: it starts again (in a new random order when shuffled).</summary>
    public PlayRequest Restart()
    {
        NewPass();
        return StartStretchAt(0);
    }

    /// <summary>Another stretch follows the playing one (or the list starts again, with "repeat all").</summary>
    public bool HasNextStretch => StretchEnd < Order.Count || Repeat == RepeatMode.All;

    /// <summary>Whether the playing song is the last of its stretch (Next starts the next stretch).</summary>
    public bool AtStretchEnd => Index >= StretchEnd - 1;

    /// <summary>Whether the playing song is the first of its stretch (Previous at its start goes to the stretch before).</summary>
    public bool AtStretchStart => Index <= StretchStart;

    /// <summary>
    /// Notes which song of the playing stretch its player plays now, by
    /// address, or by title when Spotify has not said the address yet.
    /// False when it is none of them.
    /// </summary>
    public bool Follow(string? uri, string? title)
    {
        // By address first; by title when Spotify has not said it, or plays a stand-in version under another.
        var at = uri is null ? -1 : FindInStretch(s => s.Uri == uri);
        if (at < 0 && title is not null)
        {
            at = FindInStretch(s => string.Equals(s.Title.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (at < 0)
        {
            return false;
        }

        Index = at;
        _queueEnd = Math.Max(_queueEnd, Index + 1);
        return true;
    }

    /// <summary>
    /// Shuffle switched while the list plays: the rest in a new random order
    /// after the current song, or back in the list's own order from it. The
    /// current song's player is told to stop after it, so the next stretch
    /// follows the new order.
    /// </summary>
    public void SetShuffle(bool shuffle)
    {
        Shuffle = shuffle;
        var current = Current;
        var queued = Order.GetRange(Index + 1, Math.Max(0, Math.Min(_queueEnd, Order.Count) - Index - 1));
        var rest = All.Where(s => !ReferenceEquals(s, current)).ToList();
        if (shuffle)
        {
            Order = [current, .. queued, .. TrueShuffle.Shuffle(rest, _nextInt)];
            Index = 0;
        }
        else
        {
            var at = IndexOf(All, current);
            if (at < 0)
            {
                // A queued song: the list goes on from its start after it.
                Order = [current, .. queued, .. All];
                Index = 0;
            }
            else
            {
                Order = [.. All.Take(at + 1), .. queued, .. All.Skip(at + 1)];
                Index = at;
            }
        }

        _queueEnd = Index + 1 + queued.Count;
        StretchStart = Index;
        StretchEnd = Index + 1;
    }

    /// <summary>"Play next" (right after the current song) or "Add to queue" (after the songs queued before).</summary>
    public void Add(TrackInfo song, bool playNext)
    {
        var at = playNext ? Index + 1 : Math.Clamp(_queueEnd, Index + 1, Order.Count);
        Order.Insert(at, song);
        _queueEnd = Math.Max(_queueEnd, Index + 1) + 1;
        StretchStart = Math.Min(StretchStart, Index);
        StretchEnd = Index + 1;
    }

    /// <summary>"Repeat all" or Play after the end: the whole list again, in a fresh random order when shuffled.</summary>
    private void NewPass()
    {
        if (!Shuffle)
        {
            Order = All.ToList();
            return;
        }

        var last = Order.Count > 0 ? Order[^1] : null;
        var pass = TrueShuffle.Shuffle(All, _nextInt);
        if (pass.Count > 1 && ReferenceEquals(pass[0], last))
        {
            // Not the song that just played, twice in a row.
            (pass[0], pass[^1]) = (pass[^1], pass[0]);
        }

        Order = pass;
    }

    /// <summary>The playing stretch's song that matches, from the current song on first (a list may hold a song twice); -1 for none.</summary>
    private int FindInStretch(Func<TrackInfo, bool> matches)
    {
        var end = Math.Min(StretchEnd, Order.Count);
        for (var i = Math.Max(Index, StretchStart); i < end; i++)
        {
            if (matches(Order[i]))
            {
                return i;
            }
        }

        for (var i = Math.Max(0, StretchStart); i < Math.Min(Index, end); i++)
        {
            if (matches(Order[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<TrackInfo> list, TrackInfo song)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], song))
            {
                return i;
            }
        }

        return -1;
    }
}
