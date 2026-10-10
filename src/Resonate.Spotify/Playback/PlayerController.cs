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
/// With <see cref="ControlChannel.WebApi"/> it never uses the Spotify app
/// on this computer (which <see cref="SpotifyAppKeeper"/> closes): it does
/// not listen to its media session, read its mixer volume or start it, and
/// commands go to whichever Spotify Connect device plays (see
/// <see cref="WebDeviceResolver"/>).
/// </summary>
public sealed partial class PlayerController : IPlayer, IDisposable
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
    internal static readonly TimeSpan OwnPlayerRefreshDelay = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan ShuffleConfirmInterval = TimeSpan.FromMilliseconds(300);
    internal const int ShuffleConfirmAttempts = 4;

    /// <summary>How many answers about another song a new song's details wait through (one poll each).</summary>
    internal const int MaxWebDetailsMisses = 4;

    /// <summary>
    /// Resonate sends at most this many song addresses in one play command.
    /// Spotify documents no limit; other apps found that about 800 are
    /// refused and long lists can stall, so lists play in windows this long.
    /// </summary>
    internal const int MaxUrisPerRequest = 100;

    private readonly ILocalMediaChannel _local;
    private readonly IAppVolume _appVolume;
    private readonly ISpotifyWebApi _api;
    private readonly LocalDeviceResolver _devices;
    private readonly WebDeviceResolver _webDevices;
    private readonly ISpotifyAppLauncher? _launcher;
    private readonly TimeProvider _time;
    private readonly Func<int, int>? _nextInt;
    private readonly Lock _gate = new();
    private readonly SerialWorker _transport = new();
    private readonly SerialWorker _volumeLane = new();

    // Starting and stopping the local channel when the channel changes, in order and off the caller's thread.
    private readonly SerialWorker _localLane = new();
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

    /// <summary>Answers since the song changed that still described another song; the details are asked for again a few times.</summary>
    private int _webDetailsMisses;
    private DateTimeOffset _webPausedUntil;
    private bool _started;
    private int _ownPlayerRefreshQueued;
    private volatile ControlChannel _channel = ControlChannel.Local;

    /// <summary>The list Resonate plays and knows the songs of; null when the music came from elsewhere.</summary>
    private ListSession? _session;

    /// <summary>The song that played last time Resonate ran, shown until Spotify says what plays; Play carries on from it.</summary>
    private LastPlayed? _resumeFrom;

    /// <summary>Spotify's own shuffle setting, as last reported or set; null until known.</summary>
    private bool? _spotifyShuffle;

    /// <summary>Until then, a report of Spotify's own shuffle may still be from before Resonate changed it.</summary>
    private DateTimeOffset _spotifyShuffleSetUntil;

    /// <summary>The context a list without a plan was started in, for <see cref="PlayerState.SourceName"/>.</summary>
    private string? _sourceContext;

    /// <summary>Until then, Spotify may still describe what played before Resonate sent a list.</summary>
    private DateTimeOffset _sessionSettledAt;

    /// <summary>See <see cref="UserCommandCount"/>.</summary>
    private long _userCommands;

    // Resonate's own player, commanded directly while it plays (see TryDirect).
    private readonly IDirectPlayer? _direct;

    // The device Spotify said plays, with "Spotify Web API only"; under _gate.
    private string? _webDeviceId;

    /// <param name="nextInt">Random numbers for shuffling, in [0, max); the system's cryptographic generator when null (tests pass their own).</param>
    public PlayerController(
        ILocalMediaChannel local,
        IAppVolume appVolume,
        ISpotifyWebApi api,
        LocalDeviceResolver devices,
        ISpotifyAppLauncher? launcher = null,
        TimeProvider? time = null,
        Func<int, int>? nextInt = null,
        WebDeviceResolver? webDevices = null,
        IDirectPlayer? direct = null)
    {
        _direct = direct;
        _local = local;
        _appVolume = appVolume;
        _api = api;
        _devices = devices;
        _webDevices = webDevices ?? new WebDeviceResolver(api, Environment.MachineName);
        _launcher = launcher;
        _time = time ?? TimeProvider.System;
        _nextInt = nextInt;
    }

    /// <summary>Raised on any thread after <see cref="State"/> changes. Read <see cref="State"/> for the newest value.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on any thread with a sentence to show when a command failed.</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// How commands and reports travel. Can change at any time; the player
    /// switches over at once, and starts or stops listening to Spotify's
    /// media session in the background.
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
                _ = _localLane.Enqueue(StartLocalAsync);
            }
            else
            {
                _ = _localLane.Enqueue(_ =>
                {
                    StopLocal();
                    return Task.CompletedTask;
                });
                _ = RefreshSoonAsync(TimeSpan.Zero);
            }
        }
    }

    /// <summary>
    /// The Spotify Connect device the user picked last (by name), used with
    /// "Spotify Web API only" when nothing plays yet.
    /// </summary>
    public string? PreferredDeviceName
    {
        get => _webDevices.PreferredName;
        set => _webDevices.PreferredName = value;
    }

    /// <summary>
    /// What Resonate's own player says it plays, shown at once with "Spotify
    /// Web API only" (the owner asked for less delay, 9 October 2026), as if
    /// Spotify had answered: the same holds keep what the user just did on
    /// screen. Only while it is the device that plays, or starts playing.
    /// </summary>
    public void ApplyOwnPlayerState(PlaybackState playback)
    {
        if (!_started || UseLocal || playback.Device?.Id is not { } device)
        {
            return;
        }

        long request;
        long epoch;
        lock (_gate)
        {
            if (!playback.IsPlaying && _webDeviceId is not null && _webDeviceId != device)
            {
                return;
            }

            request = ++_webRequests;
            epoch = _commandEpoch;
        }

        ApplyWeb(playback, request, epoch);
    }

    /// <summary>
    /// Sends <paramref name="action"/> straight to Resonate's own player when
    /// it is the device that plays ("Spotify Web API only"): it acts at once,
    /// with no trip to Spotify's servers. False otherwise.
    /// </summary>
    private bool TryDirect(string action, double value = 0)
    {
        if (UseLocal || _direct?.DeviceId is not { } own)
        {
            return false;
        }

        lock (_gate)
        {
            if (_webDeviceId != own)
            {
                return false;
            }
        }

        return _direct.TryControl(action, value);
    }

    /// <summary>
    /// Asks Spotify what plays shortly, instead of at the next poll, with
    /// "Spotify Web API only": Resonate's own player calls it when its song
    /// or play state changed. Several calls close together ask once.
    /// </summary>
    /// <summary>
    /// Shows the song that played last time Resonate ran, paused where it was,
    /// while nothing else is known (the owner's request, 10 October 2026).
    /// Spotify's own answer replaces it; Play carries on from it.
    /// </summary>
    public void ShowLastPlayed(LastPlayed? last)
    {
        if (last is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_state.Title is not null)
            {
                return;
            }

            _resumeFrom = last;
            SetState(last.ToState(_state, _time.GetUtcNow()));
        }

        RaiseStateChanged();
    }

    public void RefreshSoon()
    {
        if (!_started || UseLocal || Interlocked.Exchange(ref _ownPlayerRefreshQueued, 1) == 1)
        {
            return;
        }

        _ = RefreshAfterOwnPlayerAsync();
    }

    /// <summary>
    /// How many commands so far changed what plays or where (play, pause,
    /// skip, seek, a new song or list). Read it before a slow job that ends
    /// by putting music back (<see cref="ResumeAsync"/>): when it has grown
    /// meanwhile, the user did something, and that wins.
    /// </summary>
    public long UserCommandCount => Interlocked.Read(ref _userCommands);

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

    /// <summary>Starts following Spotify: through the local channel (unless the Web API is the only channel) and the Web API.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (UseLocal)
        {
            await _localLane.Enqueue(StartLocalAsync).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _ = PollWebApiAsync(_stopping.Token);
    }

    /// <summary>Starts listening to Spotify's media session and shows what it reports.</summary>
    private async Task StartLocalAsync(CancellationToken cancellationToken)
    {
        if (!UseLocal)
        {
            return;
        }

        _local.Changed -= OnLocalChanged;
        _local.Changed += OnLocalChanged;
        try
        {
            await _local.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorOccurred?.Invoke(this, "Resonate could not reach Windows' media controls; playback goes through Spotify's servers instead.");
            return;
        }

        if (UseLocal)
        {
            ApplyLocal(_local.Current);
            ReadAppVolume();
        }
    }

    /// <summary>Stops listening to Spotify's media session (Web API only).</summary>
    private void StopLocal()
    {
        if (UseLocal)
        {
            return;
        }

        _local.Changed -= OnLocalChanged;
        _local.Stop();
        lock (_gate)
        {
            _lastLocal = LocalMediaSnapshot.None;
            _localTrackKey = default;
        }

        _devices.Invalidate();
    }

    /// <summary>
    /// Sends a command through Windows' media controls, or returns false so
    /// the caller uses the Web API: also while Spotify's session has no song
    /// in it (Spotify just started, or it went blank), when Spotify takes a
    /// local play or skip and does nothing.
    /// </summary>
    private Task<bool> TryLocalAsync(Func<CancellationToken, Task<bool>> command, CancellationToken cancellationToken)
    {
        bool hasSong;
        lock (_gate)
        {
            hasSong = HasSong(_lastLocal);
        }

        if (!UseLocal || !hasSong)
        {
            PlaybackLog.Note(UseLocal ? "local: no song in Spotify's session, using the Web API" : "web api only");
            return Task.FromResult(false);
        }

        return command(cancellationToken);
    }

    /// <summary>Spotify's session describes a song (it can be there with nothing in it).</summary>
    private static bool HasSong(LocalMediaSnapshot snapshot) => snapshot.HasSession && !string.IsNullOrEmpty(snapshot.Title);

    public Task TogglePlayPauseAsync() => State.IsPlaying ? PauseAsync() : PlayAsync();

    public Task PlayAsync() => PlayAsync(byUser: true);

    /// <param name="byUser">False when Resonate puts back what played (see <see cref="UserCommandCount"/>).</param>
    private Task PlayAsync(bool byUser)
    {
        if (byUser)
        {
            CountUserCommand();
        }

        PlayerState before;
        long generation;
        LastPlayed? resume;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            _playingHold = new Hold<bool>(true, now + PlayStateHold);

            // The song kept from last time, still shown and with nothing playing it: Spotify is asked to play it from its place.
            resume = _resumeFrom is { } last && last.TrackUri == _state.TrackUri && !_webConnected && !HasSong(Local) ? last : null;
            _resumeFrom = null;
            if (resume is not null)
            {
                resume.PositionMs = (long)_state.Position.TotalMilliseconds;
            }

            SetState(_state with { IsPlaying = true, Position = _state.PositionAt(now), PositionTimestamp = now });
        }

        RaiseStateChanged();
        var edits = TakeUpNextForPlay();
        return RunTransportAsync(
            async ct =>
            {
                if (edits is not null)
                {
                    // Up next: the edited order starts where the song was paused, in one command.
                    await WithDeviceAsync((id, c) => SendUpNextAsync(edits, id, c), ct, starts: true).ConfigureAwait(false);
                }
                else if (resume is not null)
                {
                    await PlayOnFromAsync(resume, ct).ConfigureAwait(false);
                }
                else if (!await TryLocalAsync(_local.PlayAsync, ct).ConfigureAwait(false) && !TryDirect("resume"))
                {
                    await WithDeviceAsync((id, c) => _api.StartPlaybackAsync(null, id, c), ct, starts: true).ConfigureAwait(false);
                }
            },
            () => RevertPlaying(generation, before.IsPlaying));
    }

    /// <summary>Carries on with the song kept from last time: in its list where Spotify still has it, else by itself, at its place.</summary>
    private async Task PlayOnFromAsync(LastPlayed last, CancellationToken cancellationToken)
    {
        var bodies = last.Bodies();
        if (bodies.InList is { } inList)
        {
            try
            {
                await WithDeviceAsync((id, c) => _api.StartPlaybackAsync(inList, id, c), cancellationToken, starts: true).ConfigureAwait(false);
                return;
            }
            catch (SpotifyApiException) when (!cancellationToken.IsCancellationRequested)
            {
                // The list is gone or no longer has the song: the song alone.
            }
        }

        await WithDeviceAsync((id, c) => _api.StartPlaybackAsync(bodies.Alone, id, c), cancellationToken, starts: true).ConfigureAwait(false);
    }

    public Task PauseAsync()
    {
        CountUserCommand();
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
                if (!await TryLocalAsync(_local.PauseAsync, ct).ConfigureAwait(false) && !TryDirect("pause"))
                {
                    await PauseOnDeviceAsync(ct).ConfigureAwait(false);
                }
            },
            () => RevertPlaying(generation, before.IsPlaying));
    }

    public Task NextAsync()
    {
        if (NextInUpNext() is { } edited)
        {
            return edited;
        }

        CountUserCommand();
        RestartPositionOptimistically();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.NextAsync, ct).ConfigureAwait(false) && !TryDirect("next"))
                {
                    await WithDeviceAsync(_api.SkipToNextAsync, ct).ConfigureAwait(false);
                }
            },
            revert: null);
    }

    public Task PreviousAsync()
    {
        CountUserCommand();

        // Spotify restarts the song when it is more than a few seconds in, and
        // goes to the previous one otherwise; either way the position is 0.
        RestartPositionOptimistically();
        return RunTransportAsync(
            async ct =>
            {
                if (!await TryLocalAsync(_local.PreviousAsync, ct).ConfigureAwait(false) && !TryDirect("previous"))
                {
                    await WithDeviceAsync(_api.SkipToPreviousAsync, ct).ConfigureAwait(false);
                }
            },
            revert: null);
    }

    /// <summary>Seeks. While the user drags, only the newest position is sent.</summary>
    public Task SeekAsync(TimeSpan position) => SeekAsync(position, byUser: true);

    /// <param name="position">Where to go in the song.</param>
    /// <param name="byUser">False when Resonate puts back what played (see <see cref="UserCommandCount"/>).</param>
    private Task SeekAsync(TimeSpan position, bool byUser)
    {
        if (byUser)
        {
            CountUserCommand();
        }

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

    /// <summary>
    /// Adds a song to the end of Spotify's queue ("Add to queue"). Files from
    /// the computer can not be queued: Spotify refuses their addresses.
    /// </summary>
    public Task AddToQueueAsync(TrackInfo track)
    {
        if (!ListSession.CanStartByUri(track))
        {
            return Task.CompletedTask;
        }

        var uri = track.Uri!;
        return RunTransportAsync(ct => WithDeviceAsync((id, c) => _api.AddToQueueAsync(uri, id, c), ct), revert: null);
    }

    /// <summary>
    /// Shuffle. While a list Resonate knows plays, this is Resonate's truly
    /// random order: the rest of the list is planned again after the current
    /// song (which keeps playing where it is). Otherwise (music started
    /// elsewhere, a list whose songs Spotify does not share, or a playlist or
    /// album Resonate has only the first part of) it is Spotify's own
    /// shuffle. Optimistic like every command.
    /// </summary>
    public Task SetShuffleAsync(bool shuffle)
    {
        PlayerState before;
        long generation;
        ListSession? previous;
        ListSession? next = null;
        StartPlaybackBody? body = null;
        lock (_gate)
        {
            if (SpotifyDj.IsPlaying(_state) || (_session is { } live && live.Shuffle == shuffle))
            {
                return Task.CompletedTask;
            }

            var now = _time.GetUtcNow();
            before = _state;
            generation = ++_generation;
            previous = _session;
            _shuffleHold = new Hold<bool>(shuffle, now + PlayStateHold);
            var state = _state with { Shuffle = shuffle };
            if (previous is { InContext: true, IsPartial: true })
            {
                // Spotify plays the playlist or album itself, and Resonate has only its
                // first songs: an order made from them would leave the rest out, so
                // Spotify shuffles it.
                _session = null;
                _sourceContext = previous.ContextUri;
            }
            else if (previous is not null)
            {
                next = previous.WithShuffle(shuffle, _state.IsPlaying);
                _session = next;
                _sessionSettledAt = now + TrackHold;
                if (!next.StartsWithNextSong)
                {
                    body = next.StartBody();
                    _positionHold = new Hold<TimeSpan>(_state.PositionAt(now), now + PositionHold);
                    state = state with { ContextUri = next.InContext ? next.ContextUri : null };
                }
            }

            SetState(state);
        }

        RaiseStateChanged();
        if (next is null)
        {
            return RunTransportAsync(
                ct => WithDeviceAsync(
                    async (id, c) =>
                    {
                        await _api.SetShuffleAsync(shuffle, id, c).ConfigureAwait(false);
                        NoteSpotifyShuffle(shuffle);
                    },
                    ct),
                () =>
                {
                    lock (_gate)
                    {
                        if (_generation == generation && _session is null)
                        {
                            // The list set aside above, if any, plays on as before.
                            _session = previous;
                        }
                    }

                    RevertSetting(generation, before);
                });
        }

        if (body is null)
        {
            // The new order starts with the next song, or once paused music plays again (see ListSession.StartsWithNextSong).
            return Task.CompletedTask;
        }

        return RunTransportAsync(
            ct => WithDeviceAsync(
                async (id, c) =>
                {
                    await TrySetSpotifyShuffleAsync(false, id, c).ConfigureAwait(false);
                    await RestartAsync(body, id, c).ConfigureAwait(false);
                },
                ct,
                starts: true),
            () =>
            {
                lock (_gate)
                {
                    if (_session == next)
                    {
                        _session = previous;
                    }
                }

                RevertSetting(generation, before);
            });
    }

    /// <summary>
    /// Repeats nothing, the whole list, or the current song, with Spotify's
    /// own repeat. A long or shuffled list Resonate plays is looped by
    /// Resonate too: it starts another pass as the last song starts.
    /// </summary>
    public Task SetRepeatAsync(RepeatMode mode)
    {
        PlayerState before;
        long generation;
        lock (_gate)
        {
            if (SpotifyDj.IsPlaying(_state))
            {
                return Task.CompletedTask;
            }

            before = _state;
            generation = ++_generation;
            _repeatHold = new Hold<RepeatMode>(mode, _time.GetUtcNow() + PlayStateHold);
            SetState(_state with { Repeat = mode });
        }

        RaiseStateChanged();
        var sent = RunTransportAsync(
            ct => WithDeviceAsync((id, c) => _api.SetRepeatAsync(mode, id, c), ct),
            () => RevertSetting(generation, before));

        // "Repeat all" switched on during a list's last song: the next pass follows.
        FollowSession();
        return sent;
    }

    /// <summary>
    /// Moves the music to another Spotify Connect device ("Spotify Web API
    /// only"), playing there if it was playing here. Shown at once; the
    /// device is remembered for when nothing plays.
    /// </summary>
    public Task TransferToAsync(string deviceId, string deviceName)
    {
        CountUserCommand();
        PlayerState before;
        long generation;
        bool play;
        lock (_gate)
        {
            before = _state;
            generation = ++_generation;
            play = _state.IsPlaying;
            SetState(_state with { DeviceName = deviceName, IsConnected = true });
        }

        PreferredDeviceName = deviceName;
        RaiseStateChanged();
        return RunTransportAsync(
            ct => _api.TransferPlaybackAsync(deviceId, play, ct),
            () =>
            {
                lock (_gate)
                {
                    if (_generation == generation)
                    {
                        SetState(_state with { DeviceName = before.DeviceName });
                    }
                }

                RaiseStateChanged();
            });
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
        return StartAsync(
            track,
            contextUri,
            session: null,
            sourceName: null,
            shuffle: null,
            async (id, c) =>
            {
                try
                {
                    await _api.StartPlaybackAsync(body, id, c).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (fallback is not null && IsRefusedContext(ex))
                {
                    await _api.StartPlaybackAsync(fallback, id, c).ConfigureAwait(false);
                }
            });
    }

    /// <summary>
    /// Plays a list the way a page shows it. When Resonate knows its songs it
    /// keeps a plan (<see cref="ListSession"/>): in the list's own order
    /// inside its Spotify context when it has one, otherwise as songs in the
    /// order shown, or shuffled in a truly random order that starts with the
    /// picked song. Spotify's own shuffle is switched off for these, so the
    /// order is exactly Resonate's. When Resonate does not know the songs,
    /// Spotify plays the context with its own shuffle.
    /// </summary>
    public Task PlayAsync(PlayRequest request)
    {
        var shuffle = request.Shuffle ?? State.Shuffle;
        if (request.StartTrack is { IsLocal: true } && request.ContextUri is null)
        {
            ErrorOccurred?.Invoke(this, "Spotify plays files from your computer only inside their playlist, in the playlist's own order.");
        }

        var session = ListSession.Create(request, shuffle, _nextInt);
        if (session is null)
        {
            return PlayInContextAsync(request);
        }

        var body = session.StartBody();
        return StartAsync(
            session.Current,
            session.InContext ? session.ContextUri : null,
            session,
            request.SourceName,
            shuffle: null,
            async (id, c) =>
            {
                var shuffleOff = await TrySetSpotifyShuffleAsync(false, id, c).ConfigureAwait(false);
                try
                {
                    await _api.StartPlaybackAsync(body, id, c).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (session.InContext && IsRefusedContext(ex))
                {
                    // Spotify will not start this context (it refuses some, such as
                    // Liked Songs at times): play the same songs as a list instead.
                    var songs = session.WithoutContext();
                    lock (_gate)
                    {
                        if (_session == session)
                        {
                            _session = songs;
                            SetState(_state with { ContextUri = null });
                        }
                    }

                    await _api.StartPlaybackAsync(songs.StartBody(), id, c).ConfigureAwait(false);
                }

                if (!shuffleOff)
                {
                    // Spotify had nothing active to switch before; now it has.
                    await TrySetSpotifyShuffleAsync(false, id, c).ConfigureAwait(false);
                }
            });
    }

    /// <summary>Plays a whole playlist or album from its start.</summary>
    public Task PlayContextAsync(string contextUri) =>
        StartAsync(
            track: null,
            contextUri,
            session: null,
            sourceName: null,
            shuffle: null,
            (id, c) => _api.StartPlaybackAsync(new StartPlaybackBody { ContextUri = contextUri }, id, c));

    /// <summary>
    /// A list Resonate does not know the songs of (a playlist Spotify will not
    /// list, a lone song standing for its album): Spotify plays the context
    /// itself, with its own shuffle switched as the request asks.
    /// </summary>
    private Task PlayInContextAsync(PlayRequest request)
    {
        if (request.ContextUri is not { } contextUri)
        {
            // Nothing Spotify can start (only files from the computer, outside their playlist).
            return Task.CompletedTask;
        }

        var picked = request.StartTrack;
        PlaybackOffset? offset = picked switch
        {
            not null when ListSession.CanStartByUri(picked) => new PlaybackOffset { Uri = picked.Uri },
            { IsLocal: true } => new PlaybackOffset { Position = picked.Position ?? request.StartIndex },
            _ => null,
        };
        var body = new StartPlaybackBody { ContextUri = contextUri, Offset = offset };
        var shuffle = request.Shuffle;
        return StartAsync(
            picked,
            contextUri,
            session: null,
            request.SourceName,
            shuffle,
            async (id, c) =>
            {
                var set = shuffle is not { } value || await TrySetSpotifyShuffleAsync(value, id, c).ConfigureAwait(false);
                await _api.StartPlaybackAsync(body, id, c).ConfigureAwait(false);
                if (!set)
                {
                    await TrySetSpotifyShuffleAsync(shuffle!.Value, id, c).ConfigureAwait(false);
                }
            });
    }

    /// <summary>
    /// Shows <paramref name="track"/> (or just "playing") at once, makes
    /// <paramref name="session"/> the list that plays, and sends
    /// <paramref name="send"/>; if it fails, what was playing comes back.
    /// </summary>
    /// <param name="shuffle">What the shuffle button shows from now on, when the start changes it.</param>
    private Task StartAsync(
        TrackInfo? track,
        string? contextUri,
        ListSession? session,
        string? sourceName,
        bool? shuffle,
        Func<string?, CancellationToken, Task> send)
    {
        CountUserCommand();
        PlayerState before;
        ListSession? previous;
        long generation;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            before = _state;
            previous = _session;
            generation = ++_generation;
            _session = session;
            _sessionSettledAt = now + TrackHold;
            _sourceContext = session is null ? contextUri : null;
            _playingHold = new Hold<bool>(true, now + TrackHold);
            var state = _state with { IsConnected = true, IsPlaying = true, ContextUri = contextUri, SourceName = sourceName };
            if (session is not null)
            {
                state = state with { CanShuffle = true };
                shuffle = session.Shuffle;
            }

            if (shuffle is { } shown)
            {
                _shuffleHold = new Hold<bool>(shown, now + TrackHold);
                state = state with { Shuffle = shown };
            }

            if (track is not null)
            {
                _trackHold = new Hold<string>(track.Title, now + TrackHold);
                _positionHold = new Hold<TimeSpan>(TimeSpan.Zero, now + TrackHold);
                state = state with
                {
                    Title = track.Title,
                    Artists = track.Artists,
                    Album = track.Album,
                    TrackUri = track.Uri,
                    ArtworkUrl = track.LargeImageUrl,
                    ArtworkBytes = null,
                    FullArtworkUrl = track.FullImageUrl,
                    Duration = track.Duration,
                    Position = TimeSpan.Zero,
                    PositionTimestamp = now,
                };
            }

            SetState(state);
        }

        RaiseStateChanged();
        return RunTransportAsync(ct => WithDeviceAsync(send, ct, starts: true), () => RevertTo(generation, before, previous));
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
    /// starting it would make a burst of sound. So is DJ, which only the
    /// Spotify app can start.
    /// </summary>
    /// <param name="stillWanted">
    /// Asked before each step. False once the user played, paused or picked
    /// something else since the restart began (see <see cref="UserCommandCount"/>):
    /// then nothing is put back, because what the user did wins.
    /// </param>
    public async Task<ResumeOutcome> ResumeAsync(
        PlayerState before,
        TimeSpan position,
        TimeSpan timeout,
        Func<bool> stillWanted,
        CancellationToken cancellationToken)
    {
        if (!before.HasTrack)
        {
            return ResumeOutcome.NothingToResume;
        }

        var reopened = UseLocal ? await WaitForLocalSessionAsync(timeout, cancellationToken).ConfigureAwait(false) : null;
        var commands = UserCommandCount;
        if (!stillWanted())
        {
            return ResumeOutcome.NothingToResume;
        }

        if (reopened is { } local && TitlesMatch(local.Title, before.Title))
        {
            await SeekAsync(position, byUser: false).ConfigureAwait(false);
            if (!before.IsPlaying)
            {
                return ResumeOutcome.Paused;
            }

            if (!stillWanted())
            {
                return ResumeOutcome.NothingToResume;
            }

            await PlayAsync(byUser: false).ConfigureAwait(false);
            return ResumeOutcome.Playing;
        }

        if (!before.IsPlaying || before.TrackUri is not { } uri)
        {
            return ResumeOutcome.NotResumed;
        }

        if (SpotifyDj.IsPlaying(before))
        {
            // Only the Spotify app can start DJ: the Web API declines it, and the music stops.
            return ResumeOutcome.NotResumed;
        }

        var positionMs = (int)Math.Clamp(position.TotalMilliseconds, 0, int.MaxValue);
        StartPlaybackBody body;
        StartPlaybackBody? fallback = null;
        PlayerState shown;
        ListSession? session;
        long generation;
        lock (_gate)
        {
            if (UserCommandCount != commands)
            {
                // The user did something just now: that wins.
                return ResumeOutcome.NothingToResume;
            }

            session = _session;
            if (session is not null && session.Locate(uri, before.Title) is var at and >= 0)
            {
                // Inside the list Resonate plays, so the rest of the list follows it.
                session.Index = at;
                body = session.StartBody();
            }
            else
            {
                body = before.ContextUri is { } context
                    ? new StartPlaybackBody { ContextUri = context, Offset = new PlaybackOffset { Uri = uri } }
                    : new StartPlaybackBody { Uris = [uri] };
                fallback = before.ContextUri is null ? null : new StartPlaybackBody { Uris = [uri], PositionMs = positionMs };
            }

            body.PositionMs = positionMs;
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
                FullArtworkUrl = before.FullArtworkUrl,
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
                    await WithDeviceAsync((id, c) => _api.StartPlaybackAsync(body, id, c), ct, starts: true).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (fallback is not null && IsRefusedContext(ex))
                {
                    await WithDeviceAsync((id, c) => _api.StartPlaybackAsync(fallback, id, c), ct, starts: true).ConfigureAwait(false);
                }

                started = true;
            },
            () => RevertTo(generation, shown, session)).ConfigureAwait(false);
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

    /// <summary>
    /// Follows the list Resonate plays as Spotify moves through it: notes
    /// which song plays, starts a waiting plan when the next song starts (or
    /// the paused song plays again), and sends the next window (or, with
    /// "repeat all", the next pass) as the last song Spotify has starts,
    /// restarting it where it is. Nothing is sent while the music is paused:
    /// Spotify's play command would start it.
    /// </summary>
    private void FollowSession()
    {
        if (FollowUpNext())
        {
            return;
        }

        StartPlaybackBody? body = null;
        var shuffleOff = false;
        lock (_gate)
        {
            if (_session is not { } session || !_state.HasTrack)
            {
                return;
            }

            var located = session.Locate(_state.TrackUri, _state.Title);
            session.PlayingOther = located < 0;
            if (located < 0)
            {
                return;
            }

            if (!_state.IsPlaying)
            {
                // The next report after the music plays again sends what is due.
                session.Index = located;
                return;
            }

            var now = _time.GetUtcNow();
            var position = _state.PositionAt(now);
            if (session.StartsWithNextSong)
            {
                if (located == session.WaitingFrom && !ListSession.CanStartByUri(session.Order[located]))
                {
                    // Still the song Spotify can not restart by its address.
                    session.Index = located;
                    return;
                }

                var next = session.StartingAt(located);
                _session = next;
                if (next.StartsWithNextSong)
                {
                    return;
                }

                body = next.StartBody();
                shuffleOff = true;
            }
            else
            {
                session.Index = located;
                body = session.ContinueAfterWindow(_state.Repeat);
            }

            if (body is not null)
            {
                _positionHold = new Hold<TimeSpan>(position, now + PositionHold);
                _sessionSettledAt = now + TrackHold;
            }
        }

        if (body is not null)
        {
            _ = RunTransportAsync(
                ct => WithDeviceAsync(
                    async (id, c) =>
                    {
                        if (shuffleOff)
                        {
                            await TrySetSpotifyShuffleAsync(false, id, c).ConfigureAwait(false);
                        }

                        await RestartAsync(body, id, c).ConfigureAwait(false);
                    },
                    ct,
                    starts: true),
                revert: null);
        }
    }

    /// <summary>
    /// Sends a new plan for the music that already plays: the current song
    /// restarts at the position it has reached, so the switch is barely
    /// heard. Repeat is set again in case Spotify forgets it with a new list.
    /// </summary>
    /// <param name="fromStart">The song was switched to and starts from its beginning.</param>
    private async Task RestartAsync(StartPlaybackBody body, string? deviceId, CancellationToken cancellationToken, bool fromStart = false)
    {
        PlayerState state;
        lock (_gate)
        {
            state = _state;
        }

        var position = state.PositionAt(_time.GetUtcNow());
        body.PositionMs = !fromStart && position > TimeSpan.FromMilliseconds(500) ? (int)position.TotalMilliseconds : null;
        await _api.StartPlaybackAsync(body, deviceId, cancellationToken).ConfigureAwait(false);
        if (state.Repeat != RepeatMode.Off)
        {
            try
            {
                await _api.SetRepeatAsync(state.Repeat, deviceId, cancellationToken).ConfigureAwait(false);
            }
            catch (SpotifyApiException)
            {
                // The music plays; repeat stays as Spotify has it.
            }
        }
    }

    /// <summary>
    /// Switches Spotify's own shuffle before a list starts (unless it is
    /// known to be so already), and waits a moment until Spotify reports it:
    /// Spotify does not promise to run player commands in the order sent, and
    /// a list started while its own shuffle is on begins at a random song.
    /// False when Spotify refused (for example with nothing active yet); the
    /// caller tries again once the music plays.
    /// </summary>
    private async Task<bool> TrySetSpotifyShuffleAsync(bool shuffle, string? deviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_spotifyShuffle == shuffle)
            {
                return true;
            }
        }

        try
        {
            await _api.SetShuffleAsync(shuffle, deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (SpotifyApiException)
        {
            return false;
        }

        NoteSpotifyShuffle(shuffle);
        for (var attempt = 0; attempt < ShuffleConfirmAttempts; attempt++)
        {
            PlaybackState? playback;
            try
            {
                playback = await _api.GetPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (playback is null || playback.ShuffleState == shuffle)
            {
                break;
            }

            await Task.Delay(ShuffleConfirmInterval, _time, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private void NoteSpotifyShuffle(bool shuffle)
    {
        lock (_gate)
        {
            _spotifyShuffle = shuffle;
            _spotifyShuffleSetUntil = _time.GetUtcNow() + TrackHold;
        }
    }

    /// <summary>Spotify would not start the context itself (not a missing Premium).</summary>
    private static bool IsRefusedContext(SpotifyApiException ex) =>
        !ex.IsPremiumRequired && ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden;

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
            PlaybackLog.Note($"web: {(int)ex.StatusCode} {(ex.IsQuotaExceeded ? "quota exceeded" : "too many requests")}, waiting {wait.TotalSeconds:0} s");
            _webPausedUntil = _time.GetUtcNow() + wait;
        }
        catch (Exception ex) when (ex is SpotifyApiException or HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            // Offline, a passing error, or something other than Spotify answering
            // (such as a Wi-Fi sign-in page): try again on the next round.
            PlaybackLog.Note($"web: asking what plays failed: {DescribeError(ex)}");
        }
    }

    public void Dispose()
    {
        _local.Changed -= OnLocalChanged;
        _stopping.Cancel();
        _upNextTimer?.Dispose();
        _transport.Dispose();
        _volumeLane.Dispose();
        _localLane.Dispose();
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
            var before = _lastLocal;
            _lastLocal = snapshot;
            var s = _state;
            if (HasSong(before) != HasSong(snapshot) || before.HasSession != snapshot.HasSession || before.IsPlaying != snapshot.IsPlaying)
            {
                PlaybackLog.Note($"local: session {(snapshot.HasSession ? "yes" : "no")}, song {(HasSong(snapshot) ? "yes" : "no")}, {(snapshot.IsPlaying ? "playing" : "paused")}");
            }

            if (!HasSong(snapshot))
            {
                // No session, or one with nothing in it: what plays is not
                // known here, so the song shown stays and the Web API says
                // what plays (asked at once when the session just went blank).
                fetchDetails = HasSong(before);
                SetState(s with
                {
                    IsConnected = snapshot.HasSession || _webConnected,
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
                        FullArtworkUrl = null,
                        Duration = snapshot.Duration,
                    };
                    _needsWebDetails = true;
                    _webDetailsMisses = 0;
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
        FollowSession();
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
            _webDeviceId = playback?.Device?.Id;
            var item = TrackInfo.From(playback?.Item);
            var localIsTruth = HasSong(Local) && Local.HasTimeline;

            // Spotify's answer often still describes the song before; the next poll asks again.
            _needsWebDetails = localIsTruth && _needsWebDetails && item is not null && !TitlesMatch(item.Title, s.Title)
                && ++_webDetailsMisses < MaxWebDetailsMisses;

            NoteWeb(playback, item);
            if (playback is null)
            {
                if (!HasSong(Local))
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
        FollowSession();
    }

    private (bool Device, bool Item, bool Playing)? _lastWebNote;

    /// <summary>Notes in the playback log when Spotify's answer changes (not every poll).</summary>
    private void NoteWeb(PlaybackState? playback, TrackInfo? item)
    {
        var now = (playback?.Device is not null, item is not null, playback?.IsPlaying == true);
        if (_lastWebNote != now)
        {
            _lastWebNote = now;
            PlaybackLog.Note(playback is null
                ? "web: nothing active (no device playing)"
                : $"web: device {(now.Item1 ? "yes" : "no")}, song {(now.Item2 ? "yes" : "no")}, {(now.Item3 ? "playing" : "paused")}");
        }
    }
    /// <summary>
    /// Whether Spotify now plays something other than the list Resonate
    /// started: another playlist or album (from the Spotify app, a phone), or
    /// Spotify's own shuffle switched on there. Not shortly after Resonate
    /// started the list, when Spotify may still describe what played before.
    /// </summary>
    private bool IsElsewhere(ListSession session, PlaybackState playback, TrackInfo? item, DateTimeOffset now)
    {
        if (now < _sessionSettledAt)
        {
            return false;
        }

        if (playback.ShuffleState && now >= _spotifyShuffleSetUntil)
        {
            return true;
        }

        var context = playback.Context?.Uri;
        if (context is null || SameContext(context, session.InContext || session.EditsPending ? session.ContextUri : null))
        {
            return false;
        }

        // An unfamiliar context counts only when the song is not one of the list's.
        return IsListContext(context) || item is null || session.Locate(item.Uri, item.Title) < 0;
    }

    /// <summary>The same context; Liked Songs has more than one name.</summary>
    private static bool SameContext(string? a, string? b) =>
        a == b || (a is not null && b is not null && IsLikedSongs(a) && IsLikedSongs(b));

    private static bool IsLikedSongs(string uri) => uri.EndsWith(":collection", StringComparison.Ordinal) || uri.Contains(":collection:", StringComparison.Ordinal);

    private static bool IsListContext(string uri) =>
        IsLikedSongs(uri)
        || uri.StartsWith("spotify:playlist:", StringComparison.Ordinal)
        || uri.StartsWith("spotify:album:", StringComparison.Ordinal)
        || uri.StartsWith("spotify:artist:", StringComparison.Ordinal)
        || uri.StartsWith("spotify:show:", StringComparison.Ordinal);

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
        var next = s with { IsConnected = true, DeviceName = playback.Device?.Name ?? s.DeviceName };
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
                    FullArtworkUrl = item.FullImageUrl,
                };
            }
        }
        else if (item is null && s.Title is not null)
        {
            // A device with no song (between songs, or long after a pause):
            // the song shown stays, only the playing state follows.
            next = next with { IsPlaying = ResolvePlaying(playback.IsPlaying, s.IsPlaying, now) };
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
                FullArtworkUrl = item?.FullImageUrl,
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

        _spotifyShuffle = playback.ShuffleState;
        if (_session is { } session && IsElsewhere(session, playback, item, now))
        {
            // The music no longer comes from the list Resonate started.
            _session = null;
            next = next with { SourceName = null };
        }
        else if (_session is null && s.SourceName is not null && playback.Context?.Uri is { } context && !SameContext(context, _sourceContext))
        {
            next = next with { SourceName = null };
        }

        if (_session is { } live)
        {
            // Resonate's own shuffle: Spotify's is off on purpose and says so.
            next = next with { Shuffle = live.Shuffle };
        }
        else if (!IsHeld(_shuffleHold, now))
        {
            next = next with { Shuffle = playback.ShuffleState };
        }

        if (!IsHeld(_repeatHold, now))
        {
            next = next with { Repeat = playback.Repeat };
        }

        // Spotify forbids shuffle and repeat during DJ. Resonate's own
        // shuffle does not need Spotify's permission.
        var dj = playback.Context?.Uri == SpotifyDj.ContextUri;
        next = next with
        {
            CanShuffle = !dj && (_session is not null || playback.Actions?.Disallowed("toggling_shuffle") != true),
            CanRepeat = !dj && playback.Actions?.Disallowed("toggling_repeat_context") != true,
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

        if ((localCanSeek && await _local.SeekAsync(target, cancellationToken).ConfigureAwait(false)) || TryDirect("seek", target.TotalMilliseconds))
        {
            return;
        }

        await WithDeviceAsync((id, c) => _api.SeekAsync(target, id, c), cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPendingVolumeAsync(CancellationToken cancellationToken)
    {
        double target;
        lock (_gate)
        {
            target = _pendingVolume ?? _state.Volume;
            _pendingVolume = null;
        }

        if ((UseLocal && _appVolume.TrySetVolume(target)) || TryDirect("volume", target))
        {
            return;
        }

        var percent = (int)Math.Round(target * 100);
        await WithDeviceAsync((id, c) => _api.SetVolumeAsync(percent, id, c), cancellationToken).ConfigureAwait(false);
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
    /// Runs <paramref name="call"/> with the device it should go to: the
    /// Spotify app on this computer, or with "Spotify Web API only" whichever
    /// device plays (see <see cref="WithWebDeviceAsync"/>).
    /// </summary>
    /// <param name="starts">The call starts music (play, a new song or list), rather than changing what plays.</param>
    private Task WithDeviceAsync(Func<string?, CancellationToken, Task> call, CancellationToken cancellationToken, bool starts = false) =>
        UseLocal ? WithLocalDeviceAsync(call, cancellationToken) : WithWebDeviceAsync(call, starts, cancellationToken);

    /// <summary>
    /// "Spotify Web API only": never the Spotify app on this computer unless
    /// Spotify lists it as a device, and never started by Resonate. While a
    /// device plays, commands carry no device, so Spotify sends them to it
    /// and nothing moves. With nothing active, music starts on the device
    /// <see cref="WebDeviceResolver"/> picks, and a command that needs a
    /// device (skip, seek) first wakes that device. Tried again once.
    /// </summary>
    private async Task WithWebDeviceAsync(Func<string?, CancellationToken, Task> call, bool starts, CancellationToken cancellationToken)
    {
        bool active;
        lock (_gate)
        {
            active = _webConnected;
        }

        string? deviceId = null;
        if (starts && !active)
        {
            deviceId = (await _webDevices.ChooseAsync(cancellationToken).ConfigureAwait(false))?.Id ?? throw NoDeviceOnline();
        }

        try
        {
            await call(deviceId, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (SpotifyApiException ex) when (ex.IsNoActiveDevice)
        {
            if (deviceId is not null)
            {
                // The device just chosen went away.
                throw NoDeviceOnline();
            }

            // The device that played has gone quiet since Spotify last said so.
        }

        var chosen = await _webDevices.ChooseAsync(cancellationToken).ConfigureAwait(false) ?? throw NoDeviceOnline();
        if (!starts)
        {
            await _api.TransferPlaybackAsync(chosen.Id!, play: false, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await call(chosen.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.IsNoActiveDevice)
        {
            throw NoDeviceOnline();
        }
    }

    /// <summary>Pauses; with "Spotify Web API only" and nothing playing anywhere there is nothing to pause, and no device is woken for it.</summary>
    private async Task PauseOnDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (UseLocal)
            {
                await WithLocalDeviceAsync(_api.PauseAsync, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _api.PauseAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (!UseLocal && ex.IsNoActiveDevice)
        {
            // Nothing plays on any device: already paused.
        }
        catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden && !ex.IsPremiumRequired)
        {
            // Spotify refuses to pause what is already paused ("Restriction violated").
        }
    }

    private static SpotifyApiException NoDeviceOnline() =>
        new(HttpStatusCode.NotFound, SpotifyApiException.NoDeviceOnlineReason, "No Spotify device is online.");

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
        // Switched to "Spotify Web API only" meanwhile: the Spotify app is left alone.
        if (!UseLocal)
        {
            throw NoDeviceOnline();
        }

        if (_launcher is not null)
        {
            var status = await _launcher.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
            if (status == SpotifyAppStatus.NotInstalled)
            {
                throw new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "The Spotify app is not installed.");
            }
        }

        // A freshly started Spotify takes a few seconds to appear as a device.
        for (var attempt = 0; attempt < 8 && UseLocal; attempt++)
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
            PlaybackLog.Note($"command failed: {DescribeError(ex)}");
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

    private void RevertTo(long generation, PlayerState before, ListSession? session)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return;
            }

            _session = session;
            _trackHold = null;
            _playingHold = null;
            _positionHold = null;
            var now = _time.GetUtcNow();
            SetState(before with { Position = before.PositionAt(now), PositionTimestamp = now, Volume = _state.Volume });
        }

        RaiseStateChanged();
    }

    private Task FetchWebDetailsSoonAsync() => RefreshSoonAsync(WebDetailsDelay);

    private async Task RefreshAfterOwnPlayerAsync()
    {
        try
        {
            await Task.Delay(OwnPlayerRefreshDelay, _time, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            Interlocked.Exchange(ref _ownPlayerRefreshQueued, 0);
        }

        await RefreshSoonAsync(TimeSpan.Zero).ConfigureAwait(false);
    }

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
                needsWeb = first || !UseLocal || !HasSong(_lastLocal) || !_lastLocal.HasTimeline || _needsWebDetails;
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

    private void CountUserCommand() => Interlocked.Increment(ref _userCommands);

    private void SetState(PlayerState state) => _state = state;

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct Hold<T>(T Value, DateTimeOffset Until);
}
