using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Windows;

namespace Resonate.App;

/// <summary>
/// The built-in plugins added when the tray icon, editing the queue, the
/// taskbar buttons and Start with Windows became part of the app (the
/// owner's request, 9 October 2026): Alarm, Focus timer, Skip intros and
/// outros, and Volume per device. Each runs only while it is on; the rules
/// they follow are in <see cref="AlarmRules"/>, <see cref="FocusRules"/> and
/// <see cref="SkipRules"/>.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan AlarmCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AlarmFadeStep = TimeSpan.FromSeconds(3);

    private DispatcherQueueTimer? _alarmTimer;
    private DispatcherQueueTimer? _alarmFadeTimer;
    private DateTimeOffset _alarmFadeStart;

    private DispatcherQueueTimer? _focusTimer;
    private bool _focusOn;
    private bool _focusPhase;
    private int _focusRound;
    private DateTimeOffset _focusPhaseEnds;

    private DispatcherQueueTimer? _skipTimer;
    private string? _skipSong;
    private bool _introSkipped;
    private bool _outroSkipped;

    private IDisposable? _deviceWatch;
    private string? _deviceName;
    private int _deviceCheckQueued;
    private bool _deviceApplying;
    private DispatcherQueueTimer? _deviceSaveTimer;

    private double? _loweredFrom;

    // ---- Alarm ----

    partial void SetUpAlarm()
    {
        FollowPlugin(BuiltInPlugins.Alarm, FollowAlarm);
        Closed += (_, _) =>
        {
            _alarmTimer?.Stop();
            _alarmFadeTimer?.Stop();
        };
        FollowAlarm();
    }

    /// <summary>Looks at the clock every 20 seconds while the alarm is on.</summary>
    internal void FollowAlarm()
    {
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.Alarm) || _services.IsDemo)
        {
            _alarmTimer?.Stop();
            return;
        }

        if (_alarmTimer is null)
        {
            _alarmTimer = DispatcherQueue.CreateTimer();
            _alarmTimer.Interval = AlarmCheck;
            _alarmTimer.Tick += (_, _) => CheckAlarm();
        }

        _alarmTimer.Start();
        CheckAlarm();
    }

    private void CheckAlarm()
    {
        var settings = _services.Settings;
        if (AlarmRules.IsDue(settings.AlarmHour, settings.AlarmMinute, settings.AlarmDays, DateTime.Now, settings.AlarmLastRang))
        {
            settings.AlarmLastRang = DateOnly.FromDateTime(DateTime.Now);
            _services.SaveSettings();
            _ = RingAlarmAsync();
        }
    }

    /// <summary>Plays what the alarm was set to, rising from a whisper to the alarm's volume (also its Try it button).</summary>
    internal async Task RingAlarmAsync()
    {
        var settings = _services.Settings;
        var playlist = settings.AlarmPlaylist is { } id ? _services.Library.Snapshot?.Playlists.FirstOrDefault(p => p.Id == id) : null;
        var item = playlist is not null ? new PlaylistNavItem(playlist) : ListNavItem(LikedSongsKey, "Liked Songs");
        var target = Math.Clamp(settings.AlarmVolume, 5, 100) / 100.0;
        var fade = TimeSpan.FromMinutes(Math.Clamp(settings.AlarmFadeMinutes, 0, 15));
        await _services.Player.SetVolumeAsync(AlarmRules.FadeVolume(TimeSpan.Zero, fade, target));
        await PlayPlaylistAsync(item, settings.AlarmShuffle);
        ShowMessage($"Alarm: {item.Name}.", InfoBarSeverity.Informational);

        _alarmFadeTimer?.Stop();
        if (fade <= TimeSpan.Zero)
        {
            return;
        }

        _alarmFadeStart = DateTimeOffset.UtcNow;
        if (_alarmFadeTimer is null)
        {
            _alarmFadeTimer = DispatcherQueue.CreateTimer();
            _alarmFadeTimer.Interval = AlarmFadeStep;
            _alarmFadeTimer.Tick += (_, _) => StepAlarmFade();
        }

        _alarmFadeTimer.Start();
    }

    private void StepAlarmFade()
    {
        var settings = _services.Settings;
        var target = Math.Clamp(settings.AlarmVolume, 5, 100) / 100.0;
        var fade = TimeSpan.FromMinutes(Math.Clamp(settings.AlarmFadeMinutes, 0, 15));
        var elapsed = DateTimeOffset.UtcNow - _alarmFadeStart;

        // Paused, or turned down by hand: the fade lets go.
        var state = _services.Player.State;
        var expected = AlarmRules.FadeVolume(elapsed - AlarmFadeStep, fade, target);
        if (!state.IsPlaying || state.Volume < expected - 0.08)
        {
            _alarmFadeTimer?.Stop();
            return;
        }

        _ = _services.Player.SetVolumeAsync(AlarmRules.FadeVolume(elapsed, fade, target));
        if (elapsed >= fade)
        {
            _alarmFadeTimer?.Stop();
        }
    }

    // ---- Focus timer ----

    partial void SetUpFocusTimer()
    {
        FollowPlugin(BuiltInPlugins.FocusTimer, () =>
        {
            if (!_services.BuiltIns.IsOn(BuiltInPlugins.FocusTimer))
            {
                StopFocus();
            }
        });
        Closed += (_, _) => _focusTimer?.Stop();
    }

    /// <summary>Whether a focus round or break is under way.</summary>
    internal bool FocusRunning => _focusOn;

    /// <summary>What the focus timer is doing, for its settings.</summary>
    internal string FocusStatus =>
        !_focusOn ? "Not running"
        : $"{(_focusPhase ? "Focus" : "Break")}, round {_focusRound} of {Math.Max(_services.Settings.FocusRounds, 1)}, {Math.Max(1, (int)Math.Ceiling((_focusPhaseEnds - DateTimeOffset.UtcNow).TotalMinutes))} min left";

    /// <summary>Starts the first round of focus, or stops the timer.</summary>
    internal void ToggleFocus()
    {
        if (_focusOn)
        {
            StopFocus();
            ShowMessage("Focus timer stopped.", InfoBarSeverity.Informational);
            return;
        }

        if (_focusTimer is null)
        {
            _focusTimer = DispatcherQueue.CreateTimer();
            _focusTimer.Interval = TimeSpan.FromSeconds(1);
            _focusTimer.Tick += (_, _) => CheckFocus();
        }

        _focusOn = true;
        StartFocusPhase(focus: true, round: 1);
        _focusTimer.Start();
    }

    private void StopFocus()
    {
        _focusOn = false;
        _focusTimer?.Stop();
    }

    private void StartFocusPhase(bool focus, int round)
    {
        var settings = _services.Settings;
        _focusPhase = focus;
        _focusRound = round;
        var minutes = focus ? Math.Clamp(settings.FocusMinutes, 1, 180) : Math.Clamp(settings.FocusBreakMinutes, 1, 60);
        _focusPhaseEnds = DateTimeOffset.UtcNow.AddMinutes(minutes);
        var player = _services.Player;
        if (settings.FocusPauseOnBreak)
        {
            if (focus && !player.State.IsPlaying && player.State.Title is not null)
            {
                _ = player.PlayAsync();
            }
            else if (!focus && player.State.IsPlaying)
            {
                _ = player.PauseAsync();
            }
        }

        var rounds = Math.Max(settings.FocusRounds, 1);
        TellFocus(focus ? $"Focus: {minutes} minutes (round {round} of {rounds})." : $"Break: {minutes} minutes.");
    }

    private void CheckFocus()
    {
        if (!_focusOn || DateTimeOffset.UtcNow < _focusPhaseEnds)
        {
            return;
        }

        if (FocusRules.Next(_focusPhase, _focusRound, _services.Settings.FocusRounds) is { } next)
        {
            StartFocusPhase(next.Focus, next.Round);
            return;
        }

        StopFocus();
        TellFocus("Focus done. Well done!");
    }

    /// <summary>In the window, and as a Windows notification while the user wants them.</summary>
    private void TellFocus(string text)
    {
        ShowMessage(text, InfoBarSeverity.Informational);
        if (!_services.Settings.FocusNotify)
        {
            return;
        }

        try
        {
            if (!_notificationsRegistered)
            {
                AppNotificationManager.Default.Register();
                _notificationsRegistered = true;
            }

            var notification = new AppNotificationBuilder().AddText("Focus timer").AddText(text).BuildNotification();
            notification.Tag = "focus";
            notification.Group = "focus";
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            CrashGuard.Write("notifications", ex, null);
        }
    }

    // ---- Skip intros and outros ----

    partial void SetUpSkipIntros()
    {
        FollowPlugin(BuiltInPlugins.SkipIntros, FollowSkips);
        FollowPlayer(FollowSkips);
        Closed += (_, _) => _skipTimer?.Stop();
        FollowSkips();
    }

    /// <summary>A new song may skip its start; while one plays with an end to skip, its place is checked each second.</summary>
    internal void FollowSkips()
    {
        var settings = _services.Settings;
        var state = _services.Player.State;
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.SkipIntros) || !state.IsPlaying || state.Duration <= TimeSpan.Zero)
        {
            _skipTimer?.Stop();
            return;
        }

        var song = state.TrackUri ?? state.Title;
        if (song != _skipSong)
        {
            _skipSong = song;
            _introSkipped = false;
            _outroSkipped = false;
        }

        var position = state.PositionAt(DateTimeOffset.UtcNow);
        if (!_introSkipped && SkipRules.IntroSkip(position, state.Duration, settings.SkipIntroSeconds, settings.SkipShortestSeconds) is { } to)
        {
            _introSkipped = true;
            _ = _services.Player.SeekAsync(to);
        }

        if (settings.SkipOutroSeconds <= 0)
        {
            _skipTimer?.Stop();
            return;
        }

        if (_skipTimer is null)
        {
            _skipTimer = DispatcherQueue.CreateTimer();
            _skipTimer.Interval = TimeSpan.FromSeconds(1);
            _skipTimer.Tick += (_, _) => CheckOutro();
        }

        _skipTimer.Start();
    }

    private void CheckOutro()
    {
        var settings = _services.Settings;
        var state = _services.Player.State;
        if (_outroSkipped || !state.IsPlaying)
        {
            return;
        }

        if (SkipRules.OutroReached(state.PositionAt(DateTimeOffset.UtcNow), state.Duration, settings.SkipOutroSeconds, settings.SkipShortestSeconds))
        {
            _outroSkipped = true;
            _ = _services.Player.NextAsync();
        }
    }

    // ---- Volume per device ----

    partial void SetUpDeviceVolume()
    {
        FollowPlugin(BuiltInPlugins.DeviceVolume, FollowDeviceVolume);
        FollowPlayer(RememberDeviceVolume);
        Closed += (_, _) => _deviceWatch?.Dispose();
        FollowDeviceVolume();
    }

    private void FollowDeviceVolume()
    {
        var on = _services.BuiltIns.IsOn(BuiltInPlugins.DeviceVolume) && !_services.IsDemo;
        if (on == (_deviceWatch is not null))
        {
            return;
        }

        if (!on)
        {
            _deviceWatch?.Dispose();
            _deviceWatch = null;
            _deviceName = null;
            return;
        }

        _deviceWatch = DefaultAudioOutput.Watch(() =>
        {
            if (Interlocked.Exchange(ref _deviceCheckQueued, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    // Windows sends a burst of notifications as an output comes or goes; let it settle.
                    await Task.Delay(400);
                    Interlocked.Exchange(ref _deviceCheckQueued, 0);
                    await CheckDeviceAsync();
                });
            }
        });
        _ = CheckDeviceAsync();
    }

    /// <summary>Another output: its own volume comes back (or the new-device volume), and the old one's was kept.</summary>
    private async Task CheckDeviceAsync()
    {
        var output = await Task.Run(DefaultAudioOutput.TryRead);
        if (_deviceWatch is null || output is null || output.Name == _deviceName)
        {
            return;
        }

        var first = _deviceName is null;
        _deviceName = output.Name;
        if (first)
        {
            RememberDeviceVolume();
            return;
        }

        var settings = _services.Settings;
        int? volume = settings.DeviceVolumes.TryGetValue(output.Name, out var saved) ? saved
            : settings.DeviceVolumeNew > 0 ? settings.DeviceVolumeNew
            : null;
        if (volume is not { } percent)
        {
            RememberDeviceVolume();
            return;
        }

        _deviceApplying = true;
        try
        {
            await _services.Player.SetVolumeAsync(Math.Clamp(percent, 0, 100) / 100.0);
        }
        finally
        {
            _deviceApplying = false;
        }

        if (settings.DeviceVolumeMessage)
        {
            ShowMessage($"{output.Name}: volume {percent}%.", InfoBarSeverity.Informational);
        }
    }

    /// <summary>Keeps the volume the user set for the output in use.</summary>
    private void RememberDeviceVolume()
    {
        if (_deviceWatch is null || _deviceApplying || _deviceName is not { } name)
        {
            return;
        }

        var percent = (int)Math.Round(_services.Player.State.Volume * 100);
        var volumes = _services.Settings.DeviceVolumes;
        if (!volumes.TryGetValue(name, out var saved) || saved != percent)
        {
            volumes[name] = percent;

            // Saved once the volume settles, not for every step of a drag.
            if (_deviceSaveTimer is null)
            {
                _deviceSaveTimer = DispatcherQueue.CreateTimer();
                _deviceSaveTimer.Interval = TimeSpan.FromSeconds(2);
                _deviceSaveTimer.IsRepeating = false;
                _deviceSaveTimer.Tick += (_, _) => _services.SaveSettings();
            }

            _deviceSaveTimer.Stop();
            _deviceSaveTimer.Start();
        }
    }

    /// <summary>Forgets every output's volume (its settings' button).</summary>
    internal void ForgetDeviceVolumes()
    {
        _services.Settings.DeviceVolumes.Clear();
        _services.SaveSettings();
        RememberDeviceVolume();
    }

    // ---- Any plugin's settings ----

    /// <summary>A plugin's setting changed in its popup: everything that reads one shows it at once.</summary>
    internal void PluginOptionsChanged()
    {
        PlayerBar.RefreshSignal();
        ShowSmartPlaylistsInSidebar();
        FollowShapeOptions();
        FollowKeepAwake();
        _nowPlayingWritten = null;
        WriteNowPlaying();
        FollowQuietHours();
        RefreshDesktopLyrics();
        RefreshBeatGlow();
        FollowSkips();
        FollowAlarm();
    }
}
