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

    [JsonIgnore]
    public Resonate.Spotify.Playback.ControlChannel ParsedControlChannel =>
        ControlChannel == "webapi" ? Resonate.Spotify.Playback.ControlChannel.WebApi : Resonate.Spotify.Playback.ControlChannel.Local;

    /// <summary>The last measured time from starting the process to the first frame.</summary>
    public double? LastStartupMilliseconds { get; set; }
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
