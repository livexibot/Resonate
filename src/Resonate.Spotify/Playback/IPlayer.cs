using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>Where the music the player bar shows comes from.</summary>
public enum PlaybackSource
{
    /// <summary>The Spotify app (on this computer or another Spotify Connect device).</summary>
    Spotify,

    /// <summary>Music files on this computer, played by Resonate itself.</summary>
    LocalFiles,
}

/// <summary>
/// What the player bar and keyboard shortcuts control. The Spotify player
/// (<see cref="PlayerController"/>) and the local files player both
/// implement it, and a router forwards to whichever is playing. Every
/// command is optimistic: <see cref="State"/> changes at once, and a
/// failure is rolled back and reported through <see cref="ErrorOccurred"/>.
/// </summary>
public interface IPlayer
{
    /// <summary>Raised on any thread after <see cref="State"/> changes. Read <see cref="State"/> for the newest value.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised on any thread with a sentence to show when a command failed.</summary>
    event EventHandler<string>? ErrorOccurred;

    PlayerState State { get; }

    Task TogglePlayPauseAsync();

    Task PlayAsync();

    Task PauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    Task SeekAsync(TimeSpan position);

    /// <summary>From 0 to 1.</summary>
    Task SetVolumeAsync(double volume);

    /// <summary>Truly random order (see <see cref="TrueShuffle"/>), from the next song on.</summary>
    Task SetShuffleAsync(bool shuffle);

    Task SetRepeatAsync(RepeatMode mode);
}
