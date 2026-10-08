namespace Resonate.Spotify.Playback;

/// <summary>Where the Spotify app ended up after <see cref="SpotifyAppKeeper.FollowAsync"/>.</summary>
public enum SpotifyAppOutcome
{
    /// <summary>Running in the background (started when it was not), as Windows' media controls need.</summary>
    Running,

    /// <summary>Not running, as "Spotify Web API only" wants.</summary>
    Closed,

    /// <summary>Wanted, but neither the installer nor the Microsoft Store version is installed.</summary>
    NotInstalled,

    /// <summary>Wanted, but it could not be started.</summary>
    CouldNotStart,

    /// <summary>Not wanted, but it did not close.</summary>
    CouldNotClose,
}

/// <summary>
/// Keeps the Spotify app on this computer in step with how Resonate talks to
/// Spotify. With Windows' media controls it is the speaker: started hidden
/// when it is not running, its window looked after. With "Spotify Web API
/// only" Resonate closes it (the owner's choice, 8 October 2026), so music
/// plays on another Spotify device; a Spotify the user opens again later is
/// left alone and plays like any other device. Steps run one after another,
/// each following the channel as it is when it runs, so switching back and
/// forth quickly ends where the user left it.
/// </summary>
public sealed class SpotifyAppKeeper : IDisposable
{
    private readonly ISpotifyAppLauncher _launcher;
    private readonly ISpotifyAppWindow _window;
    private readonly Func<ControlChannel> _channel;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="channel">How Resonate talks to Spotify right now.</param>
    public SpotifyAppKeeper(ISpotifyAppLauncher launcher, ISpotifyAppWindow window, Func<ControlChannel> channel)
    {
        _launcher = launcher;
        _window = window;
        _channel = channel;
    }

    /// <summary>
    /// Starts the Spotify app hidden (Windows' media controls) or closes it
    /// ("Spotify Web API only"). Call it when Resonate starts and after each
    /// switch of the channel.
    /// </summary>
    public async Task<SpotifyAppOutcome> FollowAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel() == ControlChannel.Local)
            {
                // Looked after before it starts, so its window is hidden as it appears.
                _window.Enabled = true;
                return await _launcher.EnsureRunningAsync(cancellationToken).ConfigureAwait(false) switch
                {
                    SpotifyAppStatus.Running or SpotifyAppStatus.Started => SpotifyAppOutcome.Running,
                    SpotifyAppStatus.NotInstalled => SpotifyAppOutcome.NotInstalled,
                    _ => SpotifyAppOutcome.CouldNotStart,
                };
            }

            // Its window stays hidden while it closes, so it does not flash back onto the taskbar.
            var closed = await _launcher.CloseAsync(cancellationToken).ConfigureAwait(false);
            if (_channel() == ControlChannel.WebApi)
            {
                // From now on the Spotify app is left alone; when it did not close,
                // what Resonate had hidden or slowed down is given back.
                _window.Enabled = false;
            }

            return closed ? SpotifyAppOutcome.Closed : SpotifyAppOutcome.CouldNotClose;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
