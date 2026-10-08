namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// MP4 and M4A (AAC and Apple Lossless): iTunes-style tags in
/// moov/udta/meta/ilst and the length from mvhd (or mdhd). The moov atom may
/// sit after the audio, so the file is walked atom by atom, skipping the
/// audio by its size.
/// </summary>
internal static class Mp4Tags
{
    private const uint Moov = 0x6D6F6F76;
    private const uint Mvhd = 0x6D766864;
    private const uint Trak = 0x7472616B;
    private const uint Mdia = 0x6D646961;
    private const uint Mdhd = 0x6D646864;
    private const uint Udta = 0x75647461;
    private const uint Meta = 0x6D657461;
    private const uint Ilst = 0x696C7374;
    private const uint Data = 0x64617461;
    private const uint Name = 0xA96E616D;        // ©nam
    private const uint Artist = 0xA9415254;      // ©ART
    private const uint AlbumArtist = 0x61415254; // aART
    private const uint Album = 0xA9616C62;       // ©alb
    private const uint Track = 0x74726B6E;       // trkn
    private const uint Disc = 0x6469736B;        // disk
    private const uint Cover = 0x636F7672;       // covr
    private const int MaxAtoms = 10_000;
    private const int MaxTextBytes = 64 * 1024;

    public static bool IsMp4(ReadOnlySpan<byte> head) => head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8);

    public static void Read(IByteSource file, TagBuilder tags)
    {
        foreach (var atom in Atoms(file, 0, file.Length))
        {
            if (atom.Type == Moov)
            {
                ReadMoov(file, atom, tags);
                return;
            }
        }
    }

    private static void ReadMoov(IByteSource file, Atom moov, TagBuilder tags)
    {
        var trackDuration = TimeSpan.Zero;
        foreach (var child in Atoms(file, moov.Data, moov.End))
        {
            switch (child.Type)
            {
                case Mvhd:
                    tags.SetDuration(HeaderDuration(file, child));
                    break;
                case Trak when trackDuration == TimeSpan.Zero:
                    trackDuration = TrackDuration(file, child);
                    break;
                case Udta:
                    foreach (var meta in Atoms(file, child.Data, child.End))
                    {
                        if (meta.Type == Meta)
                        {
                            ReadMeta(file, meta, tags);
                        }
                    }

                    break;
                case Meta:
                    ReadMeta(file, child, tags);
                    break;
            }
        }

        tags.SetDuration(trackDuration);
    }

    private static TimeSpan TrackDuration(IByteSource file, Atom trak)
    {
        foreach (var mdia in Atoms(file, trak.Data, trak.End))
        {
            if (mdia.Type != Mdia)
            {
                continue;
            }

            foreach (var mdhd in Atoms(file, mdia.Data, mdia.End))
            {
                if (mdhd.Type == Mdhd)
                {
                    return HeaderDuration(file, mdhd);
                }
            }
        }

        return TimeSpan.Zero;
    }

    /// <summary>mvhd and mdhd share the layout: version 1 uses 64-bit times.</summary>
    private static TimeSpan HeaderDuration(IByteSource file, Atom atom)
    {
        var version = file.Exactly(atom.Data, 1)[0];
        uint timescale;
        ulong duration;
        if (version == 1)
        {
            timescale = file.U32BE(atom.Data + 20);
            duration = file.U64BE(atom.Data + 24);
        }
        else
        {
            timescale = file.U32BE(atom.Data + 12);
            duration = file.U32BE(atom.Data + 16);
        }

        return timescale == 0 || duration == 0 || duration == uint.MaxValue || duration == ulong.MaxValue
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)duration / timescale);
    }

    private static void ReadMeta(IByteSource file, Atom meta, TagBuilder tags)
    {
        // An ISO "full box" starts with four bytes of version and flags;
        // QuickTime's meta does not.
        var start = file.U32BE(meta.Data) == 0 ? meta.Data + 4 : meta.Data;
        foreach (var ilst in Atoms(file, start, meta.End))
        {
            if (ilst.Type != Ilst)
            {
                continue;
            }

            foreach (var item in Atoms(file, ilst.Data, ilst.End))
            {
                ReadItem(file, item, tags);
            }
        }
    }

    private static void ReadItem(IByteSource file, Atom item, TagBuilder tags)
    {
        foreach (var data in Atoms(file, item.Data, item.End))
        {
            if (data.Type != Data || data.End - data.Data < 8)
            {
                continue;
            }

            // Version and a 24-bit type, four bytes of locale, then the value.
            var type = file.U32BE(data.Data) & 0x00FFFFFF;
            var value = data.Data + 8;
            var length = data.End - value;
            switch (item.Type)
            {
                case Name:
                    tags.Set(TagField.Title, Text(file, value, length, type));
                    break;
                case Artist:
                    tags.Set(TagField.Artist, Text(file, value, length, type));
                    break;
                case AlbumArtist:
                    tags.Set(TagField.AlbumArtist, Text(file, value, length, type));
                    break;
                case Album:
                    tags.Set(TagField.Album, Text(file, value, length, type));
                    break;
                case Track when length >= 4:
                    tags.TrackNumber ??= Positive(file.U16BE(value + 2));
                    break;
                case Disc when length >= 4:
                    tags.DiscNumber ??= Positive(file.U16BE(value + 2));
                    break;
                case Cover when length > 0 && length <= TagReader.MaxCoverBytes:
                    var mime = type switch { 13 => "image/jpeg", 14 => "image/png", 27 => "image/bmp", _ => null };
                    tags.OfferCover(new CoverRef(value, (int)length, mime), isFront: true);
                    break;
            }

            // Only the first value of each item counts.
            return;
        }
    }

    private static string? Text(IByteSource file, long offset, long length, uint type)
    {
        var bytes = file.Read(offset, (int)Math.Min(length, MaxTextBytes));
        return type == 2 ? TagText.Utf16(bytes, bigEndian: true) : TagText.Utf8Text(bytes);
    }

    private static int? Positive(int value) => value > 0 ? value : null;

    internal static IEnumerable<Atom> Atoms(IByteSource file, long pos, long end)
    {
        for (var i = 0; i < MaxAtoms && pos + 8 <= end; i++)
        {
            long size = file.U32BE(pos);
            var type = file.U32BE(pos + 4);
            var header = 8L;
            if (size == 1)
            {
                size = (long)Math.Min(file.U64BE(pos + 8), long.MaxValue);
                header = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }

            if (size < header || pos + size > end)
            {
                yield break;
            }

            yield return new Atom(type, pos + header, pos + size);
            pos += size;
        }
    }

    internal readonly record struct Atom(uint Type, long Data, long End);
}
