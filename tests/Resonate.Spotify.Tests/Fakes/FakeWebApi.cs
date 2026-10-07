using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for the Spotify Web API and records the player commands it receives.</summary>
internal sealed class FakeWebApi : ISpotifyWebApi
{
    public List<string> Commands { get; } = [];

    public List<StartPlaybackBody?> PlayBodies { get; } = [];

    public List<Device> Devices { get; } =
    [
        new Device { Id = "here", Name = "MY-PC", Type = "Computer" },
    ];

    public PlaybackState? Playback { get; set; }

    /// <summary>Thrown by the next player command, then cleared.</summary>
    public Exception? FailNextCommand { get; set; }

    /// <summary>Thrown by start playback whenever a context URI is given.</summary>
    public Exception? FailContextPlayback { get; set; }

    public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SpotifyUser { Id = "me", DisplayName = "Me" });

    /// <summary>All of the user's playlists; served in pages of the requested size.</summary>
    public List<SimplifiedPlaylist> Playlists { get; } = [];

    public Playlist? PlaylistResult { get; set; }

    public Exception? PlaylistItemsFailure { get; set; }

    public Task<Page<SimplifiedPlaylist>> GetMyPlaylistsAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var items = Playlists.Skip(offset).Take(limit).ToList<SimplifiedPlaylist?>();
        return Task.FromResult(new Page<SimplifiedPlaylist>
        {
            Items = items,
            Total = Playlists.Count,
            Offset = offset,
            Limit = limit,
            Next = offset + items.Count < Playlists.Count ? "next" : null,
        });
    }

    /// <summary>Liked Songs, newest first; served in pages of the requested size.</summary>
    public List<SavedTrack> SavedTracks { get; } = [];

    public int SavedTrackReads { get; private set; }

    /// <summary>When set, reading Liked Songs answers with what was liked when asked, but only once this task completes.</summary>
    public Task? HoldSavedTrackReads { get; set; }

    public Task<Page<SavedTrack>> GetSavedTracksAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        SavedTrackReads++;
        var page = Paged(SavedTracks.ToList<SavedTrack?>(), offset, limit);
        return HoldSavedTrackReads is { } hold ? AnswerLaterAsync(hold, page) : Task.FromResult(page);
    }

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken) =>
        Task.FromResult(PlaylistResult ?? new Playlist { Id = playlistId });

    /// <summary>Songs per playlist ID; served in pages of the requested size.</summary>
    public Dictionary<string, List<PlaylistEntry>> PlaylistEntries { get; } = [];

    public int PlaylistItemReads { get; private set; }

    public Task<Page<PlaylistEntry>> GetPlaylistItemsAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken)
    {
        PlaylistItemReads++;
        if (PlaylistItemsFailure is { } failure)
        {
            return Task.FromException<Page<PlaylistEntry>>(failure);
        }

        return Task.FromResult(PlaylistEntries.TryGetValue(playlistId, out var entries) ? Paged(entries.ToList<PlaylistEntry?>(), offset, limit) : new Page<PlaylistEntry>());
    }

    public Task<SearchResults> SearchAsync(string query, SearchTypes types, int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(new SearchResults());

    public Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Device>>(Devices);

    public int PlaybackStateReads => Volatile.Read(ref _playbackStateReads);

    private int _playbackStateReads;

    /// <summary>Thrown by every read of the playback state while set.</summary>
    public Exception? PlaybackFailure { get; set; }

    /// <summary>
    /// Makes the next read of the playback state answer with what is true at
    /// the moment it is asked, but only once this task completes (a slow answer).
    /// </summary>
    public Task? HoldNextPlaybackAnswer { get; set; }

    public Task<PlaybackState?> GetPlaybackStateAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _playbackStateReads);
        if (PlaybackFailure is { } failure)
        {
            return Task.FromException<PlaybackState?>(failure);
        }

        var answer = Playback;
        if (HoldNextPlaybackAnswer is { } hold)
        {
            HoldNextPlaybackAnswer = null;
            return AnswerLaterAsync(hold, answer);
        }

        return Task.FromResult(answer);
    }

    public Task StartPlaybackAsync(StartPlaybackBody? body, string? deviceId, CancellationToken cancellationToken)
    {
        if (body?.ContextUri is not null && FailContextPlayback is { } contextFailure)
        {
            return Task.FromException(contextFailure);
        }

        PlayBodies.Add(body);
        return Record($"play@{deviceId}");
    }

    public Task PauseAsync(string? deviceId, CancellationToken cancellationToken) => Record($"pause@{deviceId}");

    public Task SkipToNextAsync(string? deviceId, CancellationToken cancellationToken) => Record($"next@{deviceId}");

    public Task SkipToPreviousAsync(string? deviceId, CancellationToken cancellationToken) => Record($"previous@{deviceId}");

    public Task SeekAsync(TimeSpan position, string? deviceId, CancellationToken cancellationToken) =>
        Record($"seek {position.TotalSeconds:0.##}@{deviceId}");

    public Task SetVolumeAsync(int percent, string? deviceId, CancellationToken cancellationToken) =>
        Record($"volume {percent}@{deviceId}");

    public Task TransferPlaybackAsync(string deviceId, bool play, CancellationToken cancellationToken) =>
        Record($"transfer@{deviceId}");

    public PlayerQueue QueueResult { get; set; } = new();

    public List<PlayHistoryItem> RecentlyPlayed { get; } = [];

    public List<Artist> TopArtists { get; } = [];

    public List<PlayableItem> TopTracks { get; } = [];

    /// <summary>URIs in the user's library (liked songs and so on).</summary>
    public HashSet<string> Library { get; } = [];

    public Dictionary<string, Album> Albums { get; } = [];

    public Dictionary<string, Artist> Artists { get; } = [];

    public List<SimplifiedAlbum> ArtistAlbums { get; } = [];

    public async Task SetShuffleAsync(bool shuffle, string? deviceId, CancellationToken cancellationToken)
    {
        await Record($"shuffle {(shuffle ? "on" : "off")}@{deviceId}");
        if (Playback is { } playback)
        {
            playback.ShuffleState = shuffle;
        }
    }

    public async Task SetRepeatAsync(RepeatMode mode, string? deviceId, CancellationToken cancellationToken)
    {
        await Record($"repeat {RepeatModes.ToSpotify(mode)}@{deviceId}");
        if (Playback is { } playback)
        {
            playback.RepeatState = RepeatModes.ToSpotify(mode);
        }
    }

    public Task AddToQueueAsync(string uri, string? deviceId, CancellationToken cancellationToken) =>
        Record($"queue {uri}@{deviceId}");

    public Task<PlayerQueue> GetQueueAsync(CancellationToken cancellationToken) => Task.FromResult(QueueResult);

    /// <summary>The "after" of each recently played request, in order.</summary>
    public List<DateTimeOffset?> RecentlyPlayedRequests { get; } = [];

    /// <summary>The range of each top artists request, in order.</summary>
    public List<TopRange> TopArtistRequests { get; } = [];

    public int TopTrackReads { get; private set; }

    /// <summary>Thrown by the top artists and top tracks requests while set (such as a sign-in without the permission).</summary>
    public Exception? TopItemsFailure { get; set; }

    public Task<CursorPage<PlayHistoryItem>> GetRecentlyPlayedAsync(int limit, DateTimeOffset? after, CancellationToken cancellationToken)
    {
        RecentlyPlayedRequests.Add(after);
        var items = RecentlyPlayed
            .Where(p => after is null || TimeOf(p) > after)
            .Take(limit)
            .ToList<PlayHistoryItem?>();
        return Task.FromResult(new CursorPage<PlayHistoryItem> { Items = items, Limit = limit });
    }

    public Task<Page<Artist>> GetTopArtistsAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken)
    {
        TopArtistRequests.Add(range);
        return TopItemsFailure is { } failure
            ? Task.FromException<Page<Artist>>(failure)
            : Task.FromResult(Paged(TopArtists.ToList<Artist?>(), offset, limit));
    }

    public Task<Page<PlayableItem>> GetTopTracksAsync(TopRange range, int offset, int limit, CancellationToken cancellationToken)
    {
        TopTrackReads++;
        return TopItemsFailure is { } failure
            ? Task.FromException<Page<PlayableItem>>(failure)
            : Task.FromResult(Paged(TopTracks.ToList<PlayableItem?>(), offset, limit));
    }

    public Task<IReadOnlyList<bool>> CheckLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<bool>>(uris.Select(Library.Contains).ToList());

    /// <summary>When set, saving and removing library items waits for this task (a slow answer).</summary>
    public Task? HoldLibraryWrites { get; set; }

    public async Task SaveToLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken)
    {
        if (HoldLibraryWrites is { } hold)
        {
            await hold;
        }

        await Record($"save {string.Join(',', uris)}");
        Library.UnionWith(uris);
    }

    public async Task RemoveFromLibraryAsync(IReadOnlyList<string> uris, CancellationToken cancellationToken)
    {
        if (HoldLibraryWrites is { } hold)
        {
            await hold;
        }

        await Record($"unsave {string.Join(',', uris)}");
        Library.ExceptWith(uris);
    }

    public async Task<string?> ReorderPlaylistItemsAsync(
        string playlistId,
        int rangeStart,
        int insertBefore,
        int rangeLength,
        string? snapshotId,
        CancellationToken cancellationToken)
    {
        await Record($"reorder {playlistId} {rangeStart}->{insertBefore} x{rangeLength}");
        return "snapshot-after-reorder";
    }

    public async Task<string?> AddPlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, int? position, CancellationToken cancellationToken)
    {
        await Record($"add {playlistId} {string.Join(',', uris)}");
        return "snapshot-after-add";
    }

    public async Task<string?> RemovePlaylistItemsAsync(string playlistId, IReadOnlyList<string> uris, string? snapshotId, CancellationToken cancellationToken)
    {
        await Record($"remove {playlistId} {string.Join(',', uris)}");
        return "snapshot-after-remove";
    }

    public async Task<SimplifiedPlaylist> CreatePlaylistAsync(string name, string? description, bool isPublic, CancellationToken cancellationToken)
    {
        await Record($"create {name}");
        return new SimplifiedPlaylist { Id = "new", Name = name, Uri = "spotify:playlist:new", Owner = new PlaylistOwner { Id = "me" } };
    }

    public Task<Album> GetAlbumAsync(string albumId, CancellationToken cancellationToken) =>
        Task.FromResult(Albums.TryGetValue(albumId, out var album) ? album : new Album { Id = albumId, Name = "Album", Uri = "spotify:album:" + albumId });

    public Task<Page<PlayableItem>> GetAlbumTracksAsync(string albumId, int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Albums.TryGetValue(albumId, out var album) && album.Tracks is { } tracks
            ? Paged(tracks.Items, offset, limit)
            : new Page<PlayableItem>());

    /// <summary>The ID of each single-artist request, in order.</summary>
    public List<string> ArtistRequests { get; } = [];

    public Task<Artist> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        ArtistRequests.Add(artistId);
        return Task.FromResult(Artists.TryGetValue(artistId, out var artist) ? artist : new Artist { Id = artistId, Name = "Artist", Uri = "spotify:artist:" + artistId });
    }

    public Task<Page<SimplifiedAlbum>> GetArtistAlbumsAsync(string artistId, int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Paged(ArtistAlbums.ToList<SimplifiedAlbum?>(), offset, limit));

    private static DateTimeOffset TimeOf(PlayHistoryItem item) =>
        DateTimeOffset.Parse(item.PlayedAt!, System.Globalization.CultureInfo.InvariantCulture);

    private static Page<T> Paged<T>(List<T?> all, int offset, int limit)
    {
        var items = all.Skip(offset).Take(limit).ToList();
        return new Page<T>
        {
            Items = items,
            Total = all.Count,
            Offset = offset,
            Limit = limit,
            Next = offset + items.Count < all.Count ? "next" : null,
        };
    }

    private static async Task<T> AnswerLaterAsync<T>(Task hold, T answer)
    {
        await hold;
        return answer;
    }

    private Task Record(string command)
    {
        if (FailNextCommand is { } failure)
        {
            FailNextCommand = null;
            return Task.FromException(failure);
        }

        Commands.Add(command);
        return Task.CompletedTask;
    }
}
