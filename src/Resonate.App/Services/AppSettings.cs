using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonate.App.Services;

/// <summary>Preferences kept between launches. Nothing secret lives here.</summary>
public sealed class AppSettings
{
    /// <summary>The user's own Spotify developer app. Not a secret with PKCE sign-in.</summary>
    public string? ClientId { get; set; }

    public string ThemeId { get; set; } = "midnight";

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

    [JsonIgnore]
    public Resonate.Spotify.Library.PlaylistSortMode ParsedPlaylistSort =>
        Enum.TryParse<Resonate.Spotify.Library.PlaylistSortMode>(PlaylistSort, ignoreCase: true, out var mode)
            ? mode
            : Resonate.Spotify.Library.PlaylistSortMode.Spotify;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON in the user's app data folder.</summary>
public sealed class SettingsStore
{
    private readonly string _path;

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

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, settings, AppJsonContext.Default.AppSettings);
            }

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
}
