using Microsoft.UI.Dispatching;

namespace Resonate.App;

/// <summary>
/// The built-in plugins that live in the window (see
/// <see cref="Services.BuiltInPlugins"/>). Each one sets itself up in its
/// own <c>MainWindow.&lt;Name&gt;.cs</c>, watching
/// <c>_services.BuiltIns.Changed</c> so it starts and stops at once; while
/// it is off it shows nothing and runs nothing.
/// </summary>
public sealed partial class MainWindow
{
    private void SetUpBuiltInPlugins()
    {
        SetUpLyrics();
        SetUpHomeStage();
        SetUpAwayScreen();
        SetUpRediscover();
        SetUpUpNext();
        SetUpArtistOrbit();
        SetUpSmartPlaylists();
        SetUpWindowShapes();
        SetUpSummonBar();
        SetUpSignalPath();
        SetUpPauseOnLock();
        SetUpPauseOnUnplug();
        SetUpTray();
        SetUpPlayerLyrics();
        SetUpSongNotifications();
        SetUpKeepAwake();
        SetUpTaskbarControls();
        SetUpNowPlayingFile();
        SetUpQuietHours();
        SetUpPauseForSounds();
        SetUpBeatGlow();
        SetUpMediaShortcuts();
        SetUpStartWithWindows();
        SetUpAlarm();
        SetUpFocusTimer();
        SetUpSkipIntros();
        SetUpDeviceVolume();
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the interface thread after the
    /// player's state changed (the changes come on any thread, often several
    /// at once): once for each burst, for plugins that follow the music.
    /// </summary>
    private void FollowPlayer(Action action)
    {
        var queued = 0;
        _services.Player.StateChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref queued, 1) == 0)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    Interlocked.Exchange(ref queued, 0);
                    action();
                });
            }
        };
    }

    /// <summary>Runs <paramref name="action"/> when the built-in plugin <paramref name="id"/> is turned on or off.</summary>
    private void FollowPlugin(string id, Action action) =>
        _services.BuiltIns.Changed += (_, changed) =>
        {
            if (changed == id)
            {
                action();
            }
        };

    partial void SetUpLyrics();

    partial void SetUpHomeStage();

    partial void SetUpAwayScreen();

    partial void SetUpRediscover();

    partial void SetUpUpNext();

    partial void SetUpArtistOrbit();

    partial void SetUpSmartPlaylists();

    partial void SetUpWindowShapes();

    partial void SetUpSummonBar();

    partial void SetUpSignalPath();

    partial void SetUpPauseOnLock();

    partial void SetUpPauseOnUnplug();

    partial void SetUpTray();

    partial void SetUpPlayerLyrics();

    partial void SetUpSongNotifications();

    partial void SetUpKeepAwake();

    partial void SetUpTaskbarControls();

    partial void SetUpNowPlayingFile();

    partial void SetUpQuietHours();

    partial void SetUpPauseForSounds();

    partial void SetUpBeatGlow();

    partial void SetUpMediaShortcuts();

    partial void SetUpStartWithWindows();

    partial void SetUpAlarm();

    partial void SetUpFocusTimer();

    partial void SetUpSkipIntros();

    partial void SetUpDeviceVolume();
}
