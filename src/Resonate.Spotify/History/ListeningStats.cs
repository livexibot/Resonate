using Resonate.Spotify.Library;

namespace Resonate.Spotify.History;

/// <summary>Listening over a stretch of time, for the cards at the top of Home.</summary>
/// <param name="Listened">
/// The played songs' lengths added up. Spotify counts a song once it played
/// for 30 seconds, so a skipped song counts in full: this is approximate.
/// </param>
/// <param name="Songs">How many songs were played (a song played twice counts twice).</param>
/// <param name="TopArtist">The artist played most (by the songs' first artist), or null when nothing was played.</param>
/// <param name="TopSong">The latest play of the song played most.</param>
public sealed record ListeningSummary(
    TimeSpan Listened,
    int Songs,
    ArtistRef? TopArtist,
    int TopArtistPlays,
    PlayRecord? TopSong,
    int TopSongPlays)
{
    public static readonly ListeningSummary Empty = new(TimeSpan.Zero, 0, null, 0, null, 0);
}

/// <summary>Statistics from the listening history. Pure functions, so they are quick to test.</summary>
public static class ListeningStats
{
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>The plays in the <paramref name="span"/> up to <paramref name="now"/>.</summary>
    public static ListeningSummary Summarize(IEnumerable<PlayRecord> plays, DateTimeOffset now, TimeSpan span)
    {
        var since = now - span;
        var listened = 0L;
        var songs = 0;
        var artists = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var tracks = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var play in plays)
        {
            if (play.PlayedAt <= since || play.PlayedAt > now)
            {
                continue;
            }

            songs++;
            listened += play.DurationMs;
            if (play.Artists.Count > 0)
            {
                Count(artists, ArtistKey(play.Artists[0]), play);
            }

            Count(tracks, play.Uri, play);
        }

        if (songs == 0)
        {
            return ListeningSummary.Empty;
        }

        // Ties: more time with the artist, then the one heard last; for songs, the one heard last.
        var topArtist = artists.Values
            .OrderByDescending(t => t.Plays)
            .ThenByDescending(t => t.Milliseconds)
            .ThenByDescending(t => t.Latest.PlayedAt)
            .FirstOrDefault();
        var topSong = tracks.Values
            .OrderByDescending(t => t.Plays)
            .ThenByDescending(t => t.Latest.PlayedAt)
            .First();

        return new ListeningSummary(
            TimeSpan.FromMilliseconds(listened),
            songs,
            topArtist?.Latest.Artists[0],
            topArtist?.Plays ?? 0,
            topSong.Latest,
            topSong.Plays);
    }

    /// <summary>The artists played most (by the songs' first artist), most first. Only artists Spotify gave an ID.</summary>
    public static List<ArtistRef> TopArtists(IEnumerable<PlayRecord> plays, int count)
    {
        var artists = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var play in plays)
        {
            if (play.Artists is [{ Id: { } id }, ..])
            {
                Count(artists, id, play);
            }
        }

        return artists.Values
            .OrderByDescending(t => t.Plays)
            .ThenByDescending(t => t.Latest.PlayedAt)
            .Take(count)
            .Select(t => t.Latest.Artists[0])
            .ToList();
    }

    /// <summary>The last songs played, each once, newest first; songs Spotify can not start by address (local files) are left out.</summary>
    public static List<PlayRecord> RecentSongs(IReadOnlyList<PlayRecord> plays, int count)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var recent = new List<PlayRecord>(count);
        foreach (var play in plays.OrderByDescending(p => p.PlayedAt))
        {
            if (recent.Count == count)
            {
                break;
            }

            if (!play.Uri.StartsWith("spotify:local:", StringComparison.Ordinal) && seen.Add(play.Uri))
            {
                recent.Add(play);
            }
        }

        return recent;
    }

    private static string ArtistKey(ArtistRef artist) => artist.Id ?? "name:" + artist.Name;

    private static void Count(Dictionary<string, Tally> tallies, string key, PlayRecord play)
    {
        if (!tallies.TryGetValue(key, out var tally))
        {
            tally = new Tally(play);
            tallies[key] = tally;
        }
        else if (play.PlayedAt > tally.Latest.PlayedAt)
        {
            tally.Latest = play;
        }

        tally.Plays++;
        tally.Milliseconds += play.DurationMs;
    }

    private sealed class Tally(PlayRecord latest)
    {
        public int Plays { get; set; }

        public long Milliseconds { get; set; }

        public PlayRecord Latest { get; set; } = latest;
    }
}
