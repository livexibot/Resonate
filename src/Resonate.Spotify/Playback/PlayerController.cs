using System.Net;
using System.Text.Json;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// The player behind the player bar. It sends commands through the local
/// media channel when it can (instant, no internet) and through the Web API
/// otherwise, and it is optimistic: every command changes <see cref="State"/>
/// at once, and for a short while afterwards a report from Spotify that
/// still shows the old situation is ignored, so a late answer can not undo
/// what the user just did. A command that really fails is rolled back and
/// reported through <see cref="ErrorOccurred"/>.
/// </summary>
public sealed class PlayerController : IPlayer, IDisposable
{
    internal static readonly TimeSpan PlayStateHold = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan PositionHold = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan TrackHold = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan VolumeHold = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan PositionTolerance = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan WebPollInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan WebOnlyPollWhilePlaying = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan WebOnlyPollWhilePaused = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan WebOnlyConfirmDelay = TimeSpan.FromMilliseconds(700);
    internal static readonly TimeSpan WebDetailsDelay = TimeSpan.FromMilliseconds(800);

    /// <summary>The Web API takes at most this many track URIs in one request.</summary>
    internal const int MaxUrisPerRequest = 100;

    private readonly ILocalMediaChannel _local;
    private readonly IAppVolume _appVolume;
    private readonly ISpotifyWebApi _api;
    private readonly LocalDeviceResolver _devices;
    private readonly ISpotifyAppLauncher? _launcher;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SerialWorker _transport = new();
    private readonly SerialWorker _volumeLane = new();
    private readonly CancellationTokenSource _stopping = new();

    private PlayerState _state = PlayerState.Empty;
    private LocalMediaSnapshot _lastLocal = LocalMediaSnapshot.None;
    private (string? Title, string? Artist, string? Album) _localTrackKey;
    private Hold<bool>? _playingHold;
    private Hold<TimeSpan>? _positionHold;
    private Hold<string>? _trackHold;
    private Hold<double>? _volumeHold;
    private Hold<bool>? _shuffleHold;
    private Hold<RepeatMode>? _repeatHold;
    private TimeSpan? _pendingSeek;
    private Task _pendingSeekTask = Task.CompletedTask;
    private double? _pendingVolume;
    private Task _pendingVolumeTask = Task.CompletedTask;
    private long _generation;
    private long _commandEpoch;
    private int _commandsInFlight;
    private long _webRequests;
    private long _webApplied;
    private bool _webConnected;
    private bool _needsWebDetails = true;
    private DateTimeOffset _webPausedUntil;
    private bool _started;
    private volatile ControlChannel _channel = ControlChannel.Local;

    public PlayerController(
        ILocalMediaChannel local,
        IAppVolume appVolume,
        ISpotifyWebApi api,
        LocalDeviceResolver devices,
        ISpotifyAppLauncher? launcher = null,
        TimeProvider? time = null)
    {
        _local = local;
        _appVolume = appVolume;
        _api = api;
        _devices = devices;
        _launcher = launcher;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised on any thread after <see cref="State"/> changes. Read <see cref="State"/> for the newest value.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on any thread with a sentence to show when a command failed.</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// How commands and reports travel. Can change at any time; the player
    /// switches over at once.
    /// </summary>
    public ControlChannel Channel
    {
        get => _channel;
        set
        {
            if (_channel == value)
            {
                return;
            }

            _channel = value;
            if (!_started)
            {
                return;
            }

            if (value == ControlChannel.Local)
            {
                LocalMediaSnapshot latest;
                lock (_gate)
                {
                    latest = _lastLocal;
                }

                ApplyLocal(latest);
                ReadAppVolume();
            }
            else
            {
                _ = RefreshSoonAsync(TimeSpan.Zero);
            }
        }
    }

    private bool UseLocal => _channel == ControlChannel.Local;

    /// <summary>The local channel's last report, or nothing when the Web API is the only channel.</summary>
    private LocalMediaSnapshot Local => UseLocal ? _lastLocal : LocalMediaSnapshot.None;

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

    /// <summary>Starts listening to the local channel and, when needed, the Web API.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _local.Changed += OnLocalChanged;
        await _local.StartAsync(cancellationToken).ConfigureAwait(false);
        ApplyLocal(_local.Current);
        ReadAppVolume();
        _ = PollWebApiAsync(_stopping.Token);
    }

    private Task<bool> TryLocalAsync(Func<CancellationToken, Task<bool>> command, CancellationToken cancellationToken) =>
        UseLocal ? command(cancellationToken) : Task.FromResult(false);

    public Task TogglePlayPauseAsync() => State.IsPlaying ? PauseAsync() : PlayAsync();

    public Task PlayAsync()
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            _playingHold = new Hold<bool>(true, now + PlayStateHold);
            SetState(_state with { IsPlaying = true, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.PlayAsync, ct).ConfigureAwait(false))
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(null, id, c), ct).ConfigureAwait(false);
                }
            },
            () => RevertPlaying(generation, before.IsPlaying));
    }

    public Task PauseAsync()
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            _playingHold = new Hold<bool>(false, now + PlayStateHold);
            SetState(_state with { IsPlaying = false, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.PauseAsync, ct).ConfigureAwait(false))
                {
                    await WithLocalDeviceAsync(_api.PauseAsync, ct).ConfigureAwait(false);
                }
            },
            () => RevertPlaying(generation, before.IsPlaying));
    }

    public Task NextAsync()
    {
        RestartPositionOptimistically();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.NextAsync, ct).ConfigureAwait(false))
                {
                    await WithLocalDeviceAsync(_api.SkipToNextAsync, ct).ConfigureAwait(false);
                }
            },
            revert: null);
    }

    public Task PreviousAsync()
    {
        // Spotify restarts the song when it is more than a few seconds in, and
        // goes to the previous one otherwise; either way the position is 0.
        RestartPositionOptimistically();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.PreviousAsync, ct).ConfigureAwait(false))
                {
                    await WithLocalDeviceAsync(_api.SkipToPreviousAsync, ct).ConfigureAwait(false);
                }
            },
            revert: null);
    }

    /// <summary>Seeks. While the user drags, only the newest position is sent.</summary>
    public Task SeekAsync(TimeSpan position)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (position < TimeSpan.Zero)
            {
                position = TimeSpan.Zero;
            }
            else if (_state.Duration > TimeSpan.Zero && position > _state.Duration)
            {
                position = _state.Duration;
            }

            _positionHold = new Hold<TimeSpan>(position, now + PositionHold);
            SetState(_state with { Position = position, PositionTimestamp = now });

            var alreadyQueued = _pendingSeek.HasValue;
            _pendingSeek = position;
            if (!alreadyQueued)
            {
                _pendingSeekTask = RunTransportAsync(SendPendingSeekAsync, revert: null);
            }
        }

        RaiseStateChanged();
        return _pendingSeekTask;
    }

    /// <summary>Sets Spotify's volume, from 0 to 1. While the user drags, only the newest value is sent.</summary>
    public Task SetVolumeAsync(double volume)
    {
        lock (_gate)
        {
            volume = Math.Clamp(volume, 0, 1);
            _volumeHold = new Hold<double>(volume, _time.GetUtcNow() + VolumeHold);
            SetState(_state with { Volume = volume });

            var alreadyQueued = _pendingVolume.HasValue;
            _pendingVolume = volume;
            if (!alreadyQueued)
            {
                _pendingVolumeTask = _volumeLane.Enqueue(async ct =>
                {
                    if (await RunReportingErrorsAsync(SendPendingVolumeAsync, revert: null, ct).ConfigureAwait(false))
                    {
                        ConfirmSoonWithoutLocalReports();
                    }
                });
            }
        }

        RaiseStateChanged();
        return _pendingVolumeTask;
    }

    /// <summary>Adds a song to the end of Spotify's queue ("Add to queue").</summary>
    public Task AddToQueueAsync(TrackInfo track)
    {
        if (track.Uri is not { } uri || !track.IsPlayable)
        {
            return Task.CompletedTask;
        }

        return RunTransportAsync(ct => WithLocalDeviceAsync((id, c) => _api.AddToQueueAsync(uri, id, c), ct), revert: null);
    }

    /// <summary>Spotify's own shuffle, through the Web API. Optimistic like every command.</summary>
    public Task SetShuffleAsync(bool shuffle)
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            before = _state;
            generation = ++_generation;
            _shuffleHold = new Hold<bool>(shuffle, _time.GetUtcNow() + PlayStateHold);
            SetState(_state with { Shuffle = shuffle });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            ct => WithLocalDeviceAsync((id, c) => _api.SetShuffleAsync(shuffle, id, c), ct),
            () => RevertSetting(generation, before));
    }

    /// <summary>Repeats nothing, the whole list, or the current song.</summary>
    public Task SetRepeatAsync(RepeatMode mode)
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            before = _state;
            generation = ++_generation;
            _repeatHold = new Hold<RepeatMode>(mode, _time.GetUtcNow() + PlayStateHold);
            SetState(_state with { Repeat = mode });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            ct => WithLocalDeviceAsync((id, c) => _api.SetRepeatAsync(mode, id, c), ct),
            () => RevertSetting(generation, before));
    }

    /// <summary>
    /// Plays <paramref name="track"/> on this computer, inside
    /// <paramref name="contextUri"/> (its playlist or album) when given, so
    /// the songs after it follow. If Spotify refuses the context (it does for
    /// some, such as Liked Songs), the song plays among
    /// <paramref name="fallbackUris"/> instead.
    /// </summary>
    public Task PlayTrackAsync(TrackInfo track, string? contextUri, IReadOnlyList<string>? fallbackUris = null)
    {
        if (track.Uri is null)
        {
            return Task.CompletedTask;
        }

        var body = contextUri is null
            ? new StartPlaybackBody { Uris = [track.Uri] }
            : new StartPlaybackBody { ContextUri = contextUri, Offset = new PlaybackOffset { Uri = track.Uri } };
        var fallback = contextUri is not null && fallbackUris is { Count: > 0 }
            ? new StartPlaybackBody { Uris = WindowAround(fallbackUris, track.Uri), Offset = new PlaybackOffset { Uri = track.Uri } }
            : null;
        return StartTrackAsync(track, contextUri, body, fallback);
    }

    /// <summary>
    /// Plays a list the way a page shows it: inside its Spotify context when
    /// it has one (so Spotify shows where it plays from), otherwise as a list
    /// of songs in the order shown.
    /// </summary>
    public Task PlayAsync(PlayRequest request)
    {
        var playable = request.Tracks.Where(t => t.IsPlayable && t.Uri is not null && t.FilePath is null).ToList();
        var start = request.StartTrack is { IsPlayable: true, Uri: not null } picked ? picked : playable.FirstOrDefault();
        if (start is null)
        {
            return request.ContextUri is null ? Task.CompletedTask : PlayContextAsync(request.ContextUri);
        }

        var uris = playable.Select(t => t.Uri!).ToList();
        if (request.ContextUri is not null)
        {
            return PlayTrackAsync(start, request.ContextUri, uris);
        }

        var body = new StartPlaybackBody { Uris = WindowAround(uris, start.Uri!), Offset = new PlaybackOffset { Uri = start.Uri } };
        return StartTrackAsync(start, null, body, fallback: null);
    }

    private Task StartTrackAsync(TrackInfo track, string? contextUri, StartPlaybackBody body, StartPlaybackBody? fallback)
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            _trackHold = new Hold<string>(track.Title, now + TrackHold);
            _playingHold = new Hold<bool>(true, now + TrackHold);
            _positionHold = new Hold<TimeSpan>(TimeSpan.Zero, now + TrackHold);
            SetState(_state with
            {
                IsConnected = true,
                IsPlaying = true,
                Title = track.Title,
                Artists = track.Artists,
                Album = track.Album,
                TrackUri = track.Uri,
                ContextUri = contextUri,
                ArtworkUrl = track.LargeImageUrl,
                ArtworkBytes = null,
                Duration = track.Duration,
                Position = TimeSpan.Zero,
                PositionTimestamp = now,
            });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            async ct =>
            {
                try
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(body, id, c), ct).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (
                    fallback is not null
                    && !ex.IsPremiumRequired
                    && ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(fallback, id, c), ct).ConfigureAwait(false);
                }
            },
            () => RevertTo(generation, before));
    }

    /// <summary>Plays a whole playlist or album from its start.</summary>
    public Task PlayContextAsync(string contextUri)
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            _playingHold = new Hold<bool>(true, now + TrackHold);
            SetState(_state with { IsConnected = true, IsPlaying = true, ContextUri = contextUri });
        }

        RaiseStateChanged();
        return RunTransportAsync(
            ct => WithLocalDeviceAsync(
                (id, c) => _api.StartPlaybackAsync(new StartPlaybackBody { ContextUri = contextUri }, id, c),
                ct),
            () => RevertTo(generation, before));
    }

    /// <summary>
    /// Puts back what <paramref name="before"/> showed once the Spotify app has
    /// restarted: the same song at <paramref name="position"/>, playing or
    /// paused as it was. Spotify reopens its last song by itself, so when its
    /// media session reports that song (within <paramref name="timeout"/>),
    /// Resonate only moves to the position and plays if it was playing. If
    /// not, a song that was playing is started again through the Web API, in
    /// its playlist or album when it had one; a paused one is left for the
    /// user to pick again (<see cref="ResumeOutcome.NotResumed"/>), because
    /// starting it would make a burst of sound.
    /// </summary>
    public async Task<ResumeOutcome> ResumeAsync(PlayerState before, TimeSpan position, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!before.HasTrack)
        {
            return ResumeOutcome.NothingToResume;
        }

        if (UseLocal
            && await WaitForLocalSessionAsync(timeout, cancellationToken).ConfigureAwait(false) is { } reopened
            && TitlesMatch(reopened.Title, before.Title))
        {
            await SeekAsync(position).ConfigureAwait(false);
            if (!before.IsPlaying)
            {
                return ResumeOutcome.Paused;
            }

            await PlayAsync().ConfigureAwait(false);
            return ResumeOutcome.Playing;
        }

        if (!before.IsPlaying || before.TrackUri is not { } uri)
        {
            return ResumeOutcome.NotResumed;
        }

        var positionMs = (int)Math.Clamp(position.TotalMilliseconds, 0, int.MaxValue);
        var body = before.ContextUri is { } context
            ? new StartPlaybackBody { ContextUri = context, Offset = new PlaybackOffset { Uri = uri }, PositionMs = positionMs }
            : new StartPlaybackBody { Uris = [uri], PositionMs = positionMs };
        var fallback = before.ContextUri is null ? null : new StartPlaybackBody { Uris = [uri], PositionMs = positionMs };

        PlayerState shown;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            shown = _state;
            generation = ++_generation;
            _trackHold = new Hold<string>(before.Title!, now + TrackHold);
            _playingHold = new Hold<bool>(true, now + TrackHold);
            _positionHold = new Hold<TimeSpan>(position, now + TrackHold);
            SetState(_state with
            {
                IsConnected = true,
                IsPlaying = true,
                Title = before.Title,
                Artists = before.Artists,
                Album = before.Album,
                TrackUri = uri,
                ContextUri = before.ContextUri,
                ArtworkUrl = before.ArtworkUrl,
                ArtworkBytes = before.ArtworkBytes,
                Duration = before.Duration,
                Position = position,
                PositionTimestamp = now,
            });
        }

        RaiseStateChanged();
        var started = false;
        await RunTransportAsync(
            async ct =>
            {
                try
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(body, id, c), ct).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (
                    fallback is not null
                    && !ex.IsPremiumRequired
                    && ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(fallback, id, c), ct).ConfigureAwait(false);
                }

                started = true;
            },
            () => RevertTo(generation, shown)).ConfigureAwait(false);
        return started ? ResumeOutcome.Playing : ResumeOutcome.NotResumed;
    }

    /// <summary>The media session's report once Spotify has one with a song, or null after <paramref name="timeout"/>.</summary>
    private async Task<LocalMediaSnapshot?> WaitForLocalSessionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + timeout;
        while (true)
        {
            LocalMediaSnapshot latest;
            lock (_gate)
            {
                latest = _lastLocal;
            }

            if (latest.HasSession && latest.Title is not null)
            {
                return latest;
            }

            if (_time.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asks the Web API what is playing and fills in what the local channel can not tell.</summary>
    public async Task RefreshFromWebApiAsync(CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() < _webPausedUntil)
        {
            return;
        }

        long request;
        long epoch;
        lock (_gate)
        {
            request = ++_webRequests;
            epoch = _commandEpoch;
        }

        try
        {
            var playback = await _api.GetPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
            ApplyWeb(playback, request, epoch);
        }
        catch (SpotifyAuthException)
        {
            // Not signed in: the local channel still works.
        }
        catch (SpotifyApiException ex) when (ex.RetryAfter is { } wait)
        {
            _webPausedUntil = _time.GetUtcNow() + wait;
        }
        catch (Exception ex) when (ex is SpotifyApiException or HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            // Offline, a passing error, or something other than Spotify answering
            // (such as a Wi-Fi sign-in page): try again on the next round.
        }
    }

    public void Dispose()
    {
        _local.Changed -= OnLocalChanged;
        _stopping.Cancel();
        _transport.Dispose();
        _volumeLane.Dispose();
    }

    internal static List<string> WindowAround(IReadOnlyList<string> uris, string uri)
    {
        var index = Math.Max(0, uris.ToList().IndexOf(uri));
        var start = Math.Max(0, Math.Min(index, uris.Count - MaxUrisPerRequest));
        return uris.Skip(start).Take(MaxUrisPerRequest).ToList();
    }

    private static bool TitlesMatch(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static TimeSpan Distance(TimeSpan a, TimeSpan b) => (a - b).Duration();

    private void OnLocalChanged(object? sender, LocalMediaSnapshot snapshot) => ApplyLocal(snapshot);

    private void ApplyLocal(LocalMediaSnapshot snapshot)
    {
        if (!UseLocal)
        {
            lock (_gate)
            {
                // Kept for switching back; the Web API drives the player now.
                _lastLocal = snapshot;
            }

            return;
        }

        var fetchDetails = false;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            _lastLocal = snapshot;
            var s = _state;

            if (!snapshot.HasSession)
            {
                SetState(s with
                {
                    IsConnected = _webConnected,
                    IsPlaying = _webConnected && s.IsPlaying,
                    Position = s.PositionAt(now),
                    PositionTimestamp = now,
                });
            }
            else if (IsHeldForNewTrack(snapshot.Title, now))
            {
                // A report about the previous song while a new one is starting.
                SetState(s with { IsConnected = true });
            }
            else
            {
                var key = (snapshot.Title, snapshot.Artist, snapshot.Album);
                var newTrack = key != _localTrackKey;
                var confirmsOptimisticTrack = newTrack && TitlesMatch(snapshot.Title, s.Title) && s.TrackUri is not null;
                _localTrackKey = key;

                var next = s with { IsConnected = true };
                if (newTrack && !confirmsOptimisticTrack)
                {
                    next = next with
                    {
                        Title = snapshot.Title,
                        Artists = snapshot.Artist,
                        Album = snapshot.Album,
                        TrackUri = null,
                        ArtworkUrl = null,
                        ArtworkBytes = snapshot.Artwork,
                        Duration = snapshot.Duration,
                    };
                    _needsWebDetails = true;
                    fetchDetails = true;
                }
                else
                {
                    if (snapshot.Artwork is not null && next.ArtworkUrl is null)
                    {
                        next = next with { ArtworkBytes = snapshot.Artwork };
                    }

                    if (snapshot.HasTimeline)
                    {
                        next = next with { Duration = snapshot.Duration };
                    }
                }

                var isPlaying = ResolvePlaying(snapshot.IsPlaying, s.IsPlaying, now);
                var position = newTrack && !confirmsOptimisticTrack
                    ? snapshot.PositionAt(now)
                    : ResolvePosition(snapshot.HasTimeline ? snapshot.PositionAt(now) : null, s, now);

                SetState(next with
                {
                    IsPlaying = isPlaying,
                    Position = position,
                    PositionTimestamp = now,
                    CanSeek = next.Duration > TimeSpan.Zero,
                });
            }
        }

        RaiseStateChanged();
        if (fetchDetails)
        {
            _ = FetchWebDetailsSoonAsync();
        }
    }

    /// <param name="request">The number of the request this answers; an older answer than one already shown is dropped.</param>
    /// <param name="epoch">The command epoch when the request was sent.</param>
    private void ApplyWeb(PlaybackState? playback, long request, long epoch)
    {
        // Read the mixer outside the lock; it is a call into the audio service.
        var mixerVolumeKnown = UseLocal && _appVolume.TryGetVolume() is not null;
        lock (_gate)
        {
            if (!IsCurrentAnswer(request, epoch))
            {
                return;
            }

            _webApplied = request;
            var now = _time.GetUtcNow();
            var s = _state;
            _webConnected = playback?.Device is not null;
            _needsWebDetails = false;
            var item = TrackInfo.From(playback?.Item);
            var localIsTruth = Local.HasSession && Local.HasTimeline;

            if (playback is null)
            {
                if (!Local.HasSession)
                {
                    SetState(s with { IsConnected = false, IsPlaying = false, Position = s.PositionAt(now), PositionTimestamp = now });
                }
            }
            else
            {
                SetState(MergeWeb(s, playback, item, localIsTruth, mixerVolumeKnown, now));
            }
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Whether an answer from the Web API may still be shown. An answer to a
    /// request sent before the user's latest command had gone through
    /// describes the situation before that command, so showing it would undo
    /// the command on screen. The holds do not cover this on their own: they
    /// end as soon as Spotify confirms the command, and a slow older answer
    /// can arrive after that. An answer older than one already shown is
    /// dropped too.
    /// </summary>
    private bool IsCurrentAnswer(long request, long epoch) =>
        request > _webApplied && epoch == _commandEpoch && _commandsInFlight == 0;

    private PlayerState MergeWeb(
        PlayerState s,
        PlaybackState playback,
        TrackInfo? item,
        bool localIsTruth,
        bool mixerVolumeKnown,
        DateTimeOffset now)
    {
        var next = s with { IsConnected = true };
        if (localIsTruth)
        {
            // The local channel already drives the player; only add what it lacks.
            if (item is not null && TitlesMatch(item.Title, s.Title))
            {
                next = next with
                {
                    TrackUri = item.Uri,
                    ContextUri = playback.Context?.Uri,
                    ArtworkUrl = s.ArtworkUrl ?? (s.ArtworkBytes is null ? item.LargeImageUrl : null),
                };
            }
        }
        else if (!IsHeldForNewTrack(item?.Title, now))
        {
            var reported = TimeSpan.FromMilliseconds(playback.ProgressMs ?? 0);
            var sameTrack = item is not null && item.Uri == s.TrackUri;
            next = next with
            {
                Title = item?.Title,
                Artists = item?.Artists,
                Album = item?.Album,
                TrackUri = item?.Uri,
                ContextUri = playback.Context?.Uri,
                ArtworkUrl = item?.LargeImageUrl,
                ArtworkBytes = sameTrack ? s.ArtworkBytes : null,
                Duration = item?.Duration ?? TimeSpan.Zero,
                CanSeek = item is not null,
            };
            next = next with
            {
                IsPlaying = ResolvePlaying(playback.IsPlaying, s.IsPlaying, now),
                Position = sameTrack ? ResolvePosition(reported, s, now) : reported,
                PositionTimestamp = now,
            };
        }

        if (!IsHeld(_shuffleHold, now))
        {
            next = next with { Shuffle = playback.ShuffleState };
        }

        if (!IsHeld(_repeatHold, now))
        {
            next = next with { Repeat = playback.Repeat };
        }

        next = next with
        {
            CanShuffle = playback.Actions?.Disallowed("toggling_shuffle") != true,
            CanRepeat = playback.Actions?.Disallowed("toggling_repeat_context") != true,
        };

        if (!mixerVolumeKnown
            && playback.Device?.VolumePercent is int percent
            && !IsHeld(_volumeHold, now))
        {
            next = next with { Volume = percent / 100.0 };
        }

        return next;
    }

    private bool IsHeldForNewTrack(string? reportedTitle, DateTimeOffset now)
    {
        if (_trackHold is not { } hold)
        {
            return false;
        }

        if (TitlesMatch(reportedTitle, hold.Value) || now >= hold.Until)
        {
            _trackHold = null;
            return false;
        }

        return true;
    }

    private bool ResolvePlaying(bool reported, bool shown, DateTimeOffset now)
    {
        if (_playingHold is { } hold && now < hold.Until && reported != hold.Value)
        {
            return shown;
        }

        _playingHold = null;
        return reported;
    }

    private TimeSpan ResolvePosition(TimeSpan? reported, PlayerState shown, DateTimeOffset now)
    {
        var expected = shown.PositionAt(now);
        if (reported is not { } position)
        {
            return expected;
        }

        if (_positionHold is { } hold && now < hold.Until)
        {
            if (Distance(position, expected) > PositionTolerance)
            {
                return expected;
            }
        }

        _positionHold = null;
        return position;
    }

    private static bool IsHeld<T>(Hold<T>? hold, DateTimeOffset now) => hold is { } h && now < h.Until;

    private void RestartPositionOptimistically()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            _positionHold = new Hold<TimeSpan>(TimeSpan.Zero, now + PositionHold);
            SetState(_state with { Position = TimeSpan.Zero, PositionTimestamp = now });
        }

        RaiseStateChanged();
    }

    private async Task SendPendingSeekAsync(CancellationToken cancellationToken)
    {
        TimeSpan target;
        bool localCanSeek;
        lock (_gate)
        {
            target = _pendingSeek ?? TimeSpan.Zero;
            _pendingSeek = null;
            localCanSeek = Local.HasSession && Local.CanSeek;
        }

        if (localCanSeek && await _local.SeekAsync(target, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await WithLocalDeviceAsync((id, c) => _api.SeekAsync(target, id, c), cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPendingVolumeAsync(CancellationToken cancellationToken)
    {
        double target;
        lock (_gate)
        {
            target = _pendingVolume ?? _state.Volume;
            _pendingVolume = null;
        }

        if (UseLocal && _appVolume.TrySetVolume(target))
        {
            return;
        }

        var percent = (int)Math.Round(target * 100);
        await WithLocalDeviceAsync((id, c) => _api.SetVolumeAsync(percent, id, c), cancellationToken).ConfigureAwait(false);
    }

    private void ReadAppVolume()
    {
        if (!UseLocal)
        {
            return;
        }

        var volume = _appVolume.TryGetVolume();
        if (volume is null)
        {
            return;
        }

        lock (_gate)
        {
            if (IsHeld(_volumeHold, _time.GetUtcNow()) || _pendingVolume.HasValue || Math.Abs(_state.Volume - volume.Value) < 0.005)
            {
                return;
            }

            SetState(_state with { Volume = volume.Value });
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Runs <paramref name="call"/> against the Spotify app on this computer.
    /// When that device is not online yet, starts Spotify and waits for it.
    /// </summary>
    private async Task WithLocalDeviceAsync(Func<string?, CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        var deviceId = await _devices.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (deviceId is null)
        {
            deviceId = await WaitForLocalDeviceAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await call(deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.IsNoActiveDevice)
        {
            _devices.Invalidate();
            deviceId = await WaitForLocalDeviceAsync(cancellationToken).ConfigureAwait(false);
            await call(deviceId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> WaitForLocalDeviceAsync(CancellationToken cancellationToken)
    {
        if (_launcher is not null)
        {
            var status = await _launcher.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
            if (status == SpotifyAppStatus.NotInstalled)
            {
                throw new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "The Spotify app is not installed.");
            }
        }

        // A freshly started Spotify takes a few seconds to appear as a device.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            _devices.Invalidate();
            if (await _devices.ResolveAsync(cancellationToken).ConfigureAwait(false) is { } id)
            {
                return id;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), _time, cancellationToken).ConfigureAwait(false);
        }

        throw new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "Spotify on this computer is not online.");
    }

    /// <summary>
    /// Queues a playback command. While it is queued or running, and for any
    /// Web API request sent before it settled, answers from the Web API are
    /// not shown (see <see cref="IsCurrentAnswer"/>).
    /// </summary>
    private Task RunTransportAsync(Func<CancellationToken, Task> command, Action? revert)
    {
        lock (_gate)
        {
            _commandEpoch++;
            _commandsInFlight++;
        }

        return _transport.Enqueue(async ct =>
        {
            var sent = false;
            try
            {
                sent = await RunReportingErrorsAsync(command, revert, ct).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _commandEpoch++;
                    _commandsInFlight--;
                }
            }

            if (sent)
            {
                ConfirmSoonWithoutLocalReports();
            }
        });
    }

    /// <summary>Runs a command; when it fails, puts back what it changed and says why. True when it went through.</summary>
    private async Task<bool> RunReportingErrorsAsync(Func<CancellationToken, Task> command, Action? revert, CancellationToken cancellationToken)
    {
        try
        {
            await command(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
            return false;
        }
        catch (Exception ex)
        {
            revert?.Invoke();
            ErrorOccurred?.Invoke(this, DescribeError(ex));
            return false;
        }
    }

    private void ConfirmSoonWithoutLocalReports()
    {
        if (!UseLocal)
        {
            // No local reports in this mode: ask Spotify how things stand.
            _ = RefreshSoonAsync(WebOnlyConfirmDelay);
        }
    }

    public static string DescribeError(Exception exception) => exception switch
    {
        SpotifyApiException api => api.UserMessage,
        SpotifyAuthException { Error: "not_signed_in" } => "Sign in to Spotify to do that.",
        SpotifyAuthException { RequiresSignIn: true } => "Your Spotify sign-in has expired. Sign in again.",
        SpotifyAuthException auth => auth.Message,
        HttpRequestException or TaskCanceledException => "Spotify could not be reached. Check the internet connection.",
        _ => "Something went wrong talking to Spotify.",
    };

    private void RevertPlaying(long generation, bool wasPlaying)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return;
            }

            _playingHold = null;
            var now = _time.GetUtcNow();
            SetState(_state with { IsPlaying = wasPlaying, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        RaiseStateChanged();
    }

    private void RevertSetting(long generation, PlayerState before)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return;
            }

            _shuffleHold = null;
            _repeatHold = null;
            SetState(_state with { Shuffle = before.Shuffle, Repeat = before.Repeat });
        }

        RaiseStateChanged();
    }

    private void RevertTo(long generation, PlayerState before)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return;
            }

            _trackHold = null;
            _playingHold = null;
            _positionHold = null;
            var now = _time.GetUtcNow();
            SetState(before with { Position = before.PositionAt(now), PositionTimestamp = now, Volume = _state.Volume });
        }

        RaiseStateChanged();
    }

    private Task FetchWebDetailsSoonAsync() => RefreshSoonAsync(WebDetailsDelay);

    private async Task RefreshSoonAsync(TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _time, _stopping.Token).ConfigureAwait(false);
            }

            await RefreshFromWebApiAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task PollWebApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PollOnceAsync(first: true, cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(NextPollDelay(), _time, cancellationToken).ConfigureAwait(false);
                await PollOnceAsync(first: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <param name="first">The first round always asks the Web API; <see cref="StartAsync"/> has just read the mixer.</param>
    private async Task PollOnceAsync(bool first, CancellationToken cancellationToken)
    {
        try
        {
            if (!first)
            {
                ReadAppVolume();
            }

            bool needsWeb;
            lock (_gate)
            {
                needsWeb = first || !UseLocal || !_lastLocal.HasSession || !_lastLocal.HasTimeline || _needsWebDetails;
            }

            if (needsWeb)
            {
                await RefreshFromWebApiAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Never let one odd answer stop the player from following Spotify.
        }
    }

    /// <summary>
    /// With the local channel the Web API only fills gaps. Without it, the
    /// Web API is the only way to see what is playing, so it is asked more
    /// often while music plays (the clock in between runs by itself).
    /// </summary>
    private TimeSpan NextPollDelay() =>
        UseLocal ? WebPollInterval : State.IsPlaying ? WebOnlyPollWhilePlaying : WebOnlyPollWhilePaused;

    private void SetState(PlayerState state) => _state = state;

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct Hold<T>(T Value, DateTimeOffset Until);
}
