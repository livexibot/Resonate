namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Finds the file in Local Files that a local song in a Spotify playlist
/// stands for. Spotify gives such songs no cover, only what its app read
/// from the file's tags (title, artist, album and length), so the row
/// takes the file's own cover from Local Files instead (the owner saw them
/// with covers in Local Files and without in their playlists, 11 October
/// 2026). Titles must match; then a matching artist, or a matching album
/// and a length within <see cref="LengthSlack"/>, makes it the same song.
/// With no artist on either side the length alone decides.
/// </summary>
public sealed class LocalFileMatch
{
    /// <summary>How far Spotify's length and the file's may differ (each reads it its own way).</summary>
    public static readonly TimeSpan LengthSlack = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, List<LocalFile>> _byTitle = new(StringComparer.Ordinal);

    public LocalFileMatch(IEnumerable<LocalFile> files)
    {
        foreach (var file in files)
        {
            var key = Key(file.Title);
            if (key.Length == 0)
            {
                continue;
            }

            if (!_byTitle.TryGetValue(key, out var list))
            {
                _byTitle[key] = list = [];
            }

            list.Add(file);
        }
    }

    /// <summary>The best file for the song, or null when none is clearly it.</summary>
    /// <param name="artists">Spotify's artists, joined with ", " as lists show them.</param>
    public LocalFile? Find(string title, string artists, string album, TimeSpan duration)
    {
        if (!_byTitle.TryGetValue(Key(title), out var candidates))
        {
            return null;
        }

        var wanted = Artists(artists);
        var albumKey = Key(album);
        LocalFile? best = null;
        var bestScore = 0;
        foreach (var file in candidates)
        {
            var have = Artists(file.Artist);
            have.UnionWith(Artists(file.AlbumArtist));
            var artist = wanted.Count > 0 && have.Overlaps(wanted);
            var sameAlbum = albumKey.Length > 0 && Key(file.Album) == albumKey;
            var length = duration > TimeSpan.Zero && file.DurationMs > 0
                && (file.Duration - duration).Duration() <= LengthSlack;
            var unnamed = wanted.Count == 0 && have.Count == 0;
            if (!artist && !(sameAlbum && length) && !(unnamed && length))
            {
                continue;
            }

            var score = (artist ? 4 : 0) + (sameAlbum ? 2 : 0) + (length ? 1 : 0);
            if (score > bestScore)
            {
                best = file;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>The whole names and each name in a list of them ("A, B", "A; B", "A / B").</summary>
    private static HashSet<string> Artists(string? names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (Key(names) is not { Length: > 0 } whole)
        {
            return set;
        }

        set.Add(whole);
        foreach (var part in names!.Split([',', ';', '/', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Key(part) is { Length: > 0 } key)
            {
                set.Add(key);
            }
        }

        return set;
    }

    /// <summary>Lower case, with runs of spaces made one and none at the ends.</summary>
    private static string Key(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
