using System.Globalization;
using System.Text.Json.Serialization;

namespace Resonate.Spotify.Library;

/// <summary>
/// What a rule of a smart playlist checks. Saved by number in the settings
/// file: never renumber, add new kinds at the end.
/// </summary>
public enum SmartRuleKind
{
    /// <summary>Saved within the last <see cref="SmartRule.Value"/> days.</summary>
    SavedInLastDays = 0,

    /// <summary>Saved in the years <see cref="SmartRule.Value"/> to <see cref="SmartRule.Value2"/> (local time).</summary>
    SavedInYears = 1,

    /// <summary>Released before the year <see cref="SmartRule.Value"/>.</summary>
    ReleasedBefore = 2,

    /// <summary>Released after the year <see cref="SmartRule.Value"/>.</summary>
    ReleasedAfter = 3,

    /// <summary>Released in the years <see cref="SmartRule.Value"/> to <see cref="SmartRule.Value2"/>.</summary>
    ReleasedBetween = 4,

    /// <summary>Longer than <see cref="SmartRule.Value"/> seconds.</summary>
    LongerThan = 5,

    /// <summary>Shorter than <see cref="SmartRule.Value"/> seconds.</summary>
    ShorterThan = 6,

    Explicit = 7,

    NotExplicit = 8,

    /// <summary>One of the song's artists is named <see cref="SmartRule.Text"/>.</summary>
    ArtistIs = 9,

    /// <summary>None of the song's artists is named <see cref="SmartRule.Text"/>.</summary>
    ArtistIsNot = 10,

    /// <summary>Not in the playlist with the ID <see cref="SmartRule.Text"/>.</summary>
    NotInPlaylist = 11,
}

/// <summary>The order of a smart playlist's songs. Saved by number: never renumber.</summary>
public enum SmartOrder
{
    /// <summary>As in the source: newest saved first for Liked Songs, the playlist's order for a playlist.</summary>
    SourceOrder = 0,
    OldestSaved = 1,
    NewestRelease = 2,
    OldestRelease = 3,
    Longest = 4,
    Shortest = 5,

    /// <summary>A random order that stays the same all day.</summary>
    Random = 6,
}

/// <summary>One condition of a smart playlist; every song must meet all of them.</summary>
public sealed class SmartRule
{
    public SmartRuleKind Kind { get; set; }

    /// <summary>Days, a year, or seconds, by <see cref="Kind"/>.</summary>
    public int Value { get; set; }

    /// <summary>The last year of a range.</summary>
    public int Value2 { get; set; }

    /// <summary>An artist's name, or a playlist's ID.</summary>
    public string? Text { get; set; }

    /// <summary>The playlist's name as last seen, for <see cref="SmartRuleKind.NotInPlaylist"/>.</summary>
    public string? Label { get; set; }

    public SmartRule Clone() => (SmartRule)MemberwiseClone();
}

/// <summary>
/// A playlist described by rules ("songs from Liked Songs, saved in 2023,
/// released before 2000"), filled from what the library already holds.
/// Kept in Resonate's settings; with <see cref="KeepOnSpotify"/>, a real
/// playlist on Spotify follows it (see <see cref="SmartPlaylistSync"/>).
/// </summary>
public sealed class SmartPlaylist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The user's own playlist the songs come from; null for Liked Songs.</summary>
    public string? SourcePlaylistId { get; set; }

    /// <summary>That playlist's name as last seen.</summary>
    public string? SourceName { get; set; }

    public List<SmartRule> Rules { get; set; } = [];

    public SmartOrder Order { get; set; }

    /// <summary>At most this many songs; null for no limit.</summary>
    public int? Limit { get; set; }

    /// <summary>Picks the random order (<see cref="SmartOrder.Random"/>).</summary>
    public int Seed { get; set; }

    /// <summary>A playlist on Spotify follows this one.</summary>
    public bool KeepOnSpotify { get; set; }

    /// <summary>That playlist on Spotify, once made.</summary>
    public string? SpotifyId { get; set; }

    /// <summary>When the playlist on Spotify was made.</summary>
    public DateTimeOffset? LinkedAt { get; set; }

    /// <summary>When Resonate last brought the playlist on Spotify up to date.</summary>
    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>The songs sent then (<see cref="SmartPlaylistSync.Signature"/>).</summary>
    public string? SyncedSignature { get; set; }

    /// <summary>The name sent then.</summary>
    public string? SyncedName { get; set; }

    [JsonIgnore]
    public bool FromLikedSongs => SourcePlaylistId is null;

    public SmartPlaylist Clone()
    {
        var copy = (SmartPlaylist)MemberwiseClone();
        copy.Rules = Rules.Select(r => r.Clone()).ToList();
        return copy;
    }
}

/// <summary>The songs the rules look at: the source, and the songs of each playlist named by a "not in" rule.</summary>
public sealed record SmartInputs(IReadOnlyList<TrackInfo> Source, IReadOnlyDictionary<string, IReadOnlySet<string>> Playlists)
{
    public static readonly SmartInputs Empty = new([], new Dictionary<string, IReadOnlySet<string>>());
}

/// <summary>Works out a smart playlist's songs. Pure: everything it needs is passed in.</summary>
public static class SmartPlaylistEvaluator
{
    /// <summary>
    /// The songs that meet every rule, in the playlist's order, cut to its
    /// limit. Each song once; local files are left out (Spotify will not
    /// start them, nor add them to a playlist).
    /// </summary>
    public static List<TrackInfo> Evaluate(SmartPlaylist playlist, SmartInputs inputs, DateTimeOffset now, TimeZoneInfo zone)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<TrackInfo>();
        foreach (var track in inputs.Source)
        {
            if (track.Uri is not { } uri || track.IsLocal || track.FilePath is not null || !seen.Add(uri))
            {
                continue;
            }

            var keep = true;
            foreach (var rule in playlist.Rules)
            {
                if (!Matches(rule, track, inputs, now, zone))
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
            {
                matches.Add(track);
            }
        }

        var ordered = Sort(matches, playlist.Order, playlist.Seed, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date));
        return playlist.Limit is > 0 and var limit && ordered.Count > limit ? ordered.GetRange(0, limit) : ordered;
    }

    /// <summary>Whether any rule needs songs' release years.</summary>
    public static bool NeedsReleaseYears(SmartPlaylist playlist) =>
        playlist.Order is SmartOrder.NewestRelease or SmartOrder.OldestRelease
        || playlist.Rules.Any(r => r.Kind is SmartRuleKind.ReleasedBefore or SmartRuleKind.ReleasedAfter or SmartRuleKind.ReleasedBetween);

    public static bool Matches(SmartRule rule, TrackInfo track, SmartInputs inputs, DateTimeOffset now, TimeZoneInfo zone)
    {
        var (low, high) = rule.Value <= rule.Value2 ? (rule.Value, rule.Value2) : (rule.Value2, rule.Value);
        return rule.Kind switch
        {
            SmartRuleKind.SavedInLastDays => track.AddedAt is { } added && added >= now.AddDays(-Math.Max(rule.Value, 0)),
            SmartRuleKind.SavedInYears => track.AddedAt is { } added && TimeZoneInfo.ConvertTime(added, zone).Year is var year && year >= low && year <= high,
            SmartRuleKind.ReleasedBefore => track.ReleaseYear < rule.Value,
            SmartRuleKind.ReleasedAfter => track.ReleaseYear > rule.Value,
            SmartRuleKind.ReleasedBetween => track.ReleaseYear >= low && track.ReleaseYear <= high,
            SmartRuleKind.LongerThan => track.Duration.TotalSeconds > rule.Value,
            SmartRuleKind.ShorterThan => track.Duration.TotalSeconds < rule.Value,
            SmartRuleKind.Explicit => track.IsExplicit,
            SmartRuleKind.NotExplicit => !track.IsExplicit,
            SmartRuleKind.ArtistIs => string.IsNullOrWhiteSpace(rule.Text) || HasArtist(track, rule.Text),
            SmartRuleKind.ArtistIsNot => string.IsNullOrWhiteSpace(rule.Text) || !HasArtist(track, rule.Text),
            SmartRuleKind.NotInPlaylist => rule.Text is null
                || !inputs.Playlists.TryGetValue(rule.Text, out var other)
                || track.Uri is not { } uri
                || !other.Contains(uri),
            _ => true,
        };
    }

    private static bool HasArtist(TrackInfo track, string name)
    {
        name = name.Trim();
        return track.ArtistRefs.Count > 0
            ? track.ArtistRefs.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            : track.Artists.Split(", ").Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<TrackInfo> Sort(List<TrackInfo> tracks, SmartOrder order, int seed, DateOnly day) => order switch
    {
        // OrderBy keeps the source's order among equals.
        SmartOrder.OldestSaved => tracks.OrderBy(t => t.AddedAt is null).ThenBy(t => t.AddedAt).ToList(),
        SmartOrder.NewestRelease => tracks.OrderBy(t => t.ReleaseYear is null).ThenByDescending(t => t.ReleaseYear).ToList(),
        SmartOrder.OldestRelease => tracks.OrderBy(t => t.ReleaseYear is null).ThenBy(t => t.ReleaseYear).ToList(),
        SmartOrder.Longest => tracks.OrderByDescending(t => t.Duration).ToList(),
        SmartOrder.Shortest => tracks.OrderBy(t => t.Duration).ToList(),
        SmartOrder.Random => tracks.OrderBy(t => Shuffle(t.Uri!, seed, day.DayNumber)).ToList(),
        _ => tracks,
    };

    /// <summary>A number that orders songs randomly, the same all day for the same playlist (FNV-1a).</summary>
    private static ulong Shuffle(string uri, int seed, int day)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in $"{seed}:{day}:{uri}")
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }
}

/// <summary>A smart playlist's rules in words, as the sentence of chips reads them.</summary>
public static class SmartPlaylistText
{
    /// <summary>A rule as the word before its chip and the chip itself: ("released", "before 2000").</summary>
    public static (string Lead, string Value) Describe(SmartRule rule)
    {
        var low = Math.Min(rule.Value, rule.Value2);
        var high = Math.Max(rule.Value, rule.Value2);
        var years = low == high ? Year(low) : $"{Year(low)}–{Year(high)}";
        return rule.Kind switch
        {
            SmartRuleKind.SavedInLastDays => ("saved", rule.Value == 1 ? "in the last day" : $"in the last {Number(rule.Value)} days"),
            SmartRuleKind.SavedInYears => ("saved", "in " + years),
            SmartRuleKind.ReleasedBefore => ("released", "before " + Year(rule.Value)),
            SmartRuleKind.ReleasedAfter => ("released", "after " + Year(rule.Value)),
            SmartRuleKind.ReleasedBetween => ("released", "in " + years),
            SmartRuleKind.LongerThan => ("longer than", Length(rule.Value)),
            SmartRuleKind.ShorterThan => ("shorter than", Length(rule.Value)),
            SmartRuleKind.Explicit => (string.Empty, "explicit"),
            SmartRuleKind.NotExplicit => (string.Empty, "not explicit"),
            SmartRuleKind.ArtistIs => ("by", string.IsNullOrWhiteSpace(rule.Text) ? "any artist" : rule.Text.Trim()),
            SmartRuleKind.ArtistIsNot => ("not by", string.IsNullOrWhiteSpace(rule.Text) ? "an artist" : rule.Text.Trim()),
            SmartRuleKind.NotInPlaylist => ("not in", rule.Label ?? "a playlist"),
            _ => (string.Empty, rule.Kind.ToString()),
        };
    }

    public static string Describe(SmartOrder order, bool fromLikedSongs) => order switch
    {
        SmartOrder.SourceOrder => fromLikedSongs ? "newest saved first" : "in playlist order",
        SmartOrder.OldestSaved => "oldest saved first",
        SmartOrder.NewestRelease => "newest releases first",
        SmartOrder.OldestRelease => "oldest releases first",
        SmartOrder.Longest => "longest first",
        SmartOrder.Shortest => "shortest first",
        SmartOrder.Random => "in random order",
        _ => order.ToString(),
    };

    public static string DescribeLimit(int? limit) => limit is > 0 and var count ? $"at most {Number(count)} songs" : "any number of songs";

    /// <summary>The source as its chip shows it.</summary>
    public static string Source(SmartPlaylist playlist) =>
        playlist.FromLikedSongs ? "Liked Songs" : playlist.SourceName ?? "a playlist";

    /// <summary>"Songs from Liked Songs, saved in 2023, released before 2000, longer than 5 min".</summary>
    public static string Sentence(SmartPlaylist playlist)
    {
        var parts = new List<string> { "Songs from " + Source(playlist) };
        foreach (var rule in playlist.Rules)
        {
            var (lead, value) = Describe(rule);
            parts.Add(lead.Length > 0 ? lead + " " + value : value);
        }

        return string.Join(", ", parts);
    }

    /// <summary>"5 min", or "4 min 30 sec".</summary>
    public static string Length(int seconds)
    {
        seconds = Math.Max(seconds, 0);
        var minutes = seconds / 60;
        var rest = seconds % 60;
        return rest == 0 ? $"{Number(minutes)} min" : minutes == 0 ? $"{Number(rest)} sec" : $"{Number(minutes)} min {Number(rest)} sec";
    }

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    // Years read best without a thousands separator.
    private static string Year(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Ready-made smart playlists to start from.</summary>
public static class SmartStarters
{
    public static IReadOnlyList<string> Names { get; } = ["Long ones", "Fresh this month", "The 90s"];

    /// <summary>Turns <paramref name="playlist"/> into the starter named <paramref name="name"/> (its source becomes Liked Songs).</summary>
    public static void Apply(SmartPlaylist playlist, string name)
    {
        playlist.Name = name;
        playlist.SourcePlaylistId = null;
        playlist.SourceName = null;
        playlist.Limit = null;
        playlist.Order = SmartOrder.SourceOrder;
        playlist.Rules = [];
        switch (name)
        {
            case "Long ones":
                playlist.Rules.Add(new SmartRule { Kind = SmartRuleKind.LongerThan, Value = 6 * 60 });
                break;
            case "Fresh this month":
                playlist.Rules.Add(new SmartRule { Kind = SmartRuleKind.SavedInLastDays, Value = 30 });
                break;
            case "The 90s":
                playlist.Rules.Add(new SmartRule { Kind = SmartRuleKind.ReleasedBetween, Value = 1990, Value2 = 1999 });
                playlist.Order = SmartOrder.OldestRelease;
                break;
        }
    }

    /// <summary>A new rule of the kind, with a sensible first value.</summary>
    public static SmartRule NewRule(SmartRuleKind kind, DateTimeOffset now) => kind switch
    {
        SmartRuleKind.SavedInLastDays => new() { Kind = kind, Value = 30 },
        SmartRuleKind.SavedInYears => new() { Kind = kind, Value = now.Year, Value2 = now.Year },
        SmartRuleKind.ReleasedBefore => new() { Kind = kind, Value = 2000 },
        SmartRuleKind.ReleasedAfter => new() { Kind = kind, Value = 2010 },
        SmartRuleKind.ReleasedBetween => new() { Kind = kind, Value = 1990, Value2 = 1999 },
        SmartRuleKind.LongerThan => new() { Kind = kind, Value = 5 * 60 },
        SmartRuleKind.ShorterThan => new() { Kind = kind, Value = 3 * 60 },
        _ => new() { Kind = kind },
    };
}
