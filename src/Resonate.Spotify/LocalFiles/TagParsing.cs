using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace Resonate.Spotify.LocalFiles;

// Small pieces every tag format uses: reading parts of a file by offset,
// collecting what was found, and decoding text. Header data is untrusted, so
// every length is checked before it is used, and a header that ends early
// throws InvalidDataException (caught by TagReader, which keeps what it had).

/// <summary>Bytes by offset: a file, or a block already in memory.</summary>
internal interface IByteSource
{
    long Length { get; }

    /// <summary>Up to <paramref name="count"/> bytes at <paramref name="offset"/>, fewer at the end. Valid until the next call.</summary>
    ReadOnlySpan<byte> Read(long offset, int count);
}

/// <summary>Reads a file in 16 KB windows, so neighbouring small reads cost one disk read.</summary>
internal sealed class FileWindow : IByteSource
{
    private const int WindowSize = 16 * 1024;

    private readonly Stream _stream;
    private byte[] _buffer = new byte[WindowSize];
    private long _start = -1;
    private int _count;

    public FileWindow(Stream stream)
    {
        _stream = stream;
        Length = stream.Length;
    }

    public long Length { get; }

    public ReadOnlySpan<byte> Read(long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset >= Length)
        {
            return [];
        }

        var wanted = (int)Math.Min(count, Length - offset);
        if (_start >= 0 && offset >= _start && offset + wanted <= _start + _count)
        {
            return _buffer.AsSpan((int)(offset - _start), wanted);
        }

        var fill = (int)Math.Min(Math.Max(wanted, WindowSize), Length - offset);
        if (fill > _buffer.Length)
        {
            _buffer = new byte[fill];
        }

        _stream.Position = offset;
        var read = 0;
        while (read < fill)
        {
            var n = _stream.Read(_buffer, read, fill - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        _start = offset;
        _count = read;
        return _buffer.AsSpan(0, Math.Min(wanted, read));
    }
}

internal sealed class MemorySource : IByteSource
{
    private readonly byte[] _bytes;

    public MemorySource(byte[] bytes) => _bytes = bytes;

    public long Length => _bytes.Length;

    public ReadOnlySpan<byte> Read(long offset, int count)
    {
        if (offset < 0 || count <= 0 || offset >= _bytes.Length)
        {
            return [];
        }

        return _bytes.AsSpan((int)offset, (int)Math.Min(count, _bytes.Length - offset));
    }
}

internal static class ByteSourceExtensions
{
    /// <summary>Exactly <paramref name="count"/> bytes, or InvalidDataException when the data ends first.</summary>
    public static ReadOnlySpan<byte> Exactly(this IByteSource source, long offset, int count)
    {
        var bytes = source.Read(offset, count);
        if (bytes.Length < count)
        {
            throw new InvalidDataException("The header ends early.");
        }

        return bytes;
    }

    public static uint U32BE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt32BigEndian(source.Exactly(offset, 4));

    public static uint U32LE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt32LittleEndian(source.Exactly(offset, 4));

    public static ushort U16BE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt16BigEndian(source.Exactly(offset, 2));

    public static ushort U16LE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt16LittleEndian(source.Exactly(offset, 2));

    public static ulong U64BE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt64BigEndian(source.Exactly(offset, 8));

    public static ulong U64LE(this IByteSource source, long offset) => BinaryPrimitives.ReadUInt64LittleEndian(source.Exactly(offset, 8));

    /// <summary>True when the bytes at <paramref name="offset"/> are <paramref name="expected"/>.</summary>
    public static bool Matches(this IByteSource source, long offset, ReadOnlySpan<byte> expected) =>
        source.Read(offset, expected.Length).SequenceEqual(expected);

    /// <summary>A copy of up to <paramref name="count"/> bytes (the span from Read is only valid until the next read).</summary>
    public static byte[] Copy(this IByteSource source, long offset, int count) => source.Read(offset, count).ToArray();
}

/// <summary>The text fields a tag can fill.</summary>
internal enum TagField
{
    Title,
    Artist,
    AlbumArtist,
    Album,
    TrackNumber,
    DiscNumber,
}

/// <summary>
/// Collects what the tags say. The first value found for a field wins, so
/// a format's main tag is read before its fallback (ID3v2 before ID3v1).
/// </summary>
internal sealed class TagBuilder
{
    private bool _coverIsFront;

    public string? Title { get; set; }

    public string? Artist { get; set; }

    public string? AlbumArtist { get; set; }

    public string? Album { get; set; }

    public int? TrackNumber { get; set; }

    public int? DiscNumber { get; set; }

    public TimeSpan Duration { get; set; }

    public CoverRef? Cover { get; private set; }

    public void Set(TagField field, string? value)
    {
        value = TagText.Clean(value);
        if (value is null)
        {
            return;
        }

        switch (field)
        {
            case TagField.Title:
                Title ??= value;
                break;
            case TagField.Artist:
                Artist ??= value;
                break;
            case TagField.AlbumArtist:
                AlbumArtist ??= value;
                break;
            case TagField.Album:
                Album ??= value;
                break;
            case TagField.TrackNumber:
                TrackNumber ??= TagText.Number(value);
                break;
            case TagField.DiscNumber:
                DiscNumber ??= TagText.Number(value);
                break;
        }
    }

    /// <summary>Keeps the first picture, or a later one that is the front cover.</summary>
    public void OfferCover(CoverRef cover, bool isFront)
    {
        if (cover.Length <= 0 && cover.Encoding != CoverEncoding.InComment)
        {
            return;
        }

        if (Cover is null || (isFront && !_coverIsFront))
        {
            Cover = cover;
            _coverIsFront = isFront;
        }
    }

    public void SetDuration(TimeSpan duration)
    {
        if (Duration <= TimeSpan.Zero && duration > TimeSpan.Zero && duration < TimeSpan.FromDays(7))
        {
            Duration = duration;
        }
    }

    public AudioTags Build() => new()
    {
        Title = Title,
        Artist = Artist,
        AlbumArtist = AlbumArtist,
        Album = Album,
        TrackNumber = TrackNumber,
        DiscNumber = DiscNumber,
        Duration = Duration,
        Cover = Cover,
    };
}

internal static class TagText
{
    // Windows-1252's characters for 0x80-0x9F, which are unused control codes
    // in ISO-8859-1; Windows taggers write these when a tag says "Latin-1".
    private const string Cp1252High =
        "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F"
        + "\u0090‘’“”•–—˜™š›œ\u009DžŸ";

    /// <summary>
    /// Text a tag calls Latin-1. Many taggers put UTF-8 there instead, so
    /// valid UTF-8 with accents is read as UTF-8.
    /// </summary>
    public static string Latin1(ReadOnlySpan<byte> bytes)
    {
        if (!Ascii.IsValid(bytes) && Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return string.Create(bytes.Length, bytes.ToArray(), static (chars, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var b = source[i];
                chars[i] = b is >= 0x80 and <= 0x9F ? Cp1252High[b - 0x80] : (char)b;
            }
        });
    }

    /// <summary>UTF-16 with or without a byte order mark (big-endian when there is none and <paramref name="bigEndian"/>).</summary>
    public static string Utf16(ReadOnlySpan<byte> bytes, bool bigEndian = false)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        return (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(bytes);
    }

    public static string Utf8Text(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>Without surrounding spaces and padding; null when nothing is left.</summary>
    public static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }

        // A byte order mark can be left inside text that held several values.
        var trimmed = text.Trim().Trim('\0', '﻿').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>"3", "3/12" or " 03 " as 3; null when there is no positive number.</summary>
    public static int? Number(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var span = text.AsSpan().Trim();
        var slash = span.IndexOf('/');
        if (slash >= 0)
        {
            span = span[..slash].Trim();
        }

        return int.TryParse(span, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;
    }

    /// <summary>A picture's type ("image/jpeg", "image/png" and so on) from its first bytes, or null.</summary>
    public static string? SniffImage(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "image/png",
        [(byte)'B', (byte)'M', ..] => "image/bmp",
        [(byte)'G', (byte)'I', (byte)'F', ..] => "image/gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        [(byte)'I', (byte)'I', 0x2A, 0x00, ..] or [(byte)'M', (byte)'M', 0x00, 0x2A, ..] => "image/tiff",
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f' or (byte)'s', ..] => "image/avif",
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', .. var rest] when IsHeifBrand(rest) => "image/heif",
        _ => null,
    };

    private static bool IsHeifBrand(ReadOnlySpan<byte> brand) =>
        brand.StartsWith("heic"u8) || brand.StartsWith("heix"u8) || brand.StartsWith("mif1"u8) || brand.StartsWith("msf1"u8);
}

/// <summary>ID3's unsynchronisation: 0xFF 0x00 in the file stands for 0xFF.</summary>
internal static class Unsync
{
    public static byte[] Decode(ReadOnlySpan<byte> data) => Decode(data, removed: null);

    /// <param name="removed">Receives, for each dropped 0x00, how many decoded bytes came before it.</param>
    public static byte[] Decode(ReadOnlySpan<byte> data, List<int>? removed)
    {
        var result = new byte[data.Length];
        var n = 0;
        for (var i = 0; i < data.Length; i++)
        {
            result[n++] = data[i];
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
            {
                removed?.Add(n);
                i++;
            }
        }

        Array.Resize(ref result, n);
        return result;
    }

    /// <summary>Where decoded byte <paramref name="index"/> is in the raw data, given <see cref="Decode(ReadOnlySpan{byte}, List{int})"/>'s list.</summary>
    public static long RawIndex(List<int> removed, int index)
    {
        // Every 0x00 dropped before this byte shifts it by one. The list is
        // strictly increasing (each drop follows its own 0xFF).
        var found = removed.BinarySearch(index);
        var before = found >= 0 ? found + 1 : ~found;
        return (long)index + before;
    }

    /// <summary>How many raw bytes produce the first <paramref name="decodedCount"/> decoded bytes.</summary>
    public static int RawLength(ReadOnlySpan<byte> raw, int decodedCount)
    {
        var decoded = 0;
        var i = 0;
        while (i < raw.Length && decoded < decodedCount)
        {
            decoded++;
            i += raw[i] == 0xFF && i + 1 < raw.Length && raw[i + 1] == 0x00 ? 2 : 1;
        }

        return i;
    }
}

/// <summary>Vorbis comments ("KEY=value", UTF-8), used by FLAC, Ogg Vorbis and Opus.</summary>
internal static class VorbisComments
{
    private const int MaxValueBytes = 64 * 1024;

    /// <summary>Reads the comment block at <paramref name="offset"/>.</summary>
    /// <param name="onPicture">Called with the offset and length of a METADATA_BLOCK_PICTURE value (base64), which is not read here.</param>
    public static void Read(IByteSource source, long offset, long end, TagBuilder tags, Action<long, int>? onPicture = null)
    {
        var pos = offset;
        var vendorLength = source.U32LE(pos);
        pos += 4 + vendorLength;
        var count = source.U32LE(pos);
        pos += 4;

        var artists = new List<string>();
        var albumArtists = new List<string>();
        for (var i = 0u; i < count && pos + 4 <= end; i++)
        {
            var length = source.U32LE(pos);
            var start = pos + 4;
            pos = start + length;
            if (pos > end)
            {
                break;
            }

            var head = source.Read(start, (int)Math.Min(length, 64));
            var equals = head.IndexOf((byte)'=');
            if (equals <= 0)
            {
                continue;
            }

            var key = Encoding.ASCII.GetString(head[..equals]).ToUpperInvariant();
            var valueStart = start + equals + 1;
            var valueLength = (int)Math.Min(length - equals - 1, int.MaxValue);
            if (key == "METADATA_BLOCK_PICTURE")
            {
                onPicture?.Invoke(valueStart, valueLength);
                continue;
            }

            TagField? field = key switch
            {
                "TITLE" => TagField.Title,
                "ARTIST" => TagField.Artist,
                "ALBUMARTIST" or "ALBUM ARTIST" or "ALBUM_ARTIST" => TagField.AlbumArtist,
                "ALBUM" => TagField.Album,
                "TRACKNUMBER" => TagField.TrackNumber,
                "DISCNUMBER" => TagField.DiscNumber,
                _ => null,
            };
            if (field is not { } known || valueLength > MaxValueBytes)
            {
                continue;
            }

            var value = TagText.Clean(TagText.Utf8Text(source.Read(valueStart, valueLength)));
            if (value is null)
            {
                continue;
            }

            if (known == TagField.Artist)
            {
                artists.Add(value);
            }
            else if (known == TagField.AlbumArtist)
            {
                albumArtists.Add(value);
            }
            else
            {
                tags.Set(known, value);
            }
        }

        tags.Set(TagField.Artist, string.Join(", ", artists));
        tags.Set(TagField.AlbumArtist, string.Join(", ", albumArtists));
    }
}

/// <summary>FLAC's picture structure (a FLAC PICTURE block, or a base64 METADATA_BLOCK_PICTURE comment).</summary>
internal static class FlacPicture
{
    public const int FrontCover = 3;

    /// <summary>Where the picture's data is, or null when the structure does not fit in <paramref name="end"/>.</summary>
    public static (int Type, string? Mime, long DataOffset, int DataLength)? Parse(IByteSource source, long offset, long end)
    {
        var pos = offset;
        var type = (int)source.U32BE(pos);
        var mimeLength = source.U32BE(pos + 4);
        if (mimeLength > 256)
        {
            return null;
        }

        var mime = Encoding.ASCII.GetString(source.Exactly(pos + 8, (int)mimeLength));
        pos += 8 + mimeLength;
        var descriptionLength = source.U32BE(pos);
        pos += 4 + descriptionLength + 16;
        var dataLength = source.U32BE(pos);
        var dataOffset = pos + 4;
        if (dataLength == 0 || dataLength > TagReader.MaxCoverBytes || dataOffset + dataLength > end)
        {
            return null;
        }

        return (type, TagText.Clean(mime), dataOffset, (int)dataLength);
    }
}
