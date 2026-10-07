using System.Text;

namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// MP3 and AAC (ADTS) files: ID3v2.2, 2.3 and 2.4 tags at the start, ID3v1
/// at the end, and the length from the first audio frame (Xing, Info or
/// VBRI header, or the bit rate for constant bit rate files).
/// </summary>
internal static class MpegTags
{
    private const int MaxTagBytes = 64 * 1024 * 1024;
    private const int MaxTextBytes = 64 * 1024;
    private const int SyncSearchBytes = 256 * 1024;

    private static readonly int[] Mpeg1Layer1 = [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448];
    private static readonly int[] Mpeg1Layer2 = [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384];
    private static readonly int[] Mpeg1Layer3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Layer1 = [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256];
    private static readonly int[] Mpeg2Layer23 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] AdtsRates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

    public static void Read(IByteSource file, TagBuilder tags)
    {
        // Some files carry more than one ID3v2 tag in a row; the first one wins.
        var audioStart = 0L;
        for (var i = 0; i < 4; i++)
        {
            var size = Id3v2.TagSize(file, audioStart);
            if (size == 0)
            {
                break;
            }

            Id3v2.Read(file, audioStart, tags);
            audioStart += size;
        }

        var audioEnd = ReadTrailingTags(file, tags);
        tags.SetDuration(Duration(file, audioStart, audioEnd));
    }

    /// <summary>Reads ID3v1 and returns where the audio ends (before ID3v1 and APEv2).</summary>
    private static long ReadTrailingTags(IByteSource file, TagBuilder tags)
    {
        var end = file.Length;
        if (end >= 128 && file.Matches(end - 128, "TAG"u8))
        {
            var v1 = file.Copy(end - 128, 128);
            tags.Set(TagField.Title, Id3v1Text(v1.AsSpan(3, 30)));
            tags.Set(TagField.Artist, Id3v1Text(v1.AsSpan(33, 30)));
            tags.Set(TagField.Album, Id3v1Text(v1.AsSpan(63, 30)));
            if (v1[125] == 0 && v1[126] != 0)
            {
                tags.TrackNumber ??= v1[126];
            }

            end -= 128;
        }

        // An APEv2 tag ends with a 32-byte footer that gives its size.
        if (end >= 32 && file.Matches(end - 32, "APETAGEX"u8))
        {
            var size = file.U32LE(end - 32 + 12);
            var hasHeader = (file.U32LE(end - 32 + 20) & 0x80000000) != 0;
            var total = size + (hasHeader ? 32L : 0);
            if (total <= end)
            {
                end -= total;
            }
        }

        return end;
    }

    private static string Id3v1Text(ReadOnlySpan<byte> field)
    {
        var zero = field.IndexOf((byte)0);
        return TagText.Latin1(zero >= 0 ? field[..zero] : field);
    }

    private static TimeSpan Duration(IByteSource file, long start, long end)
    {
        var limit = Math.Min(end - 4, start + SyncSearchBytes);
        for (var pos = start; pos < limit; pos++)
        {
            var window = file.Read(pos, 4);
            if (window.Length < 4 || window[0] != 0xFF)
            {
                continue;
            }

            if ((window[1] & 0xF6) == 0xF0)
            {
                if (AdtsDuration(file, pos, end) is { } adts)
                {
                    return adts;
                }

                continue;
            }

            if (MpegFrame.Parse(window) is not { } frame)
            {
                continue;
            }

            // A real frame is followed by another one (unless the file ends).
            var next = pos + frame.Length;
            if (next + 4 <= end && MpegFrame.Parse(file.Read(next, 4)) is null)
            {
                continue;
            }

            return MpegDuration(file, pos, end, frame);
        }

        return TimeSpan.Zero;
    }

    private static TimeSpan MpegDuration(IByteSource file, long pos, long end, MpegFrame frame)
    {
        // Xing or Info (LAME) after the side information, or VBRI 32 bytes in.
        var sideInfo = frame.IsMpeg1 ? (frame.IsMono ? 17 : 32) : (frame.IsMono ? 9 : 17);
        var xing = pos + 4 + sideInfo;
        if (file.Matches(xing, "Xing"u8) || file.Matches(xing, "Info"u8))
        {
            var flags = file.U32BE(xing + 4);
            if ((flags & 1) != 0)
            {
                var frames = file.U32BE(xing + 8);
                return Seconds((double)frames * frame.SamplesPerFrame / frame.SampleRate);
            }
        }

        var vbri = pos + 4 + 32;
        if (file.Matches(vbri, "VBRI"u8))
        {
            var frames = file.U32BE(vbri + 14);
            return Seconds((double)frames * frame.SamplesPerFrame / frame.SampleRate);
        }

        return Seconds((end - pos) * 8.0 / (frame.BitrateKbps * 1000.0));
    }

    /// <summary>The length of an ADTS (raw AAC) stream, from the average size of its first frames.</summary>
    private static TimeSpan? AdtsDuration(IByteSource file, long start, long end)
    {
        var header = file.Read(start, 7);
        if (header.Length < 7)
        {
            return null;
        }

        var rateIndex = (header[2] >> 2) & 0x0F;
        if (rateIndex >= AdtsRates.Length)
        {
            return null;
        }

        var rate = AdtsRates[rateIndex];
        var pos = start;
        var frames = 0;
        while (frames < 256 && pos + 7 <= end)
        {
            var h = file.Read(pos, 7);
            if (h.Length < 7 || h[0] != 0xFF || (h[1] & 0xF6) != 0xF0)
            {
                break;
            }

            var length = ((h[3] & 0x03) << 11) | (h[4] << 3) | (h[5] >> 5);
            if (length < 7)
            {
                break;
            }

            pos += length;
            frames++;
        }

        if (frames < 2)
        {
            return null;
        }

        var averageFrame = (double)(pos - start) / frames;
        var totalFrames = (end - start) / averageFrame;
        return Seconds(totalFrames * 1024 / rate);
    }

    private static TimeSpan Seconds(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

    private readonly record struct MpegFrame(bool IsMpeg1, bool IsMono, int BitrateKbps, int SampleRate, int SamplesPerFrame, int Length)
    {
        public static MpegFrame? Parse(ReadOnlySpan<byte> h)
        {
            if (h.Length < 4 || h[0] != 0xFF || (h[1] & 0xE0) != 0xE0)
            {
                return null;
            }

            var version = (h[1] >> 3) & 0x03; // 0: MPEG 2.5, 2: MPEG 2, 3: MPEG 1
            var layer = (h[1] >> 1) & 0x03;   // 1: III, 2: II, 3: I
            var bitrateIndex = (h[2] >> 4) & 0x0F;
            var rateIndex = (h[2] >> 2) & 0x03;
            if (version == 1 || layer == 0 || bitrateIndex is 0 or 15 || rateIndex == 3)
            {
                return null;
            }

            var mpeg1 = version == 3;
            var table = (mpeg1, layer) switch
            {
                (true, 3) => Mpeg1Layer1,
                (true, 2) => Mpeg1Layer2,
                (true, _) => Mpeg1Layer3,
                (false, 3) => Mpeg2Layer1,
                _ => Mpeg2Layer23,
            };
            var bitrate = table[bitrateIndex];
            var rate = rateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 };
            rate = version switch { 3 => rate, 2 => rate / 2, _ => rate / 4 };
            var padding = (h[2] >> 1) & 1;
            var samples = layer switch { 3 => 384, 2 => 1152, _ => mpeg1 ? 1152 : 576 };
            var length = layer == 3
                ? ((12 * bitrate * 1000 / rate) + padding) * 4
                : (samples / 8 * bitrate * 1000 / rate) + padding;
            return length < 4 ? null : new MpegFrame(mpeg1, (h[3] >> 6) == 3, bitrate, rate, samples, length);
        }
    }

    /// <summary>ID3v2 tags (versions 2.2, 2.3 and 2.4).</summary>
    internal static class Id3v2
    {
        /// <summary>The whole tag's size (header, frames and footer), or 0 when there is no tag at <paramref name="offset"/>.</summary>
        public static long TagSize(IByteSource file, long offset)
        {
            var h = file.Read(offset, 10);
            if (h.Length < 10 || h[0] != 'I' || h[1] != 'D' || h[2] != '3' || h[3] is < 2 or > 4 || !IsSyncSafe(h[6..10]))
            {
                return 0;
            }

            var footer = h[3] == 4 && (h[5] & 0x10) != 0 ? 10 : 0;
            return 10 + SyncSafe(h[6..10]) + footer;
        }

        public static void Read(IByteSource file, long offset, TagBuilder tags)
        {
            var h = file.Exactly(offset, 10);
            var major = h[3];
            var flags = h[5];
            var size = SyncSafe(h[6..10]);
            var bodyStart = offset + 10;
            var bodyEnd = Math.Min(file.Length, bodyStart + size);

            // Version 2.2 compression was never defined; such tags are skipped.
            if (major == 2 && (flags & 0x40) != 0)
            {
                return;
            }

            if ((flags & 0x80) != 0 && major < 4)
            {
                // The whole tag is unsynchronised: undo it in memory, keeping a
                // map back to the file for the cover's position.
                if (bodyEnd - bodyStart > MaxTagBytes)
                {
                    return;
                }

                var removed = new List<int>();
                var body = Unsync.Decode(file.Read(bodyStart, (int)(bodyEnd - bodyStart)), removed);
                var memory = new MemorySource(body);
                var start = major == 3 ? SkipExtendedHeader(memory, 0, flags, major) : 0;
                ReadFrames(memory, start, body.Length, major, tags, unsyncAll: false, coverAt: (dataStart, dataEnd, mime) =>
                {
                    var rawStart = bodyStart + Unsync.RawIndex(removed, (int)dataStart);
                    var rawEnd = bodyStart + Unsync.RawIndex(removed, (int)dataEnd - 1) + 1;
                    return new CoverRef(rawStart, (int)(rawEnd - rawStart), mime, CoverEncoding.Unsynchronised);
                });
                return;
            }

            var framesStart = SkipExtendedHeader(file, bodyStart, flags, major);
            ReadFrames(file, framesStart, bodyEnd, major, tags, unsyncAll: major == 4 && (flags & 0x80) != 0, coverAt: null);
        }

        private static long SkipExtendedHeader(IByteSource source, long pos, byte flags, int major)
        {
            if ((flags & 0x40) == 0 || major < 3)
            {
                return pos;
            }

            // 2.3: the size leaves out its own four bytes; 2.4: syncsafe, and includes them.
            return major == 3
                ? pos + 4 + source.U32BE(pos)
                : pos + SyncSafe(source.Exactly(pos, 4));
        }

        /// <param name="coverAt">Turns a picture's data range into a cover reference, for tags read from memory.</param>
        private static void ReadFrames(
            IByteSource source,
            long pos,
            long end,
            int major,
            TagBuilder tags,
            bool unsyncAll,
            Func<long, long, string?, CoverRef>? coverAt)
        {
            var headerSize = major == 2 ? 6 : 10;
            var artists = new List<string>();
            var albumArtists = new List<string>();
            while (pos + headerSize <= end)
            {
                var h = source.Exactly(pos, headerSize);
                if (h[0] == 0)
                {
                    break; // padding
                }

                var idLength = major == 2 ? 3 : 4;
                if (!IsFrameId(h[..idLength]))
                {
                    break;
                }

                var id = Encoding.ASCII.GetString(h[..idLength]);
                long size;
                byte formatFlags = 0;
                if (major == 2)
                {
                    size = (h[3] << 16) | (h[4] << 8) | h[5];
                }
                else if (major == 3)
                {
                    size = ((long)h[4] << 24) | ((long)h[5] << 16) | ((long)h[6] << 8) | h[7];
                    formatFlags = h[9];
                }
                else
                {
                    // Flags first: finding the size can read further on, which
                    // refills the buffer h points into.
                    formatFlags = h[9];
                    size = V24FrameSize(source, pos, end, h);
                }

                var dataStart = pos + headerSize;
                var dataEnd = dataStart + size;
                pos = dataEnd;
                if (dataEnd > end)
                {
                    break;
                }

                if (size <= 0)
                {
                    continue;
                }

                var unsync = unsyncAll || (major == 4 && (formatFlags & 0x02) != 0);
                if (major == 3)
                {
                    if ((formatFlags & 0xC0) != 0)
                    {
                        continue; // compressed or encrypted
                    }

                    dataStart += (formatFlags & 0x20) != 0 ? 1 : 0;
                }
                else if (major == 4)
                {
                    if ((formatFlags & 0x0C) != 0)
                    {
                        continue; // compressed or encrypted
                    }

                    dataStart += ((formatFlags & 0x40) != 0 ? 1 : 0) + ((formatFlags & 0x01) != 0 ? 4 : 0);
                }

                if (dataStart >= dataEnd)
                {
                    continue;
                }

                switch (id)
                {
                    case "TIT2" or "TT2":
                        tags.Set(TagField.Title, FirstValue(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TPE1" or "TP1":
                        artists.AddRange(Values(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TPE2" or "TP2":
                        albumArtists.AddRange(Values(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TALB" or "TAL":
                        tags.Set(TagField.Album, FirstValue(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TRCK" or "TRK":
                        tags.Set(TagField.TrackNumber, FirstValue(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TPOS" or "TPA":
                        tags.Set(TagField.DiscNumber, FirstValue(ReadText(source, dataStart, dataEnd, unsync)));
                        break;
                    case "TLEN" or "TLE":
                        if (TagText.Number(FirstValue(ReadText(source, dataStart, dataEnd, unsync))) is { } ms)
                        {
                            tags.SetDuration(TimeSpan.FromMilliseconds(ms));
                        }

                        break;
                    case "APIC" or "PIC":
                        ReadPicture(source, dataStart, dataEnd, major, unsync, tags, coverAt);
                        break;
                }
            }

            tags.Set(TagField.Artist, string.Join(", ", artists));
            tags.Set(TagField.AlbumArtist, string.Join(", ", albumArtists));
        }

        /// <summary>
        /// 2.4 sizes are syncsafe, but some taggers (old iTunes among them)
        /// wrote plain numbers. Use whichever leads to the next frame.
        /// </summary>
        private static long V24FrameSize(IByteSource source, long pos, long end, ReadOnlySpan<byte> h)
        {
            var plain = ((long)h[4] << 24) | ((long)h[5] << 16) | ((long)h[6] << 8) | h[7];
            if (!IsSyncSafe(h[4..8]))
            {
                return plain;
            }

            var syncSafe = SyncSafe(h[4..8]);
            if (syncSafe == plain || LooksLikeFrameStart(source, pos + 10 + syncSafe, end))
            {
                return syncSafe;
            }

            return LooksLikeFrameStart(source, pos + 10 + plain, end) ? plain : syncSafe;
        }

        private static bool LooksLikeFrameStart(IByteSource source, long pos, long end)
        {
            if (pos == end)
            {
                return true;
            }

            if (pos > end)
            {
                return false;
            }

            var id = source.Read(pos, 4);
            return id.Length == 0 || id[0] == 0 || (id.Length == 4 && IsFrameId(id));
        }

        private static string ReadText(IByteSource source, long start, long end, bool unsync)
        {
            var length = (int)Math.Min(end - start, MaxTextBytes);
            ReadOnlySpan<byte> data = source.Read(start, length).ToArray();
            if (unsync)
            {
                data = Unsync.Decode(data);
            }

            if (data.Length < 1)
            {
                return string.Empty;
            }

            var text = data[1..];
            return data[0] switch
            {
                1 => TagText.Utf16(text),
                2 => TagText.Utf16(text, bigEndian: true),
                3 => TagText.Utf8Text(text),
                _ => TagText.Latin1(text),
            };
        }

        /// <summary>2.4 separates several values with a zero character.</summary>
        private static IEnumerable<string> Values(string text) =>
            text.Split('\0').Select(TagText.Clean).OfType<string>();

        private static string? FirstValue(string text) => Values(text).FirstOrDefault();

        private static void ReadPicture(
            IByteSource source,
            long start,
            long end,
            int major,
            bool unsync,
            TagBuilder tags,
            Func<long, long, string?, CoverRef>? coverAt)
        {
            // Encoding, MIME type (or a three-letter format in 2.2), picture
            // type, description, then the picture. Only the part before the
            // picture is read now.
            var rawHead = source.Read(start, (int)Math.Min(end - start, 4096)).ToArray();
            var head = unsync ? Unsync.Decode(rawHead) : rawHead;
            if (head.Length < 4)
            {
                return;
            }

            var encoding = head[0];
            int pos;
            string? mime;
            if (major == 2)
            {
                var format = Encoding.ASCII.GetString(head, 1, 3).ToUpperInvariant();
                mime = format switch { "PNG" => "image/png", "JPG" => "image/jpeg", _ => null };
                pos = 4;
            }
            else
            {
                var zero = Array.IndexOf(head, (byte)0, 1);
                if (zero < 0)
                {
                    return;
                }

                mime = TagText.Clean(Encoding.ASCII.GetString(head, 1, zero - 1));
                pos = zero + 1;
            }

            if (pos >= head.Length)
            {
                return;
            }

            var pictureType = head[pos++];
            var descriptionEnd = TerminatorEnd(head, pos, wide: encoding is 1 or 2);
            if (descriptionEnd < 0)
            {
                return;
            }

            if (mime is not null && !mime.Contains('/', StringComparison.Ordinal))
            {
                mime = "image/" + mime.ToLowerInvariant().Replace("jpg", "jpeg", StringComparison.Ordinal);
            }

            CoverRef cover;
            if (coverAt is not null)
            {
                cover = coverAt(start + descriptionEnd, end, mime);
            }
            else if (unsync)
            {
                var rawOffset = Unsync.RawLength(rawHead, descriptionEnd);
                cover = new CoverRef(start + rawOffset, (int)(end - start - rawOffset), mime, CoverEncoding.Unsynchronised);
            }
            else
            {
                cover = new CoverRef(start + descriptionEnd, (int)(end - start - descriptionEnd), mime);
            }

            if (cover.Length <= TagReader.MaxCoverBytes)
            {
                tags.OfferCover(cover, pictureType == FlacPicture.FrontCover);
            }
        }

        /// <summary>The index just after a zero terminator (two zero bytes, aligned, for UTF-16), or -1.</summary>
        private static int TerminatorEnd(byte[] data, int start, bool wide)
        {
            if (!wide)
            {
                var zero = Array.IndexOf(data, (byte)0, start);
                return zero < 0 ? -1 : zero + 1;
            }

            for (var i = start; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    return i + 2;
                }
            }

            return -1;
        }

        private static bool IsFrameId(ReadOnlySpan<byte> id)
        {
            foreach (var c in id)
            {
                if (c is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSyncSafe(ReadOnlySpan<byte> bytes) => ((bytes[0] | bytes[1] | bytes[2] | bytes[3]) & 0x80) == 0;

        private static long SyncSafe(ReadOnlySpan<byte> bytes) =>
            ((long)(bytes[0] & 0x7F) << 21) | ((long)(bytes[1] & 0x7F) << 14) | ((long)(bytes[2] & 0x7F) << 7) | (long)(bytes[3] & 0x7F);
    }
}
