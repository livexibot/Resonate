using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// The local files player: Resonate playing the user's own music files
/// (never Spotify's), with its own queue, true shuffle, repeat, the
/// equalizer and Windows' media controls. The sound comes from an
/// <see cref="ILocalAudioEngine"/>; this class keeps the queue and what the
/// player bar shows. Like the Spotify player it is optimistic: every command
/// changes <see cref="State"/> at once, and the engine catches up in the
/// background, in order, so a late answer can not undo what the user did.
/// </summary>
public sealed class LocalPlayer : ILocalPlayer
{
    /// <summary>"Previous" further into a song than this starts it again.</summary>
    internal static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    internal static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DriftTolerance = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan TimelineInterval = TimeSpan.FromSeconds(5);

    private readonly ILocalAudioEngine _engine;
    private readonly ILocalSystemControls? _controls;
    private readonly Func<string, byte[]?> _readCover;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly LocalPlayQueue _queue;

    // Opening, playing and seeking go one after another; volume, the
    // equalizer and the media controls never wait behind a slow open.
    private readonly SerialWorker _transport = new();
    private readonly SerialWorker _side = new();
    private readonly ITimer _clock;

    private PlayerState _state = PlayerState.Empty with { Source = PlaybackSource.LocalFiles, IsConnected = true, CanSeek = true };
    private string? _shownFolder;
    private string? _openPath;
    private long _track;
    private long _epoch;
    private int _failures;
    private TimeSpan? _pendingSeek;
    private Task _pendingSeekTask = Task.CompletedTask;
    private double? _pendingVolume;
    private Task _pendingVolumeTask = Task.CompletedTask;
    private EqualizerSettings? _equalizer;
    private bool _equalizerQueued;
    private bool _controlsEnabled;
    private int _controlsQueued;
    private int _clockQueued;
    private DateTimeOffset _lastTimeline;
    private volatile bool _disposed;

    /// <param name="readCover">Reads a file's cover; <see cref="LocalCovers.Read"/> when null.</param>
    /// <param name="random">A random integer in [0, max) for shuffling (tests pass their own).</param>
    public LocalPlayer(
        ILocalAudioEngine engine,
        ILocalSystemControls? controls = null,
        Func<string, byte[]?>? readCover = null,
        TimeProvider? time = null,
        Func<int, int>? random = null)
    {
        _engine = engine;
        _controls = controls;
        _readCover = readCover ?? LocalCovers.Read;
        _time = time ?? TimeProvider.System;
        _queue = new LocalPlayQueue(random);

        _engine.TrackEnded += OnTrackEnded;
        _engine.Failed += OnEngineFailed;
        if (_controls is not null)
        {
            _controls.ButtonPressed += OnControlButton;
            _controls.SeekRequested += OnControlSeek;
            _controls.ShuffleRequested += OnControlShuffle;
            _controls.RepeatRequested += OnControlRepeat;
        }

        _clock = _time.CreateTimer(_ => OnClock(), null, ClockInterval, ClockInterval);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<string>? ErrorOccurred;

    public PlayerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IReadOnlyList<TrackInfo> Upcoming
    {
        get
        {
            lock (_gate)
            {
                return _queue.Upcoming;
            }
        }
    }

    /// <summary>
    /// Whether Windows' media controls show this player. On only while local
    /// files are what the player bar shows, so the media keys reach the
    /// Spotify app the rest of the time.
    /// </summary>
    public bool SystemControlsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _controlsEnabled;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_controlsEnabled == value)
                {
                    return;
                }

                _controlsEnabled = value;
            }

            UpdateControls();
        }
    }

    public Task PlayAsync(PlayRequest request)
    {
        var tracks = request.Tracks.Where(t => t.FilePath is not null).ToList();
        if (tracks.Count == 0)
        {
            return Task.CompletedTask;
        }

        var start = request.StartTrack is { FilePath: not null } picked ? tracks.FindIndex(t => ReferenceEquals(t, picked)) : -1;
        long track;
        long epoch;
        lock (_gate)
        {
            var first = _queue.Load(tracks, start, request.Shuffle ?? _state.Shuffle)!;
            _failures = 0;
            (track, epoch) = NextTrack();
            SetState(ForTrack(first, playing: true) with { SourceName = request.SourceName });
        }

        Raise();
        return _transport.Enqueue(ct => OpenAsync(track, epoch, TimeSpan.Zero, play: true, ct));
    }

    public Task AddToQueueAsync(TrackInfo track)
    {
        if (track.FilePath is null)
        {
            return Task.CompletedTask;
        }

        long? load = null;
        var epoch = 0L;
        lock (_gate)
        {
            _queue.AddToQueue(track);
            if (_queue.Current is null && _queue.MoveNext(skipped: true) is { } first)
            {
                // Nothing chosen yet: the queued song is ready, paused.
                (var t, epoch) = NextTrack();
                load = t;
                SetState(ForTrack(first, playing: false));
            }
        }

        Raise();
        return _transport.Enqueue(async ct =>
        {
            if (load is { } t)
            {
                await OpenAsync(t, epoch, TimeSpan.Zero, play: false, ct).ConfigureAwait(false);
            }

            UpdateNext();
        });
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
        lock (_gate)
        {
            _equalizer = settings;
            if (_equalizerQueued)
            {
                return;
            }

            _equalizerQueued = true;
        }

        // While a slider moves only the newest setting is applied.
        _ = _side.Enqueue(_ =>
        {
            EqualizerSettings? latest;
            lock (_gate)
            {
                latest = _equalizer;
                _equalizerQueued = false;
            }

            _engine.SetEqualizer(latest);
            return Task.CompletedTask;
        });
    }

    public Task TogglePlayPauseAsync() => State.IsPlaying ? PauseAsync() : PlayAsync();

    public Task PlayAsync()
    {
        long track;
        long epoch;
        TimeSpan position;
        lock (_gate)
        {
            if (_queue.Current is null)
            {
                return Task.CompletedTask;
            }

            var now = _time.GetUtcNow();
            track = _track;
            epoch = ++_epoch;
            position = _state.PositionAt(now);
            SetState(_state with { IsPlaying = true, Position = position, PositionTimestamp = now });
        }

        Raise();
        return _transport.Enqueue(async ct =>
        {
            if (_openPath is null)
            {
                await OpenAsync(track, epoch, position, play: true, ct).ConfigureAwait(false);
                return;
            }

            await _engine.PlayAsync().ConfigureAwait(false);
        });
    }

    public Task PauseAsync()
    {
        long epoch;
        lock (_gate)
        {
            if (_queue.Current is null)
            {
                return Task.CompletedTask;
            }

            var now = _time.GetUtcNow();
            epoch = ++_epoch;
            SetState(_state with { IsPlaying = false, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        Raise();
        return _transport.Enqueue(async _ =>
        {
            if (_openPath is null)
            {
                return;
            }

            await _engine.PauseAsync().ConfigureAwait(false);
            SyncPosition(epoch);
        });
    }

    /// <summary>The next song (the queue first). After the last one, with repeat off, the list goes back to its start, paused.</summary>
    public Task NextAsync() => SkipAsync(back: false);

    /// <summary>The song before, or the start of this one when it has played more than three seconds.</summary>
    public Task PreviousAsync() => SkipAsync(back: true);

    public Task SeekAsync(TimeSpan position)
    {
        lock (_gate)
        {
            if (_queue.Current is null)
            {
                return Task.CompletedTask;
            }

            position = Clamp(position, _state.Duration);
            ++_epoch;
            SetState(_state with { Position = position, PositionTimestamp = _time.GetUtcNow() });

            // While the user drags, only the newest position is sent.
            var alreadyQueued = _pendingSeek.HasValue;
            _pendingSeek = position;
            if (!alreadyQueued)
            {
                _pendingSeekTask = _transport.Enqueue(SendPendingSeekAsync);
            }
        }

        Raise();
        return _pendingSeekTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        lock (_gate)
        {
            volume = Math.Clamp(volume, 0, 1);
            SetState(_state with { Volume = volume });
            var alreadyQueued = _pendingVolume.HasValue;
            _pendingVolume = volume;
            if (!alreadyQueued)
            {
                _pendingVolumeTask = _side.Enqueue(_ =>
                {
                    double latest;
                    lock (_gate)
                    {
                        latest = _pendingVolume ?? _state.Volume;
                        _pendingVolume = null;
                    }

                    _engine.SetVolume(latest);
                    return Task.CompletedTask;
                });
            }
        }

        Raise();
        return _pendingVolumeTask;
    }

    /// <summary>Truly random order from the next song on; the song playing keeps playing.</summary>
    public Task SetShuffleAsync(bool shuffle)
    {
        lock (_gate)
        {
            _queue.SetShuffle(shuffle);
            SetState(_state with { Shuffle = shuffle });
        }

        Raise();
        return _transport.Enqueue(_ =>
        {
            UpdateNext();
            return Task.CompletedTask;
        });
    }

    public Task SetRepeatAsync(RepeatMode mode)
    {
        lock (_gate)
        {
            _queue.Repeat = mode;
            SetState(_state with { Repeat = mode });
        }

        Raise();
        return _transport.Enqueue(_ =>
        {
            _engine.SetLooping(mode == RepeatMode.One);
            UpdateNext();
            return Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _clock.Dispose();
        _engine.TrackEnded -= OnTrackEnded;
        _engine.Failed -= OnEngineFailed;
        if (_controls is not null)
        {
            _controls.ButtonPressed -= OnControlButton;
            _controls.SeekRequested -= OnControlSeek;
            _controls.ShuffleRequested -= OnControlShuffle;
            _controls.RepeatRequested -= OnControlRepeat;
            _controls.Dispose();
        }

        _transport.Dispose();
        _side.Dispose();
        _engine.Dispose();
    }

    /// <summary>Completes once the work queued so far has run (for tests).</summary>
    internal async Task WhenIdleAsync()
    {
        await _transport.Enqueue(_ => Task.CompletedTask).ConfigureAwait(false);
        await _side.Enqueue(_ => Task.CompletedTask).ConfigureAwait(false);
    }

    private Task SkipAsync(bool back)
    {
        long track = 0;
        long epoch;
        var restart = false;
        bool playing;
        lock (_gate)
        {
            if (_queue.Current is not { } current)
            {
                return Task.CompletedTask;
            }

            var now = _time.GetUtcNow();
            epoch = ++_epoch;
            playing = true;
            TrackInfo? song;
            if (back && _state.PositionAt(now) > RestartThreshold)
            {
                song = null;
                restart = true;
                playing = _state.IsPlaying;
            }
            else if (back)
            {
                song = _queue.MovePrevious();
                restart = ReferenceEquals(song, current);
                playing = restart ? _state.IsPlaying : true;
            }
            else
            {
                song = _queue.MoveNext(skipped: true);
                if (song is null)
                {
                    // The end of the list: back to the start, stopped (as Spotify does).
                    song = _queue.ResetToStart();
                    playing = false;
                }
            }

            if (restart)
            {
                SetState(_state with { Position = TimeSpan.Zero, PositionTimestamp = now });
            }
            else
            {
                (track, epoch) = NextTrack();
                SetState(ForTrack(song!, playing));
            }
        }

        Raise();
        if (restart)
        {
            return SeekAsync(TimeSpan.Zero);
        }

        return _transport.Enqueue(ct => OpenAsync(track, epoch, TimeSpan.Zero, playing, ct));
    }

    /// <summary>Opens the current song in the engine (on the transport lane); a song that can not play is skipped.</summary>
    private async Task OpenAsync(long track, long epoch, TimeSpan position, bool play, CancellationToken cancellationToken)
    {
        TrackInfo? song;
        lock (_gate)
        {
            song = track == _track ? _queue.Current : null;
        }

        if (song?.FilePath is not { } path)
        {
            return;
        }

        _ = LoadCoverAsync(track, path);
        try
        {
            var duration = await _engine.OpenAsync(path, position, play, cancellationToken).ConfigureAwait(false);
            _openPath = path;
            lock (_gate)
            {
                _failures = 0;
                if (track == _track)
                {
                    var next = _state with { Duration = duration > TimeSpan.Zero ? duration : _state.Duration };

                    // The sound starts now, not when the song was picked; unless
                    // the user did something since, start the clock from here.
                    if (epoch == _epoch)
                    {
                        next = next with { Position = position, PositionTimestamp = _time.GetUtcNow() };
                    }

                    SetState(next);
                }
            }

            Raise();
            UpdateNext();
        }
        catch (LocalAudioException ex) when (ex.IsDeviceProblem)
        {
            _openPath = null;
            Halt(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _openPath = null;
            await SkipBrokenAsync(track, song, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tells the user the first time a song can not play, then moves on, as Spotify does with a missing file.</summary>
    private async Task SkipBrokenAsync(long track, TrackInfo song, Exception error, CancellationToken cancellationToken)
    {
        string? message = null;
        long next;
        long epoch;
        bool playing;
        lock (_gate)
        {
            if (track != _track)
            {
                return;
            }

            if (_failures == 0)
            {
                message = error is LocalAudioException ? error.Message : $"Resonate could not play “{song.Title}”.";
            }

            _failures++;
            var count = _queue.List.Count + _queue.Queued.Count;
            var following = _failures < Math.Min(count, 25) ? _queue.MoveNext(skipped: true) : null;
            if (following is null)
            {
                SetState(_state with { IsPlaying = false, Position = TimeSpan.Zero, PositionTimestamp = _time.GetUtcNow() });
                next = 0;
                epoch = 0;
                playing = false;
            }
            else
            {
                playing = _state.IsPlaying;
                (next, epoch) = NextTrack();
                SetState(ForTrack(following, playing));
            }
        }

        if (message is not null)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        Raise();
        if (next != 0)
        {
            await OpenAsync(next, epoch, TimeSpan.Zero, playing, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendPendingSeekAsync(CancellationToken cancellationToken)
    {
        TimeSpan target;
        long track;
        long epoch;
        bool playing;
        lock (_gate)
        {
            target = _pendingSeek ?? TimeSpan.Zero;
            _pendingSeek = null;
            track = _track;
            epoch = _epoch;
            playing = _state.IsPlaying;
        }

        if (_openPath is null)
        {
            await OpenAsync(track, epoch, target, playing, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _engine.SeekAsync(target).ConfigureAwait(false);
    }

    /// <summary>Tells the engine which file follows, so it can start it without a gap.</summary>
    private void UpdateNext()
    {
        string? next;
        lock (_gate)
        {
            // "Repeat one" loops inside the engine instead.
            next = _queue.Repeat == RepeatMode.One ? null : _queue.PeekNext()?.FilePath;
        }

        _engine.SetNext(next);
    }

    private void OnTrackEnded(object? sender, LocalTrackEnded ended) =>
        _ = _transport.Enqueue(ct => HandleEndedAsync(ended, ct));

    private async Task HandleEndedAsync(LocalTrackEnded ended, CancellationToken cancellationToken)
    {
        // A file that ended after a newer command replaced it means nothing now.
        if (_openPath is null || !string.Equals(_openPath, ended.Path, StringComparison.Ordinal))
        {
            return;
        }

        long track;
        long epoch;
        TrackInfo song;
        bool playing;
        lock (_gate)
        {
            var next = _queue.MoveNext(skipped: false);
            playing = next is not null;
            song = next ?? _queue.ResetToStart()!;
            (track, epoch) = NextTrack();
            SetState(ForTrack(song, playing));
        }

        Raise();
        if (playing && ended.NextPath is not null && string.Equals(song.FilePath, ended.NextPath, StringComparison.Ordinal))
        {
            // The engine already started it, without a gap.
            _openPath = ended.NextPath;
            _ = LoadCoverAsync(track, ended.NextPath);
            UpdateNext();
            return;
        }

        await OpenAsync(track, epoch, TimeSpan.Zero, playing, cancellationToken).ConfigureAwait(false);
    }

    private void OnEngineFailed(object? sender, string message)
    {
        // Play opens the file again, on whatever sound device there is then.
        _ = _transport.Enqueue(_ =>
        {
            _openPath = null;
            return Task.CompletedTask;
        });
        Halt(message);
    }

    /// <summary>Pauses where the song is and tells the user why.</summary>
    private void Halt(string message)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            ++_epoch;
            SetState(_state with { IsPlaying = false, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        Raise();
        ErrorOccurred?.Invoke(this, message);
    }

    /// <summary>Once a second while playing: keeps the shown position on the engine's, and the media controls' timeline fresh.</summary>
    private void OnClock()
    {
        if (_disposed || !State.IsPlaying || Interlocked.Exchange(ref _clockQueued, 1) == 1)
        {
            return;
        }

        long epoch;
        lock (_gate)
        {
            epoch = _epoch;
        }

        _ = _transport.Enqueue(_ =>
        {
            Interlocked.Exchange(ref _clockQueued, 0);
            if (_openPath is not null)
            {
                SyncPosition(epoch, quietUnlessDrifted: true);
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>Takes the engine's position, unless a command came after <paramref name="epoch"/>.</summary>
    private void SyncPosition(long epoch, bool quietUnlessDrifted = false)
    {
        var position = _engine.Position;
        var changed = false;
        var timeline = false;
        lock (_gate)
        {
            if (epoch != _epoch)
            {
                return;
            }

            var now = _time.GetUtcNow();
            if (!quietUnlessDrifted || (position - _state.PositionAt(now)).Duration() > DriftTolerance)
            {
                SetState(_state with { Position = position, PositionTimestamp = now });
                changed = true;
            }

            if (now - _lastTimeline >= TimelineInterval)
            {
                _lastTimeline = now;
                timeline = true;
            }
        }

        if (changed)
        {
            Raise();
        }
        else if (timeline)
        {
            UpdateControls();
        }
    }

    private async Task LoadCoverAsync(long track, string path)
    {
        byte[]? cover = null;
        try
        {
            cover = await Task.Run(() => _readCover(path)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No cover: the player bar shows the album's colour tile.
        }

        lock (_gate)
        {
            if (track != _track || cover is null || (_state.ArtworkBytes is { } shown && shown.AsSpan().SequenceEqual(cover)))
            {
                return;
            }

            SetState(_state with { ArtworkBytes = cover });
        }

        Raise();
    }

    private void UpdateControls()
    {
        if (_controls is null || _disposed || Interlocked.Exchange(ref _controlsQueued, 1) == 1)
        {
            return;
        }

        _ = _side.Enqueue(async _ =>
        {
            Interlocked.Exchange(ref _controlsQueued, 0);
            PlayerState? shown;
            lock (_gate)
            {
                shown = _controlsEnabled && _queue.Current is not null ? _state : null;
            }

            try
            {
                await _controls.ShowAsync(shown).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The media controls are a convenience; playback goes on without them.
            }
        });
    }

    private void OnControlButton(object? sender, LocalControlButton button) => _ = button switch
    {
        LocalControlButton.Play => PlayAsync(),
        LocalControlButton.Pause or LocalControlButton.Stop => PauseAsync(),
        LocalControlButton.Next => NextAsync(),
        LocalControlButton.Previous => PreviousAsync(),
        _ => Task.CompletedTask,
    };

    private void OnControlSeek(object? sender, TimeSpan position) => _ = SeekAsync(position);

    private void OnControlShuffle(object? sender, bool shuffle) => _ = SetShuffleAsync(shuffle);

    private void OnControlRepeat(object? sender, RepeatMode mode) => _ = SetRepeatAsync(mode);

    /// <summary>A new song: work still queued for the one before is dropped. Call under the lock.</summary>
    private (long Track, long Epoch) NextTrack() => (++_track, ++_epoch);

    /// <summary>The state for a song from its start. Call under the lock.</summary>
    private PlayerState ForTrack(TrackInfo track, bool playing)
    {
        // The next song of the same album usually has the same cover: keep
        // it until the new one is read, so the player bar does not blink.
        var folder = Path.GetDirectoryName(track.FilePath);
        var sameAlbum = track.Album.Length > 0
            && track.Album == _state.Album
            && string.Equals(folder, _shownFolder, StringComparison.Ordinal);
        _shownFolder = folder;
        return _state with
        {
            Title = track.Title,
            Artists = track.Artists,
            Album = track.Album,
            TrackUri = track.Uri,
            ContextUri = null,
            ArtworkUrl = null,
            ArtworkBytes = sameAlbum ? _state.ArtworkBytes : null,
            Position = TimeSpan.Zero,
            PositionTimestamp = _time.GetUtcNow(),
            Duration = track.Duration,
            IsPlaying = playing,
            Shuffle = _queue.Shuffle,
            Repeat = _queue.Repeat,
        };
    }

    private void SetState(PlayerState state) => _state = state;

    private void Raise()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        UpdateControls();
    }

    private static TimeSpan Clamp(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero && position > duration ? duration : position;
    }
}
