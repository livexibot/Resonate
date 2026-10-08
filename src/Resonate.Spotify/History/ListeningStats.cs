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

    /// <summary>
    /// Listening per clock hour, in milliseconds, for Home's chart of the
    /// past day: <paramref name="hours"/> whole hours on the clock of
    /// <paramref name="zone"/>, oldest first, the hour now running last. The
    /// hours are real hours, so a zone half an hour off UTC or a change of
    /// clocks still gets one bar per hour.
    /// </summary>
    public static long[] Hourly(IEnumerable<PlayRecord> plays, DateTimeOffset now, TimeZoneInfo zone, int hours = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hours);
        var first = FirstHour(now, zone, hours);
        var listened = new long[hours];
        foreach (var play in plays)
        {
            if (play.PlayedAt < first || play.PlayedAt > now)
            {
                continue;
            }

            var index = (int)((play.PlayedAt - first).Ticks / TimeSpan.TicksPerHour);
            listened[Math.Min(index, hours - 1)] += play.DurationMs;
        }

        return listened;
    }

    /// <summary>
    /// When each of <see cref="Hourly"/>'s hours starts, on the clock of
    /// <paramref name="zone"/> (so a label reads the time the clock showed,
    /// also across a change of clocks), oldest first.
    /// </summary>
    public static DateTimeOffset[] HourStarts(DateTimeOffset now, TimeZoneInfo zone, int hours = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hours);
        var first = FirstHour(now, zone, hours);
        var starts = new DateTimeOffset[hours];
        for (var i = 0; i < hours; i++)
        {
            starts[i] = TimeZoneInfo.ConvertTime(first.AddHours(i), zone);
        }

        return starts;
    }

    private static DateTimeOffset FirstHour(DateTimeOffset now, TimeZoneInfo zone, int hours)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var thisHour = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
        return thisHour.AddHours(-(hours - 1));
    }

    /// <summary>
    /// Listening per day, in milliseconds, for Home's chart of the past week:
    /// <paramref name="days"/> calendar days on the clock of
    /// <paramref name="zone"/>, oldest first, today last.
    /// </summary>
    public static long[] Daily(IEnumerable<PlayRecord> plays, DateTimeOffset now, TimeZoneInfo zone, int days = 7)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var today = LocalDay(now, zone);
        var listened = new long[days];
        foreach (var play in plays)
        {
            if (play.PlayedAt > now)
            {
                continue;
            }

            var index = days - 1 - (today.DayNumber - LocalDay(play.PlayedAt, zone).DayNumber);
            if (index >= 0)
            {
                listened[index] += play.DurationMs;
            }
        }

        return listened;
    }

    /// <summary>Midnight at the start of <paramref name="now"/>'s day on the clock of <paramref name="zone"/>.</summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = local.Date;

        // Where the clocks jump forward at midnight, the day starts when they land.
        return zone.IsInvalidTime(midnight)
            ? new DateTimeOffset(midnight.AddHours(1), local.Offset)
            : new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>
    /// How much more (above 0) or less (below 0) was listened over the
    /// <paramref name="span"/> up to <paramref name="now"/> than over the
    /// span before it, as a fraction (0.25 is a quarter more). Null while the
    /// history does not reach back over the earlier span, or nothing was
    /// played in it, since there is then nothing fair to compare with.
    /// </summary>
    public static double? Change(IReadOnlyCollection<PlayRecord> plays, DateTimeOffset now, TimeSpan span)
    {
        if (plays.Count == 0 || plays.Min(p => p.PlayedAt) > now - span - span)
        {
            return null;
        }

        var before = Summarize(plays, now - span, span).Listened;
        if (before <= TimeSpan.Zero)
        {
            return null;
        }

        return (Summarize(plays, now, span).Listened - before) / before;
    }

    /// <summary>
    /// The albums that come up most in <paramref name="tracks"/>, most first
    /// (ties go to the album met first), as one song of each, one with a
    /// cover when the album has any. Songs without an album are left out.
    /// For the four covers on a mix's card.
    /// </summary>
    public static List<TrackInfo> MostFrequentAlbums(IEnumerable<TrackInfo> tracks, int count)
    {
        var albums = new Dictionary<string, AlbumTally>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            var key = track.AlbumUri ?? (track.Album.Length > 0 ? "name:" + track.Album : null);
            if (key is null)
            {
                continue;
            }

            if (!albums.TryGetValue(key, out var album))
            {
                albums[key] = new AlbumTally(track, albums.Count);
                continue;
            }

            album.Songs++;
            if (album.Song.LargeImageUrl is null && track.LargeImageUrl is not null)
            {
                album.Song = track;
            }
        }

        return albums.Values
            .OrderByDescending(a => a.Songs)
            .ThenBy(a => a.Order)
            .Take(count)
            .Select(a => a.Song)
            .ToList();
    }

    private static DateOnly LocalDay(DateTimeOffset time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);

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

    private sealed class AlbumTally(TrackInfo song, int order)
    {
        public TrackInfo Song { get; set; } = song;

        public int Songs { get; set; } = 1;

        public int Order { get; } = order;
    }
}
