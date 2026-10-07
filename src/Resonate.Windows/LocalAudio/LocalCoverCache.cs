using System.Collections.Concurrent;
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
/// A file without a cover leaves a marker, so it is not looked at again
/// until it changes.
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
    private static string KeyFor(LocalFile file)
    {
        var text = $"{file.Path.ToUpperInvariant()}|{file.Size}|{file.LastWriteTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }

    private async Task<byte[]?> LoadAsync(LocalFile file, string key)
    {
        var thumbnail = Path.Combine(_folder, key + ".jpg");
        var none = Path.Combine(_folder, key + ".none");
        try
        {
            if (File.Exists(thumbnail))
            {
                return await File.ReadAllBytesAsync(thumbnail).ConfigureAwait(false);
            }

            if (File.Exists(none))
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Make it again below.
        }

        await _working.WaitAsync().ConfigureAwait(false);
        try
        {
            var cover = LocalCovers.Read(file.Path);
            var bytes = cover is null ? null : await ShrinkAsync(cover).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_folder);
                if (bytes is null)
                {
                    await File.WriteAllBytesAsync(none, []).ConfigureAwait(false);
                }
                else
                {
                    var temporary = thumbnail + ".tmp";
                    await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                    File.Move(temporary, thumbnail, overwrite: true);
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
