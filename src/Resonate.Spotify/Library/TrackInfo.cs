using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>A song (or episode) as the interface shows it in a list.</summary>
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
    public static TrackInfo? From(PlayableItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var images = item.Album?.Images ?? item.Images;
        var artists = item.Artists is { Count: > 0 }
            ? string.Join(", ", item.Artists.Select(a => a.Name))
            : item.Show?.Name ?? string.Empty;

        return new TrackInfo(
            item.Uri,
            item.Name,
            artists,
            item.Album?.Name ?? item.Show?.Name ?? string.Empty,
            item.Album?.Uri,
            TimeSpan.FromMilliseconds(item.DurationMs),
            ImagePicker.Pick(images, 64),
            ImagePicker.Pick(images, 300),
            item.Explicit,
            // Local files can not be started through the Web API.
            !item.IsLocal && item.Uri is not null && item.IsPlayable != false);
    }
}

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
