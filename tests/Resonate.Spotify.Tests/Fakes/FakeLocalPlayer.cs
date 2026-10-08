using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>Stands in for the local files player and records what it was asked.</summary>
internal sealed class FakeLocalPlayer : ILocalPlayer
{
    private PlayerState _state = PlayerState.Empty with { Source = PlaybackSource.LocalFiles };

    public event EventHandler? StateChanged;

    public event EventHandler<string>? ErrorOccurred;

    public List<string> Commands { get; } = [];

    public List<PlayRequest> Played { get; } = [];

    public List<TrackInfo> Queued { get; } = [];

    /// <summary>The sink the visualiser handed over, or null.</summary>
    public ILocalAudioSink? Sink { get; private set; }

    public PlayerState State => _state;

    public IReadOnlyList<TrackInfo> Upcoming => Queued;

    /// <summary>Changes what the player reports, as if it had changed by itself.</summary>
    public void Report(PlayerState state)
    {
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Fail(string message) => ErrorOccurred?.Invoke(this, message);

    public Task PlayAsync(PlayRequest request)
    {
        Played.Add(request);
        Report(_state with { IsPlaying = true, Title = request.StartTrack?.Title ?? request.Tracks.FirstOrDefault()?.Title });
        return Task.CompletedTask;
    }

    public Task AddToQueueAsync(TrackInfo track)
    {
        Queued.Add(track);
        return Task.CompletedTask;
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
    }

    public void SetAudioSink(ILocalAudioSink? sink) => Sink = sink;

    public Task TogglePlayPauseAsync() => State.IsPlaying ? PauseAsync() : PlayAsync();

    public Task PlayAsync()
    {
        Commands.Add("play");
        Report(_state with { IsPlaying = true });
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        Commands.Add("pause");
        Report(_state with { IsPlaying = false });
        return Task.CompletedTask;
    }

    public Task NextAsync() => Record("next");

    public Task PreviousAsync() => Record("previous");

    public Task SeekAsync(TimeSpan position) => Record($"seek {position.TotalSeconds:0.##}");

    public Task SetVolumeAsync(double volume) => Record($"volume {volume:0.##}");

    public Task SetShuffleAsync(bool shuffle) => Record($"shuffle {(shuffle ? "on" : "off")}");

    public Task SetRepeatAsync(RepeatMode mode) => Record($"repeat {RepeatModes.ToSpotify(mode)}");

    public void Dispose()
    {
    }

    private Task Record(string command)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }
}
