using Resonate.App.Demo;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;

namespace Resonate.App.Helpers;

/// <summary>
/// The playing Spotify song with its artists' and album's IDs, so its names
/// can open their pages (<see cref="SongLinks"/>). The player knows only the
/// names, so the song is looked up once, the first time it is needed: one
/// <c>/me/player</c> request (the demo's own catalogue in demo mode). Call
/// on the interface thread.
/// </summary>
internal static class PlayingTrack
{
    private static string? _uri;
    private static TrackInfo? _track;

    /// <summary>Raised on the interface thread when the playing song has been looked up.</summary>
    public static event Action? Resolved;

    /// <summary>The playing song, or null while it is unknown (a lookup then starts) or nothing from Spotify plays.</summary>
    public static TrackInfo? Get()
    {
        var state = App.Services.Player.State;
        if (state.Source != PlaybackSource.Spotify || state.TrackUri is not { } uri)
        {
            return null;
        }

        if (uri != _uri)
        {
            _uri = uri;
            _track = null;
            _ = LookUpAsync(uri);
        }

        return _track;
    }

    private static async Task LookUpAsync(string uri)
    {
        TrackInfo? track = null;
        try
        {
            if (App.Services.IsDemo)
            {
                track = TrackInfo.From(DemoCatalog.FindByUri(uri));
            }
            else if (await App.Services.Api.GetPlaybackStateAsync(CancellationToken.None) is { Item: { } item } && item.Uri == uri)
            {
                track = TrackInfo.From(item);
            }
        }
        catch (Exception)
        {
            // Offline or refused: the names stay plain, and the next visit asks again.
        }

        if (uri != _uri)
        {
            return;
        }

        if (track is null)
        {
            _uri = null;
            return;
        }

        _track = track;
        Resolved?.Invoke();
    }
}
