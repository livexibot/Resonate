using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

/// <summary>Builds BMP pictures and skin archives in code, so the tests need no files.</summary>
internal static class SkinFixtures
{
    public const uint Uncompressed = 0;
    public const uint Rle8 = 1;
    public const uint Rle4 = 2;
    public const uint BitFields = 3;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// A BMP file put together field by field. A negative height stores the
    /// rows top first. Masks go inside headers of 52 bytes or more and after
    /// the 40-byte one; the palette follows, and the pixels come right after
    /// it unless <paramref name="pixelOffset"/> puts them elsewhere (over the
    /// palette, even, as a badly written file would).
    /// </summary>
    public static byte[] Bmp(
        int width,
        int height,
        int bitCount,
        byte[] pixels,
        uint[]? palette = null,
        uint compression = Uncompressed,
        int headerSize = 40,
        uint[]? masks = null,
        uint colorsUsed = 0,
        int? pixelOffset = null)
    {
        var isCore = headerSize == 12;
        var entrySize = isCore ? 3 : 4;
        var masksAfter = headerSize == 40 && masks is not null ? masks.Length * 4 : 0;
        var paletteStart = 14 + headerSize + masksAfter;
        var natural = paletteStart + ((palette?.Length ?? 0) * entrySize);
        var offset = pixelOffset ?? natural;
        var file = new byte[Math.Max(natural, offset + pixels.Length)];
        var span = file.AsSpan();

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], offset);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], headerSize);
        if (isCore)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[18..], (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(span[20..], (ushort)height);
            BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(span[24..], (ushort)bitCount);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
            BinaryPrimitives.WriteInt32LittleEndian(span[22..], height);
            BinaryPrimitives.WriteUInt16LittleEndian(span[26..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)bitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(span[30..], compression);
            BinaryPrimitives.WriteInt32LittleEndian(span[34..], pixels.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span[38..], 2835);
            BinaryPrimitives.WriteInt32LittleEndian(span[42..], 2835);
            BinaryPrimitives.WriteUInt32LittleEndian(span[46..], colorsUsed);
            if (headerSize >= 108)
            {
                // LCS_sRGB, as Windows writes it.
                BinaryPrimitives.WriteUInt32LittleEndian(span[70..], 0x73524742);
            }
        }

        if (masks is not null)
        {
            var maskStart = headerSize >= 52 ? 14 + 40 : 14 + headerSize;
            var count = headerSize >= 52 ? Math.Min(masks.Length, (headerSize - 40) / 4) : masks.Length;
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[(maskStart + (i * 4))..], masks[i]);
            }
        }

        for (var i = 0; i < (palette?.Length ?? 0); i++)
        {
            var at = paletteStart + (i * entrySize);
            var colour = palette![i];
            span[at] = (byte)colour;
            span[at + 1] = (byte)(colour >> 8);
            span[at + 2] = (byte)(colour >> 16);
        }

        pixels.CopyTo(span[offset..]);
        return file;
    }

    /// <summary>
    /// Uncompressed pixel rows in file order (bottom row first unless
    /// <paramref name="topDown"/>), each padded to four bytes. The value is a
    /// palette index, a 16-bit word, 0xRRGGBB for 24 bits, or the 32-bit word.
    /// </summary>
    public static byte[] Rows(int width, int height, int bitCount, Func<int, int, uint> value, bool topDown = false)
    {
        var stride = ((width * bitCount) + 31) / 32 * 4;
        var bytes = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            var y = topDown ? row : height - 1 - row;
            var target = bytes.AsSpan(row * stride, stride);
            for (var x = 0; x < width; x++)
            {
                var v = value(x, y);
                switch (bitCount)
                {
                    case <= 8:
                        var bit = x * bitCount;
                        target[bit >> 3] |= (byte)(v << (8 - bitCount - (bit & 7)));
                        break;
                    case 16:
                        BinaryPrimitives.WriteUInt16LittleEndian(target[(x * 2)..], (ushort)v);
                        break;
                    case 24:
                        target[x * 3] = (byte)v;
                        target[(x * 3) + 1] = (byte)(v >> 8);
                        target[(x * 3) + 2] = (byte)(v >> 16);
                        break;
                    default:
                        BinaryPrimitives.WriteUInt32LittleEndian(target[(x * 4)..], v);
                        break;
                }
            }
        }

        return bytes;
    }

    /// <summary>Distinct opaque colours for a palette.</summary>
    public static uint[] Palette(int count) =>
        [.. Enumerable.Range(0, count).Select(i => 0xFF000000 | ((uint)((i * 37) + 11) % 256 << 16) | ((uint)((i * 91) + 23) % 256 << 8) | ((uint)((i * 53) + 7) % 256))];

    /// <summary>A picture whose pixels come from <paramref name="colour"/>.</summary>
    public static SkinImage Image(int width, int height, Func<int, int, uint> colour)
    {
        var image = new SkinImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = colour(x, y);
            }
        }

        return image;
    }

    /// <summary>A picture with a different opaque colour at every pixel.</summary>
    public static SkinImage Pattern(int width, int height, int seed = 0) =>
        Image(width, height, (x, y) => 0xFF000000 | (uint)(((x * 7) + (y * 131) + (seed * 977)) * 2654435761u & 0xFFFFFF));

    public static SkinImage Filled(int width, int height, uint colour) => Image(width, height, (_, _) => colour);

    /// <summary>A colour of its own for each sheet, so tests can tell which file a picture came from.</summary>
    public static uint SheetColour(SkinSheet sheet) => 0xFF000000 | (uint)(0x0A0B0C + ((int)sheet * 0x111111));

    /// <summary>A sheet at its usual size, filled with <see cref="SheetColour"/>, as a 24-bit BMP.</summary>
    public static byte[] SheetBmp(SkinSheet sheet)
    {
        var (width, height) = SkinSheets.ExpectedSize(sheet);
        return BmpEncoder.Encode(Filled(width, height, SheetColour(sheet)));
    }

    /// <summary>All twelve sheets, deflated, under their usual names.</summary>
    public static IEnumerable<ZipItem> ClassicSheets() =>
        SkinSheets.All.Select(sheet => new ZipItem(SkinSheets.FileName(sheet), SheetBmp(sheet)));

    /// <summary>A complete classic skin archive.</summary>
    public static byte[] ClassicSkin() => Zip(ClassicSheets());

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A zip file written byte by byte, so tests choose every field, including ones that lie.</summary>
    public static byte[] Zip(IEnumerable<ZipItem> items, bool zip64 = false)
    {
        using var output = new MemoryStream();
        using var central = new MemoryStream();
        var count = 0;
        foreach (var item in items)
        {
            var name = item.RawName ?? Encoding.UTF8.GetBytes(item.Name);
            var method = item.Method ?? (item.Deflate ? 8 : 0);
            var packed = item.Method is null && item.Deflate ? Deflate(item.Data) : item.Data;
            var crc = Crc32(item.Data);
            var size = item.ClaimedSize ?? item.Data.Length;
            var flags = (item.Encrypted ? 1 : 0) | (name.All(b => b < 0x80) ? 0 : 0x800);
            var offset = output.Position;

            Write32(output, 0x04034B50);
            Write16(output, 20);
            Write16(output, flags);
            Write16(output, method);
            Write16(output, 0);
            Write16(output, 0x21);
            Write32(output, crc);
            Write32(output, (uint)packed.Length);
            Write32(output, (uint)size);
            Write16(output, name.Length);
            Write16(output, 0);
            output.Write(name);
            output.Write(packed);

            Write32(central, 0x02014B50);
            Write16(central, 20);
            Write16(central, zip64 ? 45 : 20);
            Write16(central, flags);
            Write16(central, method);
            Write16(central, 0);
            Write16(central, 0x21);
            Write32(central, crc);
            Write32(central, zip64 ? 0xFFFFFFFF : (uint)packed.Length);
            Write32(central, zip64 ? 0xFFFFFFFF : (uint)size);
            Write16(central, name.Length);
            Write16(central, zip64 ? 28 : 0);
            Write16(central, 0);
            Write16(central, 0);
            Write16(central, 0);
            Write32(central, 0);
            Write32(central, zip64 ? 0xFFFFFFFF : (uint)offset);
            central.Write(name);
            if (zip64)
            {
                Write16(central, 1);
                Write16(central, 24);
                Write64(central, (ulong)size);
                Write64(central, (ulong)packed.Length);
                Write64(central, (ulong)offset);
            }

            count++;
        }

        var directoryOffset = output.Position;
        central.WriteTo(output);
        var directorySize = output.Position - directoryOffset;
        if (zip64)
        {
            var record = output.Position;
            Write32(output, 0x06064B50);
            Write64(output, 44);
            Write16(output, 45);
            Write16(output, 45);
            Write32(output, 0);
            Write32(output, 0);
            Write64(output, (ulong)count);
            Write64(output, (ulong)count);
            Write64(output, (ulong)directorySize);
            Write64(output, (ulong)directoryOffset);

            Write32(output, 0x07064B50);
            Write32(output, 0);
            Write64(output, (ulong)record);
            Write32(output, 1);
        }

        Write32(output, 0x06054B50);
        Write16(output, 0);
        Write16(output, 0);
        Write16(output, zip64 ? 0xFFFF : count);
        Write16(output, zip64 ? 0xFFFF : count);
        Write32(output, zip64 ? 0xFFFFFFFF : (uint)directorySize);
        Write32(output, zip64 ? 0xFFFFFFFF : (uint)directoryOffset);
        Write16(output, 0);
        return output.ToArray();
    }

    /// <summary>A zip written by .NET's own ZipArchive; <paramref name="streaming"/> makes it use data descriptors, as zip tools writing to a pipe do.</summary>
    public static byte[] ZipWithDotNet(IEnumerable<ZipItem> items, bool streaming)
    {
        using var buffer = new MemoryStream();
        using (var target = streaming ? new ForwardOnlyStream(buffer) : (Stream)new NonClosingStream(buffer))
        using (var zip = new ZipArchive(target, ZipArchiveMode.Create))
        {
            foreach (var item in items)
            {
                var entry = zip.CreateEntry(item.Name, item.Deflate ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(item.Data);
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private static void Write16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        stream.Write(bytes);
    }

    private static void Write32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Write64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    /// <summary>A stream that can only be written forwards, like a pipe.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    /// <summary>Passes everything through but leaves the inner stream open when disposed.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}

/// <summary>One file for <see cref="SkinFixtures.Zip"/>.</summary>
internal sealed record ZipItem(string Name, byte[] Data)
{
    /// <summary>Deflated (the default) or stored.</summary>
    public bool Deflate { get; init; } = true;

    /// <summary>Marks the entry encrypted (the data is left as it is).</summary>
    public bool Encrypted { get; init; }

    /// <summary>Writes this packing method and the data as given, packed or not.</summary>
    public int? Method { get; init; }

    /// <summary>The unpacked size the archive claims, true or not.</summary>
    public long? ClaimedSize { get; init; }

    /// <summary>The name's bytes, when they are not plain UTF-8 of <see cref="Name"/>.</summary>
    public byte[]? RawName { get; init; }
}
