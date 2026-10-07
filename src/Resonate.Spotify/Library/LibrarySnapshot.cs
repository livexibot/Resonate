using System.Text.Json;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>
/// The account and its playlists as last seen, so the sidebar can appear the
/// moment Resonate opens, before Spotify answers.
/// </summary>
public sealed class LibrarySnapshot
{
    public SpotifyUser? User { get; set; }

    public List<SimplifiedPlaylist> Playlists { get; set; } = [];

    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>
/// Keeps a <see cref="LibrarySnapshot"/> in a file. It holds names and
/// picture links only, never tokens. Failures are ignored: the cache is only
/// a head start.
/// </summary>
public sealed class LibraryCache
{
    private readonly string _path;

    public LibraryCache(string path) => _path = path;

    public LibrarySnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.LibrarySnapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(LibrarySnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, snapshot, SpotifyJsonContext.Default.LibrarySnapshot);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
