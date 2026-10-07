namespace Resonate.Spotify.WebApi;

/// <summary>What Resonate asks of the Spotify Web API.</summary>
public interface ISpotifyWebApi
{
    Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task<Page<SimplifiedPlaylist>> GetMyPlaylistsAsync(int offset, int limit, CancellationToken cancellationToken);

    /// <summary>Liked Songs.</summary>
    Task<Page<SavedTrack>> GetSavedTracksAsync(int offset, int limit, CancellationToken cancellationToken);

    Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken);

    Task<Page<PlaylistEntry>> GetPlaylistItemsAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>Search. Spotify caps <paramref name="limit"/> at <see cref="SpotifyWebApi.MaxSearchLimit"/> per type.</summary>
    Task<SearchResults> SearchAsync(string query, SearchTypes types, int offset, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Null when nothing is playing on any device.</summary>
    Task<PlaybackState?> GetPlaybackStateAsync(CancellationToken cancellationToken);

    Task StartPlaybackAsync(StartPlaybackBody? body, string? deviceId, CancellationToken cancellationToken);

    Task PauseAsync(string? deviceId, CancellationToken cancellationToken);

    Task SkipToNextAsync(string? deviceId, CancellationToken cancellationToken);

    Task SkipToPreviousAsync(string? deviceId, CancellationToken cancellationToken);

    Task SeekAsync(TimeSpan position, string? deviceId, CancellationToken cancellationToken);

    Task SetVolumeAsync(int percent, string? deviceId, CancellationToken cancellationToken);

    Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken cancellationToken);
}

[Flags]
public enum SearchTypes
{
    None = 0,
    Track = 1,
    Album = 2,
    Artist = 4,
    Playlist = 8,
    All = Track | Album | Artist | Playlist,
}
