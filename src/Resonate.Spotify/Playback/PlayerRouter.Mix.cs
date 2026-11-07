using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// Lists that mix Spotify's songs with files from this computer: a Spotify
/// playlist's local songs play from their files in Local Files, in turns
/// with the playlist's Spotify songs (see <see cref="MixedList"/>). Each
/// stretch goes to its player as a list that hands over at its end, and the
/// next stretch then starts on the other player. Next and Previous cross
/// between stretches; shuffle and repeat belong to the whole list.
/// </summary>
public sealed partial class PlayerRouter
{
    private readonly Lock _mixGate = new();

    // The mixed list that plays; null for none. Written under _mixGate.
    private MixedList? _mix;

    /// <summary>
    /// Finds the files of a playlist's local songs, so they play from Local
    /// Files; null leaves them to Spotify (which plays them only in its app,
    /// inside their playlist).
    /// </summary>
    public ILocalSongFinder? LocalSongs { get; set; }

    /// <summary>While a mixed list plays: the songs after the current one, from both players, in the order they play. Null otherwise.</summary>
    public IReadOnlyList<TrackInfo>? MixedUpcoming
    {
        get
        {
            lock (_mixGate)
            {
                return _mix is { Finished: false } mix ? mix.Upcoming : null;
            }
        }
    }

    /// <summary>A list's songs as they can play, when it has local songs or files.</summary>
    /// <param name="Songs">Songs Spotify can start and files (local songs with the file found), in the list's order.</param>
    /// <param name="Start">The picked song (or the next one that can play) in <paramref name="Songs"/>; -1 for none.</param>
    /// <param name="MissingPick">The picked song is a local song whose file was not found.</param>
    private sealed record MixPlan(IReadOnlyList<TrackInfo> Songs, int Start, bool HasFiles, bool HasSpotifySongs, TrackInfo? MissingPick);

    /// <summary>
    /// Looks up the files of the request's local songs. Null when it has none
    /// to look up (or only part of the list is known: Spotify plays the rest
    /// of the playlist itself, as before).
    /// </summary>
    private async Task<MixPlan?> PlanAsync(PlayRequest request)
    {
        var tracks = request.Tracks;
        if (request.IsPartial || !tracks.Any(t => t.FilePath is not null || t.IsLocal))
        {
            return null;
        }

        IReadOnlyList<string?> paths = [];
        if (LocalSongs is { } finder && tracks.Any(t => t is { IsLocal: true, FilePath: null }))
        {
            try
            {
                paths = await finder.FindAsync(tracks, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Local Files can not be read now: the local songs are left to Spotify, as before.
                PlaybackLog.Note($"local songs: finding files failed: {ex.Message}");
            }
        }
        else if (LocalSongs is null && !tracks.Any(t => t.FilePath is not null))
        {
            return null;
        }

        var songs = new List<TrackInfo>(tracks.Count);
        var start = -1;
        var hasFiles = false;
        var hasSpotify = false;
        TrackInfo? missing = null;
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            TrackInfo? song = null;
            if (track.FilePath is not null)
            {
                song = track;
            }
            else if (track.IsLocal)
            {
                if (i < paths.Count && paths[i] is { } path)
                {
                    // Keeps its spotify:local: address, so its row in the playlist shows it plays.
                    song = track with { FilePath = path };
                }
                else if (i == request.StartIndex)
                {
                    missing = track;
                }
            }
            else if (ListSession.CanStartByUri(track))
            {
                song = track;
            }

            if (song is null)
            {
                continue;
            }

            if (start < 0 && request.StartIndex >= 0 && i >= request.StartIndex)
            {
                // The picked song, or the next one that can play when it can not.
                start = songs.Count;
            }

            hasFiles |= song.FilePath is not null;
            hasSpotify |= song.FilePath is null;
            songs.Add(song);
        }

        return new MixPlan(songs, start, hasFiles, hasSpotify, missing);
    }

    /// <summary>What a mixed list shows over its players' own: its shuffle, repeat and name.</summary>
    private PlayerState WithMix(PlayerState state) =>
        Volatile.Read(ref _mix) is { } mix
            ? state with
            {
                Shuffle = mix.Shuffle,
                Repeat = mix.Repeat,
                SourceName = mix.SourceName ?? state.SourceName,
                CanShuffle = true,
                CanRepeat = true,
            }
            : state;

    private Task StartMixAsync(MixedList mix)
    {
        PlayRequest stretch;
        lock (_mixGate)
        {
            _mix = mix;
            stretch = mix.StartStretchAt(mix.Index);
        }

        return StartStretchAsync(mix, stretch);
    }

    /// <summary>
    /// Hands <paramref name="stretch"/> to its player and makes it the one
    /// the bar shows: the other is paused if it plays, and the new one takes
    /// its volume (one slider for the whole list) and the list's repeat
    /// ("repeat one" stays with each song; the list repeats itself).
    /// </summary>
    private async Task StartStretchAsync(MixedList mix, PlayRequest stretch)
    {
        IPlayer target = MixedList.IsFile(stretch.Tracks[0]) ? _local : Spotify;
        var previous = Active;
        if (previous != target && previous.State.IsPlaying)
        {
            // Paused first, so it is not taken for starting by itself.
            _ = previous.PauseAsync();
        }

        Interlocked.Exchange(ref _active, target);
        var started = target == _local ? _local.PlayAsync(stretch) : Spotify.PlayAsync(stretch);
        lock (_mixGate)
        {
            if (ReferenceEquals(mix.Stretch, stretch))
            {
                mix.StretchStarted = true;
            }
        }

        var repeat = mix.Repeat == RepeatMode.One ? RepeatMode.One : RepeatMode.Off;
        if (target.State.Repeat != repeat)
        {
            _ = target.SetRepeatAsync(repeat);
        }

        var volume = previous.State.Volume;
        if (previous != target && Math.Abs(target.State.Volume - volume) > 0.005)
        {
            _ = target.SetVolumeAsync(volume);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        await started.ConfigureAwait(false);
    }

    /// <summary>A stretch ended on its player: the next one starts on the other (or the list ended).</summary>
    private void OnStretchEnded(object? sender, PlayRequest ended)
    {
        MixedList? mix;
        PlayRequest? next;
        lock (_mixGate)
        {
            mix = _mix;
            if (mix is null || !ReferenceEquals(mix.Stretch, ended))
            {
                return;
            }

            next = mix.NextStretch();
        }

        if (next is null)
        {
            // The end of the list (repeat off): it waits, paused, for Play to start it again.
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Not on the ending player's thread: starting the next stretch may wait for it.
        _ = Task.Run(() => StartStretchAsync(mix, next));
    }

    /// <summary>The mixed list is over (another list plays, or music from elsewhere took over); returns it, or null when none played.</summary>
    private MixedList? EndMix()
    {
        lock (_mixGate)
        {
            var mix = _mix;
            _mix = null;
            return mix;
        }
    }

    /// <summary>After a mixed list (when one played), both players repeat as it did (each stretch had its own repeat switched off).</summary>
    private void KeepRepeat(MixedList? ended)
    {
        if (ended is null)
        {
            return;
        }

        var repeat = ended.Repeat;
        if (Spotify.State.Repeat != repeat && Spotify.State.HasTrack)
        {
            _ = Spotify.SetRepeatAsync(repeat);
        }

        if (_local.State.Repeat != repeat)
        {
            _ = _local.SetRepeatAsync(repeat);
        }
    }

    /// <summary>
    /// Follows the active player through the playing stretch. When Spotify no
    /// longer plays the stretch it was given (music from elsewhere), the
    /// mixed list is over.
    /// </summary>
    private void FollowMix(IPlayer player)
    {
        var state = player.State;
        lock (_mixGate)
        {
            if (_mix is not { Finished: false, StretchStarted: true, Stretch: { } stretch } mix || !state.HasTrack)
            {
                return;
            }

            if (player == Spotify && MixedList.IsFile(stretch.Tracks[0]))
            {
                return;
            }

            if (player == Spotify && !Spotify.IsPlayingList(stretch))
            {
                _mix = null;
                return;
            }

            mix.Follow(state.TrackUri, state.Title);
        }
    }

    /// <summary>Next on a stretch's last song: the next stretch, on the other player. Null to leave Next to the player.</summary>
    private Task? NextInMix()
    {
        MixedList? mix;
        PlayRequest? next;
        lock (_mixGate)
        {
            mix = _mix;
            if (mix is not { Finished: false, AtStretchEnd: true, HasNextStretch: true } || mix.NextStretch() is not { } following)
            {
                return null;
            }

            next = following;
        }

        return StartStretchAsync(mix, next);
    }

    /// <summary>Previous at the start of a stretch's first song: the stretch before, from its last song. Null to leave Previous to the player.</summary>
    private Task? PreviousInMix()
    {
        var position = Active.State.PositionAt(_time.GetUtcNow());
        MixedList? mix;
        PlayRequest? previous;
        lock (_mixGate)
        {
            mix = _mix;
            if (mix is not { Finished: false }
                || !mix.AtStretchStart
                || position > LocalFiles.LocalPlayer.RestartThreshold
                || mix.PreviousStretch() is not { } before)
            {
                return null;
            }

            previous = before;
        }

        return StartStretchAsync(mix, previous);
    }

    /// <summary>Play after a mixed list ended: it starts again. Null when no list ended.</summary>
    private Task? RestartFinishedMix()
    {
        MixedList? mix;
        PlayRequest stretch;
        lock (_mixGate)
        {
            mix = _mix;
            if (mix is not { Finished: true })
            {
                return null;
            }

            stretch = mix.Restart();
        }

        return StartStretchAsync(mix, stretch);
    }

    /// <summary>Shuffle for a mixed list: its order changes from the next song on. Null when none plays.</summary>
    private Task? ShuffleMix(bool shuffle)
    {
        lock (_mixGate)
        {
            if (_mix is not { } mix)
            {
                return null;
            }

            if (mix.Shuffle == shuffle)
            {
                return Task.CompletedTask;
            }

            mix.SetShuffle(shuffle);
        }

        EndStretchAfterCurrent();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Repeat for a mixed list: "repeat one" goes to the playing song's player, the rest is the list's own. Null when none plays.</summary>
    private Task? RepeatMix(RepeatMode mode)
    {
        lock (_mixGate)
        {
            if (_mix is not { } mix)
            {
                return null;
            }

            mix.Repeat = mode;
        }

        var active = Active;
        var repeat = mode == RepeatMode.One ? RepeatMode.One : RepeatMode.Off;
        var sent = active.State.Repeat != repeat ? active.SetRepeatAsync(repeat) : Task.CompletedTask;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return sent;
    }

    /// <summary>"Play next" or "Add to queue" while a mixed list plays: into its order. False when none plays.</summary>
    private bool AddToMix(TrackInfo track, bool playNext)
    {
        if (track.FilePath is null && !ListSession.CanStartByUri(track))
        {
            return false;
        }

        lock (_mixGate)
        {
            if (_mix is not { Finished: false } mix)
            {
                return false;
            }

            mix.Add(track, playNext);
        }

        EndStretchAfterCurrent();
        return true;
    }

    /// <summary>
    /// The mixed list's order changed after the current song: its player
    /// stops after it (Up next's "clear"), and the next stretch follows the
    /// new order.
    /// </summary>
    private void EndStretchAfterCurrent()
    {
        if (Active is IUpNext player && player.UpNext is { Upcoming.Count: > 0 })
        {
            player.ClearUpNext();
        }
    }
}
