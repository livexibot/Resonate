using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>What a DJ set is made of. Saved nowhere; new kinds go at the end.</summary>
public enum DjSetKind
{
    /// <summary>The songs played most in the last two months.</summary>
    Favourites,

    /// <summary>Songs liked over a year ago and not played for three months.</summary>
    Throwbacks,

    /// <summary>Songs liked in the last six weeks.</summary>
    FreshFinds,

    /// <summary>Songs liked a while ago and never played since Resonate began keeping count.</summary>
    DeepCuts,

    /// <summary>Several songs by one artist the user loves.</summary>
    Spotlight,

    /// <summary>Songs the user usually plays at this time of day.</summary>
    RightNow,

    /// <summary>A bit of everything liked, when nothing else is left.</summary>
    Mix,
}

/// <summary>One set: a few songs with a theme and a line for the DJ's voice.</summary>
public sealed record DjSet(DjSetKind Kind, string Title, string Intro, IReadOnlyList<TrackInfo> Tracks);

/// <summary>
/// Resonate's own DJ (the owner's choice of 10 October 2026, since Spotify
/// lets no other app start its DJ): an endless run of short themed sets
/// from the user's Liked Songs and listening history, each introduced by a
/// line the DJ's voice can say. No song comes twice in one run, and a set
/// is only made when there are enough songs for it.
/// </summary>
public static class DjMix
{
    public const int DefaultSetSize = 6;

    /// <summary>A set needs at least this many songs, or another kind takes its turn.</summary>
    public const int SmallestSet = 3;

    private static readonly DjSetKind[] Rotation =
    [
        DjSetKind.Favourites, DjSetKind.FreshFinds, DjSetKind.Throwbacks, DjSetKind.Spotlight,
        DjSetKind.DeepCuts, DjSetKind.RightNow, DjSetKind.Spotlight, DjSetKind.Mix,
    ];

    /// <summary>
    /// <paramref name="sets"/> sets of about <paramref name="setSize"/> songs,
    /// starting with <paramref name="first"/> when there are songs for it
    /// (else with the next kind in turn). <paramref name="now"/> is local time.
    /// </summary>
    public static IReadOnlyList<DjSet> Build(
        IReadOnlyList<TrackInfo> liked,
        IReadOnlyList<PlayRecord> plays,
        DateTimeOffset now,
        Random random,
        int sets = 20,
        int setSize = DefaultSetSize,
        DjSetKind? first = null,
        IReadOnlySet<string>? skip = null)
    {
        setSize = Math.Clamp(setSize, SmallestSet, 15);
        var library = Playable(liked);
        var known = new Dictionary<string, TrackInfo>(StringComparer.Ordinal);
        foreach (var track in library)
        {
            known.TryAdd(track.Uri!, track);
        }

        foreach (var play in plays)
        {
            if (!known.ContainsKey(play.Uri) && ToTrack(play) is { } track)
            {
                known[play.Uri] = track;
            }
        }

        var recent = plays.Where(p => now - p.PlayedAt <= TimeSpan.FromDays(60)).ToList();
        var counts = recent.GroupBy(p => p.Uri).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var lastPlayed = plays.GroupBy(p => p.Uri).ToDictionary(g => g.Key, g => g.Max(p => p.PlayedAt), StringComparer.Ordinal);
        var used = new HashSet<string>(skip ?? new HashSet<string>(), StringComparer.Ordinal);
        var spotlit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DjSet>();

        var turn = first is { } start ? Array.IndexOf(Rotation, start) : 0;
        turn = Math.Max(turn, 0);
        var misses = 0;
        while (result.Count < sets && known.Count > 0)
        {
            var kind = Rotation[turn % Rotation.Length];
            turn++;
            if (Make(kind) is { } set)
            {
                result.Add(set with { Intro = (result.Count == 0 ? "Hey, it's your DJ. " : string.Empty) + set.Intro });
                misses = 0;
                continue;
            }

            // Every kind came up empty: the run starts over with every song free again.
            if (++misses >= Rotation.Length)
            {
                if (used.Count == 0)
                {
                    break;
                }

                used.Clear();
                misses = 0;
            }
        }

        return result;

        DjSet? Make(DjSetKind kind)
        {
            string? artist = null;
            IEnumerable<TrackInfo> candidates = kind switch
            {
                DjSetKind.Favourites => counts.Where(c => c.Value >= 2).OrderByDescending(c => c.Value)
                    .Select(c => known.GetValueOrDefault(c.Key)).OfType<TrackInfo>().Take(setSize * 4),
                DjSetKind.Throwbacks => library.Where(t => t.AddedAt is { } added && now - added >= TimeSpan.FromDays(365)
                    && (!lastPlayed.TryGetValue(t.Uri!, out var last) || now - last >= TimeSpan.FromDays(90))),
                DjSetKind.FreshFinds => library.Where(t => t.AddedAt is { } added && now - added <= TimeSpan.FromDays(45)),
                DjSetKind.DeepCuts => library.Where(t => !lastPlayed.ContainsKey(t.Uri!)
                    && (t.AddedAt is not { } added || now - added > TimeSpan.FromDays(45))),
                DjSetKind.Spotlight => SpotlightSongs(out artist),
                DjSetKind.RightNow => recent.Where(p => HoursApart(p.PlayedAt.ToOffset(now.Offset).Hour, now.Hour) <= 2)
                    .GroupBy(p => p.Uri).Select(g => known.GetValueOrDefault(g.Key)).OfType<TrackInfo>(),
                _ => library,
            };

            var free = candidates.Where(t => !used.Contains(t.Uri!)).DistinctBy(t => t.Uri).ToList();
            if (free.Count < Math.Min(SmallestSet, setSize))
            {
                return null;
            }

            // Favourites keep their order of plays, loosely; the rest are drawn at random.
            var picked = kind == DjSetKind.Favourites
                ? free.Take(setSize * 2).OrderBy(_ => random.Next()).Take(setSize).ToList()
                : free.OrderBy(_ => random.Next()).Take(setSize).ToList();
            foreach (var track in picked)
            {
                used.Add(track.Uri!);
            }

            if (artist is not null)
            {
                spotlit.Add(artist);
            }

            var period = Period(now.Hour);
            var (title, intros) = kind switch
            {
                DjSetKind.Favourites => ("Your favourites", new[] { "Let's start with the songs you can't stop playing.", "Here are your favourites lately." }),
                DjSetKind.Throwbacks => ("Throwbacks", new[] { "Let's go back a bit, to songs you liked a while ago.", "Some throwbacks you haven't heard in a while." }),
                DjSetKind.FreshFinds => ("Fresh finds", new[] { "Here's what you've been liking lately.", "Fresh finds, songs you saved recently." }),
                DjSetKind.DeepCuts => ("Deep cuts", new[] { "Some deep cuts from your library, ones you rarely play.", "Songs you saved but haven't played much. Let's change that." }),
                DjSetKind.Spotlight => ($"More from {artist}", new[] { $"A few from {artist}, one of your favourites.", $"Let's hear more from {artist}." }),
                DjSetKind.RightNow => ($"Your {period} picks", new[] { $"What you usually play in the {period}.", $"Some {period} favourites." }),
                _ => ("Mix", new[] { "A little bit of everything.", "Let's mix it up." }),
            };

            return new DjSet(kind, title, intros[random.Next(intros.Length)], picked);
        }

        IEnumerable<TrackInfo> SpotlightSongs(out string? artist)
        {
            // The artist played most lately (else liked most) who has enough liked songs and was not just spotlit.
            var byArtist = library.GroupBy(t => FirstArtist(t), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && !spotlit.Contains(g.Key) && g.Count(t => !used.Contains(t.Uri!)) >= SmallestSet)
                .OrderByDescending(g => recent.Count(p => string.Equals(FirstArtist(p), g.Key, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(g => g.Count())
                .FirstOrDefault();
            artist = byArtist?.Key;
            return byArtist ?? Enumerable.Empty<TrackInfo>();
        }
    }

    /// <summary>The songs in the order they play, and which set each starts.</summary>
    public static IReadOnlyList<TrackInfo> Flatten(IReadOnlyList<DjSet> sets) => [.. sets.SelectMany(s => s.Tracks)];

    /// <summary>Which set the song at <paramref name="index"/> of the flattened run belongs to, or -1.</summary>
    public static int SetAt(IReadOnlyList<DjSet> sets, int index)
    {
        if (index < 0)
        {
            return -1;
        }

        for (var i = 0; i < sets.Count; i++)
        {
            if (index < sets[i].Tracks.Count)
            {
                return i;
            }

            index -= sets[i].Tracks.Count;
        }

        return -1;
    }

    /// <summary>"morning", "afternoon", "evening" or "late-night".</summary>
    public static string Period(int hour) => hour switch
    {
        >= 5 and < 12 => "morning",
        >= 12 and < 17 => "afternoon",
        >= 17 and < 22 => "evening",
        _ => "late-night",
    };

    private static int HoursApart(int a, int b)
    {
        var apart = Math.Abs(a - b) % 24;
        return Math.Min(apart, 24 - apart);
    }

    private static List<TrackInfo> Playable(IReadOnlyList<TrackInfo> tracks) =>
        tracks.Where(t => t.Uri is { } uri && IsSong(uri) && t.IsPlayable && t.FilePath is null)
            .DistinctBy(t => t.Uri)
            .ToList();

    /// <summary>A song Spotify can be asked to play: not a local file in a playlist, not an episode.</summary>
    private static bool IsSong(string uri) =>
        uri.Length > 0 && !uri.StartsWith("spotify:local:", StringComparison.Ordinal) && !uri.StartsWith("spotify:episode:", StringComparison.Ordinal);

    private static string FirstArtist(TrackInfo track) =>
        track.ArtistRefs.Count > 0 ? track.ArtistRefs[0].Name : track.Artists.Split(',')[0].Trim();

    private static string FirstArtist(PlayRecord play) => play.Artists.Count > 0 ? play.Artists[0].Name : string.Empty;

    private static TrackInfo? ToTrack(PlayRecord play) =>
        IsSong(play.Uri)
            ? new TrackInfo(play.Uri, play.Title, string.Join(", ", play.Artists.Select(a => a.Name)), play.Album, play.AlbumUri, TimeSpan.FromMilliseconds(play.DurationMs), play.ImageUrl, play.ImageUrl, false, true)
            {
                Id = play.Uri.Split(':')[^1],
                ArtistRefs = play.Artists,
            }
            : null;
}
