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
    public bool SpinningCover { get; set; }

    /// <summary>Looks with the song cover backdrop show the cover itself, blurred (off: only its colours).</summary>
    public bool BlurredCoverBackground { get; set; } = true;

    /// <summary>The library sidebar runs the window's full height, beside the player (off unless the user switches it on).</summary>
    public bool SidebarFullHeight { get; set; }

    /// <summary>Links the user hid from the sidebar (Settings, Layout), by page key. Home always shows; Local Files has <see cref="ShowLocalFiles"/>.</summary>
    public List<string> HiddenSidebarLinks { get; set; } = [];

    /// <summary>The mini player button in the title bar (Ctrl+M opens the mini player either way).</summary>
    public bool ShowMiniPlayerButton { get; set; } = true;

    /// <summary>The sections of Settings the user folded away, by their headings (see Controls/SettingsGroup).</summary>
    public List<string> CollapsedSettingsSections { get; set; } = [];

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

    /// <summary>The classic player at Winamp's double size.</summary>
    public bool ClassicDoubleSize { get; set; }

    /// <summary>The classic player in shade mode (just its title strip).</summary>
    public bool ClassicShaded { get; set; }

    /// <summary>The classic player's visualiser: a <see cref="Resonate.Themes.Skins.VisualiserMode"/> name.</summary>
    public string ClassicVisualiser { get; set; } = nameof(Resonate.Themes.Skins.VisualiserMode.Spectrum);

    /// <summary>The classic player's time display counts down.</summary>
    public bool ClassicShowRemaining { get; set; }

    /// <summary>The mini player's size: each skin pixel 1 to 4 times as large (on top of the display's scaling).</summary>
    public int MiniPlayerSize { get; set; } = 1;

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

    /// <summary>The Home stage shows the playing cover blurred behind its clouds (the user's choice, off at first).</summary>
    public bool HomeStageBlurredCover { get; set; }

    /// <summary>The last cover's colours (#RRGGBB), so the stage opens in them before any cover is read.</summary>
    public List<string> HomeStageColours { get; set; } = [];

    /// <summary>The stage shows the visualizer's bars along its bottom (on unless the user turns it off).</summary>
    public bool HomeStageVisualizer { get; set; } = true;

    // Away screen

    /// <summary>Minutes without touching the mouse or keyboard before the away screen shows (2, 5, 10 or 15).</summary>
    public int AwayScreenMinutes { get; set; } = 5;

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

    // Signal path

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
    /// <summary>Settings that should survive reinstalling (roaming app data).</summary>
    public static string SettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Resonate", "settings.json");

    /// <summary>
    /// Caches. Velopack installs to %LocalAppData%\Resonate and only replaces
    /// its "current" folder on updates, so this survives updates and is
    /// removed on uninstall.
    /// </summary>
    public static string CacheFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonate", "data");

    /// <summary>
    /// Plugins that are on, and the helper that runs them. Next to the cache,
    /// so it survives updates and goes on uninstall; emptied when the last
    /// plugin is turned off.
    /// </summary>
    public static string PluginsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonate", "plugins");

    /// <summary>Which plugins are on, their settings and what they keep (next to the settings).</summary>
    public static string PluginsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Resonate", "plugins.json");

    /// <summary>
    /// WebView2's own folder for Resonate's own player ("Spotify Web API
    /// only"). It runs InPrivate, so no cookies, cache or history stay here.
    /// </summary>
    public static string WebPlayerFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonate", "webplayer");

    /// <summary>Classic player skins the user added (copies), next to <see cref="CacheFolder"/>.</summary>
    public static string SkinsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonate", "skins");
}
