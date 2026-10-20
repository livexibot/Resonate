using System.Text.Json;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Playback;

/// <summary>
/// The list around the song that played last (see <see cref="LastPlayed"/>),
/// kept in the cache folder so that after a restart Play, Next, Previous and
/// Up next go on through it, with no request to Spotify for the list (the
/// owner found only the kept song playing after a restart, 18 October 2026).
/// A part of the list: a few songs before the one that played and a few
/// hundred after it, not every song of a long list.
/// </summary>
public sealed class KeptList
{
    /// <summary>The songs in the order they play.</summary>
    public List<TrackInfo> Songs { get; set; } = [];

    /// <summary>Where in <see cref="Songs"/> the song that played last is.</summary>
    public int Index { get; set; }

    /// <summary>The part of <see cref="Songs"/> Spotify had last (from, and up to but not including, the end); unused inside a context.</summary>
    public int WindowStart { get; set; }

    public int WindowEnd { get; set; }

    /// <summary>Resonate's shuffle was on.</summary>
    public bool Shuffle { get; set; }

    /// <summary>Spotify played <see cref="ContextUri"/> itself, in the list's own order.</summary>
    public bool InContext { get; set; }

    public string? ContextUri { get; set; }

    public string? SourceName { get; set; }

    /// <summary><see cref="Songs"/> is the whole list (it was short enough), not a part of it.</summary>
    public bool Complete { get; set; }

    /// <summary>Up next had edited the order.</summary>
    public bool Edited { get; set; }

    /// <summary>Spotify's own shuffle, as last known (Resonate switches it off for its lists); null when not known.</summary>
    public bool? SpotifyShuffle { get; set; }

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> hold the same (so the file need not be written again).</summary>
    public static bool Same(KeptList? a, KeptList? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        if (a.Index != b.Index || a.WindowStart != b.WindowStart || a.WindowEnd != b.WindowEnd
            || a.Shuffle != b.Shuffle || a.InContext != b.InContext || a.ContextUri != b.ContextUri
            || a.SourceName != b.SourceName || a.Complete != b.Complete || a.Edited != b.Edited
            || a.SpotifyShuffle != b.SpotifyShuffle || a.Songs.Count != b.Songs.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Songs.Count; i++)
        {
            if (!ReferenceEquals(a.Songs[i], b.Songs[i]) && a.Songs[i].Uri != b.Songs[i].Uri)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The file that holds the <see cref="KeptList"/> (song names and picture
/// links only, never tokens). Written off the interface thread whenever the
/// list or the song in it changes; the newest write wins. Failures are
/// ignored: without the file, the kept song plays by itself, as before.
/// </summary>
public sealed class KeptListFile
{
    private readonly string? _path;
    private readonly Lock _gate = new();
    private long _asked;
    private long _written;

    /// <param name="path">Where the file goes; null keeps nothing (demo mode, tests).</param>
    public KeptListFile(string? path) => _path = path;

    public KeptList? Load()
    {
        if (_path is null)
        {
            return null;
        }

        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SpotifyJsonContext.Default.KeptList);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="list"/> (null deletes the file) on a thread of its own.</summary>
    public void SaveSoon(KeptList? list)
    {
        var version = Interlocked.Increment(ref _asked);
        _ = Task.Run(() => Write(list, version));
    }

    /// <summary>Writes <paramref name="list"/> (null deletes the file) before returning, as Resonate closes.</summary>
    public void SaveNow(KeptList? list) => Write(list, Interlocked.Increment(ref _asked));

    private void Write(KeptList? list, long version)
    {
        if (_path is null)
        {
            return;
        }

        lock (_gate)
        {
            // A newer list was written already (writes can start out of order).
            if (version <= _written)
            {
                return;
            }

            _written = version;
            try
            {
                if (list is null)
                {
                    File.Delete(_path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                using (var stream = File.Create(temporary))
                {
                    JsonSerializer.Serialize(stream, list, SpotifyJsonContext.Default.KeptList);
                }

                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}
