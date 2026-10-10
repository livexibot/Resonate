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
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan LastPlayedEvery = TimeSpan.FromSeconds(15);

    private DispatcherQueueTimer? _lastPlayedTimer;
    private (string? Track, bool Playing) _lastPlayedSaved;

    private void SetUpLastPlayed()
    {
        if (_services.IsDemo || StartupOptions.Current is not { StartupBenchmarkFile: null, ScreenshotFolder: null, PerformanceFolder: null, UpdateCheckFeed: null })
        {
            return;
        }

        _services.Player.Spotify.ShowLastPlayed(_services.Settings.LastPlayed);
        _lastPlayedSaved = (_services.Settings.LastPlayed?.TrackUri, false);
        FollowPlayer(FollowLastPlayed);
        Closed += (_, _) => KeepLastPlayed();
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

    private void KeepLastPlayed()
    {
        var state = _services.Player.State;
        if (LastPlayed.From(state, DateTimeOffset.UtcNow) is not { } last)
        {
            return;
        }

        _lastPlayedSaved = (state.TrackUri, state.IsPlaying);
        _services.Settings.LastPlayed = last;
        _services.SaveSettings();
    }
}
