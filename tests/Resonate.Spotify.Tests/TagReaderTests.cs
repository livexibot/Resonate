using System.Text;
using Resonate.Spotify.LocalFiles;
using static Resonate.Spotify.Tests.Fakes.AudioFiles;

namespace Resonate.Spotify.Tests;

public sealed class TagReaderTests
{
    [Fact]
    public void Mp3_with_id3v23_reads_every_field_and_the_xing_length()
    {
        var file = Concat(
            Id3Tag(
                3,
                0,
                Frame23("TIT2", Latin1Text("Glass Tides")),
                Frame23("TPE1", Utf16Text("Mira Sol")),
                Frame23("TPE2", Latin1Text("Various")),
                Frame23("TALB", Latin1Text("Echoes")),
                Frame23("TRCK", Latin1Text("3/12")),
                Frame23("TPOS", Latin1Text("2/2")),
                Frame23("APIC", Apic("image/jpeg", 3, "front", Jpeg))),
            XingFrame(1000),
            MpegFrames(2));

        var tags = Read(file, "song.mp3");

        Assert.Equal("Glass Tides", tags.Title);
        Assert.Equal("Mira Sol", tags.Artist);
        Assert.Equal("Various", tags.AlbumArtist);
        Assert.Equal("Echoes", tags.Album);
        Assert.Equal(3, tags.TrackNumber);
        Assert.Equal(2, tags.DiscNumber);
        Assert.Equal(1000 * 1152 / 44100.0, tags.Duration.TotalSeconds, 2);
        Assert.Equal("image/jpeg", tags.Cover!.MimeType);
        Assert.Equal(Jpeg, ReadCover(file, tags.Cover));
    }

    [Fact]
    public void Constant_bit_rate_mp3_length_comes_from_the_file_size()
    {
        var file = Concat(MpegFrames(100), Id3v1("Old Song", "Old Band", "Old Album", 7));

        var tags = Read(file, "old.mp3");

        // 100 frames of 417 bytes at 128 kbit/s; the ID3v1 tag is not audio.
        Assert.Equal(100 * 417 * 8 / 128_000.0, tags.Duration.TotalSeconds, 3);
        Assert.Equal("Old Song", tags.Title);
        Assert.Equal("Old Band", tags.Artist);
        Assert.Equal("Old Album", tags.Album);
        Assert.Equal(7, tags.TrackNumber);
    }

    [Fact]
    public void Id3v2_wins_over_id3v1_and_v1_fills_the_gaps()
    {
        var file = Concat(Id3Tag(3, 0, Frame23("TIT2", Latin1Text("New Title"))), MpegFrames(3), Id3v1("Old Title", "From v1", "Album v1", 1));

        var tags = Read(file, "a.mp3");

        Assert.Equal("New Title", tags.Title);
        Assert.Equal("From v1", tags.Artist);
        Assert.Equal("Album v1", tags.Album);
    }

    [Fact]
    public void Id3v24_reads_utf8_several_artists_and_an_unsynchronised_picture()
    {
        var picture = Apic("image/png", 3, string.Empty, [.. Png, 0xFF, 0xE0, 0xFF, 0x00, 7]);
        var file = Concat(
            Id3Tag(
                4,
                0,
                Frame24("TIT2", Utf8Text("Señorita")),
                Frame24("TPE1", Utf8Text("Ana\0Ben")),
                Frame24("APIC", Unsynchronise(picture), formatFlags: 0x02)),
            MpegFrames(3));

        var tags = Read(file, "b.mp3");

        Assert.Equal("Señorita", tags.Title);
        Assert.Equal("Ana, Ben", tags.Artist);
        Assert.Equal(CoverEncoding.Unsynchronised, tags.Cover!.Encoding);
        Assert.Equal<byte>([.. Png, 0xFF, 0xE0, 0xFF, 0x00, 7], ReadCover(file, tags.Cover)!);
    }

    [Fact]
    public void Id3v24_frame_sizes_written_as_plain_numbers_still_read()
    {
        // Old iTunes wrote 2.4 sizes as plain numbers: 200 is 0x000000C8, not syncsafe.
        var title = Latin1Text(new string('x', 199));
        var frame = Concat(Ascii("TIT2"), BE32((uint)title.Length), [0, 0], title);
        var file = Concat(Id3Tag(4, 0, frame, Frame24("TALB", Latin1Text("After"))), MpegFrames(3));

        var tags = Read(file, "c.mp3");

        Assert.Equal(new string('x', 199), tags.Title);
        Assert.Equal("After", tags.Album);
    }

    [Fact]
    public void Whole_tag_unsynchronisation_is_undone_and_the_cover_reads_back()
    {
        byte[] image = [.. Jpeg, 0xFF, 0xFF, 0xE2, 0x00];
        var file = Concat(
            Id3TagUnsynchronised(
                Frame23("TIT2", Latin1Text("Ünsync")),
                Frame23("APIC", Apic("image/jpeg", 0, "x", image)),
                Frame23("TALB", Latin1Text("Behind the picture"))),
            MpegFrames(3));

        var tags = Read(file, "d.mp3");

        Assert.Equal("Ünsync", tags.Title);
        Assert.Equal("Behind the picture", tags.Album);
        Assert.Equal(image, ReadCover(file, tags.Cover!));
    }

    [Fact]
    public void Id3v22_reads_three_letter_frames_and_pic()
    {
        var pic = Concat([0], Ascii("PNG"), [3], Ascii("d"), [0], Png);
        var file = Concat(Id3Tag(2, 0, Frame22("TT2", Latin1Text("Twenty Two")), Frame22("TP1", Latin1Text("Band")), Frame22("PIC", pic)), MpegFrames(3));

        var tags = Read(file, "e.mp3");

        Assert.Equal("Twenty Two", tags.Title);
        Assert.Equal("Band", tags.Artist);
        Assert.Equal("image/png", tags.Cover!.MimeType);
        Assert.Equal(Png, ReadCover(file, tags.Cover));
    }

    [Fact]
    public void The_front_cover_wins_over_other_pictures()
    {
        byte[] back = [.. Jpeg, 1];
        byte[] front = [.. Jpeg, 2];
        var file = Concat(Id3Tag(3, 0, Frame23("APIC", Apic("image/jpeg", 4, "back", back)), Frame23("APIC", Apic("image/jpeg", 3, "front", front))), MpegFrames(3));

        var tags = Read(file, "f.mp3");

        Assert.Equal(front, ReadCover(file, tags.Cover!));
    }

    [Fact]
    public void Latin1_frames_holding_utf8_or_windows_quotes_read_as_meant()
    {
        var utf8 = Concat([0], Encoding.UTF8.GetBytes("Café"));
        var quotes = Concat([0], [0x93, (byte)'H', (byte)'i', 0x94]);
        var file = Concat(Id3Tag(3, 0, Frame23("TIT2", utf8), Frame23("TALB", quotes)), MpegFrames(3));

        var tags = Read(file, "g.mp3");

        Assert.Equal("Café", tags.Title);
        Assert.Equal("“Hi”", tags.Album);
    }

    [Fact]
    public void Flac_reads_streaminfo_comments_and_picture()
    {
        var file = Flac(
            44_100,
            44_100L * 180,
            FlacBlock(4, VorbisComment("TITLE=Lossless", "ARTIST=One", "ARTIST=Two", "ALBUM=Hi-Res", "TRACKNUMBER=05", "DISCNUMBER=1/2", "ALBUMARTIST=Both")),
            FlacBlock(6, FlacPictureStructure(3, "image/png", Png), last: true));

        var tags = Read(file, "h.flac");

        Assert.Equal("Lossless", tags.Title);
        Assert.Equal("One, Two", tags.Artist);
        Assert.Equal("Both", tags.AlbumArtist);
        Assert.Equal("Hi-Res", tags.Album);
        Assert.Equal(5, tags.TrackNumber);
        Assert.Equal(1, tags.DiscNumber);
        Assert.Equal(180, tags.Duration.TotalSeconds, 3);
        Assert.Equal(Png, ReadCover(file, tags.Cover!));
    }

    [Fact]
    public void Mp4_with_moov_after_the_audio_reads_ilst_and_mvhd()
    {
        var ilst = Atom(
            "ilst",
            Atom(ItunesType("©nam"), Mp4Data(1, Encoding.UTF8.GetBytes("Apple Song"))),
            Atom(ItunesType("©ART"), Mp4Data(1, Encoding.UTF8.GetBytes("Apple Band"))),
            Atom(ItunesType("aART"), Mp4Data(1, Encoding.UTF8.GetBytes("Album Band"))),
            Atom(ItunesType("©alb"), Mp4Data(1, Encoding.UTF8.GetBytes("Orchard"))),
            Atom(ItunesType("trkn"), Mp4Data(0, [0, 0, 0, 9, 0, 12, 0, 0])),
            Atom(ItunesType("disk"), Mp4Data(0, [0, 0, 0, 2, 0, 2])),
            Atom(ItunesType("covr"), Mp4Data(13, Jpeg)));
        var meta = Atom("meta", new byte[4], Atom("hdlr", new byte[25]), ilst);
        var file = Concat(
            Atom("ftyp", Ascii("M4A "), new byte[4], Ascii("M4A isom")),
            Atom("mdat", new byte[5000]),
            Atom("moov", Mvhd(1000, 215_500), Atom("udta", meta)));

        var tags = Read(file, "i.m4a");

        Assert.Equal("Apple Song", tags.Title);
        Assert.Equal("Apple Band", tags.Artist);
        Assert.Equal("Album Band", tags.AlbumArtist);
        Assert.Equal("Orchard", tags.Album);
        Assert.Equal(9, tags.TrackNumber);
        Assert.Equal(2, tags.DiscNumber);
        Assert.Equal(215.5, tags.Duration.TotalSeconds, 3);
        Assert.Equal("image/jpeg", tags.Cover!.MimeType);
        Assert.Equal(Jpeg, ReadCover(file, tags.Cover));
    }

    [Fact]
    public void Wav_reads_list_info_and_the_length_from_the_data_chunk()
    {
        var format = Concat(LE16(1), LE16(2), LE32(44_100), LE32(1000), LE16(4), LE16(16));
        var info = Concat(Ascii("INFO"), Chunk("INAM", Ascii("Wave Title\0")), Chunk("IART", Ascii("Wave Artist\0")), Chunk("IPRD", Ascii("Wave Album\0")));
        var body = Concat(Ascii("WAVE"), Chunk("fmt ", format), Chunk("LIST", info), Chunk("data", new byte[5000]));
        var file = Concat(Ascii("RIFF"), LE32((uint)body.Length), body);

        var tags = Read(file, "j.wav");

        Assert.Equal("Wave Title", tags.Title);
        Assert.Equal("Wave Artist", tags.Artist);
        Assert.Equal("Wave Album", tags.Album);
        Assert.Equal(5, tags.Duration.TotalSeconds, 3);
    }

    [Fact]
    public void Ogg_vorbis_comments_spanning_pages_and_the_picture_read()
    {
        var identification = Concat([1], Ascii("vorbis"), LE32(0), [2], LE32(44_100), LE32(0), LE32(128_000), LE32(0), [0xB8, 1]);
        var picture = Convert.ToBase64String(FlacPictureStructure(3, "image/png", Png));
        var comment = Concat([3], Ascii("vorbis"), VorbisComment("TITLE=Ogg Song", "ARTIST=Xiph", "METADATA_BLOCK_PICTURE=" + picture, "ALBUM=Free"), [1]);
        var file = Concat(
            OggPages(77, 1, identification, comment),
            OggPage(77, 99, 44_100UL * 200, [10], new byte[10]));

        var tags = Read(file, "k.ogg");

        Assert.Equal("Ogg Song", tags.Title);
        Assert.Equal("Xiph", tags.Artist);
        Assert.Equal("Free", tags.Album);
        Assert.Equal(200, tags.Duration.TotalSeconds, 3);
        Assert.Equal(CoverEncoding.InComment, tags.Cover!.Encoding);
        Assert.Equal(Png, ReadCover(file, tags.Cover));
    }

    [Fact]
    public void Opus_length_leaves_out_the_pre_skip()
    {
        var head = Concat(Ascii("OpusHead"), [1, 2], LE16(312), LE32(48_000), LE16(0), [0]);
        var tags = Concat(Ascii("OpusTags"), VorbisComment("TITLE=Opus Song", "TRACKNUMBER=4"));
        var file = Concat(OggPages(5, 255, head, tags), OggPage(5, 2, (48_000UL * 60) + 312, [3], new byte[3]));

        var read = Read(file, "l.opus");

        Assert.Equal("Opus Song", read.Title);
        Assert.Equal(4, read.TrackNumber);
        Assert.Equal(60, read.Duration.TotalSeconds, 3);
    }

    [Fact]
    public void Wma_reads_the_asf_header()
    {
        const string fileProperties = "8CABDCA1-A947-11CF-8EE4-00C00C205365";
        const string content = "75B22633-668E-11CF-A6D9-00AA0062CE6C";
        const string extended = "D2D0A440-E307-11D2-97F0-00A0C95EA850";
        var title = Utf16Z("Windows Song");
        var author = Utf16Z("Media Band");
        var properties = AsfObject(fileProperties, new byte[16], LE64(0), LE64(0), LE64(0), LE64((183UL + 3) * 10_000_000), LE64(0), LE64(3000), new byte[16]);
        var description = AsfObject(content, LE16((ushort)title.Length), LE16((ushort)author.Length), LE16(0), LE16(0), LE16(0), title, author);
        var picture = Concat([3], LE32((uint)Jpeg.Length), Utf16Z("image/jpeg"), Utf16Z(string.Empty), Jpeg);
        var extendedObject = AsfObject(
            extended,
            LE16(3),
            Descriptor("WM/AlbumTitle", 0, Utf16Z("Media Album")),
            Descriptor("WM/TrackNumber", 3, LE32(6)),
            Descriptor("WM/Picture", 1, picture));
        var children = Concat(properties, description, extendedObject);
        var header = Concat(new Guid("75B22630-668E-11CF-A6D9-00AA0062CE6C").ToByteArray(), LE64((ulong)(30 + children.Length)), LE32(3), [1, 2], children);
        var file = Concat(header, new byte[100]);

        var tags = Read(file, "m.wma");

        Assert.Equal("Windows Song", tags.Title);
        Assert.Equal("Media Band", tags.Artist);
        Assert.Equal("Media Album", tags.Album);
        Assert.Equal(6, tags.TrackNumber);
        Assert.Equal(183, tags.Duration.TotalSeconds, 3);
        Assert.Equal(Jpeg, ReadCover(file, tags.Cover!));

        static byte[] Descriptor(string name, ushort type, byte[] value)
        {
            var nameBytes = Utf16Z(name);
            return Concat(LE16((ushort)nameBytes.Length), nameBytes, LE16(type), LE16((ushort)value.Length), value);
        }
    }

    [Fact]
    public void A_file_without_tags_is_named_after_the_file()
    {
        var tags = Read(new byte[100], @"Music/Some Artist - Some Song.mp3");

        Assert.Equal("Some Artist - Some Song", tags.Title);
        Assert.Null(tags.Artist);
        Assert.Equal(TimeSpan.Zero, tags.Duration);
        Assert.Null(tags.Cover);
    }

    [Fact]
    public void A_damaged_header_keeps_what_was_read_and_never_throws()
    {
        var whole = Concat(Id3Tag(3, 0, Frame23("TIT2", Latin1Text("Survivor")), Frame23("TALB", Latin1Text("Lost"))), MpegFrames(2));
        var cut = whole[..30];

        var tags = Read(cut, "cut.mp3");

        Assert.Equal("Survivor", tags.Title);
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(1234);
        foreach (var name in new[] { "x.mp3", "x.flac", "x.m4a", "x.ogg", "x.wav", "x.wma", "x.aac" })
        {
            for (var i = 0; i < 200; i++)
            {
                var bytes = new byte[random.Next(0, 600)];
                random.NextBytes(bytes);

                // Sometimes start like a real file so the parsers go deeper.
                var prefix = (i % 6) switch
                {
                    0 => Ascii("ID3\u0003\0\0"),
                    1 => Ascii("fLaC"),
                    2 => Concat(BE32(24), Ascii("ftyp")),
                    3 => Ascii("OggS"),
                    4 => Concat(Ascii("RIFF"), LE32(500), Ascii("WAVE")),
                    _ => [],
                };
                var file = Concat(prefix, bytes);

                var tags = Read(file, name);

                Assert.NotNull(tags.Title);
                _ = tags.Cover is { } cover ? ReadCover(file, cover) : null;
            }
        }
    }

    [Theory]
    [InlineData("a.MP3", true)]
    [InlineData("b.flac", true)]
    [InlineData("c.m4a", true)]
    [InlineData("d.opus", true)]
    [InlineData("e.wma", true)]
    [InlineData("f.jpg", false)]
    [InlineData("g.m4p", false)]
    [InlineData("noextension", false)]
    public void Only_music_files_are_listed(string name, bool supported) =>
        Assert.Equal(supported, TagReader.IsSupported(name));

    private static AudioTags Read(byte[] file, string name)
    {
        using var stream = new MemoryStream(file, writable: false);
        return TagReader.Read(stream, name);
    }

    private static byte[]? ReadCover(byte[] file, CoverRef cover)
    {
        using var stream = new MemoryStream(file, writable: false);
        return TagReader.ReadCover(stream, cover);
    }
}
