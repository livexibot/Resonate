using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for the Spotify app's process: running or not, and restarting on request.</summary>
internal sealed class FakeSpotifyApp : ISpotifyAppLauncher, ISpotifyAppRestarter
{
    private bool _running;

    public bool IsRunning
    {
        get
        {
            IsRunningReads++;
            return _running;
        }

        set => _running = value;
    }

    /// <summary>How often Resonate asked whether Spotify runs.</summary>
    public int IsRunningReads { get; private set; }

    /// <summary>How often Resonate asked for Spotify to be started if needed.</summary>
    public int EnsureRunningCalls { get; private set; }

    public Action? BeforeStart { get; set; }

    /// <summary>Whether Spotify agrees to close.</summary>
    public bool Closes { get; set; } = true;

    /// <summary>Runs once Spotify has started again (to report what it plays).</summary>
    public Action? Started { get; set; }

    public List<string> Events { get; } = [];

    public Task<SpotifyAppStatus> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        EnsureRunningCalls++;
        if (_running)
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

        _running = false;
        Events.Add("closed");
        whileClosed();
        Start();
        return Task.FromResult(SpotifyRestartStatus.Restarted);
    }

    private void Start()
    {
        BeforeStart?.Invoke();
        _running = true;
        Events.Add("started");
        Started?.Invoke();
    }
}
