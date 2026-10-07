using System.Globalization;
using System.Text.Json.Serialization;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.Spotify.History;

/// <summary>An artist a daily mix can be built around.</summary>
public sealed record MixSeed(string Id, string Name, string? ImageUrl);

/// <summary>A daily mix: songs the user already loves, around one favourite artist.</summary>
public sealed class DailyMix
{
    /// <summary>1 for "Daily Mix 1", and so on.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The artist the mix is built around.</summary>
    public string SeedId { get; set; } = string.Empty;

    /// <summary>The seed first, then the artists with the most songs in the mix.</summary>
    public List<string> ArtistNames { get; set; } = [];

    /// <summary>The seed artist's picture, when known.</summary>
    public string? ImageUrl { get; set; }

    public List<TrackInfo> Tracks { get; set; } = [];

    /// <summary>"Mira Sol, Lumen, Echo Harbor".</summary>
    [JsonIgnore]
    public string Subtitle => string.Join(", ", ArtistNames);
}

/// <summary>
/// Makes daily mixes on this computer. Spotify no longer lets new apps use
/// its recommendations or related artists, so a mix takes the user's Liked
/// Songs by one of their top artists, plus Liked Songs by the artists that
/// keep company with that artist in the user's own playlists and listening
/// sessions. The order is shuffled with the day as the seed, so a mix stays
/// the same all day and changes the next.
/// </summary>
public static class DailyMixBuilder
{
    public const int MaxMixes = 6;

    /// <summary>The longest a mix gets.</summary>
    public const int MaxSongs = 50;

    /// <summary>A mix is topped up with more of the seed artist's songs until it has this many, when there are enough.</summary>
    public const int TargetSongs = 30;

    /// <summary>Fewer songs than this and the mix is not offered.</summary>
    public const int MinSongs = 10;

    /// <summary>A seed needs at least this many liked songs of its own that no earlier mix took.</summary>
    private const int MinSeedSongs = 2;

    private const int MaxSeedSongs = 20;

    private const int MaxSongsPerArtist = 5;

    /// <summary>Plays further apart than this belong to different listening sessions.</summary>
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <param name="seeds">The user's top artists, most listened first.</param>
    /// <param name="liked">Liked Songs: the songs mixes are made of.</param>
    /// <param name="playlists">The songs of the user's own playlists, for which artists go together.</param>
    /// <param name="history">The listening history, for the same.</param>
    /// <param name="day">The day the mixes are for; it seeds their order.</param>
    public static List<DailyMix> Build(
        IReadOnlyList<MixSeed> seeds,
        IReadOnlyList<TrackInfo> liked,
        IReadOnlyList<IReadOnlyList<TrackInfo>> playlists,
        IReadOnlyList<PlayRecord> history,
        DateOnly day)
    {
        var songs = liked.Where(IsUsable).DistinctBy(t => t.Uri).ToList();
        var byArtist = new Dictionary<string, List<TrackInfo>>(StringComparer.Ordinal);
        foreach (var song in songs)
        {
            foreach (var id in song.ArtistRefs.Select(a => a.Id).OfType<string>().Distinct())
            {
                if (!byArtist.TryGetValue(id, out var list))
                {
                    list = [];
                    byArtist[id] = list;
                }

                list.Add(song);
            }
        }

        var names = songs.SelectMany(s => s.ArtistRefs)
            .Concat(history.SelectMany(p => p.Artists))
            .Where(a => a.Id is not null)
            .DistinctBy(a => a.Id)
            .ToDictionary(a => a.Id!, a => a.Name, StringComparer.Ordinal);
        var groups = ArtistGroups(playlists, history, songs);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var mixes = new List<DailyMix>();
        foreach (var seed in Candidates(seeds, history, byArtist))
        {
            if (mixes.Count == MaxMixes)
            {
                break;
            }

            if (MakeMix(seed, byArtist, groups, used, day) is not { } mix)
            {
                continue;
            }

            var number = mixes.Count + 1;
            mixes.Add(new DailyMix
            {
                Number = number,
                Title = "Daily Mix " + number.ToString(CultureInfo.InvariantCulture),
                SeedId = seed.Id,
                ArtistNames = [seed.Name, .. mix.Others.Take(2).Select(id => names.GetValueOrDefault(id, string.Empty)).Where(n => n.Length > 0)],
                ImageUrl = seed.ImageUrl,
                Tracks = mix.Tracks,
            });
        }

        return mixes;
    }

    /// <summary>
    /// The seeds to try, in order: the given top artists, then the artists
    /// played most in the history, then those with the most liked songs
    /// (so a new account with liked songs still gets mixes).
    /// </summary>
    internal static IEnumerable<MixSeed> Candidates(
        IReadOnlyList<MixSeed> seeds,
        IReadOnlyList<PlayRecord> history,
        Dictionary<string, List<TrackInfo>> byArtist)
    {
        var played = ListeningStats.TopArtists(history, 20).Select(a => new MixSeed(a.Id!, a.Name, null));
        var mostLiked = byArtist
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(20)
            .Select(pair => new MixSeed(pair.Key, pair.Value[0].ArtistRefs.First(a => a.Id == pair.Key).Name, null));
        return seeds.Concat(played).Concat(mostLiked).DistinctBy(s => s.Id);
    }

    /// <summary>
    /// For each artist, how strongly they go with <paramref name="seedId"/>:
    /// each playlist, listening session or song they share adds to it, and
    /// small groups count more than big ones (a playlist of 200 artists says
    /// little about any two of them). Strongest first; equally strong
    /// artists in an order that changes with the <paramref name="day"/>.
    /// </summary>
    internal static List<string> Related(string seedId, IReadOnlyList<HashSet<string>> groups, DateOnly day)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (!group.Contains(seedId))
            {
                continue;
            }

            var weight = 1 / Math.Sqrt(group.Count);
            foreach (var other in group)
            {
                if (other != seedId)
                {
                    weights[other] = weights.GetValueOrDefault(other) + weight;
                }
            }
        }

        return weights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => DailyRandom.Hash(DailyRandom.DayKey(day) + "|" + seedId + "|" + pair.Key))
            .Select(pair => pair.Key)
            .ToList();
    }

    /// <summary>The sets of artists that appear together: per playlist, per listening session, and per song with several artists.</summary>
    internal static List<HashSet<string>> ArtistGroups(
        IReadOnlyList<IReadOnlyList<TrackInfo>> playlists,
        IReadOnlyList<PlayRecord> history,
        IReadOnlyList<TrackInfo> songs)
    {
        var groups = new List<HashSet<string>>();
        foreach (var playlist in playlists)
        {
            Add(playlist.SelectMany(t => t.ArtistRefs).Select(a => a.Id));
        }

        foreach (var session in Sessions(history))
        {
            Add(session.SelectMany(p => p.Artists).Select(a => a.Id));
        }

        foreach (var song in songs)
        {
            Add(song.ArtistRefs.Select(a => a.Id));
        }

        return groups;

        void Add(IEnumerable<string?> ids)
        {
            var set = ids.OfType<string>().ToHashSet(StringComparer.Ordinal);
            if (set.Count >= 2)
            {
                groups.Add(set);
            }
        }
    }

    /// <summary>The history split where no song was played for a while.</summary>
    internal static IEnumerable<List<PlayRecord>> Sessions(IReadOnlyList<PlayRecord> history)
    {
        var current = new List<PlayRecord>();
        foreach (var play in history.OrderBy(p => p.PlayedAt))
        {
            if (current.Count > 0 && play.PlayedAt - current[^1].PlayedAt > SessionGap)
            {
                yield return current;
                current = [];
            }

            current.Add(play);
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Moves songs so the same artist does not play twice in a row where
    /// another song can go between. Keeps the order otherwise.
    /// </summary>
    internal static List<TrackInfo> Spread(List<TrackInfo> tracks)
    {
        for (var i = 1; i < tracks.Count; i++)
        {
            if (!SameArtist(tracks[i], tracks[i - 1]))
            {
                continue;
            }

            for (var j = i + 1; j < tracks.Count; j++)
            {
                if (!SameArtist(tracks[j], tracks[i - 1]))
                {
                    (tracks[i], tracks[j]) = (tracks[j], tracks[i]);
                    break;
                }
            }
        }

        return tracks;
    }

    private static (List<TrackInfo> Tracks, List<string> Others)? MakeMix(
        MixSeed seed,
        Dictionary<string, List<TrackInfo>> byArtist,
        IReadOnlyList<HashSet<string>> groups,
        HashSet<string> used,
        DateOnly day)
    {
        if (!byArtist.TryGetValue(seed.Id, out var seedSongs))
        {
            return null;
        }

        // Same day and artist, same numbers: the mix does not change during the day.
        var random = DailyRandom.For(day, seed.Id);

        // Each song goes in one mix only, so mixes of artists that go together stay different.
        var available = TrueShuffle.Shuffle(seedSongs.Where(s => !used.Contains(s.Uri!)), random.Next);
        if (available.Count < MinSeedSongs)
        {
            return null;
        }

        var tracks = available.Take(MaxSeedSongs).ToList();
        var inMix = tracks.Select(t => t.Uri!).ToHashSet(StringComparer.Ordinal);
        var others = new List<(string Id, int Count)>();
        foreach (var artistId in Related(seed.Id, groups, day))
        {
            if (tracks.Count >= MaxSongs)
            {
                break;
            }

            if (!byArtist.TryGetValue(artistId, out var theirs))
            {
                continue;
            }

            var picked = TrueShuffle.Shuffle(theirs.Where(s => !used.Contains(s.Uri!) && !inMix.Contains(s.Uri!)), random.Next)
                .Take(Math.Min(MaxSongsPerArtist, MaxSongs - tracks.Count))
                .ToList();
            if (picked.Count == 0)
            {
                continue;
            }

            tracks.AddRange(picked);
            inMix.UnionWith(picked.Select(t => t.Uri!));
            others.Add((artistId, picked.Count));
        }

        // Few artists go with this one: more of the seed's own songs make up the length.
        foreach (var song in available.Skip(MaxSeedSongs))
        {
            if (tracks.Count >= TargetSongs)
            {
                break;
            }

            if (inMix.Add(song.Uri!))
            {
                tracks.Add(song);
            }
        }

        if (tracks.Count < MinSongs)
        {
            return null;
        }

        used.UnionWith(inMix);
        var ordered = Spread(TrueShuffle.Shuffle(tracks, random.Next));
        var named = others.OrderByDescending(o => o.Count).Select(o => o.Id).ToList();
        return (ordered, named);
    }

    private static bool IsUsable(TrackInfo track) =>
        track.IsPlayable && track.Uri is not null && track.FilePath is null && !track.IsLocal;

    private static bool SameArtist(TrackInfo a, TrackInfo b) =>
        string.Equals(a.PrimaryArtist, b.PrimaryArtist, StringComparison.Ordinal);
}

/// <summary>
/// A small random number generator (SplitMix64) that gives the same numbers
/// for the same seed on every computer and .NET version, which
/// <see cref="Random"/> does not promise.
/// </summary>
internal sealed class DailyRandom
{
    private ulong _state;

    public DailyRandom(ulong seed) => _state = seed;

    /// <summary>The numbers for one day and one key (such as an artist ID).</summary>
    public static DailyRandom For(DateOnly day, string key) => new(Hash(DayKey(day) + "|" + key));

    public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A number in [0, <paramref name="max"/>).</summary>
    public int Next(int max) => max <= 1 ? 0 : (int)(NextUInt64() % (ulong)max);

    private ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>FNV-1a: a stable hash (string.GetHashCode changes every run).</summary>
    public static ulong Hash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }
}
