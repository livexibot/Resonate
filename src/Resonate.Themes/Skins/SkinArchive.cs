using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Resonate.Themes.Skins;

/// <summary>
/// Reads a .wsz archive into a <see cref="Skin"/>, treating it as untrusted
/// input. It reads the zip's directory itself so it can stop after
/// <see cref="MaxEntries"/> files, unpacks only the files the classic player
/// uses (never trusting the sizes the archive claims), and keeps every file
/// and the whole skin under a size limit. A picture that cannot be read is
/// left out, so the built-in skin's shows instead, as Winamp did.
/// </summary>
internal static class SkinArchive
{
    /// <summary>Only this many files of an archive are looked at.</summary>
    public const int MaxEntries = 1024;

    /// <summary>The most one unpacked file may hold.</summary>
    public const int MaxEntryBytes = 8 * 1024 * 1024;

    /// <summary>The most all unpacked files together may hold.</summary>
    public const int MaxTotalBytes = 32 * 1024 * 1024;

    // What the user is told; short and plain, shown as they are.
    public const string TooLargeMessage = "This skin is too large. Skins can be up to 16 MB.";
    public const string NotAnArchiveMessage = "This file is not a skin archive.";
    public const string ModernSkinMessage = "This is a modern Winamp skin. The classic player reads classic (Winamp 2) skins only.";
    public const string NoPicturesMessage = "This archive has no classic skin pictures in it.";
    public const string DamagedMessage = "The pictures in this skin are damaged or in a format Resonate cannot read.";
    public const string FileTooLargeMessage = "A file inside this skin is too large.";
    public const string UnpacksTooLargeMessage = "This skin is too large once unpacked.";
    public const string UnreadableMessage = "This skin could not be read.";

    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint DirectoryHeaderSignature = 0x02014B50;
    private const uint EndSignature = 0x06054B50;
    private const uint Zip64EndSignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const int LocalHeaderSize = 30;
    private const int DirectoryHeaderSize = 46;
    private const int EndSize = 22;
    private const int Zip64EndSize = 56;
    private const int Zip64LocatorSize = 20;
    private const uint Unknown32 = 0xFFFFFFFF;
    private const int Stored = 0;
    private const int Deflated = 8;

    // The sheets (slot = the SkinSheet's value), then viscolor.txt and pledit.txt.
    private static readonly byte[][] WantedNames =
    [
        .. SkinSheets.All.Select(sheet => Encoding.ASCII.GetBytes(SkinSheets.FileName(sheet))),
        "viscolor.txt"u8.ToArray(),
        "pledit.txt"u8.ToArray(),
    ];

    private static int VisColorSlot => SkinSheets.All.Count;

    private static int PlaylistSlot => SkinSheets.All.Count + 1;

    public static Skin Read(ReadOnlySpan<byte> archive, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (archive.Length > SkinFolder.MaxArchiveBytes)
        {
            throw new SkinFormatException(TooLargeMessage);
        }

        try
        {
            return ReadCore(archive, name);
        }
        catch (Exception e) when (e is not SkinFormatException and not OutOfMemoryException)
        {
            // A safety net: a mistake in here must never take the app down over a broken skin.
            throw new SkinFormatException(UnreadableMessage, e);
        }
    }

    /// <summary>
    /// Builds the 108 x 13 nums_ex.bmp from a skin's numbers.bmp, which has no
    /// minus sign: the blank cell is copied after the digits and the middle bar
    /// of its "2" into it, so the time display has one way of drawing.
    /// </summary>
    internal static SkinImage NumbersWithMinus(SkinImage numbers)
    {
        var (width, height) = SkinSheets.ExpectedSize(SkinSheet.NumsEx);
        height = Math.Min(height, numbers.Height);
        if (numbers.Width >= width)
        {
            return numbers.Crop(0, 0, width, height);
        }

        var sheet = new SkinImage(width, height);
        sheet.Fill(0xFF000000);
        sheet.Draw(numbers, 0, 0, 99, 13, 0, 0);
        sheet.Draw(numbers, 90, 0, 9, 13, 99, 0);
        sheet.Draw(numbers, 20, 6, 5, 1, 101, 6);
        return sheet;
    }

    /// <summary>
    /// How much of a sheet is kept: the size classic skins draw at (numbers.bmp
    /// may be 108 wide); bigger pictures are cut down, since nothing past that
    /// is ever drawn.
    /// </summary>
    internal static (int Width, int Height) KeptSize(SkinSheet sheet) =>
        sheet == SkinSheet.Numbers ? SkinSheets.ExpectedSize(SkinSheet.NumsEx) : SkinSheets.ExpectedSize(sheet);

    private static Skin ReadCore(ReadOnlySpan<byte> archive, string name)
    {
        if (!TryFindDirectory(archive, out var directory))
        {
            throw new SkinFormatException(NotAnArchiveMessage);
        }

        var slots = new Entry?[WantedNames.Length];
        var isModern = ScanDirectory(archive, directory, slots);

        var budget = MaxTotalBytes;
        var sheets = new Dictionary<SkinSheet, SkinImage>();
        var hasSheetFiles = false;
        foreach (var sheet in SkinSheets.All)
        {
            if (slots[(int)sheet] is not { } entry)
            {
                continue;
            }

            hasSheetFiles = true;
            var bytes = Extract(archive, entry, directory.Bias, ref budget);
            if (bytes is null || !BmpDecoder.IsBmp(bytes))
            {
                // Damaged, or a PNG or the like wearing a .bmp name: left out, so the built-in skin's shows.
                continue;
            }

            try
            {
                var (width, height) = KeptSize(sheet);
                sheets[sheet] = BmpDecoder.Decode(bytes, width, height);
            }
            catch (SkinFormatException)
            {
                // Winamp silently skipped pictures it could not read; so does Resonate.
            }
        }

        if (sheets.Count == 0)
        {
            throw new SkinFormatException(hasSheetFiles ? DamagedMessage : isModern ? ModernSkinMessage : NoPicturesMessage);
        }

        if (!sheets.ContainsKey(SkinSheet.NumsEx) && sheets.TryGetValue(SkinSheet.Numbers, out var numbers))
        {
            sheets[SkinSheet.NumsEx] = NumbersWithMinus(numbers);
        }

        var visColors = ReadText(archive, slots[VisColorSlot], directory.Bias, ref budget) is { } visText
            ? SkinTextFiles.ParseVisColors(visText)
            : SkinTextFiles.DefaultVisColors;
        var playlist = ReadText(archive, slots[PlaylistSlot], directory.Bias, ref budget) is { } playlistText
            ? SkinTextFiles.ParsePlaylistColors(playlistText)
            : SkinTextFiles.DefaultPlaylistColors;
        return new Skin(name, sheets, visColors, playlist);
    }

    private static byte[]? ReadText(ReadOnlySpan<byte> archive, Entry? entry, int bias, ref int budget) =>
        entry is { } found ? Extract(archive, found, bias, ref budget) : null;

    /// <summary>
    /// Finds the zip's central directory through its end record, which sits in
    /// the last 64 KB (after an optional comment). Archives with something in
    /// front of them, like self-extracting ones, have all offsets shifted; the
    /// shift is worked out from where the directory really is.
    /// </summary>
    private static bool TryFindDirectory(ReadOnlySpan<byte> data, out CentralDirectory directory)
    {
        directory = default;
        var lowest = Math.Max(0, data.Length - EndSize - ushort.MaxValue);
        for (var at = data.Length - EndSize; at >= lowest; at--)
        {
            if (data[at] != (byte)'P' || ReadUInt32(data, at) != EndSignature)
            {
                continue;
            }

            long size = ReadUInt32(data, at + 12);
            long offset = ReadUInt32(data, at + 16);
            long end = at;
            if ((size == Unknown32 || offset == Unknown32) && !TryReadZip64End(data, at, ref size, ref offset, ref end))
            {
                continue;
            }

            if (offset + size > end)
            {
                continue;
            }

            var bias = end - (offset + size);
            if (size == 0 || IsDirectoryHeader(data, offset))
            {
                bias = 0;
            }
            else if (!IsDirectoryHeader(data, offset + bias))
            {
                continue;
            }

            directory = new CentralDirectory((int)(offset + bias), (int)(offset + bias + size), (int)bias);
            return true;
        }

        return false;
    }

    /// <summary>Zip64 archives keep the real directory size and offset in a second end record, found through a locator.</summary>
    private static bool TryReadZip64End(ReadOnlySpan<byte> data, int endRecord, ref long size, ref long offset, ref long end)
    {
        var locator = endRecord - Zip64LocatorSize;
        if (locator < Zip64EndSize || ReadUInt32(data, locator) != Zip64LocatorSignature)
        {
            return false;
        }

        var record = BinaryPrimitives.ReadUInt64LittleEndian(data[(locator + 8)..]);
        if (record > (ulong)(locator - Zip64EndSize) || ReadUInt32(data, (int)record) != Zip64EndSignature)
        {
            return false;
        }

        var bigSize = BinaryPrimitives.ReadUInt64LittleEndian(data[((int)record + 40)..]);
        var bigOffset = BinaryPrimitives.ReadUInt64LittleEndian(data[((int)record + 48)..]);
        if (bigSize > (ulong)data.Length || bigOffset > (ulong)data.Length)
        {
            return false;
        }

        (size, offset, end) = ((long)bigSize, (long)bigOffset, (long)record);
        return true;
    }

    /// <summary>
    /// Walks the first <see cref="MaxEntries"/> entries of the directory and
    /// keeps, for each wanted name, the last readable entry with that name in
    /// any folder. Says whether the archive looks like a modern Winamp skin.
    /// </summary>
    private static bool ScanDirectory(ReadOnlySpan<byte> data, CentralDirectory directory, Entry?[] slots)
    {
        var isModern = false;
        var at = directory.Start;
        for (var count = 0; count < MaxEntries; count++)
        {
            if (at > directory.End - DirectoryHeaderSize || ReadUInt32(data, at) != DirectoryHeaderSignature)
            {
                break;
            }

            var flags = ReadUInt16(data, at + 8);
            var method = ReadUInt16(data, at + 10);
            long compressed = ReadUInt32(data, at + 20);
            long uncompressed = ReadUInt32(data, at + 24);
            var nameLength = ReadUInt16(data, at + 28);
            var extraLength = ReadUInt16(data, at + 30);
            var commentLength = ReadUInt16(data, at + 32);
            long localOffset = ReadUInt32(data, at + 42);
            var next = at + DirectoryHeaderSize + nameLength + extraLength + commentLength;
            if (next > directory.End)
            {
                break;
            }

            var fullName = data.Slice(at + DirectoryHeaderSize, nameLength);
            var extra = data.Slice(at + DirectoryHeaderSize + nameLength, extraLength);
            at = next;

            var fileName = LastPart(fullName);
            if (fileName.IsEmpty)
            {
                continue;
            }

            if (Ascii.EqualsIgnoreCase(fileName, "skin.xml"u8) || EndsWithIgnoreCase(fileName, ".wal"u8))
            {
                isModern = true;
            }

            var slot = FindSlot(fileName);

            // Encrypted entries, and packing methods other than stored and deflate, are left out.
            if (slot < 0 || (flags & 1) != 0 || method is not (Stored or Deflated))
            {
                continue;
            }

            if (compressed == Unknown32 || uncompressed == Unknown32 || localOffset == Unknown32)
            {
                ReadZip64Sizes(extra, ref uncompressed, ref compressed, ref localOffset);
            }

            slots[slot] = new Entry(method, compressed, uncompressed, localOffset);
        }

        return isModern;
    }

    /// <summary>Zip64 entries keep the sizes and offset that did not fit in 32 bits in an extra field, in this order.</summary>
    private static void ReadZip64Sizes(ReadOnlySpan<byte> extra, ref long uncompressed, ref long compressed, ref long localOffset)
    {
        while (extra.Length >= 4)
        {
            var id = ReadUInt16(extra, 0);
            var size = ReadUInt16(extra, 2);
            if (4 + size > extra.Length)
            {
                return;
            }

            if (id == 1)
            {
                var field = extra.Slice(4, size);
                Next(ref field, ref uncompressed);
                Next(ref field, ref compressed);
                Next(ref field, ref localOffset);
                return;
            }

            extra = extra[(4 + size)..];
        }

        static void Next(ref ReadOnlySpan<byte> field, ref long value)
        {
            if (value == Unknown32 && field.Length >= 8)
            {
                value = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(field), long.MaxValue);
                field = field[8..];
            }
        }
    }

    /// <summary>
    /// Unpacks one entry, reading at most the size limit (or what is left of
    /// the whole skin's budget) plus one byte, whatever the archive claims.
    /// Returns null when the entry is damaged; throws when it is too large.
    /// </summary>
    private static byte[]? Extract(ReadOnlySpan<byte> data, Entry entry, int bias, ref int budget)
    {
        var local = entry.LocalOffset + bias;
        if (local < 0 || local > data.Length - LocalHeaderSize || ReadUInt32(data, (int)local) != LocalHeaderSignature)
        {
            return null;
        }

        // The local header's name and extra field can differ in length from the directory's.
        var start = local + LocalHeaderSize + ReadUInt16(data, (int)local + 26) + ReadUInt16(data, (int)local + 28);
        if (start > data.Length || entry.CompressedSize > data.Length - start)
        {
            return null;
        }

        var packed = data.Slice((int)start, (int)entry.CompressedSize);
        var limit = Math.Min(MaxEntryBytes, budget);
        if (entry.Method == Stored && packed.Length > limit)
        {
            throw TooLarge(limit);
        }

        var bytes = entry.Method == Stored ? packed.ToArray() : Inflate(packed, limit, entry.UncompressedSize);

        if (bytes is not null)
        {
            budget -= bytes.Length;
        }

        return bytes;
    }

    private static byte[]? Inflate(ReadOnlySpan<byte> packed, int limit, long claimedSize)
    {
        // The claimed size only sets the first buffer; deflate never grows data more than about 1032 times.
        var first = Math.Min(Math.Max(claimedSize, 4096), Math.Min(limit, Math.Max(packed.Length * 1032L, 4096)));
        var buffer = new byte[(int)first + 1];
        var length = 0;
        using var input = new MemoryStream(packed.ToArray(), writable: false);
        using var inflater = new DeflateStream(input, CompressionMode.Decompress);
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    if (length > limit)
                    {
                        break;
                    }

                    Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, limit + 1L));
                }

                var read = inflater.Read(buffer, length, buffer.Length - length);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (length > limit)
        {
            throw TooLarge(limit);
        }

        return length == buffer.Length ? buffer : buffer[..length];
    }

    private static SkinFormatException TooLarge(int limit) =>
        new(limit < MaxEntryBytes ? UnpacksTooLargeMessage : FileTooLargeMessage);

    private static int FindSlot(ReadOnlySpan<byte> fileName)
    {
        for (var slot = 0; slot < WantedNames.Length; slot++)
        {
            if (Ascii.EqualsIgnoreCase(fileName, WantedNames[slot]))
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>The part of a path after its last / or \ (zips made on Windows use either).</summary>
    private static ReadOnlySpan<byte> LastPart(ReadOnlySpan<byte> path) => path[(path.LastIndexOfAny((byte)'/', (byte)'\\') + 1)..];

    private static bool EndsWithIgnoreCase(ReadOnlySpan<byte> text, ReadOnlySpan<byte> ending) =>
        text.Length >= ending.Length && Ascii.EqualsIgnoreCase(text[^ending.Length..], ending);

    private static bool IsDirectoryHeader(ReadOnlySpan<byte> data, long at) =>
        at >= 0 && at <= data.Length - 4 && ReadUInt32(data, (int)at) == DirectoryHeaderSignature;

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

    private static int ReadUInt16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);

    /// <summary>Where the central directory is, and how far every offset in the archive is shifted.</summary>
    private readonly record struct CentralDirectory(int Start, int End, int Bias);

    /// <summary>One wanted file, as the central directory describes it.</summary>
    private readonly record struct Entry(int Method, long CompressedSize, long UncompressedSize, long LocalOffset);
}
