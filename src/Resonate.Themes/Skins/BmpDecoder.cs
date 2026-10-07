using System.Buffers.Binary;
using System.Numerics;

namespace Resonate.Themes.Skins;

/// <summary>
/// Reads Windows BMP pictures the way classic skins hold them, from every
/// editor of the last 25 years: 1, 2, 4, 8, 16, 24 and 32 bits per pixel,
/// RLE4 and RLE8, bit fields, bottom-up and top-down rows, and the OS/2 and
/// Windows headers. The bytes are untrusted: every size and offset is checked
/// before memory is taken, and anything wrong throws
/// <see cref="SkinFormatException"/>.
/// </summary>
public static class BmpDecoder
{
    private const int FileHeaderSize = 14;
    private const uint Black = 0xFF000000;

    // Values of the header's compression field.
    private const uint Uncompressed = 0;
    private const uint Rle8 = 1;
    private const uint Rle4 = 2;
    private const uint BitFields = 3;
    private const uint AlphaBitFields = 6;

    private const string NotBmpMessage = "This picture is not a BMP file.";
    private const string DamagedMessage = "This picture is damaged.";
    private const string TooLargeMessage = "This picture is too large.";
    private const string UnsupportedMessage = "This kind of BMP picture is not supported.";

    /// <summary>True when the bytes start like a BMP file; PNG, JPEG and the rest do not.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M';

    /// <summary>Decodes a whole BMP picture, as opaque pixels (any alpha in the file is ignored, as Winamp 2 did).</summary>
    public static SkinImage Decode(ReadOnlySpan<byte> data) => Decode(data, SkinImage.MaxSide, SkinImage.MaxSide);

    /// <summary>
    /// Decodes the top-left <paramref name="maxWidth"/> x <paramref name="maxHeight"/>
    /// of a BMP picture (all of it when it is smaller). The picture's own size
    /// must still be at most <see cref="SkinImage.MaxSide"/> each way. Pixel
    /// data that ends early leaves the rest black.
    /// </summary>
    public static SkinImage Decode(ReadOnlySpan<byte> data, int maxWidth, int maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        try
        {
            return DecodeCore(data, maxWidth, maxHeight);
        }
        catch (Exception e) when (e is not SkinFormatException and not OutOfMemoryException)
        {
            // A safety net: a mistake in here must never take the app down over a broken skin.
            throw new SkinFormatException(DamagedMessage, e);
        }
    }

    private static SkinImage DecodeCore(ReadOnlySpan<byte> data, int maxWidth, int maxHeight)
    {
        if (!IsBmp(data))
        {
            throw new SkinFormatException(NotBmpMessage);
        }

        if (data.Length < FileHeaderSize + 4)
        {
            throw new SkinFormatException(DamagedMessage);
        }

        var pixelOffset = ReadUInt32(data, 10);
        var headerSize = ReadUInt32(data, FileHeaderSize);

        // 12 is OS/2 1.x (BITMAPCOREHEADER); 40, 52, 56, 108 and 124 are Windows'
        // headers; anything else from 16 to 64 is an OS/2 2.x header.
        var isCore = headerSize == 12;
        var isWindows = headerSize is 40 or 52 or 56 or 108 or 124;
        var isOs2 = !isCore && !isWindows && headerSize is >= 16 and <= 64;
        if (!isCore && !isWindows && !isOs2)
        {
            throw new SkinFormatException(UnsupportedMessage);
        }

        if (FileHeaderSize + headerSize > data.Length)
        {
            throw new SkinFormatException(DamagedMessage);
        }

        int width;
        int height;
        int bitCount;
        var topDown = false;
        var compression = Uncompressed;
        uint colorsUsed = 0;
        if (isCore)
        {
            width = ReadUInt16(data, 18);
            height = ReadUInt16(data, 20);
            bitCount = ReadUInt16(data, 24);
        }
        else
        {
            width = ReadInt32(data, 18);
            height = ReadInt32(data, 22);
            bitCount = ReadUInt16(data, 28);
            if (headerSize >= 20)
            {
                compression = ReadUInt32(data, 30);
            }

            if (headerSize >= 36)
            {
                colorsUsed = ReadUInt32(data, 46);
            }

            // A negative height means the rows are stored top row first.
            if (height < 0 && height != int.MinValue)
            {
                topDown = true;
                height = -height;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new SkinFormatException(DamagedMessage);
        }

        if (width > SkinImage.MaxSide || height > SkinImage.MaxSide)
        {
            throw new SkinFormatException(TooLargeMessage);
        }

        CheckFormat(compression, bitCount, isWindows);

        var headerEnd = FileHeaderSize + (int)headerSize;
        var (redMask, greenMask, blueMask) = (0u, 0u, 0u);
        if (compression is BitFields or AlphaBitFields)
        {
            if (headerSize >= 52)
            {
                redMask = ReadUInt32(data, FileHeaderSize + 40);
                greenMask = ReadUInt32(data, FileHeaderSize + 44);
                blueMask = ReadUInt32(data, FileHeaderSize + 48);
            }
            else
            {
                // The 40-byte header keeps its masks just after it.
                var maskBytes = compression == AlphaBitFields ? 16 : 12;
                if (headerEnd + maskBytes > data.Length)
                {
                    throw new SkinFormatException(DamagedMessage);
                }

                redMask = ReadUInt32(data, headerEnd);
                greenMask = ReadUInt32(data, headerEnd + 4);
                blueMask = ReadUInt32(data, headerEnd + 8);
                headerEnd += maskBytes;
            }
        }

        if (redMask == 0 && greenMask == 0 && blueMask == 0)
        {
            // Plain 16-bit pictures are 5-5-5, plain 32-bit ones 8-8-8 with a spare byte.
            (redMask, greenMask, blueMask) = bitCount == 16 ? (0x7C00u, 0x03E0u, 0x001Fu) : (0xFF0000u, 0xFF00u, 0xFFu);
        }

        if (pixelOffset > (uint)data.Length || pixelOffset < (uint)headerEnd)
        {
            throw new SkinFormatException(DamagedMessage);
        }

        var pixelStart = (int)pixelOffset;
        var palette = bitCount <= 8 ? ReadPalette(data, headerEnd, pixelStart, bitCount, colorsUsed, isCore ? 3 : 4) : [];
        var image = new SkinImage(Math.Min(width, maxWidth), Math.Min(height, maxHeight));
        image.Fill(Black);

        var pixels = data[pixelStart..];
        var layout = new Layout(width, height, topDown, image);
        switch (compression)
        {
            case Rle8:
            case Rle4:
                DecodeRle(pixels, layout, palette, compression == Rle4);
                break;
            default:
                DecodeRows(pixels, layout, bitCount, palette, new Channel(redMask), new Channel(greenMask), new Channel(blueMask));
                break;
        }

        return image;
    }

    private static void CheckFormat(uint compression, int bitCount, bool isWindows)
    {
        var supported = compression switch
        {
            Uncompressed => bitCount is 1 or 2 or 4 or 8 or 16 or 24 or 32,
            Rle8 => bitCount == 8,
            Rle4 => bitCount == 4,

            // OS/2 2.x uses 3 and 4 for Huffman and RLE24, which no skin needs.
            BitFields or AlphaBitFields => isWindows && bitCount is 16 or 32,

            // JPEG and PNG inside a BMP, and anything newer.
            _ => false,
        };
        if (!supported)
        {
            throw new SkinFormatException(UnsupportedMessage);
        }
    }

    /// <summary>
    /// Reads the colour table. It ends where the pixels begin, so a table
    /// shorter than the header claims is cut there; colours it lacks are black.
    /// </summary>
    private static uint[] ReadPalette(ReadOnlySpan<byte> data, int start, int pixelStart, int bitCount, uint colorsUsed, int entrySize)
    {
        var palette = new uint[1 << bitCount];
        Array.Fill(palette, Black);
        var declared = colorsUsed != 0 && colorsUsed < (uint)palette.Length ? (int)colorsUsed : palette.Length;
        var count = Math.Min(declared, (pixelStart - start) / entrySize);
        for (var i = 0; i < count; i++)
        {
            var at = start + (i * entrySize);
            palette[i] = Rgb(data[at + 2], data[at + 1], data[at]);
        }

        return palette;
    }

    /// <summary>Uncompressed rows: each padded to four bytes, one after another from the first row stored.</summary>
    private static void DecodeRows(ReadOnlySpan<byte> pixels, Layout layout, int bitCount, uint[] palette, Channel red, Channel green, Channel blue)
    {
        // Width is at most 2048 and the depth 32, so this cannot overflow.
        var stride = ((layout.Width * bitCount) + 31) / 32 * 4;
        var image = layout.Image;
        for (var row = 0; row < layout.Height; row++)
        {
            var y = layout.ImageRow(row);
            var start = row * stride;
            if (y >= image.Height || start >= pixels.Length)
            {
                continue;
            }

            var source = pixels.Slice(start, Math.Min(stride, pixels.Length - start));
            var target = image.Pixels.AsSpan(y * image.Width, image.Width);
            if (bitCount <= 8)
            {
                DecodeIndexedRow(source, target, bitCount, palette);
            }
            else
            {
                DecodeTrueColourRow(source, target, bitCount, red, green, blue);
            }
        }
    }

    private static void DecodeIndexedRow(ReadOnlySpan<byte> source, Span<uint> target, int bitCount, uint[] palette)
    {
        var mask = (1 << bitCount) - 1;
        var count = Math.Min(target.Length, source.Length * 8 / bitCount);
        for (var x = 0; x < count; x++)
        {
            // Pixels fill each byte from its highest bits down.
            var bit = x * bitCount;
            var index = (source[bit >> 3] >> (8 - bitCount - (bit & 7))) & mask;
            target[x] = palette[index];
        }
    }

    private static void DecodeTrueColourRow(ReadOnlySpan<byte> source, Span<uint> target, int bitCount, Channel red, Channel green, Channel blue)
    {
        var bytesPerPixel = bitCount / 8;
        var count = Math.Min(target.Length, source.Length / bytesPerPixel);
        for (var x = 0; x < count; x++)
        {
            var pixel = source.Slice(x * bytesPerPixel, bytesPerPixel);
            target[x] = bitCount switch
            {
                24 => Rgb(pixel[2], pixel[1], pixel[0]),
                16 => FromFields(BinaryPrimitives.ReadUInt16LittleEndian(pixel), red, green, blue),
                _ => FromFields(BinaryPrimitives.ReadUInt32LittleEndian(pixel), red, green, blue),
            };
        }
    }

    private static uint FromFields(uint value, Channel red, Channel green, Channel blue) =>
        Rgb(red.Scale(value), green.Scale(value), blue.Scale(value));

    /// <summary>
    /// RLE8 and RLE4: pairs of (count, colour) runs, and escapes for the end of
    /// a row, the end of the picture, a jump ahead, and a stretch of literal
    /// pixels padded to two bytes. Skipped pixels stay black.
    /// </summary>
    private static void DecodeRle(ReadOnlySpan<byte> stream, Layout layout, uint[] palette, bool fourBit)
    {
        var at = 0;
        var x = 0;
        var row = 0;
        while (row < layout.Height && at + 1 < stream.Length)
        {
            int count = stream[at];
            int value = stream[at + 1];
            at += 2;
            if (count > 0)
            {
                // A run: one colour, or with four bits two colours taking turns.
                for (var i = 0; i < count; i++)
                {
                    var index = !fourBit ? value : (i & 1) == 0 ? value >> 4 : value & 0x0F;
                    layout.Put(x + i, row, palette[index]);
                }

                x = Math.Min(x + count, layout.Width);
                continue;
            }

            switch (value)
            {
                case 0:
                    x = 0;
                    row++;
                    break;
                case 1:
                    return;
                case 2:
                    if (at + 1 >= stream.Length)
                    {
                        return;
                    }

                    x = Math.Min(x + stream[at], layout.Width);
                    row += stream[at + 1];
                    at += 2;
                    break;
                default:
                    // Literal pixels; the bytes they take are padded to an even number.
                    var bytes = fourBit ? (value + 1) / 2 : value;
                    for (var i = 0; i < value; i++)
                    {
                        var offset = at + (fourBit ? i / 2 : i);
                        if (offset >= stream.Length)
                        {
                            return;
                        }

                        var index = !fourBit ? stream[offset] : (i & 1) == 0 ? stream[offset] >> 4 : stream[offset] & 0x0F;
                        layout.Put(x + i, row, palette[index]);
                    }

                    x = Math.Min(x + value, layout.Width);
                    at += bytes + (bytes & 1);
                    break;
            }
        }
    }

    private static uint Rgb(int red, int green, int blue) => Black | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

    private static int ReadInt32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data[at..]);

    private static int ReadUInt16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);

    /// <summary>Where the rows of the file land in the (possibly cropped) picture.</summary>
    private readonly struct Layout(int width, int height, bool topDown, SkinImage image)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public bool TopDown { get; } = topDown;

        public SkinImage Image { get; } = image;

        /// <summary>The picture row of the <paramref name="row"/>-th row stored in the file.</summary>
        public int ImageRow(int row) => TopDown ? row : Height - 1 - row;

        public void Put(int x, int row, uint colour)
        {
            if (x < Image.Width && row < Height)
            {
                var y = ImageRow(row);
                if (y < Image.Height)
                {
                    Image[x, y] = colour;
                }
            }
        }
    }

    /// <summary>One colour of a bit-field pixel: where its bits are and how to stretch them to 0-255.</summary>
    private readonly struct Channel
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly ulong _max;

        public Channel(uint mask)
        {
            _mask = mask;
            _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
            _max = mask >> _shift;
        }

        public int Scale(uint pixel) => _max == 0 ? 0 : (int)(((((pixel & _mask) >> _shift) * 255UL) + (_max / 2)) / _max);
    }
}
