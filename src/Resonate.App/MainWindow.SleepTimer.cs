using Microsoft.UI.Dispatching;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App;

/// <summary>
/// Sleep timer, a built-in plugin (a downloaded one until 10 October 2026,
/// with the same ID, menu and settings): from the player's plugin menu it
/// pauses the music after 15, 30 or 60 minutes or the user's own length,
/// or at the end of the song. With the fade on, the volume falls over the
/// last minute and is put back after the pause, so the next listen starts
/// at the usual volume. Rules in <see cref="SleepTimerRules"/>.
/// </summary>
public sealed partial class MainWindow
{
    private enum SleepMode
    {
        Off,
        Time,
        SongEnd,
    }

    private SleepMode _sleepMode;
    private DateTimeOffset _sleepDeadline;
    private string? _sleepSong;
    private DispatcherQueueTimer? _sleepTicker;
    private DispatcherQueueTimer? _sleepSongEnd;
    private double? _sleepFadeFrom;
    private TimeSpan _sleepLastFadeStep = TimeSpan.MaxValue;

    partial void SetUpSleepTimer()
    {
        _services.BuiltIns.AddMenu(BuiltInPlugins.SleepTimer, SleepCommands, SleepStatus);
        FollowPlugin(BuiltInPlugins.SleepTimer, () =>
        {
            if (!_services.BuiltIns.IsOn(BuiltInPlugins.SleepTimer))
            {
                StopSleepTimer(restore: true);
            }
        });
        FollowPlayer(FollowSleepSong);
        Closed += (_, _) => StopSleepTimer(restore: true);
    }

    private IReadOnlyList<PluginCommand> SleepCommands()
    {
        if (_sleepMode == SleepMode.Off)
        {
            return
            [
                .. SleepTimerRules.Choices(_services.Settings.SleepTimerMinutes)
                    .Select(minutes => new PluginCommand($"Pause in {SleepTimerRules.MinutesText(minutes)}", () => StartSleepTimer(minutes))),
                new PluginCommand("Pause at the end of this song", StartSleepAtSongEnd),
            ];
        }

        List<PluginCommand> commands = [];
        if (_sleepMode == SleepMode.Time)
        {
            commands.Add(new PluginCommand("Add 15 minutes", () =>
            {
                _sleepDeadline += TimeSpan.FromMinutes(15);
                RestoreSleepVolume();
                _services.BuiltIns.NotifyMenus();
            }));
        }

        commands.Add(new PluginCommand("Cancel the sleep timer", () => StopSleepTimer(restore: true)));
        return commands;
    }

    private string? SleepStatus() => _sleepMode switch
    {
        SleepMode.Time => $"Pauses in {SleepTimerRules.ClockText(_sleepDeadline - DateTimeOffset.UtcNow)}",
        SleepMode.SongEnd => "Pauses at the end of this song",
        _ => null,
    };

    private void StartSleepTimer(int minutes)
    {
        StopSleepTimer(restore: true);
        _sleepMode = SleepMode.Time;
        _sleepDeadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(minutes);
        StartSleepTicker();
    }

    private void StartSleepAtSongEnd()
    {
        StopSleepTimer(restore: true);
        var state = _services.Player.State;
        _sleepMode = SleepMode.SongEnd;
        _sleepSong = SleepSongKey(state);
        StartSleepTicker();
        AimAtSongEnd();
    }

    private void StartSleepTicker()
    {
        if (_sleepTicker is null)
        {
            _sleepTicker = DispatcherQueue.CreateTimer();
            _sleepTicker.Interval = TimeSpan.FromSeconds(1);
            _sleepTicker.Tick += (_, _) => SleepTick();
        }

        _sleepTicker.Start();
        _services.BuiltIns.NotifyMenus();
    }

    /// <summary>Once a second while it runs: the countdown, the fade, and the end of a time.</summary>
    private void SleepTick()
    {
        if (_sleepMode == SleepMode.Time)
        {
            var left = _sleepDeadline - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                FinishSleepTimer();
                return;
            }

            FadeForSleep(left);
            _services.BuiltIns.NotifyMenus();
        }
        else if (_sleepMode == SleepMode.SongEnd && SongLeft(_services.Player.State) is { } left)
        {
            FadeForSleep(left);
        }
    }

    /// <summary>A new song before the timer fired (a late position, a skip) pauses now; a seek or pause aims again.</summary>
    private void FollowSleepSong()
    {
        if (_sleepMode != SleepMode.SongEnd)
        {
            return;
        }

        var state = _services.Player.State;
        if (SleepSongKey(state) is { } song && song != _sleepSong)
        {
            FinishSleepTimer();
            return;
        }

        AimAtSongEnd();
    }

    /// <summary>A one-off timer just before the song's end, aimed again whenever the position changes.</summary>
    private void AimAtSongEnd()
    {
        _sleepSongEnd?.Stop();
        var state = _services.Player.State;
        if (_sleepMode != SleepMode.SongEnd || !state.IsPlaying || SongLeft(state) is not { } left)
        {
            return;
        }

        if (_sleepSongEnd is null)
        {
            _sleepSongEnd = DispatcherQueue.CreateTimer();
            _sleepSongEnd.IsRepeating = false;
            _sleepSongEnd.Tick += (_, _) => FinishSleepTimer();
        }

        _sleepSongEnd.Interval = TimeSpan.FromSeconds(Math.Max(0, left.TotalSeconds - 0.4));
        _sleepSongEnd.Start();
    }

    private static TimeSpan? SongLeft(PlayerState state) =>
        state.Duration > TimeSpan.Zero ? state.Duration - state.PositionAt(DateTimeOffset.UtcNow) : null;

    private static string? SleepSongKey(PlayerState state) =>
        state.TrackUri ?? (state.Title is { } title ? $"{title}\n{state.Artists}" : null);

    /// <summary>Lowers the volume in steps as the end comes closer, from the volume it had when the fade began.</summary>
    private void FadeForSleep(TimeSpan left)
    {
        if (!_services.Settings.SleepTimerFade || SleepTimerRules.FadeShare(left) is not { } share)
        {
            return;
        }

        var state = _services.Player.State;
        if (_sleepFadeFrom is null)
        {
            if (!state.IsPlaying)
            {
                return;
            }

            _sleepFadeFrom = state.Volume;
            _sleepLastFadeStep = TimeSpan.MaxValue;
        }

        if (_sleepLastFadeStep - left < SleepTimerRules.FadeStep)
        {
            return;
        }

        _sleepLastFadeStep = left;
        _ = _services.Player.SetVolumeAsync(_sleepFadeFrom.Value * share);
    }

    private void RestoreSleepVolume()
    {
        if (_sleepFadeFrom is { } volume)
        {
            _sleepFadeFrom = null;
            _ = _services.Player.SetVolumeAsync(volume);
        }
    }

    private void FinishSleepTimer()
    {
        var restore = _sleepFadeFrom;
        _sleepFadeFrom = null;
        StopSleepTimer(restore: false);
        _ = PauseThenRestoreAsync(restore);
    }

    /// <summary>The volume comes back after the pause has landed, so the music never gets louder before it stops.</summary>
    private async Task PauseThenRestoreAsync(double? volume)
    {
        await _services.Player.PauseAsync();
        if (volume is { } v)
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            await _services.Player.SetVolumeAsync(v);
        }
    }

    private void StopSleepTimer(bool restore)
    {
        _sleepTicker?.Stop();
        _sleepSongEnd?.Stop();
        if (restore)
        {
            RestoreSleepVolume();
        }

        var was = _sleepMode;
        _sleepMode = SleepMode.Off;
        _sleepSong = null;
        if (was != SleepMode.Off)
        {
            _services.BuiltIns.NotifyMenus();
        }
    }
}
