using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Up next (a built-in plugin, off until turned on): the upcoming songs of
/// the list Resonate plays can be reordered, removed, cleared, shuffled and
/// added to. Spotify does not let apps change its queue, so each edit
/// changes Resonate's own order at once (<see cref="ListSession"/>), and
/// about two seconds after the last one (never while the user still drags a
/// song) Spotify gets one play command with a window of the new order that
/// starts with the current song at the position it has reached, so the
/// music carries on. Until then "play" and "next" already follow the edited
/// order, and a song Spotify starts from its old order is replaced by the
/// edited order's next one. Music Resonate did not start has no
/// <see cref="UpNext"/>: Spotify's own queue can only be read.
/// </summary>
public sealed partial class PlayerController : IUpNext
{
    /// <summary>How long after the last edit Spotify gets the new order, so several edits make one switch.</summary>
    internal static readonly TimeSpan UpNextDelay = TimeSpan.FromSeconds(2);

    /// <summary>Close to a song's end the edits go out sooner, before Spotify moves on through its old order.</summary>
    internal static readonly TimeSpan UpNextEndMargin = TimeSpan.FromSeconds(1.5);

    /// <summary>After Spotify did not take an edited order, the next try (unless the song changes first).</summary>
    internal static readonly TimeSpan UpNextRetryDelay = TimeSpan.FromSeconds(30);

    private ITimer? _upNextTimer;
    private DateTimeOffset _upNextDue;
    private bool _upNextHeld;

    public UpNextList? UpNext
    {
        get
        {
            lock (_gate)
            {
                return _session?.UpNextList();
            }
        }
    }

    public bool MoveUpNext(UpNextPick song, int to) => EditUpNext(s => s.MoveUpNext(song, to));

    public bool RemoveUpNext(IReadOnlyList<UpNextPick> songs) => EditUpNext(s => s.RemoveUpNext(songs));

    public bool ClearUpNext() => EditUpNext(s => s.ClearUpNext());

    public bool ShuffleUpNext() => EditUpNext(s => s.ShuffleUpNext());

    public bool AddToUpNext(TrackInfo track, bool playNext) => EditUpNext(s => s.AddToUpNext(track, playNext));

    /// <summary>While held (the user is dragging a song), edits are not sent; once released they go out after the usual pause.</summary>
    public void HoldUpNext(bool held)
    {
        TimeSpan? delay = null;
        lock (_gate)
        {
            _upNextHeld = held;
            if (!held && _session is { EditsPending: true })
            {
                delay = ScheduleUpNext();
            }
        }

        ArmUpNext(delay);
    }

    private bool EditUpNext(Func<ListSession, bool> edit)
    {
        TimeSpan delay;
        lock (_gate)
        {
            if (_session is not { } session || SpotifyDj.IsPlaying(_state) || !edit(session))
            {
                return false;
            }

            delay = ScheduleUpNext();
        }

        ArmUpNext(delay);
        return true;
    }

    /// <summary>Under the lock: when the edits are due. Sooner when the song is about to end.</summary>
    private TimeSpan ScheduleUpNext()
    {
        var now = _time.GetUtcNow();
        var delay = UpNextDelay;
        if (_state.IsPlaying && _state.Duration > TimeSpan.Zero)
        {
            var left = _state.Duration - _state.PositionAt(now) - UpNextEndMargin;
            delay = left < TimeSpan.Zero ? TimeSpan.Zero : left < delay ? left : delay;
        }

        _upNextDue = now + delay;
        return delay;
    }

    /// <summary>Outside the lock: follows the list again once the edits are due.</summary>
    private void ArmUpNext(TimeSpan? delay)
    {
        if (delay is not { } due || _stopping.IsCancellationRequested)
        {
            return;
        }

        var timer = _upNextTimer;
        if (timer is null)
        {
            var created = _time.CreateTimer(_ => OnUpNextDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer = Interlocked.CompareExchange(ref _upNextTimer, created, null) ?? created;
            if (!ReferenceEquals(timer, created))
            {
                created.Dispose();
            }
        }

        try
        {
            timer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private void OnUpNextDue()
    {
        if (!_stopping.IsCancellationRequested)
        {
            FollowSession();
        }
    }

    /// <summary>
    /// Follows a list whose edits Spotify has not got yet (called first by
    /// <see cref="FollowSession"/>, whose usual work waits meanwhile). True
    /// when there is one. Sends the edits once due while the song plays;
    /// when Spotify moved on through its old order, starts the edited
    /// order's next song instead (or stops, when nothing follows).
    /// </summary>
    private bool FollowUpNext()
    {
        UpNextSend? send = null;
        lock (_gate)
        {
            if (_session is not { EditsPending: true } session)
            {
                return false;
            }

            if (!_state.HasTrack || _stopping.IsCancellationRequested)
            {
                return true;
            }

            var now = _time.GetUtcNow();
            var located = session.Locate(_state.TrackUri, _state.Title);
            if (located != session.Index && session.IsStaleNext(_state.TrackUri, _state.Title))
            {
                // The song ended (or a media key skipped) before Spotify had the new order.
                if (!session.AdvanceInEdits(_state.Repeat))
                {
                    // The edited order ends here: the music stops, as it would have after the current song.
                    session.DropEdits();
                    _playingHold = new Hold<bool>(false, now + PlayStateHold);
                    SetState(_state with { IsPlaying = false, Position = _state.PositionAt(now), PositionTimestamp = now });
                }
                else
                {
                    send = TakeUpNext(session, now, switching: !IsShowing(session.Current));
                }
            }
            else if (located >= 0 && located <= session.Index)
            {
                // The same song, or back to an earlier one (those are as Spotify has them).
                session.Index = located;
                session.PlayingOther = false;
                if (!_state.IsPlaying || _upNextHeld || now < _upNextDue)
                {
                    return true;
                }

                send = TakeUpNext(session, now, switching: false);
            }
            else
            {
                // A song from elsewhere, such as Spotify's own queue: the edits wait for the list.
                session.PlayingOther = true;
                return true;
            }
        }

        RaiseStateChanged();
        if (send is null)
        {
            _ = RunTransportAsync(
                async ct =>
                {
                    if (!await TryLocalAsync(_local.PauseAsync, ct).ConfigureAwait(false))
                    {
                        await PauseOnDeviceAsync(ct).ConfigureAwait(false);
                    }
                },
                revert: null);
        }
        else
        {
            _ = RunTransportAsync(ct => WithDeviceAsync((id, c) => SendUpNextAsync(send, id, c), ct, starts: true), revert: null);
        }

        return true;
    }

    /// <summary>"Play" with edits waiting: the edited order starts where the song was paused, in the same command.</summary>
    private UpNextSend? TakeUpNextForPlay()
    {
        UpNextSend send;
        lock (_gate)
        {
            if (_session is not { EditsPending: true } session
                || _upNextHeld
                || session.Locate(_state.TrackUri, _state.Title) != session.Index)
            {
                return null;
            }

            send = TakeUpNext(session, _time.GetUtcNow(), switching: false);
        }

        RaiseStateChanged();
        return send;
    }

    /// <summary>"Next" with edits waiting: the edited order's next song, rather than the one Spotify still has next.</summary>
    private Task? NextInUpNext()
    {
        UpNextSend send;
        TrackInfo song;
        string? source;
        lock (_gate)
        {
            if (_session is not { EditsPending: true } session
                || session.Locate(_state.TrackUri, _state.Title) != session.Index
                || !session.AdvanceInEdits(_state.Repeat)
                || session.Current is not { } next)
            {
                return null;
            }

            send = new UpNextSend(session, session.TakeEdits());
            song = next;
            source = _state.SourceName;
        }

        return StartAsync(song, contextUri: null, send.Session, source, shuffle: null, (id, c) => SendUpNextAsync(send, id, c));
    }

    /// <summary>Under the lock: the edited order to send, shown at once.</summary>
    /// <param name="switching">Spotify plays another song than the order's current one, which then starts from its beginning.</param>
    private UpNextSend TakeUpNext(ListSession session, DateTimeOffset now, bool switching)
    {
        var body = session.TakeEdits();
        _sessionSettledAt = now + TrackHold;
        _playingHold = new Hold<bool>(true, now + PlayStateHold);

        // A list of songs has no context.
        var state = _state with { IsPlaying = true, ContextUri = null };
        if (switching && session.Current is { } song)
        {
            _trackHold = new Hold<string>(song.Title, now + TrackHold);
            _positionHold = new Hold<TimeSpan>(TimeSpan.Zero, now + TrackHold);
            state = state with
            {
                Title = song.Title,
                Artists = song.Artists,
                Album = song.Album,
                TrackUri = song.Uri,
                ArtworkUrl = song.LargeImageUrl,
                ArtworkBytes = null,
                Duration = song.Duration,
                Position = TimeSpan.Zero,
                PositionTimestamp = now,
            };
        }
        else
        {
            var position = _state.PositionAt(now);
            _positionHold = new Hold<TimeSpan>(position, now + PositionHold);
            state = state with { Position = position, PositionTimestamp = now };
        }

        SetState(state);
        return new UpNextSend(session, body);
    }

    /// <summary>Whether the player shows <paramref name="song"/> now.</summary>
    private bool IsShowing(TrackInfo? song) =>
        song is not null && (_state.TrackUri is { } uri ? uri == song.Uri : TitlesMatch(_state.Title, song.Title));

    /// <summary>
    /// Sends the edited order, restarting the current song where it is (see
    /// <see cref="RestartAsync"/>). If Spotify does not take it, the edits
    /// wait for another try.
    /// </summary>
    private async Task SendUpNextAsync(UpNextSend send, string? deviceId, CancellationToken cancellationToken)
    {
        try
        {
            await RestartAsync(send.Body, deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_session == send.Session)
                {
                    send.Session.RestoreEdits();
                    _upNextDue = _time.GetUtcNow() + UpNextRetryDelay;
                }
            }

            throw;
        }
    }

    private sealed record UpNextSend(ListSession Session, StartPlaybackBody Body);
}
