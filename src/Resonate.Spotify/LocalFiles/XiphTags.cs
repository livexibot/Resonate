namespace Resonate.Spotify.LocalFiles;

/// <summary>FLAC: STREAMINFO for the length, VORBIS_COMMENT for the tags, PICTURE for the cover.</summary>
internal static class FlacTags
{
    private const int MaxBlocks = 512;

    public static void Read(IByteSource file, TagBuilder tags)
    {
        // Some taggers put an ID3v2 tag in front of the stream. It only fills
        // what the FLAC comments leave out.
        var id3 = MpegTags.Id3v2.TagSize(file, 0);
        var pos = id3;
        if (!file.Matches(pos, "fLaC"u8))
        {
            return;
        }

        pos += 4;
        for (var i = 0; i < MaxBlocks && pos + 4 <= file.Length; i++)
        {
            var header = file.Exactly(pos, 4);
            var last = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7F;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            var data = pos + 4;
            var end = data + length;
            if (end > file.Length)
            {
                break;
            }

            switch (type)
            {
                case 0 when length >= 18:
                    ReadStreamInfo(file.Exactly(data, 18), tags);
                    break;
                case 4:
                    VorbisComments.Read(file, data, end, tags);
                    break;
                case 6:
                    if (FlacPicture.Parse(file, data, end) is { } picture && picture.Mime != "-->")
                    {
                        tags.OfferCover(new CoverRef(picture.DataOffset, picture.DataLength, picture.Mime), picture.Type == FlacPicture.FrontCover);
                    }

                    break;
            }

            pos = end;
            if (last)
            {
                break;
            }
        }

        if (id3 > 0)
        {
            MpegTags.Id3v2.Read(file, 0, tags);
        }
    }

    private static void ReadStreamInfo(ReadOnlySpan<byte> b, TagBuilder tags)
    {
        // 20 bits of sample rate, 3 of channels, 5 of bits per sample, 36 of total samples.
        var sampleRate = (b[10] << 12) | (b[11] << 4) | (b[12] >> 4);
        var samples = ((long)(b[13] & 0x0F) << 32) | ((long)b[14] << 24) | ((long)b[15] << 16) | ((long)b[16] << 8) | b[17];
        if (sampleRate > 0 && samples > 0)
        {
            tags.SetDuration(TimeSpan.FromSeconds((double)samples / sampleRate));
        }
    }
}

/// <summary>
/// Ogg Vorbis and Opus: the identification and comment packets at the start,
/// and the length from the last page's granule position. Packets can span
/// pages, so they are read through a map of where each piece is.
/// </summary>
internal static class OggTags
{
    private const int MaxPages = 20_000;
    private const int TailBytes = 64 * 1024;

    public static void Read(IByteSource file, TagBuilder tags)
    {
        if (Open(file) is not { } stream)
        {
            return;
        }

        VorbisComments.Read(stream.Comment, stream.CommentStart, stream.Comment.Length, tags, (offset, length) =>
        {
            // The picture's type is in its first four bytes: the first eight base64 characters.
            var type = 0;
            if (length >= 8)
            {
                var head = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(stream.Comment.Read(offset, 8)));
                type = head.Length < 4 ? 0 : (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
            }

            tags.OfferCover(new CoverRef(0, 0, null, CoverEncoding.InComment), type == FlacPicture.FrontCover);
        });

        if (LastGranule(file, stream.Serial) is { } granule && stream.SampleRate > 0)
        {
            var samples = (double)granule - stream.PreSkip;
            tags.SetDuration(TimeSpan.FromSeconds(Math.Max(0, samples) / stream.SampleRate));
        }
    }

    /// <summary>The cover stored in the comments (METADATA_BLOCK_PICTURE), or null.</summary>
    public static byte[]? ReadCover(IByteSource file)
    {
        if (Open(file) is not { } stream)
        {
            return null;
        }

        (long Offset, int Length)? best = null;
        var bestIsFront = false;
        VorbisComments.Read(stream.Comment, stream.CommentStart, stream.Comment.Length, new TagBuilder(), (offset, length) =>
        {
            var isFront = false;
            if (length >= 8)
            {
                var head = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(stream.Comment.Read(offset, 8)));
                isFront = head.Length >= 4 && ((head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3]) == FlacPicture.FrontCover;
            }

            if (best is null || (isFront && !bestIsFront))
            {
                best = (offset, length);
                bestIsFront = isFront;
            }
        });

        if (best is not { } picture || picture.Length > TagReader.MaxCoverBytes * 4 / 3 + 4)
        {
            return null;
        }

        var structure = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(stream.Comment.Read(picture.Offset, picture.Length)));
        var memory = new MemorySource(structure);
        return FlacPicture.Parse(memory, 0, structure.Length) is { } parsed
            ? structure.AsSpan((int)parsed.DataOffset, parsed.DataLength).ToArray()
            : null;
    }

    private static OggStream? Open(IByteSource file)
    {
        var packets = FirstPackets(file, 2, out var serial);
        if (packets.Count < 2)
        {
            return null;
        }

        var identification = new PacketSource(file, packets[0]);
        var comment = new PacketSource(file, packets[1]);
        if (identification.Matches(0, "\u0001vorbis"u8) && comment.Matches(0, "\u0003vorbis"u8))
        {
            return new OggStream(serial, identification.U32LE(12), 0, comment, 7);
        }

        if (identification.Matches(0, "OpusHead"u8) && comment.Matches(0, "OpusTags"u8))
        {
            // Opus always counts at 48 kHz, after skipping the encoder's pre-skip.
            return new OggStream(serial, 48_000, identification.U16LE(10), comment, 8);
        }

        return null;
    }

    /// <summary>Where the pieces of the first <paramref name="count"/> packets of the first stream are.</summary>
    private static List<List<(long Offset, int Length)>> FirstPackets(IByteSource file, int count, out uint serial)
    {
        var packets = new List<List<(long Offset, int Length)>>();
        var current = new List<(long Offset, int Length)>();
        uint? first = null;
        var pos = 0L;
        for (var page = 0; page < MaxPages && pos + 27 <= file.Length; page++)
        {
            var header = file.Exactly(pos, 27);
            if (!header[..4].SequenceEqual("OggS"u8))
            {
                break;
            }

            var pageSerial = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
            var segments = header[26];
            var table = file.Exactly(pos + 27, segments).ToArray();
            var data = pos + 27 + segments;
            first ??= pageSerial;
            if (pageSerial != first)
            {
                pos = data + table.Sum(b => (long)b);
                continue;
            }

            foreach (var lacing in table)
            {
                if (lacing > 0)
                {
                    if (current.Count > 0 && current[^1].Offset + current[^1].Length == data)
                    {
                        current[^1] = (current[^1].Offset, current[^1].Length + lacing);
                    }
                    else
                    {
                        current.Add((data, lacing));
                    }
                }

                data += lacing;
                if (lacing < 255)
                {
                    packets.Add(current);
                    current = [];
                    if (packets.Count == count)
                    {
                        serial = first.Value;
                        return packets;
                    }
                }
            }

            pos = data;
        }

        serial = first ?? 0;
        return packets;
    }

    private static ulong? LastGranule(IByteSource file, uint serial)
    {
        var start = Math.Max(0, file.Length - TailBytes);
        var tail = file.Copy(start, (int)(file.Length - start));
        for (var i = tail.Length - 27; i >= 0; i--)
        {
            if (tail[i] != 'O' || !tail.AsSpan(i, 4).SequenceEqual("OggS"u8) || tail[i + 4] != 0)
            {
                continue;
            }

            var granule = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(i + 6, 8));
            var pageSerial = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 14, 4));
            if (pageSerial == serial && granule != ulong.MaxValue)
            {
                return granule;
            }
        }

        return null;
    }

    private sealed record OggStream(uint Serial, long SampleRate, long PreSkip, PacketSource Comment, int CommentStart);

    /// <summary>A packet read as one block, though its pieces are spread over pages.</summary>
    private sealed class PacketSource : IByteSource
    {
        private readonly IByteSource _file;
        private readonly List<(long Offset, int Length)> _pieces;
        private readonly long[] _starts;
        private byte[] _joined = [];

        public PacketSource(IByteSource file, List<(long Offset, int Length)> pieces)
        {
            _file = file;
            _pieces = pieces;
            _starts = new long[pieces.Count];
            var total = 0L;
            for (var i = 0; i < pieces.Count; i++)
            {
                _starts[i] = total;
                total += pieces[i].Length;
            }

            Length = total;
        }

        public long Length { get; }

        public ReadOnlySpan<byte> Read(long offset, int count)
        {
            if (offset < 0 || count <= 0 || offset >= Length)
            {
                return [];
            }

            count = (int)Math.Min(count, Length - offset);
            var index = Array.BinarySearch(_starts, offset);
            if (index < 0)
            {
                index = ~index - 1;
            }

            var within = offset - _starts[index];
            if (within + count <= _pieces[index].Length)
            {
                return _file.Read(_pieces[index].Offset + within, count);
            }

            // Across pages: join the pieces.
            if (_joined.Length < count)
            {
                _joined = new byte[count];
            }

            var copied = 0;
            while (copied < count && index < _pieces.Count)
            {
                var piece = _pieces[index];
                var take = (int)Math.Min(piece.Length - within, count - copied);
                var bytes = _file.Read(piece.Offset + within, take);
                bytes.CopyTo(_joined.AsSpan(copied));
                copied += bytes.Length;
                if (bytes.Length < take)
                {
                    break;
                }

                index++;
                within = 0;
            }

            return _joined.AsSpan(0, copied);
        }
    }
}
