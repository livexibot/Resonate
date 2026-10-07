using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.History;

/// <summary>
/// One song the user played, as Spotify's listening history reported it.
/// Kept small (one picture link, no lists of images) because months of
/// plays are stored.
/// </summary>
public sealed class PlayRecord
{
    public string Uri { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<ArtistRef> Artists { get; set; } = [];

    public string Album { get; set; } = string.Empty;

    public string? AlbumUri { get; set; }

    /// <summary>The album cover, about 300 pixels wide.</summary>
    public string? ImageUrl { get; set; }

    public int DurationMs { get; set; }

    /// <summary>When Spotify counted the play (a song counts once it played for 30 seconds).</summary>
    public DateTimeOffset PlayedAt { get; set; }

    /// <summary>The playlist, album or artist it was played from, when Spotify said.</summary>
    public string? ContextUri { get; set; }

    /// <summary>A play from GET /me/player/recently-played; null when it lacks a song or a time.</summary>
    public static PlayRecord? From(PlayHistoryItem? item)
    {
        if (item?.Track is not { Uri: { } uri } track || TrackInfo.ParseTime(item.PlayedAt) is not { } playedAt)
        {
            return null;
        }

        var album = track.Album;
        return new PlayRecord
        {
            Uri = uri,
            Title = track.Name,
            Artists = track.Artists?.Select(a => new ArtistRef(a.Name, a.Id)).ToList() ?? [],
            Album = album?.Name ?? string.Empty,
            AlbumUri = album?.Uri,
            ImageUrl = ImagePicker.Pick(album?.Images ?? track.Images, 300),
            DurationMs = track.DurationMs,
            PlayedAt = playedAt,
            ContextUri = item.Context?.Uri,
        };
    }

    /// <summary>The song as lists show it, so it can be played, liked or added to a playlist.</summary>
    public TrackInfo ToTrack()
    {
        var isLocal = Uri.StartsWith("spotify:local:", StringComparison.Ordinal);
        return new TrackInfo(
            Uri,
            Title,
            string.Join(", ", Artists.Select(a => a.Name)),
            Album,
            AlbumUri,
            TimeSpan.FromMilliseconds(DurationMs),
            ImageUrl,
            ImageUrl,
            IsExplicit: false,
            IsPlayable: !isLocal)
        {
            Id = LastPart(Uri),
            IsLocal = isLocal,
            ArtistRefs = Artists,
            AlbumId = LastPart(AlbumUri),
        };
    }

    /// <summary>The ID in "spotify:track:&lt;id&gt;" or "spotify:album:&lt;id&gt;".</summary>
    private static string? LastPart(string? uri) =>
        uri?.Split(':') is [_, _, { Length: > 0 } id] ? id : null;
}
