using System.Globalization;
using Resonate.Spotify.Library;

namespace Resonate.Spotify.History;

/// <summary>What a card in Rediscover's row on Home brings back.</summary>
public enum RediscoverKind
{
    /// <summary>A song liked a whole number of years ago this week.</summary>
    OnThisDay,

    /// <summary>An album with liked songs that was released on this day years ago.</summary>
    AlbumBirthday,

    /// <summary>A song liked long ago and not played for months.</summary>
    GatheringDust,

    /// <summary>An album with several liked songs, and songs on it not liked yet.</summary>
    DeepCut,
}

/// <summary>One card in Rediscover's row.</summary>
/// <param name="Title">The song's title or the album's name.</param>
/// <param name="Subtitle">The artists.</param>
/// <param name="Note">Why it is here: "Liked 3 years ago today", "Liked March 2022", "4 songs you haven't liked yet".</param>
/// <param name="Track">The song, for song cards; null for albums.</param>
public sealed record RediscoverCard(
    RediscoverKind Kind,
    string Title,
    string Subtitle,
    string Note,
    string? ImageUrl,
    string? AlbumId,
    TrackInfo? Track)
{
    /// <summary>The songs a click plays: the dusty songs from this one on, or an album's songs not liked yet in album order.</summary>
    public IReadOnlyList<TrackInfo> Tracks { get; init; } = [];

    /// <summary>The eyebrow over the card: "ON THIS DAY", "GATHERING DUST" or "DEEP CUTS".</summary>
    public string Label => Kind switch
    {
        RediscoverKind.GatheringDust => "GATHERING DUST",
        RediscoverKind.DeepCut => "DEEP CUTS",
        _ => "ON THIS DAY",
    };
}

/// <summary>Rediscover's row for one day.</summary>
public sealed class RediscoverPicks
{
    public static readonly RediscoverPicks Empty = new();

    public DateOnly Day { get; init; }

    /// <summary>The cards in the order the row shows them.</summary>
    public IReadOnlyList<RediscoverCard> Cards { get; init; } = [];

    /// <summary>Whether the history reaches back far enough to tell which songs have not been played in months.</summary>
    public bool DustReady { get; init; }
}

/// <summary>An album's songs as Spotify listed them, kept so each album is asked for about once a month.</summary>
public sealed class RediscoverAlbum
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Uri { get; set; }

    public string Artists { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    /// <summary>"1997", "1997-05" or "1997-05-21".</summary>
    public string? ReleaseDate { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public List<TrackInfo> Tracks { get; set; } = [];
}

/// <summary>What <see cref="RediscoverBuilder"/> works from; all of it is already on this computer.</summary>
/// <param name="Liked">Liked Songs, with when each was liked.</param>
/// <param name="History">Resonate's listening history (about 90 days, as far as it has polled).</param>
/// <param name="TopUris">Spotify's own top songs: a guard while the history is still short.</param>
/// <param name="Albums">Albums already asked for, by ID.</param>
public sealed record RediscoverInput(
    IReadOnlyList<TrackInfo> Liked,
    IReadOnlyList<PlayRecord> History,
    IReadOnlyCollection<string> TopUris,
    IReadOnlyDictionary<string, RediscoverAlbum> Albums,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    CultureInfo Culture);

/// <summary>
/// Picks Rediscover's cards: songs liked on this day years ago and albums
/// released on it, songs liked long ago that have not been played for months
/// ("Gathering dust"), and favourite albums with songs not liked yet ("Deep
/// cuts"). Each pick is ranked by a hash of the day and the song or album, so
/// the row stays the same all day, a song played meanwhile leaves it without
/// reshuffling the rest, and it changes the next day.
/// </summary>
public static class RediscoverBuilder
{
    /// <summary>A song liked this many days either side of today's date, years ago, is "on this day".</summary>
    public const int AnniversaryWindowDays = 3;

    public const int MaxAnniversaries = 2;

    public const int MaxBirthdays = 1;

    /// <summary>The longest dusty list a click plays.</summary>
    public const int DustSize = 30;

    public const int MaxDustCards = 6;

    /// <summary>An album needs this many liked songs to count as a favourite.</summary>
    public const int DeepCutMinLiked = 3;

    public const int MaxDeepCuts = 6;

    /// <summary>Albums asked of Spotify per day at most (single GET /albums/{id} requests).</summary>
    public const int AlbumFetchesPerDay = 6;

    /// <summary>How many albums' songs are kept at most.</summary>
    public const int MaxStoredAlbums = 200;

    /// <summary>A song liked at least this long ago can gather dust.</summary>
    public static readonly TimeSpan DustAge = TimeSpan.FromDays(180);

    /// <summary>
    /// The history only fills while Resonate runs, so "not played for months"
    /// means nothing until it reaches back at least this far.
    /// </summary>
    public static readonly TimeSpan DustNeedsHistory = TimeSpan.FromDays(21);

    /// <summary>Until the history reaches back this far, Spotify's own top songs are left out of the dust too.</summary>
    public static readonly TimeSpan DustTrustsHistory = TimeSpan.FromDays(60);

    /// <summary>An album's stored songs are asked for again after this long.</summary>
    public static readonly TimeSpan AlbumRefreshAfter = TimeSpan.FromDays(45);

    public static RediscoverPicks Build(RediscoverInput input)
    {
        var today = LocalDay(input.Now, input.Zone);
        var liked = input.Liked.Where(IsUsable).DistinctBy(t => t.Uri).ToList();
        var anniversaries = Anniversaries(liked, today, input.Zone);
        var birthdays = Birthdays(liked, input.Albums, today);
        var onThisDay = anniversaries.Select(a => a.Track.Uri!).ToHashSet(StringComparer.Ordinal);
        var dust = GatheringDust(input with { Liked = liked.Where(t => !onThisDay.Contains(t.Uri!)).ToList() }, out var dustReady);
        var deepCuts = DeepCuts(liked, input.Albums, today);

        var cards = new List<RediscoverCard>();
        foreach (var (track, years, days) in anniversaries)
        {
            var when = days == 0 ? "today" : "this week";
            cards.Add(new RediscoverCard(RediscoverKind.OnThisDay, track.Title, track.Artists, $"Liked {YearsAgo(years)} {when}", track.LargeImageUrl, track.AlbumId, track)
            {
                Tracks = [track],
            });
        }

        cards.AddRange(birthdays);

        var dustCards = dust.Take(MaxDustCards)
            .Select((track, i) => new RediscoverCard(
                RediscoverKind.GatheringDust,
                track.Title,
                track.Artists,
                "Liked " + LocalDay(track.AddedAt!.Value, input.Zone).ToString("MMMM yyyy", input.Culture),
                track.LargeImageUrl,
                track.AlbumId,
                track)
            {
                // From this song on, then round to the ones before it.
                Tracks = [.. dust.Skip(i), .. dust.Take(i)],
            })
            .ToList();
        cards.AddRange(Interleave(dustCards, deepCuts));
        return new RediscoverPicks { Day = today, Cards = cards, DustReady = dustReady };
    }

    /// <summary>
    /// The albums to ask Spotify for next: favourites never asked for (most
    /// liked songs first), then those asked for longest ago, at most
    /// <paramref name="budget"/>.
    /// </summary>
    public static List<string> AlbumsToFetch(IReadOnlyList<TrackInfo> liked, IReadOnlyDictionary<string, RediscoverAlbum> albums, DateTimeOffset now, int budget)
    {
        if (budget <= 0)
        {
            return [];
        }

        var favourites = Favourites(liked.Where(IsUsable).DistinctBy(t => t.Uri));
        var missing = favourites
            .Where(pair => !albums.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key);
        var stale = favourites
            .Where(pair => albums.TryGetValue(pair.Key, out var album) && now - album.FetchedAt > AlbumRefreshAfter)
            .OrderBy(pair => albums[pair.Key].FetchedAt)
            .Select(pair => pair.Key);
        return missing.Concat(stale).Take(budget).ToList();
    }

    /// <summary>The stored albums worth keeping: favourites only, at most <see cref="MaxStoredAlbums"/>, most liked first.</summary>
    public static List<RediscoverAlbum> AlbumsToKeep(IReadOnlyList<TrackInfo> liked, IEnumerable<RediscoverAlbum> albums)
    {
        var favourites = Favourites(liked.Where(IsUsable).DistinctBy(t => t.Uri));
        return albums
            .Where(a => favourites.ContainsKey(a.Id))
            .OrderByDescending(a => favourites[a.Id])
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Take(MaxStoredAlbums)
            .ToList();
    }

    /// <summary>"a year ago", "3 years ago".</summary>
    public static string YearsAgo(int years) => years == 1 ? "a year ago" : $"{years} years ago";

    /// <summary>Liked songs whose day of liking comes round this week, a year or more later; nearest first, round numbers before others.</summary>
    internal static List<(TrackInfo Track, int Years, int Days)> Anniversaries(IReadOnlyList<TrackInfo> liked, DateOnly today, TimeZoneInfo zone)
    {
        var found = new List<(TrackInfo Track, int Years, int Days)>();
        foreach (var track in liked)
        {
            if (track.AddedAt is { } added && Anniversary(LocalDay(added, zone), today, AnniversaryWindowDays) is { } match)
            {
                found.Add((track, match.Years, match.Days));
            }
        }

        var key = DailyRandom.DayKey(today);
        return found
            .OrderBy(f => Math.Abs(f.Days))
            .ThenBy(f => f.Years % 5 == 0 ? 0 : 1)
            .ThenBy(f => DailyRandom.Hash(key + "|" + f.Track.Uri))
            .DistinctBy(f => f.Track.AlbumId ?? f.Track.Uri)
            .Take(MaxAnniversaries)
            .ToList();
    }

    /// <summary>
    /// When <paramref name="date"/> comes round within <paramref name="window"/>
    /// days of <paramref name="today"/>, at least a year later: how many years,
    /// and how many days from today (negative when it has passed). 29 February
    /// comes round on 28 February in other years.
    /// </summary>
    internal static (int Years, int Days)? Anniversary(DateOnly date, DateOnly today, int window)
    {
        for (var year = today.Year - 1; year <= today.Year + 1; year++)
        {
            var years = year - date.Year;
            if (years < 1)
            {
                continue;
            }

            var day = new DateOnly(year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month)));
            var days = day.DayNumber - today.DayNumber;
            if (Math.Abs(days) <= window)
            {
                return (years, days);
            }
        }

        return null;
    }

    /// <summary>A release date given to the day ("1997-05-21"); null for "1997" or "1997-05".</summary>
    internal static DateOnly? ExactDate(string? releaseDate) =>
        releaseDate is { Length: 10 } && DateOnly.TryParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>Albums with liked songs released on this day, a year or more ago; round numbers first, then the most liked.</summary>
    internal static List<RediscoverCard> Birthdays(IReadOnlyList<TrackInfo> liked, IReadOnlyDictionary<string, RediscoverAlbum> albums, DateOnly today)
    {
        var key = DailyRandom.DayKey(today);
        return liked
            .Where(t => t.AlbumId is not null)
            .GroupBy(t => t.AlbumId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                albums.TryGetValue(group.Key, out var stored);
                var released = ExactDate(group.Select(t => t.ReleaseDate).FirstOrDefault(d => d is not null) ?? stored?.ReleaseDate);
                var match = released is { } date ? Anniversary(date, today, 0) : null;
                return (Song: first, Stored: stored, Liked: group.Count(), Years: match?.Years ?? 0);
            })
            .Where(a => a.Years > 0)
            .OrderBy(a => a.Years % 10 == 0 ? 0 : a.Years % 5 == 0 ? 1 : 2)
            .ThenByDescending(a => a.Liked)
            .ThenBy(a => DailyRandom.Hash(key + "|" + a.Song.AlbumId))
            .Take(MaxBirthdays)
            .Select(a => new RediscoverCard(
                RediscoverKind.AlbumBirthday,
                a.Song.Album,
                a.Stored?.Artists is { Length: > 0 } artists ? artists : a.Song.PrimaryArtist,
                a.Years == 1 ? "Released a year ago today" : $"Turns {a.Years} today",
                a.Song.LargeImageUrl ?? a.Stored?.ImageUrl,
                a.Song.AlbumId,
                null))
            .ToList();
    }

    /// <summary>
    /// Songs liked at least <see cref="DustAge"/> ago that the history has
    /// not heard; none until the history reaches back <see cref="DustNeedsHistory"/>,
    /// and Spotify's top songs left out until it reaches back <see cref="DustTrustsHistory"/>.
    /// </summary>
    internal static List<TrackInfo> GatheringDust(RediscoverInput input, out bool ready)
    {
        var history = input.History;
        var reaches = history.Count == 0 ? TimeSpan.Zero : input.Now - history.Min(p => p.PlayedAt);
        ready = reaches >= DustNeedsHistory;
        if (!ready)
        {
            return [];
        }

        var played = history.Select(p => p.Uri).ToHashSet(StringComparer.Ordinal);

        // The same song can come back under another address (another release of it).
        var playedNames = history.Select(p => SongKey(p.Title, p.Artists.Count > 0 ? p.Artists[0].Name : string.Empty)).ToHashSet(StringComparer.Ordinal);
        var guard = reaches < DustTrustsHistory ? input.TopUris : [];
        var key = DailyRandom.DayKey(LocalDay(input.Now, input.Zone));
        return input.Liked
            .Where(t => t.AddedAt is { } added && input.Now - added >= DustAge)
            .Where(t => !played.Contains(t.Uri!) && !playedNames.Contains(SongKey(t.Title, t.PrimaryArtist)) && !guard.Contains(t.Uri!))
            .OrderBy(t => DailyRandom.Hash(key + "|" + t.Uri))
            .Take(DustSize)
            .ToList();
    }

    /// <summary>Favourite albums (see <see cref="DeepCutMinLiked"/>) that were asked for and have songs not liked yet, in the day's order.</summary>
    internal static List<RediscoverCard> DeepCuts(IReadOnlyList<TrackInfo> liked, IReadOnlyDictionary<string, RediscoverAlbum> albums, DateOnly today)
    {
        var likedUris = liked.Select(t => t.Uri!).ToHashSet(StringComparer.Ordinal);
        var likedIds = liked.Select(t => t.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var likedNames = liked.Select(t => SongKey(t.Title, t.PrimaryArtist)).ToHashSet(StringComparer.Ordinal);
        var key = DailyRandom.DayKey(today);
        var cards = new List<RediscoverCard>();
        foreach (var (albumId, _) in Favourites(liked).OrderBy(pair => DailyRandom.Hash(key + "|" + pair.Key)))
        {
            if (cards.Count == MaxDeepCuts)
            {
                break;
            }

            if (!albums.TryGetValue(albumId, out var album))
            {
                continue;
            }

            var missing = album.Tracks
                .Where(t => IsUsable(t) && !likedUris.Contains(t.Uri!) && (t.Id is null || !likedIds.Contains(t.Id)) && !likedNames.Contains(SongKey(t.Title, t.PrimaryArtist)))
                .ToList();
            if (missing.Count == 0)
            {
                continue;
            }

            var cover = album.ImageUrl ?? liked.FirstOrDefault(t => t.AlbumId == albumId)?.LargeImageUrl;
            var note = missing.Count == 1 ? "1 song you haven't liked yet" : $"{missing.Count} songs you haven't liked yet";
            cards.Add(new RediscoverCard(RediscoverKind.DeepCut, album.Name, album.Artists, note, cover, albumId, null) { Tracks = missing });
        }

        return cards;
    }

    /// <summary>Albums with at least <see cref="DeepCutMinLiked"/> liked songs, with how many.</summary>
    private static Dictionary<string, int> Favourites(IEnumerable<TrackInfo> liked) =>
        liked
            .Where(t => t.AlbumId is not null)
            .GroupBy(t => t.AlbumId!, StringComparer.Ordinal)
            .Where(g => g.Count() >= DeepCutMinLiked)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static IEnumerable<RediscoverCard> Interleave(List<RediscoverCard> first, List<RediscoverCard> second)
    {
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                yield return first[i];
            }

            if (i < second.Count)
            {
                yield return second[i];
            }
        }
    }

    private static string SongKey(string title, string artist) =>
        title.Trim().ToUpperInvariant() + "\n" + artist.Trim().ToUpperInvariant();

    private static DateOnly LocalDay(DateTimeOffset time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);

    private static bool IsUsable(TrackInfo track) =>
        track.IsPlayable && track.Uri is not null && track.FilePath is null && !track.IsLocal;
}
