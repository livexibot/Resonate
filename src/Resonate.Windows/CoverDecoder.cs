using Windows.Graphics.Imaging;

namespace Resonate.Windows;

/// <summary>
/// Scales a song's cover with Windows' image decoder: down to a few dozen
/// pixels for colour picking, or up for the song cover backdrop.
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
            using var stream = await ImageStreams.FromBytesAsync(image).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            // Fant shrinks smoothly; a cover made larger (the backdrop's) is drawn up with Cubic, which keeps it from looking blocky.
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)size,
                ScaledHeight = (uint)size,
                InterpolationMode = decoder.PixelWidth < size || decoder.PixelHeight < size ? BitmapInterpolationMode.Cubic : BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);
            var bgra = pixels.DetachPixelData();

            // Opaque: with the alpha channel ignored, its bytes are not
            // promised to be 255, and a see-through cover would let the dark
            // colour under the backdrop show instead.
            for (var i = 3; i < bgra.Length; i += 4)
            {
                bgra[i] = 0xFF;
            }

            return bgra;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // Not a picture Windows can read.
            return null;
        }
    }
}
