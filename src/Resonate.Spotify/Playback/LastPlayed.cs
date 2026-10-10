namespace Resonate.Spotify.Playback;

/// <summary>
/// The Spotify song that played last and where it was, kept in the settings
/// so Resonate opens showing it, paused at that place, ready to play on
/// (the owner's request, 10 October 2026). Local files are not kept.
/// </summary>
public sealed class LastPlayed
{
    public string TrackUri { get; set; } = string.Empty;

    /// <summary>The playlist, album or other list it played from, so playing on carries on through it.</summary>
    public string? ContextUri { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Artists { get; set; }

    public string? Album { get; set; }

    public string? ArtworkUrl { get; set; }

    public string? FullArtworkUrl { get; set; }

    public long DurationMs { get; set; }

    public long PositionMs { get; set; }

    /// <summary>What to keep of <paramref name="state"/>, or null for nothing worth keeping (no Spotify song).</summary>
    public static LastPlayed? From(PlayerState state, DateTimeOffset now)
    {
        if (state.Source != PlaybackSource.Spotify
            || state.Title is not { Length: > 0 } title
            || state.TrackUri is not { } uri
            || !uri.StartsWith("spotify:", StringComparison.Ordinal)
            || uri.StartsWith("spotify:local:", StringComparison.Ordinal))
        {
            return null;
        }

        var position = state.PositionAt(now);
        return new LastPlayed
        {
            TrackUri = uri,
            ContextUri = state.ContextUri,
            Title = title,
            Artists = state.Artists,
            Album = state.Album,
            ArtworkUrl = state.ArtworkUrl,
            FullArtworkUrl = state.FullArtworkUrl,
            DurationMs = (long)state.Duration.TotalMilliseconds,
            PositionMs = (long)Math.Clamp(position.TotalMilliseconds, 0, Math.Max(0, state.Duration.TotalMilliseconds)),
        };
    }

    /// <summary>The player showing it, paused where it was, until Spotify says what plays.</summary>
    public PlayerState ToState(PlayerState current, DateTimeOffset now) => current with
    {
        IsPlaying = false,
        Title = Title,
        Artists = Artists,
        Album = Album,
        TrackUri = TrackUri,
        ContextUri = ContextUri,
        ArtworkUrl = ArtworkUrl,
        ArtworkBytes = null,
        FullArtworkUrl = FullArtworkUrl,
        Duration = TimeSpan.FromMilliseconds(DurationMs),
        Position = TimeSpan.FromMilliseconds(PositionMs),
        PositionTimestamp = now,
        Source = PlaybackSource.Spotify,
    };

    /// <summary>What Spotify is asked to play to carry on: the list from this song, or the song alone, at its place.</summary>
    public StartPlaybackBodyChoice Bodies() => new(
        ContextUri is { Length: > 0 } context
            ? new WebApi.StartPlaybackBody { ContextUri = context, Offset = new WebApi.PlaybackOffset { Uri = TrackUri }, PositionMs = (int)PositionMs }
            : null,
        new WebApi.StartPlaybackBody { Uris = [TrackUri], PositionMs = (int)PositionMs });
}

/// <summary>Playing on from a list where it can, else the song by itself.</summary>
public sealed record StartPlaybackBodyChoice(WebApi.StartPlaybackBody? InList, WebApi.StartPlaybackBody Alone);
