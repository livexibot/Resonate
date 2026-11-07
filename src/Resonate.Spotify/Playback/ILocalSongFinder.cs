using Resonate.Spotify.Library;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Finds the music files on this computer that local songs in Spotify
/// playlists stand for (<see cref="TrackInfo.IsLocal"/>), so Resonate can
/// play them itself: Spotify's web player can not, and Spotify's app only
/// inside the playlist, in its own order.
/// </summary>
public interface ILocalSongFinder
{
    /// <summary>For each song, the path of its file in Local Files, or null when none is clearly it (or it is not a local song).</summary>
    Task<IReadOnlyList<string?>> FindAsync(IReadOnlyList<TrackInfo> songs, CancellationToken cancellationToken);
}
