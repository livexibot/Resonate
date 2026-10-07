using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;

namespace Resonate.Windows;

/// <summary>
/// Shrinks a song's cover to a few dozen pixels with Windows' image decoder,
/// for colour picking and the blurred background of the glass look.
/// </summary>
public static class CoverDecoder
{
    /// <summary>
    /// The image scaled to <paramref name="size"/> × <paramref name="size"/>
    /// pixels, as 8-bit BGRA rows; null if the bytes are not an image.
    /// </summary>
    public static async Task<byte[]?> DecodeAsync(byte[] image, int size, CancellationToken cancellationToken)
    {
        try
        {
            using var memory = new MemoryStream(image, writable: false);
            using var stream = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)size,
                ScaledHeight = (uint)size,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);
            return pixels.DetachPixelData();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
