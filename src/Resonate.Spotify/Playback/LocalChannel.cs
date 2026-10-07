namespace Resonate.Spotify.Playback;

/// <summary>
/// What the operating system's media controls report about the Spotify app
/// on this computer (on Windows, Spotify's system media transport controls
/// session). Arrives without an internet round trip.
/// </summary>
public sealed record LocalMediaSnapshot
{
    public static readonly LocalMediaSnapshot None = new();

    /// <summary>Spotify has a media session, so local control works.</summary>
    public bool HasSession { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    /// <summary>The cover as encoded image bytes, when Spotify shares one.</summary>
    public byte[]? Artwork { get; init; }

    public bool IsPlaying { get; init; }

    public TimeSpan Position { get; init; }

    /// <summary>When <see cref="Position"/> was sampled.</summary>
    public DateTimeOffset PositionUpdatedAt { get; init; }

    /// <summary>Zero when Spotify does not report a timeline.</summary>
    public TimeSpan Duration { get; init; }

    public bool CanSeek { get; init; }

    public bool CanSkipNext { get; init; }

    public bool CanSkipPrevious { get; init; }

    public bool HasTimeline => Duration > TimeSpan.Zero;

    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = IsPlaying ? Position + (now - PositionUpdatedAt) : Position;
        return HasTimeline ? Clamp(position, Duration) : position;
    }

    internal static TimeSpan Clamp(TimeSpan position, TimeSpan duration) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
}

/// <summary>
/// The local, instant way to control the Spotify app. Each command returns
/// false when it could not be sent locally, so the caller can fall back to
/// the Web API.
/// </summary>
public interface ILocalMediaChannel : IDisposable
{
    event EventHandler<LocalMediaSnapshot>? Changed;

    LocalMediaSnapshot Current { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task<bool> PlayAsync(CancellationToken cancellationToken);

    Task<bool> PauseAsync(CancellationToken cancellationToken);

    Task<bool> NextAsync(CancellationToken cancellationToken);

    Task<bool> PreviousAsync(CancellationToken cancellationToken);

    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken);
}

/// <summary>
/// Spotify's own volume in the operating system's mixer. Volume is not part
/// of the media controls, so it has its own channel.
/// </summary>
public interface IAppVolume
{
    /// <summary>From 0 to 1, or null when Spotify has no audio session yet.</summary>
    double? TryGetVolume();

    bool TrySetVolume(double volume);
}

/// <summary>For platforms or tests without local control.</summary>
public sealed class NoLocalMediaChannel : ILocalMediaChannel, IAppVolume
{
    public event EventHandler<LocalMediaSnapshot>? Changed
    {
        add { }
        remove { }
    }

    public LocalMediaSnapshot Current => LocalMediaSnapshot.None;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> PlayAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> PauseAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> NextAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> PreviousAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken) => Task.FromResult(false);

    public double? TryGetVolume() => null;

    public bool TrySetVolume(double volume) => false;

    public void Dispose()
    {
    }
}

/// <summary>How the player talks to Spotify.</summary>
public enum ControlChannel
{
    /// <summary>
    /// Windows' media controls first (instant, no internet), the Web API for
    /// what they can not do. The default.
    /// </summary>
    Local,

    /// <summary>
    /// The Spotify Web API only: every command goes through Spotify's
    /// servers, and what is playing is read from them.
    /// </summary>
    WebApi,
}

public enum SpotifyAppStatus
{
    /// <summary>Spotify was already running.</summary>
    Running,

    /// <summary>Resonate started Spotify in the background.</summary>
    Started,

    /// <summary>Neither the installer nor the Microsoft Store version was found.</summary>
    NotInstalled,

    Failed,
}

/// <summary>Finds the Spotify desktop app and starts it hidden when needed.</summary>
public interface ISpotifyAppLauncher
{
    bool IsRunning { get; }

    Task<SpotifyAppStatus> EnsureRunningAsync(CancellationToken cancellationToken);
}
