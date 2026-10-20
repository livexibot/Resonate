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

    /// <summary>What it played from, in words ("Liked Songs", a playlist's name), when Resonate knew.</summary>
    public string? SourceName { get; set; }

    /// <summary>The page of what it played from (a navigation key the app understands), so the player's title can open it.</summary>
    public string? SourceKey { get; set; }

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
            SourceName = state.SourceName,
            Title = title,
            Artists = state.Artists,
            Album = state.Album,
            ArtworkUrl = state.ArtworkUrl,
            FullArtworkUrl = state.FullArtworkUrl,
            DurationMs = (long)state.Duration.TotalMilliseconds,
            PositionMs = (long)Math.Clamp(position.TotalMilliseconds, 0, Math.Max(0, state.Duration.TotalMilliseconds)),
        };
    }

    /// <summary>A copy at another place in the song (this one stays as it is).</summary>
    public LastPlayed At(long positionMs)
    {
        var copy = (LastPlayed)MemberwiseClone();
        copy.PositionMs = positionMs;
        return copy;
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
        SourceName = SourceName,
    };

    /// <summary>
    /// What Spotify is asked to play to carry on when Resonate kept no list of
    /// its songs (see <see cref="KeptList"/>): the list from this song, or the
    /// song alone, at its place. Never Spotify's DJ: the Web API declines it,
    /// and the music stops.
    /// </summary>
    public StartPlaybackBodyChoice Bodies() => new(
        ContextUri is { Length: > 0 } context && context != SpotifyDj.ContextUri
            ? new WebApi.StartPlaybackBody { ContextUri = context, Offset = new WebApi.PlaybackOffset { Uri = TrackUri }, PositionMs = (int)PositionMs }
            : null,
        new WebApi.StartPlaybackBody { Uris = [TrackUri], PositionMs = (int)PositionMs });
}

/// <summary>Playing on from a list where it can, else the song by itself.</summary>
public sealed record StartPlaybackBodyChoice(WebApi.StartPlaybackBody? InList, WebApi.StartPlaybackBody Alone);
