namespace Resonate.Spotify.Playback;

/// <summary>How restarting the Spotify app went.</summary>
public enum SpotifyRestartStatus
{
    /// <summary>Spotify closed and started again (or, when it was not running, started).</summary>
    Restarted,

    /// <summary>Spotify did not close, so it keeps running as it was.</summary>
    CouldNotClose,

    /// <summary>Spotify closed but could not be started again.</summary>
    CouldNotStart,

    /// <summary>Spotify closed, and neither the installer nor the Microsoft Store version was found to start again.</summary>
    NotInstalled,
}

/// <summary>
/// Closes and reopens the Spotify app, for settings it only reads when it
/// starts (such as its equalizer).
/// </summary>
public interface ISpotifyAppRestarter
{
    /// <summary>
    /// Runs on a background thread right before Resonate starts Spotify
    /// (Spotify is not running then), so settings written now are the ones
    /// it starts with.
    /// </summary>
    Action? BeforeStart { get; set; }

    /// <summary>
    /// Closes the Spotify app (asking it first, ending it when it does not
    /// close in time), waits until all of its processes are gone, runs
    /// <paramref name="whileClosed"/>, then starts it again in the background
    /// the way Resonate always does. Nothing can start Spotify through
    /// Resonate in the meantime.
    /// </summary>
    Task<SpotifyRestartStatus> RestartAsync(Action whileClosed, CancellationToken cancellationToken);
}

/// <summary>How putting back what was playing went after the Spotify app restarted.</summary>
public enum ResumeOutcome
{
    /// <summary>Nothing was playing or paused, so there was nothing to put back.</summary>
    NothingToResume,

    /// <summary>The same song plays on from where it was.</summary>
    Playing,

    /// <summary>The same song is back, paused where it was.</summary>
    Paused,

    /// <summary>
    /// Spotify did not come back with the same song (or did not say), and it
    /// could not be started again; the user has to pick it again.
    /// </summary>
    NotResumed,
}
