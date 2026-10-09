namespace Resonate.App.Services;

/// <summary>A plugin built into Resonate, listed in Settings, Plugins.</summary>
public sealed record BuiltInPlugin(string Id, string Name, string Description);

/// <summary>
/// Features built into Resonate that stay off until they are turned on in
/// Settings, Plugins, like the downloaded plugins (the owner asked for them
/// as optional plugins, 8 October 2026). They need Resonate's own windows,
/// pages and the Spotify Web API, which the JavaScript helper cannot reach,
/// so they are compiled in; while one is off, nothing of it runs or shows.
/// Each one watches <see cref="Changed"/> to start and stop at once.
/// </summary>
public sealed class BuiltInPlugins
{
    public const string Lyrics = "lyrics";
    public const string HomeStage = "home-stage";
    public const string AwayScreen = "away-screen";
    public const string Rediscover = "rediscover";
    public const string UpNext = "up-next";
    public const string ArtistOrbit = "artist-orbit";
    public const string SmartPlaylists = "smart-playlists";
    public const string WindowShapes = "window-shapes";
    public const string SummonBar = "summon-bar";
    public const string SignalPath = "signal-path";
    public const string PauseOnLock = "pause-on-lock";
    public const string PauseOnUnplug = "pause-on-unplug";
    public const string TrayIcon = "tray-icon";
    public const string PlayerLyrics = "player-lyrics";
    public const string SongNotifications = "song-notifications";
    public const string KeepAwake = "keep-awake";
    public const string TaskbarControls = "taskbar-controls";
    public const string NowPlayingFile = "now-playing-file";
    public const string QuietHours = "quiet-hours";
    public const string PauseForSounds = "pause-for-sounds";
    public const string DesktopLyrics = "desktop-lyrics";
    public const string BeatGlow = "beat-glow";
    public const string ResumeOnStart = "resume-on-start";
    public const string MediaShortcuts = "media-hotkeys";
    public const string StartWithWindows = "start-with-windows";
    public const string HistoryExport = "history-export";
    public const string Alarm = "alarm";
    public const string FocusTimer = "focus-timer";
    public const string SkipIntros = "skip-intros";
    public const string DeviceVolume = "device-volume";

    private readonly AppSettings _settings;
    private readonly Action _save;

    public BuiltInPlugins(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    /// <summary>Raised on the interface thread with a plugin's ID after it was turned on or off.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>
    /// Every built-in plugin, with a plain name and one sentence about it (the
    /// owner asked for obvious names, 9 October 2026; the IDs stay, so what
    /// was on stays on). Settings lists them by name.
    /// </summary>
    public static IReadOnlyList<BuiltInPlugin> All { get; } =
    [
        new(AwayScreen, "Screensaver", "After a few idle minutes, covers the screen with the song, a clock and a visualizer, above every app."),
        new(Rediscover, "Rediscover songs", "Adds a Home row of songs you liked on this day and ones you have not played in a while."),

        new(ArtistOrbit, "Related artists", "Shows the artists you play alongside an artist on their page."),
        new(SmartPlaylists, "Smart playlists", "Makes playlists that fill themselves from your Liked Songs by rules you set."),
        new(WindowShapes, "Compact window", "Shrinks the window to a small player or a one-line strip that can stay on top."),
        new(SummonBar, "Quick search", "Opens a search box over any app with a shortcut, to play or queue a song."),
        new(SignalPath, "Lossless badge", "Shows in the player whether you hear lossless sound, and what changes it."),
        new(PauseOnLock, "Pause on lock", "Pauses when you lock your PC and plays on when you unlock it."),
        new(PauseOnUnplug, "Pause on unplug", "Pauses when your headphones or speaker disconnect."),

        new(PlayerLyrics, "Lyrics in the player", "Shows the sung line and the next one under the song in the player bar."),
        new(SongNotifications, "Song notifications", "Shows a Windows notification with the cover when a new song starts while Resonate is in the background."),
        new(KeepAwake, "Keep PC awake", "Stops your PC from going to sleep while music plays."),

        new(NowPlayingFile, "Now playing file", "Writes the playing song to a text file, for stream overlays such as OBS."),
        new(QuietHours, "Quiet hours", "Keeps the volume down during the hours you choose, such as at night."),
        new(PauseForSounds, "Pause for other sounds", "Pauses the music while another app plays sound, such as a call or a video, and plays on after."),
        new(DesktopLyrics, "Desktop lyrics", "Shows the sung line in a small window that stays on top of every app."),
        new(BeatGlow, "Beat glow", "Makes the window's edge glow with the beat of the music."),
        new(ResumeOnStart, "Resume on start", "Plays the song you were listening to when you open Resonate."),
        new(MediaShortcuts, "Media shortcuts", "Shortcuts that play, pause, skip, change the volume or like the song from any app."),

        new(HistoryExport, "Export history", "Saves your listening history as a spreadsheet file."),
        new(Alarm, "Alarm", "Wakes you with your music at the time you pick, rising slowly from quiet."),
        new(FocusTimer, "Focus timer", "Times rounds of focus with breaks between them, pausing the music for each break."),
        new(SkipIntros, "Skip intros and outros", "Skips the first and last seconds of every song, for long intros and fade-outs."),
        new(DeviceVolume, "Volume per device", "Remembers a volume for each pair of headphones and speakers and puts it back when you switch."),
    ];

    /// <summary>
    /// Part of Resonate itself now, always on and not listed: Lyrics and the
    /// Home stage (the owner's choice, 8 October 2026), the tray icon, editing
    /// the queue and the taskbar buttons (9 October 2026). Start with Windows
    /// became a switch in Settings, About (<see cref="AppSettings.StartWithWindows"/>).
    /// </summary>
    public static bool IsAlwaysOn(string id) => id is Lyrics or HomeStage or TrayIcon or UpNext or TaskbarControls;

    public bool IsOn(string id) => IsAlwaysOn(id) || _settings.BuiltInPlugins.Contains(id, StringComparer.Ordinal);

    /// <summary>Turns a plugin on or off and saves it. Call on the interface thread.</summary>
    public void Set(string id, bool on)
    {
        if (IsOn(id) == on)
        {
            return;
        }

        if (on)
        {
            _settings.BuiltInPlugins.Add(id);
        }
        else
        {
            _settings.BuiltInPlugins.RemoveAll(i => string.Equals(i, id, StringComparison.Ordinal));
        }

        _save();
        Changed?.Invoke(this, id);
    }
}
