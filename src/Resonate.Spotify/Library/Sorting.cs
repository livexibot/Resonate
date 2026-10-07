using System.Globalization;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>What a song list is sorted by.</summary>
public enum TrackSortField
{
    /// <summary>The list's own order (the playlist as its owner arranged it).</summary>
    Custom,
    Title,
    Artist,
    Album,
    DateAdded,
    Duration,
}

/// <summary>A sort order for a song list; <see cref="Default"/> keeps the list's own order.</summary>
public readonly record struct TrackSort(TrackSortField Field, bool Descending)
{
    public static readonly TrackSort Default = new(TrackSortField.Custom, false);

    public bool IsDefault => Field == TrackSortField.Custom && !Descending;

    /// <summary>For settings files, such as "artist" or "dateadded-desc".</summary>
    public string Serialize() => Field.ToString().ToLowerInvariant() + (Descending ? "-desc" : string.Empty);

    public static TrackSort Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Default;
        }

        var descending = text.EndsWith("-desc", StringComparison.Ordinal);
        var name = descending ? text[..^5] : text;
        return Enum.TryParse<TrackSortField>(name, ignoreCase: true, out var field) ? new TrackSort(field, descending) : Default;
    }
}

/// <summary>Sorting and filtering song lists the way the interface shows them.</summary>
public static class TrackSorter
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions TextOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    /// <summary>A sorted copy. Ties keep the list's own order, so sorting is stable.</summary>
    public static List<TrackInfo> Apply(IEnumerable<TrackInfo> tracks, TrackSort sort)
    {
        var indexed = tracks.Select((t, i) => (Track: t, Index: t.Position ?? i)).ToList();
        Comparison<(TrackInfo Track, int Index)> compare = sort.Field switch
        {
            TrackSortField.Title => (a, b) => Text(a.Track.Title, b.Track.Title),
            TrackSortField.Artist => (a, b) => FirstNonZero(
                Text(a.Track.PrimaryArtist, b.Track.PrimaryArtist),
                Text(a.Track.Album, b.Track.Album),
                Nullable(a.Track.DiscNumber, b.Track.DiscNumber),
                Nullable(a.Track.TrackNumber, b.Track.TrackNumber)),
            TrackSortField.Album => (a, b) => FirstNonZero(
                Text(a.Track.Album, b.Track.Album),
                Nullable(a.Track.DiscNumber, b.Track.DiscNumber),
                Nullable(a.Track.TrackNumber, b.Track.TrackNumber)),
            TrackSortField.DateAdded => (a, b) => Nullable(a.Track.AddedAt, b.Track.AddedAt),
            TrackSortField.Duration => (a, b) => a.Track.Duration.CompareTo(b.Track.Duration),
            _ => (_, _) => 0,
        };

        indexed.Sort((a, b) =>
        {
            var result = compare(a, b);
            if (sort.Descending)
            {
                result = -result;
            }

            return result != 0 ? result : a.Index.CompareTo(b.Index);
        });
        return indexed.Select(x => x.Track).ToList();
    }

    /// <summary>Whether the song matches a filter typed by the user (title, artist or album; case and accents ignored).</summary>
    public static bool Matches(TrackInfo track, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Compare.IndexOf(track.Title, word, TextOptions) < 0
                && Compare.IndexOf(track.Artists, word, TextOptions) < 0
                && Compare.IndexOf(track.Album, word, TextOptions) < 0)
            {
                return false;
            }
        }

        return true;
    }

    internal static int Text(string? a, string? b) => Compare.Compare(a ?? string.Empty, b ?? string.Empty, TextOptions);

    private static int Nullable<T>(T? a, T? b)
        where T : struct, IComparable<T> =>
        (a, b) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => a.Value.CompareTo(b.Value),
        };

    private static int FirstNonZero(params ReadOnlySpan<int> results)
    {
        foreach (var result in results)
        {
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}

/// <summary>How the sidebar orders playlists.</summary>
public enum PlaylistSortMode
{
    /// <summary>As Spotify lists them (newest in the library first).</summary>
    Spotify,

    /// <summary>The user's own order, arranged by dragging; new playlists go on top.</summary>
    Custom,
    Alphabetical,
    Creator,

    /// <summary>The ones played most recently in Resonate first.</summary>
    RecentlyPlayed,
}

public static class PlaylistSorter
{
    /// <param name="customOrder">Playlist IDs in the user's order (for <see cref="PlaylistSortMode.Custom"/>).</param>
    /// <param name="lastPlayed">When each playlist (by ID) was last played (for <see cref="PlaylistSortMode.RecentlyPlayed"/>).</param>
    public static List<SimplifiedPlaylist> Apply(
        IReadOnlyList<SimplifiedPlaylist> playlists,
        PlaylistSortMode mode,
        IReadOnlyList<string>? customOrder = null,
        IReadOnlyDictionary<string, DateTimeOffset>? lastPlayed = null)
    {
        var indexed = playlists.Select((p, i) => (Playlist: p, Index: i)).ToList();
        switch (mode)
        {
            case PlaylistSortMode.Custom:
                var rank = new Dictionary<string, int>();
                for (var i = 0; i < (customOrder?.Count ?? 0); i++)
                {
                    rank.TryAdd(customOrder![i], i);
                }

                // Playlists not placed yet (new ones) come first, in Spotify's order.
                indexed.Sort((a, b) =>
                {
                    var ra = rank.TryGetValue(a.Playlist.Id, out var x) ? x : -1;
                    var rb = rank.TryGetValue(b.Playlist.Id, out var y) ? y : -1;
                    return ra != rb ? ra.CompareTo(rb) : a.Index.CompareTo(b.Index);
                });
                break;
            case PlaylistSortMode.Alphabetical:
                indexed.Sort((a, b) => Tie(TrackSorter.Text(a.Playlist.Name, b.Playlist.Name), a.Index, b.Index));
                break;
            case PlaylistSortMode.Creator:
                indexed.Sort((a, b) => Tie(
                    TrackSorter.Text(Creator(a.Playlist), Creator(b.Playlist)) is var byCreator and not 0
                        ? byCreator
                        : TrackSorter.Text(a.Playlist.Name, b.Playlist.Name),
                    a.Index,
                    b.Index));
                break;
            case PlaylistSortMode.RecentlyPlayed:
                indexed.Sort((a, b) =>
                {
                    var ta = lastPlayed is not null && lastPlayed.TryGetValue(a.Playlist.Id, out var x) ? x : DateTimeOffset.MinValue;
                    var tb = lastPlayed is not null && lastPlayed.TryGetValue(b.Playlist.Id, out var y) ? y : DateTimeOffset.MinValue;
                    return Tie(tb.CompareTo(ta), a.Index, b.Index);
                });
                break;
        }

        return indexed.Select(x => x.Playlist).ToList();
    }

    /// <summary>The custom order after the user dragged <paramref name="playlistId"/> to <paramref name="newIndex"/> in <paramref name="shown"/>.</summary>
    public static List<string> Move(IReadOnlyList<string> shown, string playlistId, int newIndex)
    {
        var order = shown.Where(id => id != playlistId).ToList();
        order.Insert(Math.Clamp(newIndex, 0, order.Count), playlistId);
        return order;
    }

    private static string Creator(SimplifiedPlaylist playlist) => playlist.Owner?.DisplayName ?? playlist.Owner?.Id ?? string.Empty;

    private static int Tie(int result, int a, int b) => result != 0 ? result : a.CompareTo(b);
}
