using System.Net;
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
public sealed class PlayerController : IDisposable
{
    internal static readonly TimeSpan PlayStateHold = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan PositionHold = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan TrackHold = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan VolumeHold = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan PositionTolerance = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan WebPollInterval = TimeSpan.FromSeconds(5);
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
    private TimeSpan? _pendingSeek;
    private Task _pendingSeekTask = Task.CompletedTask;
    private double? _pendingVolume;
    private Task _pendingVolumeTask = Task.CompletedTask;
    private long _generation;
    private bool _webConnected;
    private bool _needsWebDetails = true;
    private DateTimeOffset _webPausedUntil;
    private bool _started;

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
                if (!await _local.PlayAsync(ct).ConfigureAwait(false))
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
                if (!await _local.PauseAsync(ct).ConfigureAwait(false))
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
                if (!await _local.NextAsync(ct).ConfigureAwait(false))
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
                if (!await _local.PreviousAsync(ct).ConfigureAwait(false))
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
                _pendingVolumeTask = _volumeLane.Enqueue(ct => RunReportingErrorsAsync(SendPendingVolumeAsync, revert: null, ct));
            }
        }

        RaiseStateChanged();
        return _pendingVolumeTask;
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
                var body = contextUri is null
                    ? new StartPlaybackBody { Uris = [track.Uri] }
                    : new StartPlaybackBody { ContextUri = contextUri, Offset = new PlaybackOffset { Uri = track.Uri } };
                try
                {
                    await WithLocalDeviceAsync((id, c) => _api.StartPlaybackAsync(body, id, c), ct).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (
                    contextUri is not null
                    && fallbackUris is { Count: > 0 }
                    && !ex.IsPremiumRequired
                    && ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
                {
                    var fallback = new StartPlaybackBody
                    {
                        Uris = WindowAround(fallbackUris, track.Uri),
                        Offset = new PlaybackOffset { Uri = track.Uri },
                    };
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

    /// <summary>Asks the Web API what is playing and fills in what the local channel can not tell.</summary>
    public async Task RefreshFromWebApiAsync(CancellationToken cancellationToken)
    {
        if (_time.GetUtcNow() < _webPausedUntil)
        {
            return;
        }

        try
        {
            var playback = await _api.GetPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
            ApplyWeb(playback);
        }
        catch (SpotifyAuthException)
        {
            // Not signed in: the local channel still works.
        }
        catch (SpotifyApiException ex) when (ex.RetryAfter is { } wait)
        {
            _webPausedUntil = _time.GetUtcNow() + wait;
        }
        catch (Exception ex) when (ex is SpotifyApiException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Offline or a passing error: try again on the next round.
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

    private void ApplyWeb(PlaybackState? playback)
    {
        // Read the mixer outside the lock; it is a call into the audio service.
        var mixerVolumeKnown = _appVolume.TryGetVolume() is not null;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var s = _state;
            _webConnected = playback?.Device is not null;
            _needsWebDetails = false;
            var item = TrackInfo.From(playback?.Item);
            var localIsTruth = _lastLocal.HasSession && _lastLocal.HasTimeline;

            if (playback is null)
            {
                if (!_lastLocal.HasSession)
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
            localCanSeek = _lastLocal.HasSession && _lastLocal.CanSeek;
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

        if (_appVolume.TrySetVolume(target))
        {
            return;
        }

        var percent = (int)Math.Round(target * 100);
        await WithLocalDeviceAsync((id, c) => _api.SetVolumeAsync(percent, id, c), cancellationToken).ConfigureAwait(false);
    }

    private void ReadAppVolume()
    {
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

    private Task RunTransportAsync(Func<CancellationToken, Task> command, Action? revert) =>
        _transport.Enqueue(ct => RunReportingErrorsAsync(command, revert, ct));

    private async Task RunReportingErrorsAsync(Func<CancellationToken, Task> command, Action? revert, CancellationToken cancellationToken)
    {
        try
        {
            await command(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            revert?.Invoke();
            ErrorOccurred?.Invoke(this, DescribeError(ex));
        }
    }

    internal static string DescribeError(Exception exception) => exception switch
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

    private async Task FetchWebDetailsSoonAsync()
    {
        try
        {
            await Task.Delay(WebDetailsDelay, _time, _stopping.Token).ConfigureAwait(false);
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
            await RefreshFromWebApiAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(WebPollInterval, _time);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ReadAppVolume();
                bool needsWeb;
                lock (_gate)
                {
                    needsWeb = !_lastLocal.HasSession || !_lastLocal.HasTimeline || _needsWebDetails;
                }

                if (needsWeb)
                {
                    await RefreshFromWebApiAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void SetState(PlayerState state) => _state = state;

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct Hold<T>(T Value, DateTimeOffset Until);
}
