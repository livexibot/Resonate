using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>What the player bar shows. Immutable; every change is a new instance.</summary>
public sealed record PlayerState
{
    public static readonly PlayerState Empty = new();

    /// <summary>There is a Spotify app to control (locally or through the Web API).</summary>
    public bool IsConnected { get; init; }

    public bool IsPlaying { get; init; }

    public string? Title { get; init; }

    public string? Artists { get; init; }

    public string? Album { get; init; }

    public string? TrackUri { get; init; }

    public string? ContextUri { get; init; }

    /// <summary>Preferred over <see cref="ArtworkBytes"/> when both are set.</summary>
    public string? ArtworkUrl { get; init; }

    public byte[]? ArtworkBytes { get; init; }

    /// <summary>The cover at about 640 pixels (Spotify's largest), for the large now-playing views; null until known.</summary>
    public string? FullArtworkUrl { get; init; }

    /// <summary>The position at <see cref="PositionTimestamp"/>; use <see cref="PositionAt"/>.</summary>
    public TimeSpan Position { get; init; }

    public DateTimeOffset PositionTimestamp { get; init; }

    public TimeSpan Duration { get; init; }

    public bool CanSeek { get; init; }

    /// <summary>From 0 to 1.</summary>
    public double Volume { get; init; } = 1;

    /// <summary>Shuffle is on (Resonate's truly random shuffle, or Spotify's own when Resonate can not list the songs).</summary>
    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }

    /// <summary>Shuffle can be changed now (Spotify forbids it in some situations).</summary>
    public bool CanShuffle { get; init; } = true;

    public bool CanRepeat { get; init; } = true;

    public PlaybackSource Source { get; init; } = PlaybackSource.Spotify;

    /// <summary>The name of the Spotify Connect device that plays, as the Web API last reported it (or as just picked).</summary>
    public string? DeviceName { get; init; }

    /// <summary>What the music plays from, in words ("Liked Songs", a playlist's name), when Resonate knows.</summary>
    public string? SourceName { get; init; }

    public bool HasTrack => Title is not null;

    /// <summary>The position now, moving on by itself while playing.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = IsPlaying ? Position + (now - PositionTimestamp) : Position;
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
    }
}
