using System.Buffers.Binary;

namespace Resonate.Themes.Skins;

/// <summary>
/// Writes pictures as plain 24-bit BMP files, the kind every skin editor
/// opens. Alpha is dropped: classic skins have no see-through pixels.
/// </summary>
public static class BmpEncoder
{
    private const int HeadersSize = 14 + 40;

    // 72 dots per inch, in dots per metre; editors show it, nothing uses it.
    private const int DotsPerMetre = 2835;

    /// <summary>The picture as the bytes of a .bmp file (bottom row first, as most BMPs are).</summary>
    public static byte[] Encode(SkinImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stride = ((image.Width * 3) + 3) & ~3;
        var pixelBytes = stride * image.Height;
        var file = new byte[HeadersSize + pixelBytes];
        var span = file.AsSpan();

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], HeadersSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(span[34..], pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(span[38..], DotsPerMetre);
        BinaryPrimitives.WriteInt32LittleEndian(span[42..], DotsPerMetre);

        for (var y = 0; y < image.Height; y++)
        {
            var row = span.Slice(HeadersSize + ((image.Height - 1 - y) * stride), stride);
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                row[x * 3] = (byte)pixel;
                row[(x * 3) + 1] = (byte)(pixel >> 8);
                row[(x * 3) + 2] = (byte)(pixel >> 16);
            }
        }

        return file;
    }
}
