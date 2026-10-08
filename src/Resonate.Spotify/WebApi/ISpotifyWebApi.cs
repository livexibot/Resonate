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

    /// <summary>Spotify's own shuffle on the playing device.</summary>
    Task SetShuffleAsync(bool shuffle, string? deviceId, CancellationToken cancellationToken);

    Task SetRepeatAsync(RepeatMode mode, string? deviceId, CancellationToken cancellationToken);

    /// <summary>Adds a song to the end of the user's queue ("Next in queue").</summary>
    Task AddToQueueAsync(string uri, string? deviceId, CancellationToken cancellationToken);

    /// <summary>What plays now and next. Spotify returns about 20 queued songs at most.</summary>
    Task<PlayerQueue> GetQueueAsync(CancellationToken cancellationToken);

    /// <summary>The last songs played (at most 50), newest first; only songs played after <paramref name="after"/> when given.</summary>
    Task<CursorPage<PlayHistoryItem>> GetRecentlyPlayedAsync(int limit, DateTimeOffset? after, CancellationToken cancellationToken);

    Task<Page<Artist>> GetTopArtistsAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken);

    Task<Page<PlayableItem>> GetTopTracksAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>Whether each item is in the user's library (Liked Songs for songs). At most <see cref="SpotifyWebApi.MaxLibraryUris"/> URIs.</summary>
    Task<IReadOnlyList<bool>> CheckLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken);

    /// <summary>Saves to the library (likes songs). At most <see cref="SpotifyWebApi.MaxLibraryUris"/> URIs.</summary>
    Task SaveToLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken);

    Task RemoveFromLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken);

    /// <summary>Moves <paramref name="rangeLength"/> songs starting at <paramref name="rangeStart"/> to before <paramref name="insertBefore"/>. Returns the new snapshot ID.</summary>
    Task<string?> ReorderPlaylistItemsAsync(string playlistId, int rangeStart, int insertBefore, int rangeLength, string? snapshotId, CancellationToken cancellationToken);

    /// <summary>Adds songs (at most 100) at <paramref name="position"/>, or at the end. Returns the new snapshot ID.</summary>
    Task<string?> AddPlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, int? position, CancellationToken cancellationToken);

    /// <summary>Removes every occurrence of the songs (at most 100). Returns the new snapshot ID.</summary>
    Task<string?> RemovePlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, string? snapshotId, CancellationToken cancellationToken);

    Task<SimplifiedPlaylist> CreatePlaylistAsync(string name, string? description, bool isPublic, CancellationToken cancellationToken);

    /// <summary>Replaces all of a playlist's songs with these (at most 100; none empties it). Returns the new snapshot ID.</summary>
    Task<string?> ReplacePlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, CancellationToken cancellationToken);

    /// <summary>Renames a playlist the user owns.</summary>
    Task ChangePlaylistDetailsAsync(string playlistId, string name, CancellationToken cancellationToken);

    Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken);

    Task<Page<PlayableItem>> GetAlbumTracksAsync(string albumId, int offset, int limit, CancellationToken cancellationToken);

    Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken);

    /// <summary>The artist's albums, singles and compilations, newest first as Spotify lists them.</summary>
    Task<Page<SimplifiedAlbum>> GetArtistAlbumsAsync(string artistId, int offset, int limit, CancellationToken cancellationToken);
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
