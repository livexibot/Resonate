using Resonate.Spotify.Playback;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for Spotify's media session.</summary>
internal sealed class FakeLocalChannel : ILocalMediaChannel, IAppVolume
{
    public event EventHandler<LocalMediaSnapshot>? Changed;

    public LocalMediaSnapshot Current { get; private set; } = LocalMediaSnapshot.None;

    /// <summary>Whether commands succeed locally (false makes the controller fall back to the Web API).</summary>
    public bool Accepts { get; set; } = true;

    public double? Volume { get; set; }

    public bool AcceptsVolume { get; set; } = true;

    public List<string> Commands { get; } = [];

    /// <summary>How often listening was started.</summary>
    public int StartCount { get; private set; }

    public bool IsListening { get; private set; }

    /// <summary>How often the mixer volume was read.</summary>
    public int VolumeReads { get; private set; }

    public void Report(LocalMediaSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCount++;
        IsListening = true;
        return Task.CompletedTask;
    }

    public void Stop() => IsListening = false;

    public Task<bool> PlayAsync(CancellationToken cancellationToken) => Record("play");

    public Task<bool> PauseAsync(CancellationToken cancellationToken) => Record("pause");

    public Task<bool> NextAsync(CancellationToken cancellationToken) => Record("next");

    public Task<bool> PreviousAsync(CancellationToken cancellationToken) => Record("previous");

    public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken) =>
        Record($"seek {position.TotalSeconds:0.##}");

    public double? TryGetVolume()
    {
        VolumeReads++;
        return Volume;
    }

    public bool TrySetVolume(double volume)
    {
        if (!AcceptsVolume)
        {
            return false;
        }

        Commands.Add($"volume {volume:0.##}");
        Volume = volume;
        return true;
    }

    public void Dispose()
    {
    }

    private Task<bool> Record(string command)
    {
        if (Accepts)
        {
            Commands.Add(command);
        }

        return Task.FromResult(Accepts);
    }
}
