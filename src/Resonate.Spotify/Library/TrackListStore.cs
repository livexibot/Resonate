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

    /// <summary>
    /// 1 when every song was read since songs carry their release year
    /// (<see cref="TrackInfo.ReleaseYear"/>); 0 for lists stored before.
    /// </summary>
    public int Format { get; set; }

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
/// Failures are ignored: the store is only a shortcut. The lists used last
/// also stay in memory (Liked Songs can be several megabytes of JSON, read
/// on every visit and every heart click otherwise). Lists handed out are
/// shared, so callers only read them.
/// </summary>
public sealed class TrackListStore
{
    /// <summary>How many lists stay in memory, most recently used first.</summary>
    private const int KeptInMemory = 2;

    private readonly string? _folder;
    private readonly Lock _gate = new();
    private readonly List<CachedTrackList> _recent = [];

    /// <param name="folder">Where the files go; null keeps nothing (demo mode, tests).</param>
    public TrackListStore(string? folder) => _folder = folder;

    public CachedTrackList? Load(string key) => Load(key, keepInMemory: true);

    /// <summary>
    /// Like <see cref="Load"/>, but a list read from disk does not take the
    /// place of the ones kept in memory (for a quick look ahead, such as
    /// fetching a playlist's covers while the pointer rests on it).
    /// </summary>
    public CachedTrackList? Peek(string key) => Load(key, keepInMemory: false);

    private CachedTrackList? Load(string key, bool keepInMemory)
    {
        if (PathFor(key) is not { } path)
        {
            return null;
        }

        lock (_gate)
        {
            if (_recent.Find(l => l.Key == key) is { } remembered)
            {
                if (keepInMemory)
                {
                    Remember(remembered);
                }

                return remembered;
            }
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            CachedTrackList? list;
            using (var stream = File.OpenRead(path))
            {
                list = JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.CachedTrackList);
            }

            if (list?.Key != key)
            {
                return null;
            }

            if (keepInMemory)
            {
                lock (_gate)
                {
                    Remember(list);
                }
            }

            return list;
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

        lock (_gate)
        {
            Remember(list);
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

        lock (_gate)
        {
            _recent.Clear();
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

    /// <summary>Puts the list first in memory, replacing an older copy with the same key. Call under the lock.</summary>
    private void Remember(CachedTrackList list)
    {
        _recent.RemoveAll(l => l.Key == list.Key);
        _recent.Insert(0, list);
        if (_recent.Count > KeptInMemory)
        {
            _recent.RemoveRange(KeptInMemory, _recent.Count - KeptInMemory);
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
