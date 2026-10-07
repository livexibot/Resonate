using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>An <see cref="ILocalAudioEngine"/> that records what it was asked to do instead of making a sound.</summary>
internal sealed class FakeAudioEngine : ILocalAudioEngine
{
    private readonly Lock _gate = new();
    private readonly List<string> _commands = [];

    public event EventHandler<LocalTrackEnded>? TrackEnded;

    public event EventHandler<string>? Failed;

    public TimeSpan Position { get; set; }

    /// <summary>Files that can not be played.</summary>
    public HashSet<string> Broken { get; } = [];

    /// <summary>When set, play, pause and open wait for it, so commands pile up behind them.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(3);

    public string? Next { get; private set; }

    public bool Looping { get; private set; }

    public double? Volume { get; private set; }

    public EqualizerSettings? Equalizer { get; private set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                return [.. _commands];
            }
        }
    }

    public IReadOnlyList<string> Opened => [.. Commands.Where(c => c.StartsWith("open ", StringComparison.Ordinal)).Select(c => c.Split(' ')[1])];

    public async Task<TimeSpan> OpenAsync(string path, TimeSpan position, bool play, CancellationToken cancellationToken)
    {
        Record($"open {path} {position.TotalSeconds} {(play ? "play" : "paused")}");
        await WaitAsync().ConfigureAwait(false);
        if (Broken.Contains(path))
        {
            throw new LocalAudioException($"Can not play {path}.");
        }

        Position = position;
        return Length;
    }

    public async Task PlayAsync()
    {
        Record("play");
        await WaitAsync().ConfigureAwait(false);
    }

    public async Task PauseAsync()
    {
        Record("pause");
        await WaitAsync().ConfigureAwait(false);
    }

    public Task SeekAsync(TimeSpan position)
    {
        Record($"seek {position.TotalSeconds}");
        Position = position;
        return Task.CompletedTask;
    }

    public void SetNext(string? path) => Next = path;

    public void SetLooping(bool looping) => Looping = looping;

    public void SetVolume(double volume)
    {
        Record($"volume {volume}");
        Volume = volume;
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
        Record("equalizer");
        Equalizer = settings;
    }

    public void End(string path, string? nextPath) => TrackEnded?.Invoke(this, new LocalTrackEnded(path, nextPath));

    public void Fail(string message) => Failed?.Invoke(this, message);

    public void Dispose() => Disposed = true;

    private void Record(string command)
    {
        lock (_gate)
        {
            _commands.Add(command);
        }
    }

    private Task WaitAsync() => Hold?.Task ?? Task.CompletedTask;
}

/// <summary>Windows' media controls, as the tests press them.</summary>
internal sealed class FakeSystemControls : ILocalSystemControls
{
    private readonly Lock _gate = new();
    private readonly List<PlayerState?> _shown = [];

    public event EventHandler<LocalControlButton>? ButtonPressed;

    public event EventHandler<TimeSpan>? SeekRequested;

    public event EventHandler<bool>? ShuffleRequested;

    public event EventHandler<RepeatMode>? RepeatRequested;

    public IReadOnlyList<PlayerState?> Shown
    {
        get
        {
            lock (_gate)
            {
                return [.. _shown];
            }
        }
    }

    public Task ShowAsync(PlayerState? state)
    {
        lock (_gate)
        {
            _shown.Add(state);
        }

        return Task.CompletedTask;
    }

    public void Press(LocalControlButton button) => ButtonPressed?.Invoke(this, button);

    public void Seek(TimeSpan position) => SeekRequested?.Invoke(this, position);

    public void Shuffle(bool shuffle) => ShuffleRequested?.Invoke(this, shuffle);

    public void Repeat(RepeatMode mode) => RepeatRequested?.Invoke(this, mode);

    public void Dispose()
    {
    }
}
