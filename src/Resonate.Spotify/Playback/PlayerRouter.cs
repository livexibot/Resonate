using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// The one player the interface talks to. It forwards to whichever player
/// has the music: the Spotify app (<see cref="PlayerController"/>) or the
/// local files player. Starting one pauses the other, and when the other
/// starts playing by itself (a media key, a phone controlling Spotify) it
/// takes over the player bar. A list that mixes Spotify's songs with files
/// from this computer plays in turns on both (see <see cref="MixedList"/>).
/// </summary>
public sealed partial class PlayerRouter : IPlayer, IDisposable
{
    private readonly ILocalPlayer _local;
    private readonly TimeProvider _time;
    private readonly Func<int, int>? _nextInt;
    private IPlayer _active;

    /// <param name="time">The clock (tests pass their own).</param>
    /// <param name="nextInt">Random numbers for shuffling a mixed list, in [0, max) (tests pass their own).</param>
    public PlayerRouter(PlayerController spotify, ILocalPlayer? local = null, TimeProvider? time = null, Func<int, int>? nextInt = null)
    {
        Spotify = spotify;
        _local = local ?? new NoLocalPlayer();
        _time = time ?? TimeProvider.System;
        _nextInt = nextInt;
        _active = spotify;

        spotify.StateChanged += OnPlayerStateChanged;
        spotify.ErrorOccurred += OnPlayerError;
        spotify.ListEnded += OnStretchEnded;
        _local.StateChanged += OnPlayerStateChanged;
        _local.ErrorOccurred += OnPlayerError;
        _local.ListEnded += OnStretchEnded;
    }

    /// <summary>Raised on any thread after <see cref="State"/> changes (or the active player changed).</summary>
    public event EventHandler? StateChanged;

    public event EventHandler<string>? ErrorOccurred;

    /// <summary>The Spotify side, for what only Spotify has (the control channel, contexts).</summary>
    public PlayerController Spotify { get; }

    public ILocalPlayer Local => _local;

    public PlaybackSource ActiveSource => Volatile.Read(ref _active) == _local ? PlaybackSource.LocalFiles : PlaybackSource.Spotify;

    /// <summary>What the active player shows; while a mixed list plays, with its shuffle, repeat and name.</summary>
    public PlayerState State => WithMix(Volatile.Read(ref _active).State);

    private IPlayer Active => Volatile.Read(ref _active);

    /// <summary>
    /// Raised (on the caller's thread) as Resonate starts playing a list or
    /// a whole playlist, with what it plays from (<see cref="PlayRequest.SourceName"/>,
    /// null for a whole playlist), before it plays: the DJ ends its run when
    /// the user picks something else (the owner's request, 11 October 2026).
    /// </summary>
    public event EventHandler<string?>? ListStarting;

    /// <summary>
    /// Plays a list: local files with the local player, Spotify's songs in
    /// the Spotify app, and a list with both in turns on each. A playlist's
    /// local songs play from their files in Local Files (see <see cref="LocalSongs"/>).
    /// </summary>
    public async Task PlayAsync(PlayRequest request)
    {
        ListStarting?.Invoke(this, request.SourceName);
        var shown = State;
        var ended = EndMix();
        if (ended is not null)
        {
            // The mixed list's shuffle and repeat were the player bar's: they stay.
            request = request with { Shuffle = request.Shuffle ?? ended.Shuffle };
        }

        if (request.IsLocalFiles)
        {
            KeepRepeat(ended);
            Activate(_local);
            await _local.PlayAsync(request).ConfigureAwait(false);
            return;
        }

        if (await PlanAsync(request).ConfigureAwait(false) is { } plan)
        {
            var shuffle = request.Shuffle ?? shown.Shuffle;
            if (plan.HasFiles)
            {
                if (plan.MissingPick is { } missing)
                {
                    ErrorOccurred?.Invoke(this, NotFound(missing));
                }

                if (plan.HasSpotifySongs)
                {
                    await StartMixAsync(new MixedList(plan.Songs, plan.Start, shuffle, shown.Repeat, request.SourceName, _nextInt)).ConfigureAwait(false);
                    return;
                }

                // Only files (the playlist's other songs could not be found): the local player plays them.
                KeepRepeat(ended);
                Activate(_local);
                await _local.PlayAsync(new PlayRequest(plan.Songs, plan.Start, ContextUri: null, request.SourceName) { Shuffle = shuffle }).ConfigureAwait(false);
                return;
            }

            if (plan.MissingPick is { } notFound && (Spotify.Channel == ControlChannel.WebApi || request.ContextUri is null))
            {
                // Neither Spotify's web player nor a list outside the playlist's
                // own order can play it (the Spotify app may, inside its playlist):
                // the next song Spotify can start plays instead.
                ErrorOccurred?.Invoke(this, NotFound(notFound));
                request = request with { StartIndex = NextStartable(request.Tracks, request.StartIndex) };
            }
        }

        KeepRepeat(ended);
        Activate(Spotify);
        await Spotify.PlayAsync(request).ConfigureAwait(false);
    }

    /// <summary>Plays a whole Spotify playlist or album from its start (for lists whose songs Spotify does not share).</summary>
    public Task PlayContextAsync(string contextUri)
    {
        ListStarting?.Invoke(this, null);
        KeepRepeat(EndMix());

        Activate(Spotify);
        return Spotify.PlayContextAsync(contextUri);
    }

    /// <summary>Raised on any thread after Resonate added a song to a queue, so a queue view can show it.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Adds a song to the queue of the player that plays such songs (files to the local player, the rest to Spotify), or to a mixed list.</summary>
    public async Task AddToQueueAsync(TrackInfo track)
    {
        if (AddToMix(track, playNext: false))
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

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

    public Task TogglePlayPauseAsync() => State.IsPlaying ? PauseAsync() : PlayAsync();

    public Task PlayAsync() => RestartFinishedMix() ?? Active.PlayAsync();

    public Task PauseAsync() => Active.PauseAsync();

    public Task NextAsync() => NextInMix() ?? Active.NextAsync();

    public Task PreviousAsync() => PreviousInMix() ?? Active.PreviousAsync();

    public Task SeekAsync(TimeSpan position) => Active.SeekAsync(position);

    public Task SetVolumeAsync(double volume) => Active.SetVolumeAsync(volume);

    public Task SetShuffleAsync(bool shuffle) => ShuffleMix(shuffle) ?? Active.SetShuffleAsync(shuffle);

    public Task SetRepeatAsync(RepeatMode mode) => RepeatMix(mode) ?? Active.SetRepeatAsync(mode);

    public void Dispose()
    {
        Spotify.StateChanged -= OnPlayerStateChanged;
        Spotify.ErrorOccurred -= OnPlayerError;
        Spotify.ListEnded -= OnStretchEnded;
        _local.StateChanged -= OnPlayerStateChanged;
        _local.ErrorOccurred -= OnPlayerError;
        _local.ListEnded -= OnStretchEnded;
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
            // The other player started by itself: it takes over (and a mixed list is over).
            if (player.State.IsPlaying)
            {
                EndMix();
                Activate(player);
            }

            return;
        }

        FollowMix(player);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerError(object? sender, string message) => ErrorOccurred?.Invoke(this, message);

    /// <summary>The first song at or after <paramref name="index"/> that Spotify can start, or -1 (the start).</summary>
    private static int NextStartable(IReadOnlyList<TrackInfo> tracks, int index)
    {
        for (var i = Math.Max(0, index); i < tracks.Count; i++)
        {
            if (ListSession.CanStartByUri(tracks[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NotFound(TrackInfo song) =>
        $"“{song.Title}” isn't in Local Files. Add its folder in Settings, Playback, Local Files to play it.";
}
