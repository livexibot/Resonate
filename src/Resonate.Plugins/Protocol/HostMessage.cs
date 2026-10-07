using System.Text.Json.Nodes;

namespace Resonate.Plugins.Protocol;

/// <summary>
/// One message between Resonate and the plugin helper, sent as a line of
/// JSON over the helper's standard input and output. <see cref="Type"/> says
/// which of the other fields matter (see <see cref="MessageTypes"/>).
/// </summary>
public sealed class HostMessage
{
    public string Type { get; set; } = string.Empty;

    /// <summary>The plugin the message is for or from.</summary>
    public string? Plugin { get; set; }

    /// <summary><see cref="MessageTypes.Load"/>: the plugin's installed folder.</summary>
    public string? Folder { get; set; }

    /// <summary><see cref="MessageTypes.Load"/>: the script to run, relative to <see cref="Folder"/>.</summary>
    public string? Main { get; set; }

    /// <summary><see cref="MessageTypes.Load"/>: what the plugin may do.</summary>
    public List<string>? Permissions { get; set; }

    /// <summary><see cref="MessageTypes.Load"/> and <see cref="MessageTypes.Settings"/>: every setting's value.</summary>
    public JsonObject? Settings { get; set; }

    /// <summary><see cref="MessageTypes.Load"/> and <see cref="MessageTypes.Storage"/>: what the plugin keeps between runs.</summary>
    public JsonObject? Storage { get; set; }

    /// <summary><see cref="MessageTypes.State"/>: what is playing, or nothing.</summary>
    public NowPlaying? State { get; set; }

    /// <summary><see cref="MessageTypes.Player"/>: see <see cref="PlayerActions"/>.</summary>
    public string? Action { get; set; }

    /// <summary><see cref="MessageTypes.Player"/>: seconds for a seek, 0 to 1 for the volume.</summary>
    public double? Value { get; set; }

    /// <summary><see cref="MessageTypes.Setting"/>: which setting the plugin changed.</summary>
    public string? Key { get; set; }

    /// <summary><see cref="MessageTypes.Setting"/>: its new value.</summary>
    public JsonNode? Data { get; set; }

    /// <summary>Status, notification, log or error text.</summary>
    public string? Text { get; set; }

    /// <summary><see cref="MessageTypes.Log"/>: "info" or "error".</summary>
    public string? Level { get; set; }

    /// <summary><see cref="MessageTypes.Commands"/>: everything the plugin offers in the player bar.</summary>
    public List<PluginCommand>? Commands { get; set; }

    /// <summary><see cref="MessageTypes.Invoke"/>: the command the user picked.</summary>
    public string? Command { get; set; }

    /// <summary><see cref="MessageTypes.Ready"/>: the helper's version.</summary>
    public string? Version { get; set; }
}

public static class MessageTypes
{
    // Resonate to the helper.

    /// <summary>Start a plugin.</summary>
    public const string Load = "load";

    /// <summary>Stop a plugin.</summary>
    public const string Unload = "unload";

    /// <summary>What is playing changed.</summary>
    public const string State = "state";

    /// <summary>A plugin's settings changed.</summary>
    public const string Settings = "settings";

    /// <summary>The user picked one of a plugin's commands.</summary>
    public const string Invoke = "invoke";

    /// <summary>Stop everything and exit.</summary>
    public const string Shutdown = "shutdown";

    // The helper to Resonate.

    /// <summary>The helper started and reads messages.</summary>
    public const string Ready = "ready";

    /// <summary>A plugin started.</summary>
    public const string Loaded = "loaded";

    /// <summary>A plugin could not start, or stopped after errors.</summary>
    public const string Failed = "failed";

    /// <summary>A plugin asks the player to do something.</summary>
    public const string Player = "player";

    /// <summary>A plugin's commands changed.</summary>
    public const string Commands = "commands";

    /// <summary>A plugin's status line changed (empty text clears it).</summary>
    public const string Status = "status";

    /// <summary>A plugin has something to tell the user now.</summary>
    public const string Notify = "notify";

    /// <summary>A plugin changed one of its own settings.</summary>
    public const string Setting = "setting";

    /// <summary>A plugin changed what it keeps between runs.</summary>
    public const string Storage = "storage";

    /// <summary>A line from a plugin's console, or an error it ran into.</summary>
    public const string Log = "log";
}

public static class PlayerActions
{
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Seek = "seek";
    public const string Volume = "volume";

    /// <summary>The permission an action needs, or null for an unknown action.</summary>
    public static string? RequiredPermission(string? action) => action switch
    {
        Play or Pause or Next or Previous or Seek => PluginPermissions.PlayerControl,
        Volume => PluginPermissions.PlayerVolume,
        _ => null,
    };
}

/// <summary>A command a plugin offers, shown in the player bar's plugin menu.</summary>
public sealed class PluginCommand
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}

/// <summary>What is playing, as plugins see it. Times are in seconds.</summary>
public sealed record NowPlaying
{
    public string? Title { get; init; }

    /// <summary>The artists' names, separated by ", ".</summary>
    public string? Artists { get; init; }

    public string? Album { get; init; }

    /// <summary>Spotify's URI for the song, such as "spotify:track:…", when known.</summary>
    public string? Uri { get; init; }

    /// <summary>The playlist or album it plays from, when known.</summary>
    public string? ContextUri { get; init; }

    public bool IsPlaying { get; init; }

    /// <summary>The position when this was sent.</summary>
    public double Position { get; init; }

    public double Duration { get; init; }

    /// <summary>From 0 to 1.</summary>
    public double Volume { get; init; } = 1;

    public bool CanSeek { get; init; }
}
