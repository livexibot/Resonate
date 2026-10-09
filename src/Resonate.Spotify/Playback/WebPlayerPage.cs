using System.Text;
using System.Text.Json;

namespace Resonate.Spotify.Playback;

/// <summary>
/// The page that runs Spotify's Web Playback SDK for <see cref="OwnPlayer"/>
/// (on Windows a hidden WebView2, see the app's WebPlayerPage). The two
/// exchange one JSON message at a time (<see cref="WebPlayerMessage"/> and
/// <see cref="WebPlayerCommands"/>).
/// </summary>
public interface IWebPlayerPage : IAsyncDisposable
{
    /// <summary>Raised with each message the page sends (JSON text), on any thread.</summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>Raised on any thread when the page stopped working (its process ended), with what happened.</summary>
    event EventHandler<string>? Failed;

    /// <summary>
    /// Opens the page and finishes once it listens. Throws
    /// <see cref="WebPlayerUnavailableException"/> when this computer cannot
    /// run it at all (no WebView2 runtime).
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>Sends a message (JSON text) to the page; any thread.</summary>
    void Post(string json);
}

/// <summary>This computer cannot run the player page at all (no WebView2 runtime).</summary>
public sealed class WebPlayerUnavailableException : Exception
{
    public WebPlayerUnavailableException()
    {
    }

    public WebPlayerUnavailableException(string message)
        : base(message)
    {
    }

    public WebPlayerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A message from the player page.</summary>
/// <param name="Type">
/// "loaded" (the page listens), "token" (Spotify's player asks for an access
/// token), "ready" and "notReady" (with <paramref name="DeviceId"/>), "error"
/// (with <paramref name="Kind"/>: initialization, authentication, account,
/// playback, script), "state" (what plays changed) or "check" (the CI check's
/// result, with <paramref name="Widevine"/>, <paramref name="Sdk"/> and
/// <paramref name="Autoplay"/>, whether sound may start without a click).
/// </param>
public sealed record WebPlayerMessage(
    string Type,
    string? DeviceId = null,
    string? Kind = null,
    string? Text = null,
    string? Widevine = null,
    string? Sdk = null,
    string? Autoplay = null)
{
    /// <summary>The message in <paramref name="json"/>, or null when it is not one.</summary>
    public static WebPlayerMessage? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Read(root, "type") is not { } type)
            {
                return null;
            }

            return new WebPlayerMessage(
                type,
                Read(root, "deviceId"),
                Read(root, "kind"),
                Read(root, "message"),
                Read(root, "widevine"),
                Read(root, "sdk"),
                Read(root, "autoplay"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>The messages <see cref="OwnPlayer"/> sends to the player page.</summary>
public static class WebPlayerCommands
{
    /// <summary>Starts Spotify's player as a device called <paramref name="name"/>.</summary>
    public static string Start(string name, double volume) => Write(w =>
    {
        w.WriteString("type", "start");
        w.WriteString("name", name);
        w.WriteNumber("volume", Math.Clamp(volume, 0, 1));
    });

    /// <summary>The access token Spotify's player asked for. Never log this message.</summary>
    public static string Token(string accessToken) => Write(w =>
    {
        w.WriteString("type", "token");
        w.WriteString("token", accessToken);
    });

    /// <summary>Connects Spotify's player again (after a fresh token).</summary>
    public static string Reconnect() => Write(w => w.WriteString("type", "reconnect"));

    /// <summary>Disconnects Spotify's player, so the device leaves Spotify's list.</summary>
    public static string Stop() => Write(w => w.WriteString("type", "stop"));

    /// <summary>CI's check: protected audio, and Spotify's player with a made-up token.</summary>
    public static string Check() => Write(w => w.WriteString("type", "check"));

    /// <summary>
    /// Tells Spotify's player on the page itself to resume, pause, skip
    /// ("next", "previous"), seek (<paramref name="value"/> in milliseconds) or
    /// set its volume (0 to 1), without a trip to Spotify's servers.
    /// </summary>
    public static string Control(string action, double value = 0) => Write(w =>
    {
        w.WriteString("type", "control");
        w.WriteString("action", action);
        w.WriteNumber("value", value);
    });

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
