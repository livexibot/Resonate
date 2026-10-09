using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Windows;

namespace Resonate.App;

/// <summary>
/// Built-in plugins that reach past the window (the owner asked for them on
/// 9 October 2026, see <see cref="BuiltInPlugins"/>): Media shortcuts,
/// Song notifications, Taskbar controls and Pause for other sounds.
/// </summary>
public sealed partial class MainWindow
{
    private const int MediaHotkeyBase = 0x5310;
    private const uint WmCommand = 0x0111;
    private const string NotificationTag = "song";

    private IDisposable? _mediaHook;
    private bool _notificationsRegistered;
    private string? _notifiedSong;
    private int _notificationCover;
    private TaskbarButtons? _taskbarButtons;
    private IDisposable? _taskbarHook;
    private Timer? _soundsTimer;
    private int _otherSoundTicks;
    private int _quietTicks;
    private bool _pausedForSounds;

    // Media shortcuts

    partial void SetUpMediaShortcuts()
    {
        FollowPlugin(BuiltInPlugins.MediaShortcuts, FollowMediaShortcuts);
        Closed += (_, _) => UnregisterMediaShortcuts();
        FollowMediaShortcuts();
    }

    private bool MediaShortcutsOn => _services.BuiltIns.IsOn(BuiltInPlugins.MediaShortcuts);

    private void FollowMediaShortcuts()
    {
        if (!MediaShortcutsOn)
        {
            UnregisterMediaShortcuts();
            _mediaHook?.Dispose();
            _mediaHook = null;
            return;
        }

        _mediaHook ??= WindowHook.Listen(Hwnd, OnMediaHotkeyMessage);
        RegisterMediaShortcuts();
    }

    /// <summary>Takes new keys for <paramref name="action"/> (null for none); says why when they cannot be used.</summary>
    internal string? SetMediaShortcut(MediaShortcut action, Shortcut? shortcut)
    {
        var settings = _services.Settings;
        if (shortcut is { } keys && MediaShortcutKeys.All.Any(other => other != action && Shortcut.Parse(MediaShortcutKeys.Read(settings, other)) == keys))
        {
            RegisterMediaShortcuts();
            return "Those keys already do something else here.";
        }

        var before = MediaShortcutKeys.Read(settings, action);
        MediaShortcutKeys.Write(settings, action, shortcut?.ToString());
        if (RegisterMediaShortcut(action) is { } problem)
        {
            MediaShortcutKeys.Write(settings, action, before);
            RegisterMediaShortcuts();
            return problem;
        }

        _services.SaveSettings();
        RegisterMediaShortcuts();
        return null;
    }

    /// <summary>While Settings records new keys, the ones in use do nothing.</summary>
    internal void PauseMediaShortcuts() => UnregisterMediaShortcuts();

    internal void ResumeMediaShortcuts() => RegisterMediaShortcuts();

    private void RegisterMediaShortcuts()
    {
        foreach (var action in MediaShortcutKeys.All)
        {
            RegisterMediaShortcut(action);
        }
    }

    private string? RegisterMediaShortcut(MediaShortcut action)
    {
        var id = MediaHotkeyBase + (int)action;
        UnregisterHotKey(Hwnd, id);
        if (!MediaShortcutsOn || Shortcut.Parse(MediaShortcutKeys.Read(_services.Settings, action)) is not { } shortcut)
        {
            return null;
        }

        if (RegisterHotKey(Hwnd, id, shortcut.Modifiers | ModNoRepeat, (uint)shortcut.Key))
        {
            return null;
        }

        return Marshal.GetLastPInvokeError() == ErrorHotkeyAlreadyRegistered
            ? "Another app already uses those keys."
            : "Windows did not take those keys.";
    }

    private void UnregisterMediaShortcuts()
    {
        foreach (var action in MediaShortcutKeys.All)
        {
            UnregisterHotKey(Hwnd, MediaHotkeyBase + (int)action);
        }
    }

    private void OnMediaHotkeyMessage(uint message, nint wParam, nint lParam)
    {
        var index = (int)wParam - MediaHotkeyBase;
        if (message == WindowHook.WmHotkey && index >= 0 && index < MediaShortcutKeys.All.Count)
        {
            DispatcherQueue.TryEnqueue(() => RunMediaShortcut((MediaShortcut)index));
        }
    }

    private void RunMediaShortcut(MediaShortcut action)
    {
        var player = _services.Player;
        var state = player.State;
        switch (action)
        {
            case MediaShortcut.PlayPause:
                _ = player.TogglePlayPauseAsync();
                break;
            case MediaShortcut.Next:
                _ = player.NextAsync();
                break;
            case MediaShortcut.Previous:
                _ = player.PreviousAsync();
                break;
            case MediaShortcut.VolumeUp:
                _ = player.SetVolumeAsync(Math.Min(1, state.Volume + 0.05));
                break;
            case MediaShortcut.VolumeDown:
                _ = player.SetVolumeAsync(Math.Max(0, state.Volume - 0.05));
                break;
            default:
                LikePlaying(state);
                break;
        }
    }

    /// <summary>Likes the playing song (or takes the like away), as its menu does.</summary>
    private void LikePlaying(Resonate.Spotify.Playback.PlayerState state)
    {
        if (state.TrackUri is not { } uri || !uri.StartsWith("spotify:track:", StringComparison.Ordinal))
        {
            return;
        }

        var liked = _services.Likes.IsLiked(uri);
        var track = new TrackInfo(uri, state.Title ?? string.Empty, state.Artists ?? string.Empty, state.Album ?? string.Empty, null, state.Duration, state.ArtworkUrl, state.ArtworkUrl, false, true);
        _ = Pages.Lists.TrackActions.SetLikedAsync(track, !liked);
        ShowMessage(liked ? $"Removed {track.Title} from Liked Songs." : $"Saved {track.Title} to Liked Songs.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
    }

    // Song notifications

    partial void SetUpSongNotifications()
    {
        FollowPlugin(BuiltInPlugins.SongNotifications, FollowSongNotifications);
        FollowPlayer(NotifySong);
        Closed += (_, _) =>
        {
            if (_notificationsRegistered)
            {
                try
                {
                    AppNotificationManager.Default.Unregister();
                }
                catch (Exception)
                {
                    // Closing anyway.
                }
            }
        };
        FollowSongNotifications();
    }

    private void FollowSongNotifications()
    {
        if (!_services.BuiltIns.IsOn(BuiltInPlugins.SongNotifications) || _notificationsRegistered)
        {
            return;
        }

        try
        {
            // A click on one brings the window forward (only while Resonate runs).
            AppNotificationManager.Default.NotificationInvoked += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                AppWindow.Show();
                BringToFront();
            });
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
            _notifiedSong = SongKey(_services.Player.State);
        }
        catch (Exception ex)
        {
            CrashGuard.Write("notifications", ex, null);
            ShowMessage("Windows did not let Resonate show notifications.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
    }

    private static string? SongKey(Resonate.Spotify.Playback.PlayerState state) => state.Title is null ? null : $"{state.Title}\n{state.Artists}";

    /// <summary>A new song plays: a quiet notification with its cover, replacing the one before.</summary>
    private void NotifySong()
    {
        var state = _services.Player.State;
        var key = SongKey(state);
        if (!_notificationsRegistered || !_services.BuiltIns.IsOn(BuiltInPlugins.SongNotifications) || key is null || key == _notifiedSong || !state.IsPlaying)
        {
            return;
        }

        _notifiedSong = key;
        if (!_services.Settings.SongNotificationsAlways && IsShown && UserPresence.IsForeground(Hwnd))
        {
            return;
        }

        _ = ShowSongNotificationAsync(state);
    }

    private async Task ShowSongNotificationAsync(Resonate.Spotify.Playback.PlayerState state)
    {
        var settings = _services.Settings;
        var builder = new AppNotificationBuilder()
            .AddText(state.Title ?? string.Empty)
            .AddText(settings.SongNotificationsAlbum && state.Album is { Length: > 0 } album ? $"{state.Artists} · {album}" : state.Artists ?? string.Empty);
        if (!settings.SongNotificationsSound)
        {
            builder.MuteAudio();
        }

        if (settings.SongNotificationsCover && await NotificationCoverAsync(state) is { } cover)
        {
            builder.SetAppLogoOverride(cover, AppNotificationImageCrop.Default);
        }

        try
        {
            var notification = builder.BuildNotification();
            notification.Tag = NotificationTag;
            notification.Group = NotificationTag;
            notification.Expiration = DateTimeOffset.Now.AddMinutes(10);
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            CrashGuard.Write("notifications", ex, null);
        }
    }

    /// <summary>The cover as a file Windows can show (notifications read files, not Spotify's addresses), or null.</summary>
    private async Task<Uri?> NotificationCoverAsync(Resonate.Spotify.Playback.PlayerState state)
    {
        try
        {
            byte[]? bytes = state.ArtworkBytes;
            if (bytes is null && state.ArtworkUrl is { } url && _services.Covers.Store is { } store && CoverStore.Handles(url))
            {
                bytes = store.TryGetRecent(url) ?? await store.GetAsync(url);
            }

            if (bytes is null)
            {
                return null;
            }

            // Two files in turn, so the one Windows still shows is never overwritten.
            _notificationCover ^= 1;
            var path = Path.Combine(AppPaths.CacheFolder, $"notification-cover-{_notificationCover}.jpg");
            await File.WriteAllBytesAsync(path, bytes);
            return new Uri(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return null;
        }
    }

    // Taskbar controls

    partial void SetUpTaskbarControls()
    {
        FollowPlugin(BuiltInPlugins.TaskbarControls, FollowTaskbarControls);
        FollowPlayer(FollowTaskbarControls);
        Closed += (_, _) =>
        {
            _taskbarHook?.Dispose();
            _taskbarButtons?.Dispose();
            _taskbarButtons = null;
        };
        FollowTaskbarControls();
    }

    private void FollowTaskbarControls()
    {
        var on = _services.BuiltIns.IsOn(BuiltInPlugins.TaskbarControls);
        if (!on)
        {
            _taskbarButtons?.Hide();
            return;
        }

        _taskbarHook ??= WindowHook.Listen(Hwnd, OnTaskbarMessage);
        _taskbarButtons ??= new TaskbarButtons(Hwnd, (int)Math.Round(16 * GetDpiForWindow(Hwnd) / 96.0));
        _taskbarButtons.Show(_services.Player.State.IsPlaying);
    }

    private void OnTaskbarMessage(uint message, nint wParam, nint lParam)
    {
        if (message == TaskbarButtons.TaskbarButtonCreatedMessage)
        {
            // Explorer started again: the buttons are added anew.
            DispatcherQueue.TryEnqueue(() =>
            {
                _taskbarButtons?.Forget();
                FollowTaskbarControls();
            });
            return;
        }

        if (message != WmCommand || ((wParam >> 16) & 0xFFFF) != TaskbarButtons.Clicked)
        {
            return;
        }

        var button = (int)(wParam & 0xFFFF);
        DispatcherQueue.TryEnqueue(() =>
        {
            var player = _services.Player;
            _ = button switch
            {
                TaskbarButtons.PreviousId => player.PreviousAsync(),
                TaskbarButtons.NextId => player.NextAsync(),
                _ => player.TogglePlayPauseAsync(),
            };
        });
    }

    // Pause for other sounds

    partial void SetUpPauseForSounds()
    {
        FollowPlugin(BuiltInPlugins.PauseForSounds, FollowPauseForSounds);
        FollowPlayer(FollowPauseForSounds);
        Closed += (_, _) => _soundsTimer?.Dispose();
        FollowPauseForSounds();
    }

    /// <summary>Listens to the mixer's levels once a second while music plays here, or while it waits to play on.</summary>
    private void FollowPauseForSounds()
    {
        var state = _services.Player.State;
        if (state.IsPlaying && _pausedForSounds)
        {
            // Played again (by the user, or by this): nothing waits any more.
            _pausedForSounds = false;
        }

        var watch = _services.BuiltIns.IsOn(BuiltInPlugins.PauseForSounds) && (state.IsPlaying || _pausedForSounds || _loweredFrom is not null);
        if (!watch)
        {
            _soundsTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _otherSoundTicks = 0;
            _quietTicks = 0;
            if (!_services.BuiltIns.IsOn(BuiltInPlugins.PauseForSounds))
            {
                _pausedForSounds = false;
            }

            return;
        }

        _soundsTimer ??= new Timer(_ => CheckOtherSounds());
        _soundsTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A thread-pool timer: another app heard for the user's few seconds (2
    /// at first) pauses the music, or lowers it; quiet seconds after that (3
    /// at first) play it on or bring the volume back, if this did it.
    /// </summary>
    private void CheckOtherSounds()
    {
        var heard = OtherAppSounds.AnyPlaying();
        DispatcherQueue.TryEnqueue(() =>
        {
            var player = _services.Player;
            var settings = _services.Settings;
            if (heard)
            {
                _quietTicks = 0;
                if (++_otherSoundTicks >= Math.Clamp(settings.PauseForSoundsWait, 1, 10) && player.State.IsPlaying && !_pausedForSounds && _loweredFrom is null)
                {
                    if (settings.PauseForSoundsLower)
                    {
                        _loweredFrom = player.State.Volume;
                        _ = player.SetVolumeAsync(player.State.Volume * Math.Clamp(settings.PauseForSoundsLowerTo, 5, 80) / 100.0);
                    }
                    else
                    {
                        _pausedForSounds = true;
                        _ = player.PauseAsync();
                    }
                }
            }
            else
            {
                _otherSoundTicks = 0;
                if ((_pausedForSounds || _loweredFrom is not null) && ++_quietTicks >= Math.Clamp(settings.PauseForSoundsResume, 1, 30))
                {
                    _quietTicks = 0;
                    if (_loweredFrom is { } volume)
                    {
                        _loweredFrom = null;
                        _ = player.SetVolumeAsync(volume);
                    }

                    if (_pausedForSounds)
                    {
                        _pausedForSounds = false;
                        if (!player.State.IsPlaying)
                        {
                            _ = player.PlayAsync();
                        }
                    }
                }
            }
        });
    }
}
