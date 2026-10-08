using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.App.Services;

/// <summary>
/// The data behind the built-in plugins Rediscover and Artist orbit, made
/// the first time a page asks while the plugin is on (never while it is
/// off). Used from the interface thread.
/// </summary>
internal static class BuiltInFeeds
{
    /// <summary>The artist orbit is worked out again at most this often (each time from stored data).</summary>
    private static readonly TimeSpan OrbitKeptFor = TimeSpan.FromMinutes(10);

    /// <summary>The user's own playlists not stored yet that one orbit may load (each is stored for next time).</summary>
    private const int MaxPlaylistsToLoad = 4;

    private const int MaxPlaylistSize = 500;

    private static RediscoverFeed? _rediscover;
    private static Task<ArtistOrbit>? _orbit;
    private static long _orbitMadeAt;
    private static string? _orbitUser;

    /// <summary>Rediscover's cards on Home, with the favourite albums kept in the cache folder.</summary>
    public static RediscoverFeed Rediscover(AppServices services) =>
        _rediscover ??= new RediscoverFeed(
            services.Library,
            services.Home,
            services.IsDemo ? null : Path.Combine(AppPaths.CacheFolder, "rediscover.json"));

    /// <summary>
    /// Which artists go together in the user's own playlists, Liked Songs and
    /// listening history: what is stored, plus a few own playlists not stored
    /// yet. Shared by every artist page for a few minutes.
    /// </summary>
    public static Task<ArtistOrbit> OrbitAsync(AppServices services)
    {
        var now = Environment.TickCount64;
        var user = services.Library.Snapshot?.User?.Id;
        if (_orbit is null || _orbit.IsFaulted || _orbit.IsCanceled || now - _orbitMadeAt > OrbitKeptFor.TotalMilliseconds || user != _orbitUser)
        {
            // Another account (after signing out) starts afresh too.
            _orbitMadeAt = now;
            _orbitUser = user;
            _orbit = Task.Run(() => MakeOrbitAsync(services));
        }

        return _orbit;
    }

    private static async Task<ArtistOrbit> MakeOrbitAsync(AppServices services)
    {
        var library = services.Library;
        var home = services.Home;
        home.LoadStored();
        var liked = library.GetStoredLikedSongs() ?? await library.GetAllLikedSongsAsync(CancellationToken.None);
        var playlists = new List<IReadOnlyList<TrackInfo>>();
        var loads = 0;
        foreach (var playlist in library.Snapshot?.Playlists.ToList() ?? [])
        {
            if (!library.CanListSongs(playlist))
            {
                continue;
            }

            // An older copy is good enough here, and reading it leaves the lists kept in memory alone.
            if (library.PeekStoredPlaylistTracks(playlist.Id) is { } stored)
            {
                playlists.Add(stored);
                continue;
            }

            if (loads >= MaxPlaylistsToLoad || playlist.ItemCount is 0 or > MaxPlaylistSize)
            {
                continue;
            }

            loads++;
            try
            {
                var list = await library.GetAllPlaylistTracksAsync(playlist.Id, playlist.SnapshotId, CancellationToken.None);
                if (!list.ItemsHidden)
                {
                    playlists.Add(list.Tracks);
                }
            }
            catch (Exception)
            {
                // One playlist less to learn from.
            }
        }

        return ArtistOrbit.Build(playlists, liked, home.History.Plays);
    }
}
