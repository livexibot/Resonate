using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Plays the user's own music files (Local Files), with its own queue,
/// shuffle and repeat. Spotify's music never goes through it: Spotify's app
/// plays every Spotify song, so lossless playback stays untouched.
/// </summary>
public interface ILocalPlayer : IPlayer, IDisposable
{
    /// <summary>
    /// Raised (on any thread) when a list played with <see cref="PlayRequest.HandsOver"/>
    /// ends: its last song ended or was skipped, or the rest would not play.
    /// The player then stays paused where it is.
    /// </summary>
    event EventHandler<PlayRequest>? ListEnded;

    /// <summary>Plays <paramref name="request"/>'s songs (all must have a <see cref="TrackInfo.FilePath"/>).</summary>
    Task PlayAsync(PlayRequest request);

    /// <summary>Adds a song after the ones queued so far.</summary>
    Task AddToQueueAsync(TrackInfo track);

    /// <summary>Takes out the songs added with "Add to queue" that have not played yet (a mixed list plays them in its own order).</summary>
    IReadOnlyList<TrackInfo> TakeQueued();

    /// <summary>The songs that play next, in order (the queue first, then the rest of the list).</summary>
    IReadOnlyList<TrackInfo> Upcoming { get; }

    /// <summary>Applies the equalizer to what this player plays; null or switched off plays the files unchanged.</summary>
    void SetEqualizer(EqualizerSettings? settings);

    /// <summary>Lets <paramref name="sink"/> hear what this player plays (for the visualiser); null stops.</summary>
    void SetAudioSink(ILocalAudioSink? sink);
}

/// <summary>For platforms and tests without a local files player.</summary>
public sealed class NoLocalPlayer : ILocalPlayer
{
    public event EventHandler<PlayRequest>? ListEnded
    {
        add { }
        remove { }
    }

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

    public IReadOnlyList<TrackInfo> TakeQueued() => [];

    public void SetEqualizer(EqualizerSettings? settings)
    {
    }

    public void SetAudioSink(ILocalAudioSink? sink)
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
