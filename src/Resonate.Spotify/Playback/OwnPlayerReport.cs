using System.Text.Json;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// What Resonate's own player says it plays (Spotify's Web Playback SDK
/// state, sent by its page), read as the Web API's <see cref="PlaybackState"/>
/// so the player can show it at once, without asking Spotify's servers (the
/// owner asked for less delay with "Spotify Web API only", 9 October 2026).
/// The device is the own player's; the SDK says nothing about its volume, so
/// that stays as it was.
/// </summary>
public static class OwnPlayerReport
{
    /// <summary>
    /// The state in <paramref name="json"/> (a "state" message with a "now"
    /// object), or null when it carries none: then the player is not playing
    /// anything, or plays elsewhere, and Spotify is asked instead.
    /// </summary>
    public static PlaybackState? Parse(string? json, string deviceId, string deviceName)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("now", out var now) || now.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var playback = new PlaybackState
            {
                Device = new Device { Id = deviceId, Name = deviceName, Type = "Computer", IsActive = true },
                IsPlaying = !Bool(now, "paused", true),
                ProgressMs = Int(now, "position"),
                ShuffleState = Bool(now, "shuffle", false),
                RepeatState = Int(now, "repeat") switch
                {
                    1 => "context",
                    2 => "track",
                    _ => "off",
                },
                Context = Text(now, "context") is { } context ? new PlaybackContext { Uri = context } : null,
                Item = now.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object ? Track(track) : null,
            };

            if (now.TryGetProperty("disallows", out var disallows) && disallows.ValueKind == JsonValueKind.Array)
            {
                playback.Actions = new PlaybackActions
                {
                    Disallows = disallows.EnumerateArray()
                        .Where(d => d.ValueKind == JsonValueKind.String)
                        .Select(d => d.GetString()!)
                        .Distinct(StringComparer.Ordinal)
                        .ToDictionary(d => d, _ => true, StringComparer.Ordinal),
                };
            }

            return playback;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlayableItem? Track(JsonElement track)
    {
        if (Text(track, "uri") is not { } uri)
        {
            return null;
        }

        var item = new PlayableItem
        {
            Id = Text(track, "id"),
            Uri = uri,
            Name = Text(track, "name") ?? string.Empty,
            Type = "track",
            DurationMs = Int(track, "duration") ?? 0,
            Artists = track.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
                ? artists.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.Object)
                    .Select(a => new SimplifiedArtist { Name = Text(a, "name") ?? string.Empty, Uri = Text(a, "uri") })
                    .ToList()
                : [],
        };

        if (track.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            item.Album = new SimplifiedAlbum
            {
                Name = Text(album, "name") ?? string.Empty,
                Uri = Text(album, "uri"),
                Images = album.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array
                    ? images.EnumerateArray()
                        .Where(i => i.ValueKind == JsonValueKind.Object && Text(i, "url") is not null)
                        .Select(i => new SpotifyImage { Url = Text(i, "url")!, Width = Int(i, "width"), Height = Int(i, "height") })
                        .ToList()
                    : null,
            };
        }

        return item;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? (int)Math.Clamp(Math.Round(number), int.MinValue, int.MaxValue)
            : null;

    private static bool Bool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
}
