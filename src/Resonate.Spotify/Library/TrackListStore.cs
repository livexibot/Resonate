using System.Text.Json;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>A whole song list as last loaded: Liked Songs, a playlist at one version, or an album.</summary>
public sealed class CachedTrackList
{
    public string Key { get; set; } = string.Empty;

    /// <summary>The playlist's snapshot ID when it was loaded; a changed playlist has a new one.</summary>
    public string? Version { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public List<TrackInfo> Tracks { get; set; } = [];
}

/// <summary>A whole song list, or a note that Spotify would not share its songs.</summary>
public sealed record FullTrackList(IReadOnlyList<TrackInfo> Tracks, bool ItemsHidden)
{
    public static readonly FullTrackList Hidden = new([], true);
}

/// <summary>
/// Keeps whole song lists on disk (one small JSON file each), so shuffling,
/// sorting and the daily mixes do not have to ask Spotify for thousands of
/// songs again. Holds song names and picture links only, never tokens.
/// Failures are ignored: the store is only a shortcut.
/// </summary>
public sealed class TrackListStore
{
    private readonly string? _folder;

    /// <param name="folder">Where the files go; null keeps nothing (demo mode, tests).</param>
    public TrackListStore(string? folder) => _folder = folder;

    public CachedTrackList? Load(string key)
    {
        if (PathFor(key) is not { } path)
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var list = JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.CachedTrackList);
            return list?.Key == key ? list : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(CachedTrackList list)
    {
        if (PathFor(list.Key) is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, list, SpotifyJsonContext.Default.CachedTrackList);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    public void Clear()
    {
        if (_folder is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private string? PathFor(string key)
    {
        if (_folder is null)
        {
            return null;
        }

        // Keys are Resonate's own ("liked", "playlist-<id>"); keep only safe characters.
        var safe = new string(key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return Path.Combine(_folder, safe + ".json");
    }
}
