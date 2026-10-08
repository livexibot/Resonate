using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Resonate.Spotify.LocalFiles;

/// <summary>Makes a cover small (Windows' image decoder in the app, a stand-in in tests).</summary>
public interface ICoverShrinker
{
    /// <summary>
    /// <paramref name="cover"/> as a JPEG at most <paramref name="size"/>
    /// pixels along its longer side, or null when it can not be read as a
    /// picture. Never throws.
    /// </summary>
    Task<byte[]?> ShrinkAsync(byte[] cover, int size);
}

/// <summary>
/// Small covers for the rows of Local Files: read from each music file (or
/// the picture next to it) the first time a row shows, shrunk to a JPEG and
/// kept in a folder, so scrolling never decodes a full-size cover twice.
/// A cover made from the picture next to a file is kept under that
/// picture's name, size and time, so a new picture shows. A file that was
/// read and has no cover leaves a marker, so it is not looked at again until
/// it or its folder changes (as when a cover.jpg is put next to it). A
/// failure that may pass (a locked file, or a picture Windows could not read
/// this time) leaves no marker: it is tried again the next time the row shows.
/// </summary>
public sealed class LocalCoverCache : IDisposable
{
    /// <summary>Pixels along the longer side; rows show covers at 40 px, so this is sharp up to 300 % scaling.</summary>
    public const int ThumbnailSize = 128;

    /// <summary>
    /// Raised when a version of Resonate made covers wrongly: older versions'
    /// covers and markers are then ignored and made again. (Version 2: the
    /// first marked songs as having no cover after any failure, and could
    /// not shrink covers in the installed app, so every song was marked.)
    /// </summary>
    internal const int Version = 2;

    private readonly string _folder;
    private readonly ICoverShrinker _shrinker;
    private readonly SemaphoreSlim _working = new(2, 2);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inFlight = new(StringComparer.Ordinal);

    // The picture next to the songs of a folder, looked up once per change of the folder.
    private readonly ConcurrentDictionary<string, (string Stamp, string? Picture)> _folderPictures = new(StringComparer.OrdinalIgnoreCase);

    // A folder's picture is shrunk once for all of its songs (each gets its own copy on disk).
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _pictures = new(StringComparer.Ordinal);

    public LocalCoverCache(string folder, ICoverShrinker shrinker)
    {
        _folder = folder;
        _shrinker = shrinker;
    }

    /// <summary>The thumbnail of <paramref name="file"/>'s cover, or null when it has none. Never throws; runs off the calling thread.</summary>
    public Task<byte[]?> GetAsync(LocalFile file)
    {
        // Rows of the same song asking at once share one read.
        var key = KeyFor(file);
        var task = _inFlight.GetOrAdd(key, k => Task.Run(async () =>
        {
            try
            {
                return await LoadAsync(file, k).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            finally
            {
                _inFlight.TryRemove(k, out _);
            }
        }));

        // It may have finished before it was added; finished ones are not kept.
        if (task.IsCompleted)
        {
            _inFlight.TryRemove(new KeyValuePair<string, Task<byte[]?>>(key, task));
        }

        return task;
    }

    public void Dispose() => _working.Dispose();

    /// <summary>A name that changes when the file does.</summary>
    internal static string KeyFor(LocalFile file) =>
        Hash($"v{Version.ToString(CultureInfo.InvariantCulture)}|{file.Path.ToUpperInvariant()}|{file.Size}|{file.LastWriteTicks}");

    private async Task<byte[]?> LoadAsync(LocalFile file, string key)
    {
        var thumbnail = Path.Combine(_folder, key + ".jpg");
        if (ReadKept(thumbnail) is { } kept)
        {
            return kept;
        }

        // A file without a cover is looked at again once its folder changes,
        // as when a cover.jpg is put next to it. The folder's time is taken
        // before looking, so a picture put there meanwhile is not missed.
        var none = Path.Combine(_folder, key + ".none");
        var stamp = FolderStamp(file.Path);
        if (ReadKept(none) is { } marker && Encoding.UTF8.GetString(marker) == stamp)
        {
            return null;
        }

        // A cover from the picture next to the file is kept under that
        // picture's name, size and time, so a new picture is read again.
        var picture = FolderPicture(file.Path, stamp);
        var fromPicture = picture is null ? null : Path.Combine(_folder, Hash($"{key}|{picture.Path.ToUpperInvariant()}|{picture.Size}|{picture.Ticks}") + ".jpg");
        if (fromPicture is not null && ReadKept(fromPicture) is { } keptPicture)
        {
            return keptPicture;
        }

        await _working.WaitAsync().ConfigureAwait(false);
        try
        {
            // The cover inside the file first, as the player bar does (LocalCovers.Read).
            if (!LocalCovers.TryReadEmbedded(file, out var embedded))
            {
                // Locked, or not downloaded from the cloud: tried again next time.
                return null;
            }

            var certain = true;
            if (embedded is not null)
            {
                if (await _shrinker.ShrinkAsync(embedded, ThumbnailSize).ConfigureAwait(false) is { } shrunk)
                {
                    Keep(thumbnail, shrunk);
                    return shrunk;
                }

                // A picture Windows could not read this time is tried again; bytes that are no picture are not.
                certain = !LocalCovers.LooksLikeImage(embedded);
            }

            if (picture is not null && fromPicture is not null)
            {
                if (await ShrinkPictureAsync(picture).ConfigureAwait(false) is { } shrunk)
                {
                    Keep(fromPicture, shrunk);
                    return shrunk;
                }

                certain = false;
            }

            if (certain)
            {
                Keep(none, Encoding.UTF8.GetBytes(stamp));
            }

            return null;
        }
        finally
        {
            _working.Release();
        }
    }

    /// <summary>The picture next to <paramref name="audioPath"/>, looked for once per change of its folder.</summary>
    private FolderPictureFile? FolderPicture(string audioPath, string stamp)
    {
        if (Path.GetDirectoryName(audioPath) is not { Length: > 0 } folder)
        {
            return null;
        }

        if (!_folderPictures.TryGetValue(folder, out var known) || known.Stamp != stamp)
        {
            known = (stamp, LocalCovers.FindFolderImage(audioPath));
            _folderPictures[folder] = known;
        }

        if (known.Picture is not { } path)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FolderPictureFile(path, info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A folder's picture made small, shared by all of its songs; a failure is forgotten so it is tried again.</summary>
    private Task<byte[]?> ShrinkPictureAsync(FolderPictureFile picture)
    {
        var id = $"{picture.Path.ToUpperInvariant()}|{picture.Size}|{picture.Ticks}";
        var task = _pictures.GetOrAdd(id, _ => LocalCovers.ReadPicture(picture.Path) is { } bytes
            ? _shrinker.ShrinkAsync(bytes, ThumbnailSize)
            : Task.FromResult<byte[]?>(null));
        _ = task.ContinueWith(
            t =>
            {
                if (t.IsFaulted || t.Result is null)
                {
                    _pictures.TryRemove(new KeyValuePair<string, Task<byte[]?>>(id, task));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    /// <summary>Writes a file to the cache folder whole or not at all; a failure only means it is made again next time.</summary>
    private void Keep(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var temporary = path + "." + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Made again next time.
        }
    }

    /// <summary>A file kept in the cache folder, or null when there is none (or it can not be read, so it is made again).</summary>
    private static byte[]? ReadKept(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>When the file's folder last changed (a file put in it, renamed or removed), as text for the "no cover" marker.</summary>
    private static string FolderStamp(string audioPath)
    {
        try
        {
            return Path.GetDirectoryName(audioPath) is { Length: > 0 } folder
                ? Directory.GetLastWriteTimeUtc(folder).Ticks.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));

    private sealed record FolderPictureFile(string Path, long Size, long Ticks);
}
