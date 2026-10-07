using System.Text.Json.Serialization;

namespace Resonate.Spotify.WebApi;

// Shapes of the Spotify Web API answers Resonate reads. Property names map
// to snake_case through SpotifyJsonContext. Since the February 2026 changes,
// several fields were renamed (a playlist's "tracks" became "items", and a
// playlist entry's "track" became "item"); both spellings are read so the
// client keeps working on either side of a change.

public sealed class SpotifyImage
{
    public string Url { get; set; } = string.Empty;

    public int? Width { get; set; }

    public int? Height { get; set; }
}

public sealed class SpotifyUser
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string Uri { get; set; } = string.Empty;

    public List<SpotifyImage>? Images { get; set; }
}

public sealed class SimplifiedArtist
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Uri { get; set; }
}

public sealed class Artist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public List<SpotifyImage>? Images { get; set; }
}

public sealed class SimplifiedAlbum
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Uri { get; set; }

    public string? AlbumType { get; set; }

    public string? ReleaseDate { get; set; }

    public List<SpotifyImage>? Images { get; set; }

    public List<SimplifiedArtist>? Artists { get; set; }
}

/// <summary>A track or a podcast episode: anything that can sit in a playlist and play.</summary>
public sealed class PlayableItem
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Uri { get; set; }

    /// <summary>"track" or "episode".</summary>
    public string? Type { get; set; }

    public int DurationMs { get; set; }

    public bool Explicit { get; set; }

    public bool IsLocal { get; set; }

    public bool? IsPlayable { get; set; }

    public List<SimplifiedArtist>? Artists { get; set; }

    public SimplifiedAlbum? Album { get; set; }

    /// <summary>Episodes carry their own images instead of an album.</summary>
    public List<SpotifyImage>? Images { get; set; }

    /// <summary>Episodes name their show instead of artists.</summary>
    public SimplifiedShow? Show { get; set; }
}

public sealed class SimplifiedShow
{
    public string Name { get; set; } = string.Empty;

    public string? Publisher { get; set; }
}

public sealed class PlaylistOwner
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}

/// <summary>The size of a playlist, as given in playlist lists.</summary>
public sealed class ItemsReference
{
    public string? Href { get; set; }

    public int Total { get; set; }
}

public sealed class SimplifiedPlaylist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Collaborative { get; set; }

    public bool? Public { get; set; }

    public string? SnapshotId { get; set; }

    public PlaylistOwner? Owner { get; set; }

    public List<SpotifyImage>? Images { get; set; }

    /// <summary>Since February 2026.</summary>
    public ItemsReference? Items { get; set; }

    /// <summary>Before February 2026.</summary>
    public ItemsReference? Tracks { get; set; }

    [JsonIgnore]
    public int ItemCount => (Items ?? Tracks)?.Total ?? 0;
}

public sealed class Playlist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Collaborative { get; set; }

    public PlaylistOwner? Owner { get; set; }

    public List<SpotifyImage>? Images { get; set; }

    /// <summary>
    /// The first page of entries. Since February 2026 Spotify only returns
    /// entries for playlists the user owns or collaborates on; for others
    /// this is null.
    /// </summary>
    public Page<PlaylistEntry>? Items { get; set; }

    public Page<PlaylistEntry>? Tracks { get; set; }

    [JsonIgnore]
    public Page<PlaylistEntry>? Entries => Items ?? Tracks;
}

public sealed class PlaylistEntry
{
    public string? AddedAt { get; set; }

    public bool IsLocal { get; set; }

    /// <summary>Since February 2026.</summary>
    public PlayableItem? Item { get; set; }

    /// <summary>Before February 2026.</summary>
    public PlayableItem? Track { get; set; }

    [JsonIgnore]
    public PlayableItem? Playable => Item ?? Track;
}

public sealed class SavedTrack
{
    public string? AddedAt { get; set; }

    public PlayableItem? Track { get; set; }
}

public sealed class Page<T>
{
    public List<T?> Items { get; set; } = [];

    public int Total { get; set; }

    public int Limit { get; set; }

    public int Offset { get; set; }

    public string? Next { get; set; }

    [JsonIgnore]
    public bool HasMore => Next is not null;
}

public sealed class SearchResults
{
    public Page<PlayableItem>? Tracks { get; set; }

    public Page<SimplifiedAlbum>? Albums { get; set; }

    public Page<Artist>? Artists { get; set; }

    /// <summary>Spotify's search can return null entries here; skip them.</summary>
    public Page<SimplifiedPlaylist>? Playlists { get; set; }
}

public sealed class Device
{
    public string? Id { get; set; }

    public bool IsActive { get; set; }

    public bool IsPrivateSession { get; set; }

    public bool IsRestricted { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>"Computer", "Smartphone", "Speaker" and so on.</summary>
    public string Type { get; set; } = string.Empty;

    public int? VolumePercent { get; set; }

    public bool SupportsVolume { get; set; }
}

public sealed class DeviceList
{
    public List<Device> Devices { get; set; } = [];
}

public sealed class PlaybackContext
{
    public string? Type { get; set; }

    public string? Uri { get; set; }
}

public sealed class PlaybackState
{
    public Device? Device { get; set; }

    public bool IsPlaying { get; set; }

    public int? ProgressMs { get; set; }

    public long Timestamp { get; set; }

    public bool ShuffleState { get; set; }

    public string? RepeatState { get; set; }

    public PlaybackContext? Context { get; set; }

    public PlayableItem? Item { get; set; }
}

/// <summary>The body of "start or resume playback".</summary>
public sealed class StartPlaybackBody
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContextUri { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Uris { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlaybackOffset? Offset { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PositionMs { get; set; }
}

public sealed class PlaybackOffset
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Position { get; set; }
}

public sealed class TransferPlaybackBody
{
    public List<string> DeviceIds { get; set; } = [];

    public bool Play { get; set; }
}

public sealed class ApiErrorEnvelope
{
    public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    public int Status { get; set; }

    public string? Message { get; set; }

    public string? Reason { get; set; }
}

public sealed class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string? TokenType { get; set; }

    public string? Scope { get; set; }

    public int ExpiresIn { get; set; }

    public string? RefreshToken { get; set; }
}

public sealed class OAuthError
{
    public string? Error { get; set; }

    public string? ErrorDescription { get; set; }
}
