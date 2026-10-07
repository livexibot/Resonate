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

    /// <summary>The position at <see cref="PositionTimestamp"/>; use <see cref="PositionAt"/>.</summary>
    public TimeSpan Position { get; init; }

    public DateTimeOffset PositionTimestamp { get; init; }

    public TimeSpan Duration { get; init; }

    public bool CanSeek { get; init; }

    /// <summary>From 0 to 1.</summary>
    public double Volume { get; init; } = 1;

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
