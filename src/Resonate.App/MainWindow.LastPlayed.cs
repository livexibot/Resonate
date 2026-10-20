using Microsoft.UI.Dispatching;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App;

/// <summary>
/// Where you left off (the owner's request, 10 October 2026; it replaced
/// the Resume on start plugin and cannot be turned off): the Spotify song
/// that played last and its place are kept in the settings, and Resonate
/// opens showing it, paused there, until Spotify says something else
/// plays; Play carries on from that place (<see cref="PlayerController.ShowLastPlayed"/>).
/// The list around it is kept too (<see cref="KeptList"/>, in the cache
/// folder), so after a restart Play, Next, Previous and Up next go on
/// through it (the owner found only the kept song playing, 18 October 2026).
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan LastPlayedEvery = TimeSpan.FromSeconds(15);

    private DispatcherQueueTimer? _lastPlayedTimer;
    private (string? Track, bool Playing) _lastPlayedSaved;
    private KeptList? _keptSaved;

    private void SetUpLastPlayed()
    {
        if (_services.IsDemo || StartupOptions.Current is not { StartupBenchmarkFile: null, ScreenshotFolder: null, PerformanceFolder: null, UpdateCheckFeed: null })
        {
            return;
        }

        var last = _services.Settings.LastPlayed;
        _services.Player.Spotify.ShowLastPlayed(last);
        _lastPlayedSaved = (last?.TrackUri, false);
        if (last is { SourceKey: { } key })
        {
            // So the player's title opens what it played from, as before the restart.
            _lastPlayed = (key, last.SourceName);
        }

        if (last is not null)
        {
            // Off the interface thread: the list is a few hundred songs.
            var player = _services.Player.Spotify;
            var file = _services.KeptList;
            _ = Task.Run(() =>
            {
                if (file.Load() is { } kept)
                {
                    player.RestoreList(kept);
                }
            });
        }

        FollowPlayer(FollowLastPlayed);
        Closed += (_, _) => KeepLastPlayed(closing: true);
    }

    /// <summary>A new song or a pause is kept at once; while music plays its place is kept every 15 seconds.</summary>
    private void FollowLastPlayed()
    {
        var state = _services.Player.State;
        if ((state.TrackUri, state.IsPlaying) != _lastPlayedSaved)
        {
            KeepLastPlayed();
        }

        if (_lastPlayedTimer is null)
        {
            _lastPlayedTimer = DispatcherQueue.CreateTimer();
            _lastPlayedTimer.Interval = LastPlayedEvery;
            _lastPlayedTimer.Tick += (_, _) => KeepLastPlayed();
        }

        if (state.IsPlaying)
        {
            if (!_lastPlayedTimer.IsRunning)
            {
                _lastPlayedTimer.Start();
            }
        }
        else
        {
            _lastPlayedTimer.Stop();
        }
    }

    /// <param name="closing">Resonate is closing: the list is written before it ends.</param>
    private void KeepLastPlayed(bool closing = false)
    {
        var state = _services.Player.State;
        if (LastPlayed.From(state, DateTimeOffset.UtcNow) is not { } last)
        {
            return;
        }

        if (_lastPlayed is { } played && state.SourceName is { } name && name == played.Name)
        {
            last.SourceKey = played.Key;
        }

        _lastPlayedSaved = (state.TrackUri, state.IsPlaying);
        _services.Settings.LastPlayed = last;
        _services.SaveSettings();

        // The list only when it changed (a new song, a new order), not with every place in the song.
        var kept = _services.Player.Spotify.KeepList();
        if (!KeptList.Same(kept, _keptSaved) || closing)
        {
            _keptSaved = kept;
            if (closing)
            {
                _services.KeptList.SaveNow(kept);
            }
            else
            {
                _services.KeptList.SaveSoon(kept);
            }
        }
    }
}
