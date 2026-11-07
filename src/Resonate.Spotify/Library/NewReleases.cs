using System.Globalization;
using System.Text.Json.Serialization;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>A release by an artist the user follows, as New releases on Home shows it.</summary>
public sealed class NewRelease
{
    /// <summary>The album's Spotify ID.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Uri { get; set; }

    /// <summary>"album" or "single", as Spotify says (an EP is a single to Spotify).</summary>
    public string? AlbumType { get; set; }

    public int? TotalTracks { get; set; }

    /// <summary>"2026-11-06", or "2026-11" where Spotify knows only the month.</summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>"Mira Sol, Lumen".</summary>
    public string Artists { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    /// <summary>The day it came out; <see cref="DateOnly.MinValue"/> when Spotify did not say.</summary>
    [JsonIgnore]
    public DateOnly Released => NewReleasesBuilder.ParseDate(ReleaseDate) ?? DateOnly.MinValue;

    /// <summary>"Album", "EP" or "Single".</summary>
    [JsonIgnore]
    public string Kind => AlbumType switch
    {
        // Spotify's own app calls a single of four songs or more an EP.
        "single" when TotalTracks >= 4 => "EP",
        "single" => "Single",
        _ => "Album",
    };
}

/// <summary>An artist the user follows, with what is known of their releases.</summary>
public sealed class FollowedArtist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>When their releases were last asked for; null until they were.</summary>
    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>The date of their newest release however old ("2019-04-12"): artists quiet for a year are asked less often.</summary>
    public string? Latest { get; set; }

    /// <summary>Their releases from the last weeks (see <see cref="NewReleasesBuilder.WindowDays"/>), as of <see cref="CheckedAt"/>.</summary>
    public List<NewRelease> Releases { get; set; } = [];
}

/// <summary>What New releases keeps on disk.</summary>
public sealed class NewReleasesFile
{
    /// <summary>The account the artists were read for; another account starts afresh.</summary>
    public string? UserId { get; set; }

    /// <summary>When the followed artists were last read in full.</summary>
    public DateTimeOffset? FollowedReadAt { get; set; }

    /// <summary>The followed artists, in the order Spotify lists them.</summary>
    public List<FollowedArtist> Artists { get; set; } = [];

    /// <summary>The local day <see cref="RequestsThatDay"/> counts.</summary>
    public DateOnly RequestDay { get; set; }

    public int RequestsThatDay { get; set; }
}

/// <summary>What Home shows of New releases.</summary>
public sealed class NewReleasesView
{
    public static readonly NewReleasesView Empty = new();

    /// <summary>Newest first, each release once.</summary>
    public IReadOnlyList<NewRelease> Releases { get; init; } = [];

    /// <summary>The sign-in predates the permission to read the followed artists: signing in again grants it.</summary>
    public bool NeedsSignIn { get; init; }

    /// <summary>Whether <paramref name="other"/> shows the same: the same releases in the same order.</summary>
    public bool SameAs(NewReleasesView other) =>
        NeedsSignIn == other.NeedsSignIn
        && Releases.Count == other.Releases.Count
        && Releases.Select(r => r.Id).SequenceEqual(other.Releases.Select(r => r.Id), StringComparer.Ordinal);
}

/// <summary>
/// The rules of New releases, kept apart from asking Spotify so they can be
/// tested: which releases count as new, which artists are due to be asked
/// about, and what Home shows.
/// </summary>
public static class NewReleasesBuilder
{
    /// <summary>A release from this many days back or fewer is new.</summary>
    public const int WindowDays = 56;

    /// <summary>The most releases Home's row has.</summary>
    public const int MaxShown = 40;

    /// <summary>
    /// The most requests New releases sends in a local day: the developer
    /// app's daily allowance is shared with everything else Resonate asks.
    /// </summary>
    public const int MaxRequestsPerDay = 150;

    /// <summary>Followed artists per request: all Spotify allows.</summary>
    public const int FollowedPageSize = 50;

    /// <summary>Pages of followed artists read at most (a thousand artists).</summary>
    public const int MaxFollowedPages = 20;

    /// <summary>Releases per request: all Spotify allows since February 2026.</summary>
    public const int ReleasesPageSize = SpotifyWebApi.MaxArtistAlbumsLimit;

    /// <summary>An artist with no release for this many days is quiet.</summary>
    public const int QuietAfterDays = 365;

    /// <summary>The followed artists are read again after this long (a little under a day, so a daily start finds them due).</summary>
    public static readonly TimeSpan FollowedEvery = TimeSpan.FromHours(20);

    /// <summary>An artist with a release in the past year is asked about again after this long.</summary>
    public static readonly TimeSpan ActiveEvery = TimeSpan.FromHours(20);

    /// <summary>A quiet artist is asked about again after this long.</summary>
    public static readonly TimeSpan QuietEvery = TimeSpan.FromDays(4);

    /// <summary>
    /// The day of a release date: "2026-11-06" as it is, "2026-11" as the
    /// month's first day. A year alone says too little and gives null.
    /// </summary>
    public static DateOnly? ParseDate(string? date)
    {
        if (date is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        return DateOnly.TryParseExact(date, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month
            : null;
    }

    /// <summary>Whether a release from <paramref name="released"/> is new on <paramref name="today"/> (a day ahead counts, for time zones).</summary>
    public static bool IsNew(DateOnly released, DateOnly today) =>
        released.DayNumber >= today.DayNumber - WindowDays && released.DayNumber <= today.DayNumber + 1;

    /// <summary>How long after its last check an artist is asked about again.</summary>
    public static TimeSpan CheckEvery(FollowedArtist artist, DateOnly today) =>
        ParseDate(artist.Latest) is { } latest && latest.DayNumber >= today.DayNumber - QuietAfterDays
            ? ActiveEvery
            : QuietEvery;

    /// <summary>The artists to ask about now: those never asked first, then the longest ago, each in Spotify's order.</summary>
    public static List<FollowedArtist> Due(IEnumerable<FollowedArtist> artists, DateTimeOffset now, DateOnly today) =>
        artists
            .Select((artist, index) => (Artist: artist, Index: index))
            .Where(a => a.Artist.CheckedAt is not { } checkedAt || now - checkedAt >= CheckEvery(a.Artist, today))
            .OrderBy(a => a.Artist.CheckedAt ?? DateTimeOffset.MinValue)
            .ThenBy(a => a.Index)
            .Select(a => a.Artist)
            .ToList();

    /// <summary>
    /// Whether an artist's singles need a request of their own: Spotify lists
    /// albums before singles, so a full page of albums with more to come hides them.
    /// </summary>
    public static bool NeedsSingles(Page<SimplifiedAlbum> page) =>
        page.Items.Count >= ReleasesPageSize
        && (page.HasMore || page.Total > page.Offset + page.Items.Count)
        && !page.Items.Any(a => a?.AlbumType == "single");

    /// <summary>An artist's releases as Spotify listed them: the new ones, and the date of the newest of all.</summary>
    public static (List<NewRelease> Recent, string? Latest) Read(IEnumerable<SimplifiedAlbum?> albums, DateOnly today)
    {
        var recent = new List<NewRelease>();
        DateOnly? newest = null;
        string? latest = null;
        foreach (var album in albums)
        {
            if (album is not { Id: { Length: > 0 } id } || album.AlbumType == "compilation")
            {
                continue;
            }

            var date = ParseDate(album.ReleaseDate);
            if (date is { } known && (newest is null || known > newest))
            {
                newest = known;
                latest = album.ReleaseDate;
            }

            if (date is not { } released || !IsNew(released, today) || recent.Any(r => r.Id == id))
            {
                continue;
            }

            recent.Add(new NewRelease
            {
                Id = id,
                Name = album.Name,
                Uri = album.Uri,
                AlbumType = album.AlbumType,
                TotalTracks = album.TotalTracks,
                ReleaseDate = album.ReleaseDate!,
                Artists = string.Join(", ", album.Artists?.Select(a => a.Name).Where(n => n.Length > 0) ?? []),
                ImageUrl = ImagePicker.Pick(album.Images, 300),
            });
        }

        return (recent, latest);
    }

    /// <summary>
    /// What Home shows: the new releases of every followed artist, newest
    /// first (albums before singles on the same day), each once. A release
    /// by two followed artists, or listed twice by Spotify (a clean and an
    /// explicit version), shows once.
    /// </summary>
    public static IReadOnlyList<NewRelease> Pick(IEnumerable<FollowedArtist> artists, DateOnly today)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shown = new List<NewRelease>();
        var all = artists
            .SelectMany(a => a.Releases)
            .Where(r => r.Released != DateOnly.MinValue && IsNew(r.Released, today))
            .OrderByDescending(r => r.Released)
            .ThenBy(r => r.AlbumType == "album" ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal);
        foreach (var release in all)
        {
            if (!ids.Add(release.Id) || !names.Add($"{release.Name.Trim()}\n{release.Artists}\n{release.ReleaseDate}"))
            {
                continue;
            }

            shown.Add(release);
            if (shown.Count == MaxShown)
            {
                break;
            }
        }

        return shown;
    }

    /// <summary>"Today", "Yesterday", "3 days ago", "Last week", "5 weeks ago".</summary>
    public static string When(DateOnly released, DateOnly today)
    {
        var days = today.DayNumber - released.DayNumber;
        return days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => $"{days} days ago",
            < 14 => "Last week",
            _ => $"{days / 7} weeks ago",
        };
    }

    /// <summary>The line under a release on Home: "Single · 3 days ago".</summary>
    public static string Caption(NewRelease release, DateOnly today) => $"{release.Kind} · {When(release.Released, today)}";

    /// <summary>
    /// The followed artists read just now, in Spotify's order, keeping what
    /// was known of each. Artists no longer followed are dropped with their
    /// releases, unless <paramref name="complete"/> is false (the list was not
    /// read to its end), when every known artist stays.
    /// </summary>
    public static List<FollowedArtist> Merge(IReadOnlyList<FollowedArtist> known, IEnumerable<Artist> followed, bool complete)
    {
        var byId = new Dictionary<string, FollowedArtist>(StringComparer.Ordinal);
        foreach (var artist in known)
        {
            byId.TryAdd(artist.Id, artist);
        }

        var merged = new List<FollowedArtist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artist in followed)
        {
            if (artist.Id.Length == 0 || !seen.Add(artist.Id))
            {
                continue;
            }

            if (byId.TryGetValue(artist.Id, out var kept))
            {
                kept.Name = artist.Name;
                merged.Add(kept);
            }
            else
            {
                merged.Add(new FollowedArtist { Id = artist.Id, Name = artist.Name });
            }
        }

        if (!complete)
        {
            merged.AddRange(known.Where(k => seen.Add(k.Id)));
        }

        return merged;
    }
}
