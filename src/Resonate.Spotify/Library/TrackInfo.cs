using System.Globalization;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>A song (or episode) as the interface shows it in a list.</summary>
/// <param name="IsPlayable">
/// It can be started on its own by its URI. Local files in a playlist can
/// not (Spotify refuses their URIs), but they still play inside their
/// playlist, by position; see <see cref="IsLocal"/> and <see cref="Position"/>.
/// </param>
public sealed record TrackInfo(
    string? Uri,
    string Title,
    string Artists,
    string Album,
    string? AlbumUri,
    TimeSpan Duration,
    string? SmallImageUrl,
    string? LargeImageUrl,
    bool IsExplicit,
    bool IsPlayable)
{
    /// <summary>Spotify's ID for the song, when it has one (local files do not).</summary>
    public string? Id { get; init; }

    /// <summary>A file on the user's computer that was added to a Spotify playlist.</summary>
    public bool IsLocal { get; init; }

    /// <summary>
    /// The full path of a music file in Local Files, which Resonate plays
    /// itself (Spotify's audio never is). Null for everything from Spotify.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>When it was added to the playlist or to Liked Songs.</summary>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>Where it sits in its playlist, album or list (0 for the first), when known.</summary>
    public int? Position { get; init; }

    /// <summary>The artists one by one, for "go to artist".</summary>
    public IReadOnlyList<ArtistRef> ArtistRefs { get; init; } = [];

    public string? AlbumId { get; init; }

    public int? TrackNumber { get; init; }

    public int? DiscNumber { get; init; }

    /// <summary>The cover at about 640 pixels, for the large now-playing views; null when Spotify has none that big.</summary>
    public string? FullImageUrl { get; init; }

    /// <summary>The first artist's name, for grouping and statistics.</summary>
    public string PrimaryArtist => ArtistRefs.Count > 0 ? ArtistRefs[0].Name : Artists;

    /// <param name="album">The album, for songs listed on an album's page (they carry none of their own).</param>
    public static TrackInfo? From(PlayableItem? item, DateTimeOffset? addedAt = null, int? position = null, SimplifiedAlbum? album = null)
    {
        if (item is null)
        {
            return null;
        }

        album ??= item.Album;
        var images = album?.Images ?? item.Images;
        var artists = item.Artists is { Count: > 0 }
            ? string.Join(", ", item.Artists.Select(a => a.Name))
            : item.Show?.Name ?? string.Empty;

        return new TrackInfo(
            item.Uri,
            item.Name,
            artists,
            album?.Name ?? item.Show?.Name ?? string.Empty,
            album?.Uri,
            TimeSpan.FromMilliseconds(item.DurationMs),
            ImagePicker.Pick(images, 64),
            ImagePicker.Pick(images, 300),
            item.Explicit,
            // Local files can not be started through the Web API by their URI.
            !item.IsLocal && item.Uri is not null && item.IsPlayable != false)
        {
            Id = item.Id,
            IsLocal = item.IsLocal || (item.Uri?.StartsWith("spotify:local:", StringComparison.Ordinal) ?? false),
            AddedAt = addedAt,
            Position = position,
            ArtistRefs = item.Artists?.Select(a => new ArtistRef(a.Name, a.Id)).ToList() ?? [],
            AlbumId = album?.Id,
            TrackNumber = item.TrackNumber,
            DiscNumber = item.DiscNumber,
            FullImageUrl = ImagePicker.Pick(images, 640),
        };
    }

    /// <summary>Reads Spotify's "added_at" (ISO 8601); null when missing or unreadable.</summary>
    public static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;
}

/// <summary>One artist of a song.</summary>
public sealed record ArtistRef(string Name, string? Id);

public static class ImagePicker
{
    /// <summary>
    /// The smallest image at least <paramref name="minimumSize"/> pixels wide,
    /// or the largest one when none is that big. Spotify lists images largest
    /// first, but this does not rely on it.
    /// </summary>
    public static string? Pick(IReadOnlyList<SpotifyImage>? images, int minimumSize)
    {
        if (images is null || images.Count == 0)
        {
            return null;
        }

        SpotifyImage? best = null;
        foreach (var image in images)
        {
            var width = image.Width ?? int.MaxValue;
            if (width >= minimumSize && (best is null || width < (best.Width ?? int.MaxValue)))
            {
                best = image;
            }
        }

        best ??= images.MaxBy(i => i.Width ?? 0);
        return best?.Url;
    }
}
