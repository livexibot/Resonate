using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Plays the user's own music files (Local Files), with its own queue,
/// shuffle and repeat. Spotify's music never goes through it: Spotify's app
/// plays every Spotify song, so lossless playback stays untouched.
/// </summary>
public interface ILocalPlayer : IPlayer, IDisposable
{
    /// <summary>Plays <paramref name="request"/>'s songs (all must have a <see cref="TrackInfo.FilePath"/>).</summary>
    Task PlayAsync(PlayRequest request);

    /// <summary>Adds a song after the ones queued so far.</summary>
    Task AddToQueueAsync(TrackInfo track);

    /// <summary>The songs that play next, in order (the queue first, then the rest of the list).</summary>
    IReadOnlyList<TrackInfo> Upcoming { get; }

    /// <summary>Applies the equalizer to what this player plays; null or switched off plays the files unchanged.</summary>
    void SetEqualizer(EqualizerSettings? settings);
}

/// <summary>For platforms and tests without a local files player.</summary>
public sealed class NoLocalPlayer : ILocalPlayer
{
    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? ErrorOccurred
    {
        add { }
        remove { }
    }

    public PlayerState State => PlayerState.Empty with { Source = PlaybackSource.LocalFiles };

    public IReadOnlyList<TrackInfo> Upcoming => [];

    public Task PlayAsync(PlayRequest request) => Task.CompletedTask;

    public Task AddToQueueAsync(TrackInfo track) => Task.CompletedTask;

    public void SetEqualizer(EqualizerSettings? settings)
    {
    }

    public Task TogglePlayPauseAsync() => Task.CompletedTask;

    public Task PlayAsync() => Task.CompletedTask;

    public Task PauseAsync() => Task.CompletedTask;

    public Task NextAsync() => Task.CompletedTask;

    public Task PreviousAsync() => Task.CompletedTask;

    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;

    public Task SetVolumeAsync(double volume) => Task.CompletedTask;

    public Task SetShuffleAsync(bool shuffle) => Task.CompletedTask;

    public Task SetRepeatAsync(RepeatMode mode) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
