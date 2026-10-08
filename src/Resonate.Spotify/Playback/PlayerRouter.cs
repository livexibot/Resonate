using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// The one player the interface talks to. It forwards to whichever player
/// has the music: the Spotify app (<see cref="PlayerController"/>) or the
/// local files player. Starting one pauses the other, and when the other
/// starts playing by itself (a media key, a phone controlling Spotify) it
/// takes over the player bar.
/// </summary>
public sealed partial class PlayerRouter : IPlayer, IDisposable
{
    private readonly ILocalPlayer _local;
    private IPlayer _active;

    public PlayerRouter(PlayerController spotify, ILocalPlayer? local = null)
    {
        Spotify = spotify;
        _local = local ?? new NoLocalPlayer();
        _active = spotify;

        spotify.StateChanged += OnPlayerStateChanged;
        spotify.ErrorOccurred += OnPlayerError;
        _local.StateChanged += OnPlayerStateChanged;
        _local.ErrorOccurred += OnPlayerError;
    }

    /// <summary>Raised on any thread after <see cref="State"/> changes (or the active player changed).</summary>
    public event EventHandler? StateChanged;

    public event EventHandler<string>? ErrorOccurred;

    /// <summary>The Spotify side, for what only Spotify has (the control channel, contexts).</summary>
    public PlayerController Spotify { get; }

    public ILocalPlayer Local => _local;

    public PlaybackSource ActiveSource => Volatile.Read(ref _active) == _local ? PlaybackSource.LocalFiles : PlaybackSource.Spotify;

    public PlayerState State => Volatile.Read(ref _active).State;

    private IPlayer Active => Volatile.Read(ref _active);

    /// <summary>Plays a list: local files with the local player, everything else in the Spotify app.</summary>
    public Task PlayAsync(PlayRequest request)
    {
        if (request.IsLocalFiles)
        {
            Activate(_local);
            return _local.PlayAsync(request);
        }

        Activate(Spotify);
        return Spotify.PlayAsync(request);
    }

    /// <summary>Plays a whole Spotify playlist or album from its start (for lists whose songs Spotify does not share).</summary>
    public Task PlayContextAsync(string contextUri)
    {
        Activate(Spotify);
        return Spotify.PlayContextAsync(contextUri);
    }

    /// <summary>Raised on any thread after Resonate added a song to a queue, so a queue view can show it.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Adds a song to the queue of the player that plays such songs (files to the local player, the rest to Spotify).</summary>
    public async Task AddToQueueAsync(TrackInfo track)
    {
        if (track.FilePath is not null)
        {
            await _local.AddToQueueAsync(track).ConfigureAwait(false);
        }
        else
        {
            await Spotify.AddToQueueAsync(track).ConfigureAwait(false);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task TogglePlayPauseAsync() => Active.TogglePlayPauseAsync();

    public Task PlayAsync() => Active.PlayAsync();

    public Task PauseAsync() => Active.PauseAsync();

    public Task NextAsync() => Active.NextAsync();

    public Task PreviousAsync() => Active.PreviousAsync();

    public Task SeekAsync(TimeSpan position) => Active.SeekAsync(position);

    public Task SetVolumeAsync(double volume) => Active.SetVolumeAsync(volume);

    public Task SetShuffleAsync(bool shuffle) => Active.SetShuffleAsync(shuffle);

    public Task SetRepeatAsync(RepeatMode mode) => Active.SetRepeatAsync(mode);

    public void Dispose()
    {
        Spotify.StateChanged -= OnPlayerStateChanged;
        Spotify.ErrorOccurred -= OnPlayerError;
        _local.StateChanged -= OnPlayerStateChanged;
        _local.ErrorOccurred -= OnPlayerError;
    }

    /// <summary>Makes <paramref name="player"/> the one the bar shows, pausing the other if it plays.</summary>
    private void Activate(IPlayer player)
    {
        var previous = Interlocked.Exchange(ref _active, player);
        if (previous == player)
        {
            return;
        }

        if (previous.State.IsPlaying)
        {
            _ = previous.PauseAsync();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (sender is not IPlayer player)
        {
            return;
        }

        if (player != Active)
        {
            // The other player started by itself: it takes over.
            if (player.State.IsPlaying)
            {
                Activate(player);
            }

            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerError(object? sender, string message) => ErrorOccurred?.Invoke(this, message);
}
