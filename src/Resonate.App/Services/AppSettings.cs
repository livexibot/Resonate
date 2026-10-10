using System.Text.Json;
using System.Text.Json.Serialization;
using Resonate.Themes;

namespace Resonate.App.Services;

/// <summary>Preferences kept between launches. Nothing secret lives here.</summary>
public sealed class AppSettings
{
    /// <summary>The user's own Spotify developer app. Not a secret with PKCE sign-in.</summary>
    public string? ClientId { get; set; }

    /// <summary>The look in use: a preset's ID, "custom", or a saved look's ID.</summary>
    public string ThemeId { get; set; } = ThemePresets.Default.Id;

    /// <summary>The look being customised (a copy of a preset with the user's changes), if any.</summary>
    public ThemeDefinition? CustomLook { get; set; }

    /// <summary>Looks the user saved under their own names.</summary>
    public List<ThemeDefinition> SavedLooks { get; set; } = [];

    /// <summary>How switching looks animates.</summary>
    public ThemeTransitionKind ThemeTransition { get; set; } = ThemeTransitionKind.Morph;

    /// <summary>The now-playing cover turns like a record while a song plays (off unless the user switches it on).</summary>

    /// <summary>Looks with the song cover backdrop show the cover itself, blurred (off: only its colours).</summary>
    public bool BlurredCoverBackground { get; set; } = true;

    /// <summary>How much the song cover backdrop (Liquid Glass) is blurred, 0 to 100.</summary>
    public int CoverBlur { get; set; } = 60;

    /// <summary>How a page comes in when another opens: Default, Rise, DrillIn, Slide, Fade or None.</summary>
    public string PageAnimation { get; set; } = "Default";

    /// <summary>How a new song arrives in the player bar: Fade, Slide, Pop or None.</summary>
    public string SongChangeAnimation { get; set; } = "Slide";


    /// <summary>Song lists show each song's cover (Settings, Layout, Song lists).</summary>
    public bool ShowSongCovers { get; set; } = true;

    /// <summary>The sidebar shows each playlist's cover (always while it shows only covers).</summary>
    public bool ShowPlaylistCovers { get; set; } = true;

    /// <summary>How large covers are in song lists and the sidebar, in percent (one of <see cref="AppScale.CoverSizes"/>).</summary>
    public int CoverSize { get; set; } = AppScale.Normal;

    /// <summary>Song lists show the row of column names (#, Title, Album...) above the songs.</summary>
    public bool ShowColumnNames { get; set; } = true;

    /// <summary>Song lists' optional columns.</summary>
    public bool ShowNumberColumn { get; set; } = true;

    public bool ShowLikeColumn { get; set; } = true;

    public bool ShowDurationColumn { get; set; } = true;

    public bool ShowAlbumColumn { get; set; } = true;

    public bool ShowAddedColumn { get; set; } = true;

    public bool ShowYearColumn { get; set; }

    /// <summary>Song stats from ReccoBeats in song lists (sends the songs' Spotify IDs there); off until the user turns it on.</summary>
    public bool SongStats { get; set; }

    public bool ShowBpmColumn { get; set; } = true;

    public bool ShowKeyColumn { get; set; } = true;

    public bool ShowLoudnessColumn { get; set; } = true;

    public bool ShowEnergyColumn { get; set; }

    /// <summary>The library sidebar runs the window's full height, beside the player (off unless the user switches it on).</summary>
    public bool SidebarFullHeight { get; set; }

    /// <summary>The Spotify song that played last and its place, shown when Resonate opens (see MainWindow.LastPlayed.cs).</summary>
    public Resonate.Spotify.Playback.LastPlayed? LastPlayed { get; set; }

    /// <summary>Resonate opens when the user signs in to Windows, quietly in the tray (Settings, General; on at first, the owner's choice of 9 October 2026).</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>The player's buttons sit in a row above the volume (Settings, Player, Style).</summary>
    public bool ButtonsAboveVolume { get; set; }

    /// <summary>Links the user hid from the sidebar (Settings, Layout), by page key. Home always shows; Local Files has <see cref="ShowLocalFiles"/>.</summary>
    public List<string> HiddenSidebarLinks { get; set; } = [];

    /// <summary>The last searches, newest first, which Search shows before anything is typed.</summary>
    public List<string> RecentSearches { get; set; } = [];

    /// <summary>What was last opened or played from Search, newest first ("Recently viewed").</summary>
    public List<Resonate.Spotify.Library.RecentSearchPick> RecentSearchPicks { get; set; } = [];

    /// <summary>The mini player button in the title bar (Ctrl+M opens the mini player either way).</summary>
    public bool ShowMiniPlayerButton { get; set; } = true;

    /// <summary>The sections of Settings the user folded away, by their headings (see Controls/SettingsGroup).</summary>
    public List<string> CollapsedSettingsSections { get; set; } = [];

    /// <summary>The sections that start folded (Customize, Effects, Winamp) which the user opened, by their headings.</summary>
    public List<string> ExpandedSettingsSections { get; set; } = [];

    /// <summary>
    /// New versions download by themselves and install when Resonate closes
    /// (on unless the user switches it off; then only Check for updates looks).
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>How large everything under the title bar is drawn, in percent (one of <see cref="AppScale.AppSizes"/>).</summary>
    public int AppSize { get; set; } = AppScale.Normal;

    /// <summary>How large text is drawn, in percent (one of <see cref="AppScale.TextSizes"/>).</summary>
    public int TextSize { get; set; } = AppScale.Normal;

    /// <summary>"modern" (Resonate's player bar) or "classic" (the skinnable player in the style of Winamp 2).</summary>
    public string PlayerStyle { get; set; } = "modern";

    /// <summary>The classic player's skin: a file name in <see cref="AppPaths.SkinsFolder"/>, or null for the built-in skin.</summary>
    public string? ClassicSkin { get; set; }

    /// <summary>
    /// The classic player at Winamp's double size, as people remember it and
    /// as Spotifast opens it: on at first (9 October 2026, the owner found
    /// normal size far too small). Saved under a new name so copies that
    /// saved the old "off" start doubled once.
    /// </summary>
    [JsonPropertyName("classicDouble")]
    public bool ClassicDoubleSize { get; set; } = true;

    /// <summary>The classic player in shade mode (just its title strip).</summary>
    public bool ClassicShaded { get; set; }

    /// <summary>The classic player's visualiser: a <see cref="Resonate.Themes.Skins.VisualiserMode"/> name.</summary>
    public string ClassicVisualiser { get; set; } = nameof(Resonate.Themes.Skins.VisualiserMode.Spectrum);

    /// <summary>The classic player's time display counts down.</summary>
    public bool ClassicShowRemaining { get; set; }

    /// <summary>The mini player's size: each skin pixel 1 to 4 times as large (on top of the display's scaling); 2 at first, under a new name for the same reason.</summary>
    [JsonPropertyName("miniPlayerScale")]
    public int MiniPlayerSize { get; set; } = 2;

    /// <summary>The mini player stays above other windows.</summary>
    public bool MiniPlayerOnTop { get; set; } = true;

    /// <summary>The mini player's main window in shade mode.</summary>
    public bool MiniPlayerShaded { get; set; }

    /// <summary>The mini player shows its equalizer window.</summary>
    public bool MiniPlayerEqualizer { get; set; }

    /// <summary>The mini player's equalizer rolled up.</summary>
    public bool MiniPlayerEqualizerShaded { get; set; }

    /// <summary>The mini player shows its playlist window (the queue).</summary>
    public bool MiniPlayerPlaylist { get; set; }

    /// <summary>The mini player's playlist rolled up.</summary>
    public bool MiniPlayerPlaylistShaded { get; set; }

    /// <summary>The mini player's playlist height in skin pixels (116, then steps of 29).</summary>
    public int MiniPlayerPlaylistHeight { get; set; } = 232;

    /// <summary>Where the mini player was left, in screen pixels; null to place it in the corner of the screen.</summary>
    public WindowPlacement? MiniPlayerPlace { get; set; }

    /// <summary>"local" (Windows' media controls first) or "webapi" (the Spotify Web API only).</summary>
    public string ControlChannel { get; set; } = "local";

    /// <summary>The Spotify Connect device picked last with "Spotify Web API only" (by name), for when nothing plays.</summary>
    public string? WebApiDeviceName { get; set; }

    /// <summary>With "Spotify Web API only", Resonate's own player plays on this PC (Spotify's web player, hidden).</summary>
    public bool WebApiPlayHere { get; set; } = true;

    /// <summary>Keep the Spotify app's window hidden and off the taskbar.</summary>
    public bool KeepSpotifyHidden { get; set; } = true;

    /// <summary>Put Spotify's window-drawing processes in efficiency mode while hidden.</summary>
    public bool SaveSpotifyResources { get; set; } = true;

    /// <summary>The equalizer is on, for Spotify's songs (the Spotify app's own equalizer) and local files alike.</summary>
    public bool EqualizerEnabled { get; set; }

    /// <summary>The equalizer's gain for each band, in decibels.</summary>
    public List<double> EqualizerGainsDb { get; set; } = [];

    /// <summary>The equalizer changed while Spotify was running; Spotify gets it the next time it starts.</summary>
    public bool EqualizerPendingForSpotify { get; set; }

    [JsonIgnore]
    public Resonate.Spotify.Audio.EqualizerSettings Equalizer
    {
        get
        {
            var settings = Resonate.Spotify.Audio.EqualizerSettings.Flat with { Enabled = EqualizerEnabled };
            for (var band = 0; band < EqualizerGainsDb.Count; band++)
            {
                settings = settings.WithGain(band, double.IsFinite(EqualizerGainsDb[band]) ? EqualizerGainsDb[band] : 0);
            }

            return settings;
        }

        set
        {
            EqualizerEnabled = value.Enabled;
            EqualizerGainsDb = [.. value.GainsDb];
        }
    }

    [JsonIgnore]
    public Resonate.Spotify.Playback.ControlChannel ParsedControlChannel =>
        ControlChannel == "webapi" ? Resonate.Spotify.Playback.ControlChannel.WebApi : Resonate.Spotify.Playback.ControlChannel.Local;

    /// <summary>The last measured time from starting the process to the first frame.</summary>
    public double? LastStartupMilliseconds { get; set; }

    /// <summary>How the sidebar orders playlists (a <see cref="Resonate.Spotify.Library.PlaylistSortMode"/> name).</summary>
    public string PlaylistSort { get; set; } = "Spotify";

    /// <summary>Playlist IDs in the user's own order, for the "Custom order" sort.</summary>
    public List<string> PlaylistOrder { get; set; } = [];

    /// <summary>When each playlist (by ID) was last played from Resonate, for the "Recently played" sort.</summary>
    public Dictionary<string, DateTimeOffset> PlaylistLastPlayed { get; set; } = [];

    /// <summary>The sort chosen for each song list (by list key), as <see cref="Resonate.Spotify.Library.TrackSort.Serialize"/> writes it.</summary>
    public Dictionary<string, string> TrackSorts { get; set; } = [];

    /// <summary>The sidebar's width as the user dragged it; null for the usual width.</summary>
    public double? SidebarWidth { get; set; }

    /// <summary>The queue pane's width as the user dragged it; null for the usual width.</summary>
    public double? QueueWidth { get; set; }

    /// <summary>The player's column beside the page (Placement Left or Right) as the user dragged it; null for the look's width.</summary>
    public double? SidePlayerWidth { get; set; }

    /// <summary>Where the window was left; null to open it centred.</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>The time range of "Your top on Spotify" on Home.</summary>
    public Resonate.Spotify.WebApi.TopRange HomeTopRange { get; set; } = Resonate.Spotify.WebApi.TopRange.ShortTerm;

    /// <summary>The width of the Settings pane on the right, as the user last dragged it; null for the usual width.</summary>
    public double? SettingsPaneWidth { get; set; }

    /// <summary>The folders Local Files looks in; null for the user's Music and Downloads folders.</summary>
    public List<string>? LocalFolders { get; set; }

    /// <summary>Show Local Files in the sidebar.</summary>
    public bool ShowLocalFiles { get; set; } = true;

    /// <summary>The local files player's volume, from 0 to 1 (the Spotify app keeps its own).</summary>
    public double LocalVolume { get; set; } = 1;

    // ---- Built-in plugins (Settings, Plugins; see BuiltInPlugins). Each keeps its settings under its own heading. ----

    /// <summary>The built-in plugins that are on, by ID.</summary>
    public List<string> BuiltInPlugins { get; set; } = [];

    // Lyrics

    // Home stage

    /// <summary>The Home stage shows the playing cover blurred behind its clouds (on unless the user turns it off).</summary>
    public bool HomeStageBlurredCover { get; set; } = true;

    /// <summary>The last cover's colours (#RRGGBB), so the stage opens in them before any cover is read.</summary>
    public List<string> HomeStageColours { get; set; } = [];

    /// <summary>The stage shows the visualizer's bars along its bottom (on unless the user turns it off).</summary>
    public bool HomeStageVisualizer { get; set; } = true;

    /// <summary>
    /// The bars follow Spotify's own sound, heard through Windows (on unless
    /// the user turns it off; the owner's choice of 8 October 2026). Off, they
    /// sway on their own for Spotify songs.
    /// </summary>
    public bool HomeStageListens { get; set; } = true;

    /// <summary>How strongly the bars answer the sound, 50 to 200 %.</summary>
    public int HomeStageSensitivity { get; set; } = 100;

    /// <summary>The visualizers' Size: how large each bar, dot, spark or ring line is, 20 to 90 %.</summary>
    public int HomeStageBarWidth { get; set; } = 56;

    /// <summary>The visualizers' Amount: how many bars, dots, sparks or rings, 16 to 96 (fewer when there is no room for them).</summary>
    public int HomeStageBars { get; set; } = 64;

    /// <summary>How much of the stage's width Home's visualizer spans, centred, 20 to 100 %.</summary>
    public int HomeStageWidth { get; set; } = 100;

    /// <summary>How tall Home's visualizer may grow, as a share of the stage's height, 10 to 60 %.</summary>
    public int HomeStageHeight { get; set; } = 26;

    /// <summary>The most Home's visualizer may grow, in pixels, 40 to 600.</summary>
    public int HomeStageMaxHeight { get; set; } = 280;

    /// <summary>How far Home's visualizer is moved right (negative: left), in pixels.</summary>
    public int HomeStageX { get; set; }

    /// <summary>How far Home's visualizer is moved down (negative: up), in pixels.</summary>
    public int HomeStageY { get; set; }

    /// <summary>How smoothly the bars rise and fall, 0 (snappy) to 100 (soft).</summary>
    public int HomeStageSmoothing { get; set; } = 60;

    // The player bar's visualizer: its own Sensitivity, Smoothing, Amount and Size (the owner's request, 9 October 2026).

    public int PlayerVisualizerSensitivity { get; set; } = 100;

    public int PlayerVisualizerSmoothing { get; set; } = 60;

    public int PlayerVisualizerAmount { get; set; } = 64;

    public int PlayerVisualizerSize { get; set; } = 56;

    public int PlayerVisualizerX { get; set; }

    /// <summary>How much of the player bar's width its visualizer spans, centred, 20 to 100 %.</summary>
    public int PlayerVisualizerWidth { get; set; } = 100;

    /// <summary>How tall the player bar's visualizer may grow, as a share of the bar's height, 10 to 100 %.</summary>
    public int PlayerVisualizerHeight { get; set; } = 45;

    /// <summary>The most the player bar's visualizer may grow, in pixels, 8 to 200.</summary>
    public int PlayerVisualizerMaxHeight { get; set; } = 200;

    public int PlayerVisualizerY { get; set; }

    // Screensaver (was Away screen; its settings keep their names)

    /// <summary>Minutes without touching the mouse or keyboard before the screensaver shows (1, 2, 5, 10, 15 or 30).</summary>
    public int AwayScreenMinutes { get; set; } = 5;

    /// <summary>The screensaver shows only while music plays (else also while paused or stopped).</summary>
    public bool ScreensaverOnlyWhilePlaying { get; set; } = true;

    /// <summary>The screensaver's visualizer (a <see cref="Resonate.Themes.VisualizerStyle"/> name, "Off"), or null for the look's Home style.</summary>
    public string? ScreensaverVisualizer { get; set; }

    /// <summary>The screensaver's background: "Song" (the cover's colours drifting), "Cover" (the blurred cover) or "Colour".</summary>
    public string ScreensaverBackground { get; set; } = "Song";

    /// <summary>The screensaver's colour, as #RRGGBB, with the "Colour" background.</summary>
    public string ScreensaverColour { get; set; } = "#000000";

    /// <summary>OLED mode: a black background, and everything moves further every minute so nothing burns in.</summary>
    public bool ScreensaverOled { get; set; }

    /// <summary>The screensaver shows the clock.</summary>
    public bool ScreensaverClock { get; set; } = true;

    // Rediscover

    // Up next

    // Artist orbit

    // Smart playlists

    /// <summary>The user's smart playlists: their rules, and the playlist on Spotify each one keeps up to date.</summary>
    public List<Resonate.Spotify.Library.SmartPlaylist> SmartPlaylists { get; set; } = [];

    // Window shapes

    /// <summary>The strip window shape stays above other windows.</summary>
    public bool WindowShapesPinned { get; set; }

    // Summon bar

    /// <summary>The keys that open the summon bar from any app, such as "Ctrl+Shift+K"; null for none (nothing is taken until the user picks).</summary>
    public string? SummonBarShortcut { get; set; }

    /// <summary>The user's own keys for the window's shortcuts, by command name (see <c>AppKeys</c>); an empty text means none.</summary>
    public Dictionary<string, string> KeyShortcuts { get; set; } = [];

    // Signal path

    // Song notifications

    /// <summary>Notifications also while Resonate is the window in front.</summary>
    public bool SongNotificationsAlways { get; set; }

    // Keep PC awake

    /// <summary>The display stays on too, not only the PC.</summary>
    public bool KeepAwakeDisplay { get; set; }

    // Now playing file

    /// <summary>Where the playing song is written; null for "Now playing.txt" in Documents\Resonate.</summary>
    public string? NowPlayingFilePath { get; set; }

    /// <summary>What is written: {title}, {artist} and {album} are filled in.</summary>
    public string NowPlayingFormat { get; set; } = "{artist} - {title}";

    // Quiet hours

    /// <summary>The hour quiet hours begin (0 to 23).</summary>
    public int QuietHoursFrom { get; set; } = 22;

    /// <summary>The hour quiet hours end (0 to 23).</summary>
    public int QuietHoursTo { get; set; } = 7;

    /// <summary>The loudest the volume may be during quiet hours, in percent.</summary>
    public int QuietHoursVolume { get; set; } = 30;

    // Desktop lyrics

    /// <summary>Where the desktop lyrics window was left (screen pixels), or null for above the taskbar.</summary>
    public WindowPlacement? DesktopLyricsPlace { get; set; }

    /// <summary>How large the desktop lyrics are, in percent.</summary>
    public int DesktopLyricsSize { get; set; } = 100;

    // Beat glow

    /// <summary>How strongly the window's edge glows, in percent.</summary>
    public int BeatGlowStrength { get; set; } = 60;

    // Media shortcuts (keys such as "Ctrl+Alt+Space"; null for none)

    public string? ShortcutPlayPause { get; set; }

    public string? ShortcutNext { get; set; }

    public string? ShortcutPrevious { get; set; }

    public string? ShortcutVolumeUp { get; set; }

    public string? ShortcutVolumeDown { get; set; }

    public string? ShortcutLike { get; set; }

    // ---- Each plugin's own settings (the owner asked for every plugin to be "highly customizable", 9 October 2026) ----

    /// <summary>Screensaver: how bright it is, 20 to 100 %.</summary>
    public int ScreensaverBrightness { get; set; } = 100;

    /// <summary>Rediscover songs: which kinds of card the row shows.</summary>
    public bool RediscoverOnThisDay { get; set; } = true;

    public bool RediscoverDust { get; set; } = true;

    public bool RediscoverDeepCuts { get; set; } = true;

    /// <summary>Related artists: how many artists, and the liked albums with them.</summary>
    public int RelatedArtistsCount { get; set; } = 10;

    public bool RelatedArtistsAlbums { get; set; } = true;

    /// <summary>Smart playlists: listed in the sidebar, and its "New smart playlist" link.</summary>
    public bool SmartPlaylistsInSidebar { get; set; } = true;

    public bool SmartPlaylistsNewLink { get; set; } = true;

    /// <summary>Compact window: the title bar's shape button, and which small shapes it may take.</summary>
    public bool WindowShapesButton { get; set; } = true;

    public bool WindowShapesStrip { get; set; } = true;

    public bool WindowShapesColumn { get; set; } = true;

    /// <summary>Quick search: also asks Spotify, after the library.</summary>
    public bool QuickSearchSpotify { get; set; } = true;

    /// <summary>Lossless badge: hidden while all is lossless, and its word beside the dot.</summary>
    public bool LosslessBadgeOnlyWhenNot { get; set; }

    public bool LosslessBadgeText { get; set; } = true;

    /// <summary>Pause on lock: plays on after unlocking.</summary>
    public bool PauseOnLockResume { get; set; } = true;

    /// <summary>Pause on unplug: plays on when the same headphones or speaker come back.</summary>
    public bool PauseOnUnplugResume { get; set; }

    /// <summary>Lyrics in the player: the next line under the sung one.</summary>
    public bool PlayerLyricsNextLine { get; set; } = true;

    /// <summary>Song notifications: the cover, the album, and Windows' sound.</summary>
    public bool SongNotificationsCover { get; set; } = true;

    public bool SongNotificationsAlbum { get; set; }

    public bool SongNotificationsSound { get; set; }

    /// <summary>Keep PC awake: also while the music is paused.</summary>
    public bool KeepAwakeWhilePaused { get; set; }

    /// <summary>Now playing file: emptied while paused (else it keeps the song).</summary>
    public bool NowPlayingClearWhenPaused { get; set; } = true;

    /// <summary>Quiet hours: every day (0), weekdays (1) or weekends (2).</summary>
    public int QuietHoursDays { get; set; }

    /// <summary>Pause for other sounds: seconds of another app before it acts, quiet seconds before it plays on, and lowering the volume instead.</summary>
    public int PauseForSoundsWait { get; set; } = 2;

    public int PauseForSoundsResume { get; set; } = 3;

    public bool PauseForSoundsLower { get; set; }

    public int PauseForSoundsLowerTo { get; set; } = 20;

    /// <summary>Desktop lyrics: the next line, and a dark background behind the text (0 to 100 %).</summary>
    public bool DesktopLyricsNextLine { get; set; } = true;

    public int DesktopLyricsBackground { get; set; }

    /// <summary>Beat glow: how far in from the edge it reaches (10 to 60 %), and the accent (0) or the second accent (1).</summary>
    public int BeatGlowSize { get; set; } = 38;

    public int BeatGlowColour { get; set; }

    /// <summary>Export history: all of it (0), or the last 7, 30 or 365 days.</summary>
    public int HistoryExportDays { get; set; }

    /// <summary>Alarm: when (local time), which days (0 every day, 1 weekdays, 2 weekends), what (a playlist ID, null for Liked Songs), how, and the day it last rang.</summary>
    public int AlarmHour { get; set; } = 7;

    public int AlarmMinute { get; set; }

    public int AlarmDays { get; set; } = 1;

    public string? AlarmPlaylist { get; set; }

    public bool AlarmShuffle { get; set; } = true;

    public int AlarmFadeMinutes { get; set; } = 2;

    public int AlarmVolume { get; set; } = 50;

    public DateOnly? AlarmLastRang { get; set; }

    /// <summary>Focus timer: minutes of focus and of break, rounds, pausing for breaks, and Windows notifications.</summary>
    public int FocusMinutes { get; set; } = 25;

    public int FocusBreakMinutes { get; set; } = 5;

    public int FocusRounds { get; set; } = 4;

    public bool FocusPauseOnBreak { get; set; } = true;

    public bool FocusNotify { get; set; } = true;

    /// <summary>Skip intros and outros: seconds skipped at the start and the end, and songs shorter than this many seconds left alone.</summary>
    public int SkipIntroSeconds { get; set; } = 10;

    public int SkipOutroSeconds { get; set; }

    public int SkipShortestSeconds { get; set; } = 90;

    /// <summary>Volume per device: each output's volume by its name, a volume for new ones (0 keeps the volume), and a word when it changes.</summary>
    public Dictionary<string, int> DeviceVolumes { get; set; } = [];

    public int DeviceVolumeNew { get; set; }

    public bool DeviceVolumeMessage { get; set; } = true;

    /// <summary>Resonate's DJ: its voice introduces each set, and how many songs a set has.</summary>
    public bool DjVoice { get; set; } = true;

    public int DjSetSize { get; set; } = 6;

    // ---- End of built-in plugins ----

    [JsonIgnore]
    public bool UsesClassicPlayer => PlayerStyle == "classic";

    [JsonIgnore]
    public Resonate.Themes.Skins.VisualiserMode ParsedClassicVisualiser =>
        Enum.TryParse<Resonate.Themes.Skins.VisualiserMode>(ClassicVisualiser, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : Resonate.Themes.Skins.VisualiserMode.Spectrum;

    [JsonIgnore]
    public Resonate.Spotify.Library.PlaylistSortMode ParsedPlaylistSort =>
        Enum.TryParse<Resonate.Spotify.Library.PlaylistSortMode>(PlaylistSort, ignoreCase: true, out var mode)
            ? mode
            : Resonate.Spotify.Library.PlaylistSortMode.Spotify;
}

/// <summary>
/// The window's size and place on the screen (in pixels, as Windows counts
/// them) when it was last not maximised, and whether it was maximised.
/// </summary>
public sealed record WindowPlacement
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool Maximized { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON in the user's app data
/// folder. Saving copies the settings at once and writes them in the
/// background (the newest copy wins), so a slow disk never holds a frame.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private byte[]? _pending;
    private bool _writing;
    private Task _writer = Task.CompletedTask;

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                using var stream = File.OpenRead(_path);
                return JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file falls back to the defaults.
        }

        return new AppSettings();
    }

    /// <summary>Call on the thread that changes the settings; the file is written in the background.</summary>
    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, AppJsonContext.Default.AppSettings);
        lock (_gate)
        {
            _pending = json;
            if (!_writing)
            {
                _writing = true;
                _writer = Task.Run(WritePending);
            }
        }
    }

    /// <summary>Waits for the last save to reach the disk (before the app closes).</summary>
    public void Flush()
    {
        Task writer;
        lock (_gate)
        {
            writer = _writer;
        }

        writer.Wait(TimeSpan.FromSeconds(5));
    }

    private void WritePending()
    {
        while (true)
        {
            byte[] json;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _writing = false;
                    return;
                }

                json = _pending;
                _pending = null;
            }

            Write(json);
        }
    }

    private void Write(byte[] json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the settings are kept for this session.
        }
    }
}

public static class AppPaths
{
    /// <summary>
    /// "--data": everything below goes in this one folder instead, so a local
    /// build run beside the installed copy shares nothing with it. Read once,
    /// after <see cref="StartupOptions.Parse"/>.
    /// </summary>
    private static readonly string? DataFolder = StartupOptions.Current.DataFolder;

    private static readonly string Roaming =
        DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Resonate");

    private static readonly string Local =
        DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonate");

    /// <summary>Settings that should survive reinstalling (roaming app data).</summary>
    public static string SettingsFile { get; } = Path.Combine(Roaming, "settings.json");

    /// <summary>
    /// Caches. Velopack installs to %LocalAppData%\Resonate and only replaces
    /// its "current" folder on updates, so this survives updates and is
    /// removed on uninstall.
    /// </summary>
    public static string CacheFolder { get; } = Path.Combine(Local, "data");

    /// <summary>
    /// Plugins that are on, and the helper that runs them. Next to the cache,
    /// so it survives updates and goes on uninstall; emptied when the last
    /// plugin is turned off.
    /// </summary>
    public static string PluginsFolder { get; } = Path.Combine(Local, "plugins");

    /// <summary>Which plugins are on, their settings and what they keep (next to the settings).</summary>
    public static string PluginsFile { get; } = Path.Combine(Roaming, "plugins.json");

    /// <summary>
    /// WebView2's own folder for Resonate's own player ("Spotify Web API
    /// only"). It runs InPrivate, so no cookies, cache or history stay here.
    /// </summary>
    public static string WebPlayerFolder { get; } = Path.Combine(Local, "webplayer");

    /// <summary>Classic player skins the user added (copies), next to <see cref="CacheFolder"/>.</summary>
    public static string SkinsFolder { get; } = Path.Combine(Local, "skins");

    /// <summary>Where the Spotify sign-in is kept in the Credential Manager; its own entry for "--data".</summary>
    public static string CredentialTarget { get; } = DataFolder is null ? "Resonate/Spotify" : "Resonate/Spotify/" + DataFolder;
}
