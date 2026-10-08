using System.Buffers.Binary;
using System.IO.Compression;

namespace Resonate.Themes;

/// <summary>
/// Writes 8-bit RGBA pixels as a PNG file, so pictures made in code (the
/// stage's cloud mask) can reach Windows' own decoder as plain bytes. One
/// IDAT chunk, no filtering; nothing is read back.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>A PNG of <paramref name="rgba"/>: rows of R, G, B, A bytes with no padding, not premultiplied.</summary>
    public static byte[] Write(ReadOnlySpan<byte> rgba, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var stride = checked(width * 4);
        if (rgba.Length < checked(stride * height))
        {
            throw new ArgumentException("The pixels do not fill the picture.", nameof(rgba));
        }

        using var file = new MemoryStream();
        file.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bits per channel
        header[9] = 6; // colour type: RGBA
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering (each row says "none")
        header[12] = 0; // not interlaced
        WriteChunk(file, "IHDR"u8, header);

        using (var data = new MemoryStream())
        {
            using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(rgba.Slice(y * stride, stride));
                }
            }

            WriteChunk(file, "IDAT"u8, data.GetBuffer().AsSpan(0, (int)data.Length));
        }

        WriteChunk(file, "IEND"u8, []);
        return file.ToArray();
    }

    /// <summary>The CRC-32 a PNG chunk ends with (ISO 3309, as zlib computes it).</summary>
    internal static uint Crc(ReadOnlySpan<byte> bytes, uint crc = 0)
    {
        crc = ~crc;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static void WriteChunk(Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        file.Write(type);
        file.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(data, Crc(type)));
        file.Write(number);
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
