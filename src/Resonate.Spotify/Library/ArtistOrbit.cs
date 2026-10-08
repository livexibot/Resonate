using System.Globalization;
using Resonate.Spotify.History;

namespace Resonate.Spotify.Library;

/// <summary>An artist who keeps another company in the user's own music, and how.</summary>
/// <param name="Playlists">How many of the user's own playlists have both.</param>
/// <param name="Sessions">How many listening sessions in the history had both.</param>
/// <param name="Songs">How many liked songs they share (one featuring the other).</param>
/// <param name="Strength">How strongly they go together; small playlists and sessions count more than big ones.</param>
public sealed record OrbitCompanion(string Id, string Name, int Playlists, int Sessions, int Songs, double Strength)
{
    /// <summary>"Together in 6 of your playlists", then the sessions and songs, one per line.</summary>
    public string Description => ArtistOrbit.Describe(this);
}

/// <summary>An album of the artist with songs in Liked Songs.</summary>
public sealed record OrbitAlbum(string Id, string Name, string? ImageUrl, int LikedSongs);

/// <summary>One artist's orbit: the artists around them, and their albums the user likes.</summary>
public sealed record ArtistOrbitView(string ArtistId, string? ArtistName, IReadOnlyList<OrbitCompanion> Companions, IReadOnlyList<OrbitAlbum> Albums)
{
    public static readonly ArtistOrbitView Empty = new(string.Empty, null, [], []);
}

/// <summary>
/// Which artists go together in the user's own music: the playlists they own
/// or share, the listening sessions in Resonate's history, and liked songs
/// with several artists. A personal stand-in for "Fans also like", which
/// Spotify no longer offers apps. Built once from stored data (no requests),
/// then asked about one artist at a time in a few milliseconds.
/// </summary>
public sealed class ArtistOrbit
{
    public const int MaxCompanions = 10;

    public const int MaxAlbums = 8;

    private readonly List<Group> _groups = [];
    private readonly Dictionary<string, List<int>> _byArtist = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<TrackInfo> _liked;

    private ArtistOrbit(IReadOnlyList<TrackInfo> liked) => _liked = liked;

    private enum Link
    {
        Playlist,
        Session,
        Song,
    }

    /// <param name="playlists">The songs of the user's own playlists.</param>
    /// <param name="liked">Liked Songs: songs with several artists, and the albums the user likes.</param>
    /// <param name="history">The listening history, split into sessions where nothing played for a while.</param>
    public static ArtistOrbit Build(
        IReadOnlyList<IReadOnlyList<TrackInfo>> playlists,
        IReadOnlyList<TrackInfo> liked,
        IReadOnlyList<PlayRecord> history)
    {
        var orbit = new ArtistOrbit(liked);
        foreach (var playlist in playlists)
        {
            orbit.Add(Link.Playlist, playlist.SelectMany(t => t.ArtistRefs));
        }

        foreach (var session in DailyMixBuilder.Sessions(history))
        {
            orbit.Add(Link.Session, session.SelectMany(p => p.Artists));
        }

        foreach (var song in liked.DistinctBy(t => t.Uri))
        {
            orbit.Add(Link.Song, song.ArtistRefs);
        }

        return orbit;
    }

    /// <summary>"Together in 6 of your playlists", "Together in 3 listening sessions", "On 2 songs together", one per line.</summary>
    public static string Describe(OrbitCompanion companion)
    {
        var lines = new List<string>();
        if (companion.Playlists > 0)
        {
            lines.Add($"Together in {Count(companion.Playlists)} of your playlists");
        }

        if (companion.Sessions > 0)
        {
            lines.Add(companion.Sessions == 1 ? "Together in 1 listening session" : $"Together in {Count(companion.Sessions)} listening sessions");
        }

        if (companion.Songs > 0)
        {
            lines.Add(companion.Songs == 1 ? "On 1 song together" : $"On {Count(companion.Songs)} songs together");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The artists around <paramref name="artistId"/>, strongest first (equally
    /// strong ones by name), and the artist's albums with the most liked songs.
    /// </summary>
    public ArtistOrbitView For(string artistId, int maxCompanions = MaxCompanions, int maxAlbums = MaxAlbums)
    {
        var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (var index in _byArtist.GetValueOrDefault(artistId) ?? [])
        {
            var group = _groups[index];
            var weight = group.Link == Link.Song ? 1 : 1 / Math.Sqrt(group.Artists.Count);
            foreach (var other in group.Artists)
            {
                if (other == artistId)
                {
                    continue;
                }

                if (!tallies.TryGetValue(other, out var tally))
                {
                    tally = new Tally();
                    tallies[other] = tally;
                }

                tally.Strength += weight;
                switch (group.Link)
                {
                    case Link.Playlist:
                        tally.Playlists++;
                        break;
                    case Link.Session:
                        tally.Sessions++;
                        break;
                    default:
                        tally.Songs++;
                        break;
                }
            }
        }

        var companions = tallies
            .Where(pair => _names.ContainsKey(pair.Key))
            .Select(pair => new OrbitCompanion(pair.Key, _names[pair.Key], pair.Value.Playlists, pair.Value.Sessions, pair.Value.Songs, pair.Value.Strength))
            .OrderByDescending(c => c.Strength)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Take(maxCompanions)
            .ToList();

        var albums = _liked
            .Where(t => t.AlbumId is not null && t.ArtistRefs.Count > 0 && t.ArtistRefs[0].Id == artistId)
            .GroupBy(t => t.AlbumId!, StringComparer.Ordinal)
            .Select(g => (Album: new OrbitAlbum(g.Key, g.First().Album, g.First().LargeImageUrl, g.Count()), Latest: g.Max(t => t.AddedAt) ?? DateTimeOffset.MinValue))
            .OrderByDescending(a => a.Album.LikedSongs)
            .ThenByDescending(a => a.Latest)
            .Take(maxAlbums)
            .Select(a => a.Album)
            .ToList();

        return new ArtistOrbitView(artistId, _names.GetValueOrDefault(artistId), companions, albums);
    }

    private static string Count(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

    private void Add(Link link, IEnumerable<ArtistRef> artists)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artist in artists)
        {
            if (artist.Id is not { Length: > 0 } id || !seen.Add(id))
            {
                continue;
            }

            ids.Add(id);
            if (artist.Name.Length > 0)
            {
                _names.TryAdd(id, artist.Name);
            }
        }

        if (ids.Count < 2)
        {
            return;
        }

        var index = _groups.Count;
        _groups.Add(new Group(link, ids));
        foreach (var id in ids)
        {
            if (!_byArtist.TryGetValue(id, out var list))
            {
                list = [];
                _byArtist[id] = list;
            }

            list.Add(index);
        }
    }

    private sealed record Group(Link Link, List<string> Artists);

    private sealed class Tally
    {
        public int Playlists { get; set; }

        public int Sessions { get; set; }

        public int Songs { get; set; }

        public double Strength { get; set; }
    }
}
