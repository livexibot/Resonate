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

    public Task<Page<SavedTrack>> GetSavedTracksAsync(int offset, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(new Page<SavedTrack>());

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken) =>
        Task.FromResult(PlaylistResult ?? new Playlist { Id = playlistId });

    public Task<Page<PlaylistEntry>> GetPlaylistItemsAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken) =>
        PlaylistItemsFailure is { } failure
            ? Task.FromException<Page<PlaylistEntry>>(failure)
            : Task.FromResult(new Page<PlaylistEntry>());

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

    private static async Task<PlaybackState?> AnswerLaterAsync(Task hold, PlaybackState? answer)
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
