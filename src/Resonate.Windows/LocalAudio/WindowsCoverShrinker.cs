using Resonate.Spotify.LocalFiles;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Resonate.Windows.LocalAudio;

/// <summary>Shrinks covers with Windows' own image decoder and encoder, for the rows of Local Files.</summary>
public sealed class WindowsCoverShrinker : ICoverShrinker
{
    public async Task<byte[]?> ShrinkAsync(byte[] cover, int size)
    {
        try
        {
            using var input = await ImageStreams.FromBytesAsync(cover).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(input);
            var (width, height) = (decoder.PixelWidth, decoder.PixelHeight);
            if (width == 0 || height == 0)
            {
                return null;
            }

            // Scaling comes before the turn a camera's orientation asks for, so
            // a turned picture comes out with its width and height swapped.
            var scale = Math.Min(1.0, (double)size / Math.Max(width, height));
            var scaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
            var scaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            var turned = decoder.OrientedPixelWidth != decoder.PixelWidth;
            var (outputWidth, outputHeight) = turned ? (scaledHeight, scaledWidth) : (scaledWidth, scaledHeight);
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

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, outputWidth, outputHeight, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            return await ImageStreams.ToBytesAsync(output).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Not a picture Windows can read (or one it fails on): the colour tile shows.
            return null;
        }
    }
}
