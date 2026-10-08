using System.Text.RegularExpressions;

namespace Resonate.Spotify.Playback;

/// <summary>What one of the Spotify app's processes does.</summary>
public enum SpotifyProcessKind
{
    /// <summary>The main process: Spotify's core, which streams and plays. Never slowed down.</summary>
    Main,

    /// <summary>Draws Spotify's own window (a Chromium renderer). Idle while the window is hidden.</summary>
    Interface,

    /// <summary>Spotify's graphics process. Idle while the window is hidden.</summary>
    Graphics,

    /// <summary>The audio service. Never slowed down.</summary>
    Audio,

    /// <summary>Anything else (network, storage, crash reporting). Left alone.</summary>
    Other,
}

/// <summary>
/// The Spotify desktop app is built on Chromium, so it runs as several
/// processes that say what they are in their command line (<c>--type=...</c>).
/// </summary>
public static partial class SpotifyProcesses
{
    /// <summary>The command line of a Spotify process tells what it does.</summary>
    public static SpotifyProcessKind Classify(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return SpotifyProcessKind.Other;
        }

        var type = Argument(commandLine, "type");
        return type switch
        {
            null => SpotifyProcessKind.Main,
            "renderer" => SpotifyProcessKind.Interface,
            "gpu-process" => SpotifyProcessKind.Graphics,
            "utility" when Argument(commandLine, "utility-sub-type")?.StartsWith("audio", StringComparison.OrdinalIgnoreCase) == true
                => SpotifyProcessKind.Audio,
            _ => SpotifyProcessKind.Other,
        };
    }

    /// <summary>
    /// Only the processes that draw Spotify's (hidden) window may be put in
    /// Windows' efficiency mode; the core and the audio keep full speed, so
    /// playback is never affected.
    /// </summary>
    public static bool MaySaveResources(SpotifyProcessKind kind) =>
        kind is SpotifyProcessKind.Interface or SpotifyProcessKind.Graphics;

    private static string? Argument(string commandLine, string name)
    {
        foreach (Match match in ArgumentPattern().Matches(commandLine))
        {
            if (string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
            {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("""(?:^|\s)--(?<name>[a-z-]+)=(?<value>"[^"]*"|[^\s"]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentPattern();
}

/// <summary>Keeps the Spotify app's window out of the way, and light while it is.</summary>
public interface ISpotifyAppWindow
{
    /// <summary>
    /// Whether Resonate looks after Spotify's window and processes at all.
    /// Off with "Spotify Web API only": then Resonate leaves the Spotify app
    /// alone, and gives back anything it had hidden or slowed down.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>Hide Spotify's window (and taskbar button) unless the user is looking at it.</summary>
    bool KeepHidden { get; set; }

    /// <summary>While hidden, put Spotify's window-drawing processes in Windows' efficiency mode.</summary>
    bool SaveResources { get; set; }

    /// <summary>
    /// Brings Spotify's window to the front, for example to sign in or change
    /// its settings. False when Spotify could not be opened (not installed).
    /// </summary>
    bool ShowSpotify();
}
