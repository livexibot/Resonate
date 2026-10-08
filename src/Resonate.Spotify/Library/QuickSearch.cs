using System.Globalization;
using System.Text;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>What a quick search result is.</summary>
public enum QuickKind
{
    Playlist,
    LikedSongs,
    Artist,
    Album,
    Song,
    LocalSong,
}

/// <summary>One result of a quick search: a list, an artist or a song, as a row shows it.</summary>
/// <param name="Id">Spotify's ID (a playlist's, album's or artist's), when it has one.</param>
public sealed record QuickItem(QuickKind Kind, string Title, string Subtitle, string? ImageUrl, string? Uri, string? Id)
{
    /// <summary>The song, for songs and local songs.</summary>
    public TrackInfo? Track { get; init; }
}

/// <summary>
/// The user's own library, searchable as fast as they type: their playlists,
/// Liked Songs and the artists, albums and songs in it, and Local Files.
/// Built once from what Resonate already keeps (off the interface thread, as
/// Liked Songs can be long); each search then only reads memory. Matching
/// ignores case and accents, and every word typed must appear.
/// </summary>
public sealed class QuickSearchIndex
{
    private readonly Entry[] _library;
    private readonly Entry[] _local;

    private QuickSearchIndex(Entry[] library, Entry[] local, IReadOnlyList<TrackInfo> localSongs)
    {
        _library = library;
        _local = local;
        LocalSongs = localSongs;
    }

    public static QuickSearchIndex Empty { get; } = new([], [], []);

    /// <summary>The local songs, in the order they were given (to play on from the one picked).</summary>
    public IReadOnlyList<TrackInfo> LocalSongs { get; }

    public static QuickSearchIndex Build(
        IReadOnlyList<SimplifiedPlaylist> playlists,
        IReadOnlyList<TrackInfo> likedSongs,
        IReadOnlyList<TrackInfo> localSongs)
    {
        var library = new List<Entry>(playlists.Count + (likedSongs.Count * 2) + 1)
        {
            Entry.Of(new QuickItem(QuickKind.LikedSongs, "Liked Songs", "Your library", null, null, null), 0),
        };

        foreach (var playlist in playlists)
        {
            var owner = playlist.Owner?.DisplayName;
            library.Add(Entry.Of(
                new QuickItem(
                    QuickKind.Playlist,
                    playlist.Name,
                    string.IsNullOrEmpty(owner) ? "Playlist" : "Playlist · " + owner,
                    ImagePicker.Pick(playlist.Images, 64),
                    playlist.Uri is { Length: > 0 } uri ? uri : "spotify:playlist:" + playlist.Id,
                    playlist.Id),
                0));
        }

        var artists = new HashSet<string>(StringComparer.Ordinal);
        var albums = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in likedSongs)
        {
            foreach (var artist in track.ArtistRefs)
            {
                if (artist.Id is { } id && artists.Add(id))
                {
                    library.Add(Entry.Of(new QuickItem(QuickKind.Artist, artist.Name, "Artist", null, "spotify:artist:" + id, id), 1));
                }
            }

            if (track.AlbumId is { } albumId && track.Album.Length > 0 && albums.Add(albumId))
            {
                library.Add(Entry.Of(
                    new QuickItem(QuickKind.Album, track.Album, "Album · " + track.PrimaryArtist, track.SmallImageUrl, track.AlbumUri ?? "spotify:album:" + albumId, albumId),
                    2));
            }

            if (track.IsPlayable)
            {
                library.Add(Entry.Of(new QuickItem(QuickKind.Song, track.Title, track.Artists, track.SmallImageUrl, track.Uri, track.Id) { Track = track }, 3));
            }
        }

        var local = localSongs
            .Select(track => Entry.Of(
                new QuickItem(QuickKind.LocalSong, track.Title, track.Artists.Length > 0 ? track.Artists : "Local file", null, null, null) { Track = track },
                3))
            .ToArray();

        return new QuickSearchIndex([.. library], local, localSongs);
    }

    /// <summary>
    /// The best matches in the user's library: lists first, then artists,
    /// albums and songs, each by how well it matches. With nothing typed,
    /// Liked Songs and the playlists in the order given.
    /// </summary>
    public IReadOnlyList<QuickItem> SearchLibrary(string query, int limit)
    {
        var tokens = Tokens(query);
        return tokens.Length == 0
            ? _library.Where(e => e.Item.Kind is QuickKind.LikedSongs or QuickKind.Playlist).Take(limit).Select(e => e.Item).ToList()
            : Search(_library, tokens, limit);
    }

    /// <summary>The best matches in Local Files; none with nothing typed.</summary>
    public IReadOnlyList<QuickItem> SearchLocal(string query, int limit)
    {
        var tokens = Tokens(query);
        return tokens.Length == 0 ? [] : Search(_local, tokens, limit);
    }

    /// <summary>Lower case without accents, so "Beyonce" finds "Beyoncé".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>The words typed, normalised.</summary>
    public static string[] Tokens(string query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// How well a title (and the words around it, such as the artist) match
    /// the words typed: 0 when one of them is missing, more when the title
    /// starts with what was typed, or one of its words does. Both normalised.
    /// </summary>
    public static int Score(string title, string text, IReadOnlyList<string> tokens) =>
        tokens.Count == 0 ? 0 : Score(title, text, new Query(tokens));

    private static int Score(string title, string text, Query query)
    {
        foreach (var token in query.Tokens)
        {
            if (!text.Contains(token, StringComparison.Ordinal))
            {
                return 0;
            }
        }

        if (title.StartsWith(query.Phrase, StringComparison.Ordinal))
        {
            return 4;
        }

        var first = query.Tokens[0];
        if (title.StartsWith(first, StringComparison.Ordinal) || title.Contains(query.SpacedFirst, StringComparison.Ordinal))
        {
            return 3;
        }

        return title.Contains(first, StringComparison.Ordinal) ? 2 : 1;
    }

    private static List<QuickItem> Search(Entry[] entries, string[] tokens, int limit)
    {
        var query = new Query(tokens);
        var matches = new List<(Entry Entry, int Score)>();
        foreach (var entry in entries)
        {
            var score = Score(entry.Title, entry.Text, query);
            if (score > 0)
            {
                matches.Add((entry, score));
            }
        }

        // Stable: equal matches keep the library's own order.
        return matches
            .OrderBy(m => m.Entry.Rank)
            .ThenByDescending(m => m.Score)
            .Take(limit)
            .Select(m => m.Entry.Item)
            .ToList();
    }

    /// <summary>The words typed, with what each search compares them in one piece.</summary>
    private sealed class Query(IReadOnlyList<string> tokens)
    {
        public IReadOnlyList<string> Tokens { get; } = tokens;

        public string Phrase { get; } = string.Join(' ', tokens);

        public string SpacedFirst { get; } = " " + tokens[0];
    }

    /// <param name="Rank">Lists 0, artists 1, albums 2, songs 3: what a search shows first.</param>
    private sealed record Entry(QuickItem Item, string Title, string Text, int Rank)
    {
        public static Entry Of(QuickItem item, int rank)
        {
            var title = Normalize(item.Title);
            return new Entry(item, title, title + " " + Normalize(item.Subtitle), rank);
        }
    }
}
