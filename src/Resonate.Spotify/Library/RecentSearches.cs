namespace Resonate.Spotify.Library;

/// <summary>What a recent search opened or played: a song, artist, album or playlist.</summary>
public enum RecentSearchKind
{
    Song,
    Artist,
    Album,
    Playlist,
}

/// <summary>
/// Something opened or played from Search, kept so Search can show it again
/// ("Recently viewed"): enough to draw it and to open or play it again
/// without asking Spotify. Saved in the settings file.
/// </summary>
public sealed record RecentSearchPick(RecentSearchKind Kind, string Uri, string? Id, string Title, string Subtitle, string? ImageUrl)
{
    /// <summary>A song's own details, so it can be played again.</summary>
    public string? Artists { get; init; }

    public string? Album { get; init; }

    public string? AlbumUri { get; init; }

    public long DurationMs { get; init; }

    public bool IsExplicit { get; init; }

    public static RecentSearchPick Song(TrackInfo track) =>
        new(RecentSearchKind.Song, track.Uri ?? string.Empty, track.Id, track.Title, track.Artists, track.SmallImageUrl)
        {
            Artists = track.Artists,
            Album = track.Album,
            AlbumUri = track.AlbumUri,
            DurationMs = (long)track.Duration.TotalMilliseconds,
            IsExplicit = track.IsExplicit,
        };

    /// <summary>The song again, to play (a song only).</summary>
    public TrackInfo ToTrack() =>
        new(Uri, Title, Artists ?? Subtitle, Album ?? string.Empty, AlbumUri, TimeSpan.FromMilliseconds(DurationMs), ImageUrl, ImageUrl, IsExplicit, IsPlayable: true)
        {
            Id = Id,
        };
}

/// <summary>
/// The recent searches Search shows before anything is typed: the last
/// queries (newest first, each once, whatever its case) and the last things
/// opened or played from the results.
/// </summary>
public static class RecentSearches
{
    /// <summary>How many recent queries and picks are kept.</summary>
    public const int MaxQueries = 8;

    public const int MaxPicks = 12;

    /// <summary>The shortest query worth keeping.</summary>
    public const int MinQueryLength = 2;

    /// <summary>Puts <paramref name="query"/> first in <paramref name="queries"/> (once, trimmed), keeping at most <see cref="MaxQueries"/>.</summary>
    public static bool AddQuery(List<string> queries, string? query)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length < MinQueryLength)
        {
            return false;
        }

        if (queries.Count > 0 && string.Equals(queries[0], text, StringComparison.Ordinal))
        {
            return false;
        }

        queries.RemoveAll(q => string.Equals(q, text, StringComparison.OrdinalIgnoreCase));
        queries.Insert(0, text);
        if (queries.Count > MaxQueries)
        {
            queries.RemoveRange(MaxQueries, queries.Count - MaxQueries);
        }

        return true;
    }

    /// <summary>Puts <paramref name="pick"/> first in <paramref name="picks"/> (once per Spotify address), keeping at most <see cref="MaxPicks"/>.</summary>
    public static bool AddPick(List<RecentSearchPick> picks, RecentSearchPick pick)
    {
        if (string.IsNullOrEmpty(pick.Uri))
        {
            return false;
        }

        picks.RemoveAll(p => p.Uri == pick.Uri);
        picks.Insert(0, pick);
        if (picks.Count > MaxPicks)
        {
            picks.RemoveRange(MaxPicks, picks.Count - MaxPicks);
        }

        return true;
    }

    /// <summary>
    /// The result shown largest, Spotify-like: an artist whose name is the
    /// query, else the first song, else the first artist, album or playlist.
    /// </summary>
    public static RecentSearchKind? TopKind(string query, SearchMatches results)
    {
        var text = query.Trim();
        if (results.Artists.Count > 0 && string.Equals(results.Artists[0].Name, text, StringComparison.OrdinalIgnoreCase))
        {
            return RecentSearchKind.Artist;
        }

        if (results.Tracks.Count > 0)
        {
            return RecentSearchKind.Song;
        }

        if (results.Artists.Count > 0)
        {
            return RecentSearchKind.Artist;
        }

        if (results.Albums.Count > 0)
        {
            return RecentSearchKind.Album;
        }

        return results.Playlists.Count > 0 ? RecentSearchKind.Playlist : null;
    }
}
