using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Spotify's queue as Resonate shows it. Spotify returns at most about 20
/// songs and pads a short queue by repeating songs (a song started on its
/// own shows as copies of itself), so the list stops at the first song
/// already seen.
/// </summary>
public static class QueuePreview
{
    public static List<TrackInfo> Upcoming(PlayerQueue queue)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (queue.CurrentlyPlaying?.Uri is { } current)
        {
            seen.Add(current);
        }

        var upcoming = new List<TrackInfo>(queue.Queue.Count);
        foreach (var item in queue.Queue)
        {
            if (TrackInfo.From(item) is not { } track)
            {
                continue;
            }

            if (track.Uri is { } uri && !seen.Add(uri))
            {
                break;
            }

            upcoming.Add(track);
        }

        return upcoming;
    }
}
