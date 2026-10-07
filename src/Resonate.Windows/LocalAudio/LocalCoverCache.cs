using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Resonate.Spotify.LocalFiles;
using Windows.Graphics.Imaging;

namespace Resonate.Windows.LocalAudio;

/// <summary>
/// Small covers for the rows of Local Files: read from each music file (or
/// the picture next to it) the first time a row shows, shrunk to a JPEG and
/// kept in a folder, so scrolling never decodes a full-size cover twice.
/// A cover made from the picture next to a file is kept under that
/// picture's name, size and time, so a new picture shows. A file without a
/// cover leaves a marker, so it is not looked at again until it or its
/// folder changes (as when a cover.jpg is put next to it), or until the
/// picture next to it changes when Windows could not read that picture.
/// </summary>
public sealed class LocalCoverCache : IDisposable
{
    /// <summary>Pixels along the longer side; rows show covers at 40 px, so this is sharp up to 300 % scaling.</summary>
    public const int ThumbnailSize = 128;

    private readonly string _folder;
    private readonly SemaphoreSlim _working = new(2, 2);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inFlight = new(StringComparer.Ordinal);

    public LocalCoverCache(string folder) => _folder = folder;

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
    private static string KeyFor(LocalFile file) => Hash($"{file.Path.ToUpperInvariant()}|{file.Size}|{file.LastWriteTicks}");

    private async Task<byte[]?> LoadAsync(LocalFile file, string key)
    {
        var thumbnail = Path.Combine(_folder, key + ".jpg");
        if (await ReadKeptAsync(thumbnail).ConfigureAwait(false) is { } kept)
        {
            return kept;
        }

        // A file without a cover is looked at again once its folder changes,
        // as when a cover.jpg is put next to it. The folder's time is taken
        // before looking, so a picture put there meanwhile is not missed.
        var none = Path.Combine(_folder, key + ".none");
        var stamp = FolderStamp(file.Path);
        if (await ReadKeptAsync(none).ConfigureAwait(false) is { } marker && Encoding.UTF8.GetString(marker) == stamp)
        {
            return null;
        }

        // A cover from the picture next to the file is kept under that
        // picture's name, size and time, so a new picture is read again.
        // A picture Windows could not read leaves a marker under the same name,
        // so it is read again once it is replaced (which may not change the folder).
        var fromPicture = FolderPictureKey(key, file.Path) is { } pictureKey ? Path.Combine(_folder, pictureKey + ".jpg") : null;
        var pictureNone = fromPicture is null ? null : Path.ChangeExtension(fromPicture, ".none");
        if (fromPicture is not null && await ReadKeptAsync(fromPicture).ConfigureAwait(false) is { } keptPicture)
        {
            return keptPicture;
        }

        if (pictureNone is not null && File.Exists(pictureNone))
        {
            return null;
        }

        await _working.WaitAsync().ConfigureAwait(false);
        try
        {
            // The cover inside the file first, as the player bar does (LocalCovers.Read).
            var target = thumbnail;
            var bytes = LocalCovers.ReadEmbedded(file.Path) is { } embedded ? await ShrinkAsync(embedded).ConfigureAwait(false) : null;
            if (bytes is null && fromPicture is not null)
            {
                target = fromPicture;
                bytes = LocalCovers.ReadFolderImage(file.Path) is { } picture ? await ShrinkAsync(picture).ConfigureAwait(false) : null;
            }

            try
            {
                Directory.CreateDirectory(_folder);
                if (bytes is null)
                {
                    await File.WriteAllTextAsync(pictureNone ?? none, stamp).ConfigureAwait(false);
                }
                else
                {
                    var temporary = target + ".tmp";
                    await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not kept this time; made again next time.
            }

            return bytes;
        }
        finally
        {
            _working.Release();
        }
    }

    /// <summary>A file kept in the cache folder, or null when there is none (or it can not be read, so it is made again).</summary>
    private static async Task<byte[]?> ReadKeptAsync(string path)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
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

    /// <summary>A name for the file's cover made from the picture next to it, which changes with that picture; null when there is none.</summary>
    private static string? FolderPictureKey(string key, string audioPath)
    {
        if (LocalCovers.FindFolderImage(audioPath) is not { } picture)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(picture);
            return Hash($"{key}|{picture.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));

    /// <summary>The cover as a small JPEG, or null when Windows can not read the picture.</summary>
    private static async Task<byte[]?> ShrinkAsync(byte[] cover)
    {
        try
        {
            using var input = new MemoryStream(cover, writable: false);
            var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
            var (width, height) = (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
            if (width == 0 || height == 0)
            {
                return null;
            }

            var scale = Math.Min(1.0, (double)ThumbnailSize / Math.Max(width, height));
            var scaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
            var scaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            using var output = new MemoryStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, scaledWidth, scaledHeight, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            return output.ToArray();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }
}
