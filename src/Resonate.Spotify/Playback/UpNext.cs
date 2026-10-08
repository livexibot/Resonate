using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>What plays next, as the Up next plugin shows and edits it.</summary>
/// <param name="Current">The song playing (or ready to play).</param>
/// <param name="Upcoming">The songs after it, in the order they play.</param>
/// <param name="CanEdit">The songs can be moved, removed, cleared and shuffled now.</param>
public sealed record UpNextList(TrackInfo? Current, IReadOnlyList<TrackInfo> Upcoming, bool CanEdit)
{
    /// <summary>The list's songs that played before the current one (for saving the whole list).</summary>
    public IReadOnlyList<TrackInfo> Played { get; init; } = [];

    /// <summary>The list started before all its songs had loaded, so it can not be edited (otherwise <see cref="CanEdit"/> comes back with the next song).</summary>
    public bool PartlyKnown { get; init; }
}

/// <summary>
/// A song picked in Up next: where it was shown, and which song it was, so
/// an edit can never hit another song when the music moved on meanwhile.
/// </summary>
public readonly record struct UpNextPick(int Index, TrackInfo Song);

/// <summary>
/// A player whose upcoming songs the user can edit (the Up next plugin).
/// Edits show at once; each returns false when it could not be made (the
/// list moved on, or it can not be edited now), and the caller reads
/// <see cref="UpNext"/> again.
/// </summary>
public interface IUpNext
{
    /// <summary>What plays next, or null when this player does not know (music started outside Resonate).</summary>
    UpNextList? UpNext { get; }

    /// <summary>Moves an upcoming song to <paramref name="to"/> (its place once moved).</summary>
    bool MoveUpNext(UpNextPick song, int to);

    bool RemoveUpNext(IReadOnlyList<UpNextPick> songs);

    /// <summary>Removes every upcoming song: the music stops after the current one (or, with "repeat all", starts the list again).</summary>
    bool ClearUpNext();

    /// <summary>Puts the upcoming songs in a truly random order (<see cref="TrueShuffle"/>).</summary>
    bool ShuffleUpNext();

    /// <summary>"Play next" (right after the current song) or "Add to queue" (after the songs queued before, ahead of the rest of the list).</summary>
    bool AddToUpNext(TrackInfo track, bool playNext);
}

/// <summary>
/// The edits, on a list of upcoming songs whose first <c>queued</c> were
/// added with "Add to queue" or "Play next": those play before the rest of
/// the list, in the order added, as in Spotify. Each returns how many are
/// queued afterwards.
/// </summary>
public static class UpNextEdits
{
    /// <summary>Whether <paramref name="pick"/> is still where it was shown.</summary>
    public static bool IsAt(IReadOnlyList<TrackInfo> upcoming, UpNextPick pick) =>
        pick.Index >= 0 && pick.Index < upcoming.Count && ReferenceEquals(upcoming[pick.Index], pick.Song);

    /// <summary>Moves the song at <paramref name="from"/> to <paramref name="to"/>; a song moved among the queued ones counts as queued.</summary>
    public static int Move(List<TrackInfo> upcoming, int queued, int from, int to)
    {
        var song = upcoming[from];
        upcoming.RemoveAt(from);
        upcoming.Insert(to, song);
        return from < queued
            ? (to < queued ? queued : queued - 1)
            : (to < queued ? queued + 1 : queued);
    }

    public static int Remove(List<TrackInfo> upcoming, int queued, IEnumerable<int> indexes)
    {
        foreach (var index in indexes.Distinct().OrderDescending())
        {
            upcoming.RemoveAt(index);
            if (index < queued)
            {
                queued--;
            }
        }

        return queued;
    }

    public static int Insert(List<TrackInfo> upcoming, int queued, TrackInfo track, bool playNext)
    {
        upcoming.Insert(playNext ? 0 : queued, track);
        return queued + 1;
    }

    /// <summary>All of them in a random order; none counts as queued afterwards.</summary>
    public static int Shuffle(List<TrackInfo> upcoming, Func<int, int>? nextInt)
    {
        var shuffled = TrueShuffle.Shuffle(upcoming, nextInt);
        upcoming.Clear();
        upcoming.AddRange(shuffled);
        return 0;
    }
}
