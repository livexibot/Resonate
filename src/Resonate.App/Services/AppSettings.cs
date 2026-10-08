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

    /// <summary>"local" (Windows' media controls first) or "webapi" (the Spotify Web API only).</summary>
    public string ControlChannel { get; set; } = "local";

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

    /// <summary>The time range of "Your top on Spotify" on Home.</summary>
    public Resonate.Spotify.WebApi.TopRange HomeTopRange { get; set; } = Resonate.Spotify.WebApi.TopRange.ShortTerm;

    /// <summary>The folders Local Files looks in; null for the user's Music and Downloads folders.</summary>
    public List<string>? LocalFolders { get; set; }

    /// <summary>Show Local Files in the sidebar.</summary>
    public bool ShowLocalFiles { get; set; } = true;

    /// <summary>The local files player's volume, from 0 to 1 (the Spotify app keeps its own).</summary>
    public double LocalVolume { get; set; } = 1;

    [JsonIgnore]
    public Resonate.Spotify.Library.PlaylistSortMode ParsedPlaylistSort =>
        Enum.TryParse<Resonate.Spotify.Library.PlaylistSortMode>(PlaylistSort, ignoreCase: true, out var mode)
            ? mode
            : Resonate.Spotify.Library.PlaylistSortMode.Spotify;
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
}
