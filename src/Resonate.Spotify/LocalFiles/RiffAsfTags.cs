using System.Text;

namespace Resonate.Spotify.LocalFiles;

/// <summary>WAV: the length from the fmt and data chunks, tags from an id3 chunk or LIST INFO.</summary>
internal static class RiffTags
{
    private const int MaxChunks = 1000;
    private const int MaxTextBytes = 64 * 1024;

    public static bool IsWave(ReadOnlySpan<byte> head) =>
        head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8);

    public static void Read(IByteSource file, TagBuilder tags)
    {
        if (!IsWave(file.Read(0, 12)))
        {
            return;
        }

        uint byteRate = 0;
        var dataSize = 0L;
        long? id3 = null;
        (long Start, long End)? info = null;
        var pos = 12L;
        for (var i = 0; i < MaxChunks && pos + 8 <= file.Length; i++)
        {
            var id = Encoding.ASCII.GetString(file.Exactly(pos, 4));
            var size = file.U32LE(pos + 4);
            var data = pos + 8;
            switch (id)
            {
                case "fmt " when size >= 16:
                    byteRate = file.U32LE(data + 8);
                    break;
                case "data":
                    // Streaming writers leave the size at 0 or all ones.
                    dataSize = size is 0 or uint.MaxValue || data + size > file.Length ? file.Length - data : size;
                    break;
                case "LIST" when size >= 4 && file.Matches(data, "INFO"u8):
                    info = (data + 4, Math.Min(file.Length, data + size));
                    break;
                case "id3 " or "ID3 ":
                    id3 = data;
                    break;
            }

            pos = data + size + (size & 1);
        }

        if (id3 is { } at && MpegTags.Id3v2.TagSize(file, at) > 0)
        {
            MpegTags.Id3v2.Read(file, at, tags);
        }

        if (info is { } list)
        {
            ReadInfo(file, list.Start, list.End, tags);
        }

        if (byteRate > 0 && dataSize > 0)
        {
            tags.SetDuration(TimeSpan.FromSeconds((double)dataSize / byteRate));
        }
    }

    private static void ReadInfo(IByteSource file, long pos, long end, TagBuilder tags)
    {
        for (var i = 0; i < MaxChunks && pos + 8 <= end; i++)
        {
            var id = Encoding.ASCII.GetString(file.Exactly(pos, 4));
            var size = file.U32LE(pos + 4);
            var data = pos + 8;
            TagField? field = id switch
            {
                "INAM" => TagField.Title,
                "IART" => TagField.Artist,
                "IPRD" => TagField.Album,
                "IPRT" or "ITRK" => TagField.TrackNumber,
                _ => null,
            };
            if (field is { } known && size <= MaxTextBytes)
            {
                var text = file.Read(data, (int)size);
                var zero = text.IndexOf((byte)0);
                tags.Set(known, TagText.Latin1(zero >= 0 ? text[..zero] : text));
            }

            pos = data + size + (size & 1);
        }
    }
}

/// <summary>WMA (ASF): title and artist, the extended descriptions (album, track, picture) and the length.</summary>
internal static class AsfTags
{
    private const int MaxObjects = 1000;

    private static readonly Guid HeaderObject = new("75B22630-668E-11CF-A6D9-00AA0062CE6C");
    private static readonly Guid FileProperties = new("8CABDCA1-A947-11CF-8EE4-00C00C205365");
    private static readonly Guid ContentDescription = new("75B22633-668E-11CF-A6D9-00AA0062CE6C");
    private static readonly Guid ExtendedContentDescription = new("D2D0A440-E307-11D2-97F0-00A0C95EA850");

    public static bool IsAsf(ReadOnlySpan<byte> head) => head.Length >= 16 && new Guid(head[..16]) == HeaderObject;

    public static void Read(IByteSource file, TagBuilder tags)
    {
        if (!IsAsf(file.Read(0, 16)))
        {
            return;
        }

        var end = (long)Math.Min(file.U64LE(16), (ulong)file.Length);
        var count = file.U32LE(24);
        var pos = 30L;
        for (var i = 0; i < count && i < MaxObjects && pos + 24 <= end; i++)
        {
            var id = new Guid(file.Exactly(pos, 16));
            var size = (long)Math.Min(file.U64LE(pos + 16), (ulong)long.MaxValue);
            if (size < 24 || pos + size > end)
            {
                break;
            }

            if (id == FileProperties && size >= 88)
            {
                // Play duration in 100 ns units, minus the preroll in milliseconds.
                var duration = TimeSpan.FromTicks((long)Math.Min(file.U64LE(pos + 64), (ulong)long.MaxValue));
                var preroll = TimeSpan.FromMilliseconds(Math.Min(file.U64LE(pos + 80), 3_600_000));
                tags.SetDuration(duration - preroll);
            }
            else if (id == ContentDescription && size >= 34)
            {
                var titleLength = file.U16LE(pos + 24);
                var authorLength = file.U16LE(pos + 26);
                tags.Set(TagField.Title, TagText.Utf16(file.Read(pos + 34, titleLength)));
                tags.Set(TagField.Artist, TagText.Utf16(file.Read(pos + 34 + titleLength, authorLength)));
            }
            else if (id == ExtendedContentDescription)
            {
                ReadExtended(file, pos + 24, pos + size, tags);
            }

            pos += size;
        }
    }

    private static void ReadExtended(IByteSource file, long pos, long end, TagBuilder tags)
    {
        var count = file.U16LE(pos);
        pos += 2;
        for (var i = 0; i < count && pos + 6 <= end; i++)
        {
            var nameLength = file.U16LE(pos);
            var name = TagText.Clean(TagText.Utf16(file.Read(pos + 2, nameLength)));
            var type = file.U16LE(pos + 2 + nameLength);
            var valueLength = file.U16LE(pos + 4 + nameLength);
            var value = pos + 6 + nameLength;
            pos = value + valueLength;
            if (pos > end)
            {
                break;
            }

            switch (name)
            {
                case "WM/AlbumTitle":
                    tags.Set(TagField.Album, StringValue(file, value, valueLength, type));
                    break;
                case "WM/AlbumArtist":
                    tags.Set(TagField.AlbumArtist, StringValue(file, value, valueLength, type));
                    break;
                case "WM/TrackNumber":
                    tags.Set(TagField.TrackNumber, StringValue(file, value, valueLength, type));
                    break;
                case "WM/Track" when type == 3 && valueLength >= 4:
                    // Older files count tracks from zero.
                    tags.TrackNumber ??= (int)file.U32LE(value) + 1;
                    break;
                case "WM/PartOfSet":
                    tags.Set(TagField.DiscNumber, StringValue(file, value, valueLength, type));
                    break;
                case "WM/Picture" when type == 1:
                    ReadPicture(file, value, value + valueLength, tags);
                    break;
            }
        }
    }

    private static string? StringValue(IByteSource file, long offset, int length, ushort type) => type switch
    {
        0 => TagText.Utf16(file.Read(offset, length)),
        3 when length >= 4 => file.U32LE(offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
        5 when length >= 2 => file.U16LE(offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>WM/Picture: picture type, data size, MIME type and description (UTF-16, zero-terminated), then the data.</summary>
    private static void ReadPicture(IByteSource file, long pos, long end, TagBuilder tags)
    {
        var pictureType = file.Exactly(pos, 1)[0];
        var dataLength = file.U32LE(pos + 1);
        pos += 5;
        var mimeEnd = WideTerminatorEnd(file, pos, end);
        if (mimeEnd < 0)
        {
            return;
        }

        var mime = TagText.Clean(TagText.Utf16(file.Read(pos, (int)(mimeEnd - pos))));
        var descriptionEnd = WideTerminatorEnd(file, mimeEnd, end);
        if (descriptionEnd < 0 || descriptionEnd + dataLength > end || dataLength > TagReader.MaxCoverBytes)
        {
            return;
        }

        tags.OfferCover(new CoverRef(descriptionEnd, (int)dataLength, mime), pictureType == FlacPicture.FrontCover);
    }

    private static long WideTerminatorEnd(IByteSource file, long pos, long end)
    {
        for (; pos + 1 < end; pos += 2)
        {
            var pair = file.Exactly(pos, 2);
            if (pair[0] == 0 && pair[1] == 0)
            {
                return pos + 2;
            }
        }

        return -1;
    }
}
