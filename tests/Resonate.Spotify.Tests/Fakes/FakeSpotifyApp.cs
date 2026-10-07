using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for the Spotify app's process: running or not, and restarting on request.</summary>
internal sealed class FakeSpotifyApp : ISpotifyAppLauncher, ISpotifyAppRestarter
{
    public bool IsRunning { get; set; }

    public Action? BeforeStart { get; set; }

    /// <summary>Whether Spotify agrees to close.</summary>
    public bool Closes { get; set; } = true;

    /// <summary>Runs once Spotify has started again (to report what it plays).</summary>
    public Action? Started { get; set; }

    public List<string> Events { get; } = [];

    public Task<SpotifyAppStatus> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.FromResult(SpotifyAppStatus.Running);
        }

        Start();
        return Task.FromResult(SpotifyAppStatus.Started);
    }

    public Task<SpotifyRestartStatus> RestartAsync(Action whileClosed, CancellationToken cancellationToken)
    {
        if (!Closes)
        {
            Events.Add("refused to close");
            return Task.FromResult(SpotifyRestartStatus.CouldNotClose);
        }

        IsRunning = false;
        Events.Add("closed");
        whileClosed();
        Start();
        return Task.FromResult(SpotifyRestartStatus.Restarted);
    }

    private void Start()
    {
        BeforeStart?.Invoke();
        IsRunning = true;
        Events.Add("started");
        Started?.Invoke();
    }
}
