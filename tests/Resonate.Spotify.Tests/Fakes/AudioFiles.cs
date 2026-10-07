using System.Buffers.Binary;
using System.Text;

namespace Resonate.Spotify.Tests.Fakes;

/// <summary>
/// Builds small music files in memory, byte by byte as each format's
/// specification lays them out, so the tag reader is tested without binary
/// fixtures. The audio itself is silence or zeros; only headers matter.
/// </summary>
internal static class AudioFiles
{
    /// <summary>The start of a JPEG (enough for the tag reader, which never decodes pictures).</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0xFF, 0x00, 0xFF, 0xD9];

    public static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] BE32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] LE32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] LE16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] LE64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }

    public static byte[] BE64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public static byte[] SyncSafe(int value) =>
        [(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];

    // ---- ID3v2 ----

    public static byte[] Id3Tag(int major, byte flags, params byte[][] frames)
    {
        var body = Concat([.. frames, new byte[16]]);
        return Concat([(byte)'I', (byte)'D', (byte)'3', (byte)major, 0, flags], SyncSafe(body.Length), body);
    }

    /// <summary>A whole-tag unsynchronised ID3v2.3 tag (flag 0x80): 0xFF 0x00 stands for 0xFF in the body.</summary>
    public static byte[] Id3TagUnsynchronised(params byte[][] frames)
    {
        var body = Unsynchronise(Concat([.. frames, new byte[16]]));
        return Concat([(byte)'I', (byte)'D', (byte)'3', 3, 0, 0x80], SyncSafe(body.Length), body);
    }

    public static byte[] Frame23(string id, byte[] data) => Concat(Ascii(id), BE32((uint)data.Length), [0, 0], data);

    public static byte[] Frame24(string id, byte[] data, byte formatFlags = 0) => Concat(Ascii(id), SyncSafe(data.Length), [0, formatFlags], data);

    public static byte[] Frame22(string id, byte[] data) =>
        Concat(Ascii(id), [(byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], data);

    public static byte[] Latin1Text(string text) => Concat([0], Encoding.Latin1.GetBytes(text));

    public static byte[] Utf16Text(string text) => Concat([1, 0xFF, 0xFE], Encoding.Unicode.GetBytes(text));

    public static byte[] Utf8Text(string text) => Concat([3], Encoding.UTF8.GetBytes(text));

    public static byte[] Apic(string mime, byte pictureType, string description, byte[] image) =>
        Concat([0], Ascii(mime), [0, pictureType], Encoding.Latin1.GetBytes(description), [0], image);

    public static byte[] Unsynchronise(byte[] data)
    {
        var result = new List<byte>(data.Length + 16);
        for (var i = 0; i < data.Length; i++)
        {
            result.Add(data[i]);
            if (data[i] == 0xFF && (i + 1 == data.Length || data[i + 1] == 0x00 || (data[i + 1] & 0xE0) == 0xE0))
            {
                result.Add(0x00);
            }
        }

        return [.. result];
    }

    public static byte[] Id3v1(string title, string artist, string album, byte track)
    {
        var tag = new byte[128];
        Ascii("TAG").CopyTo(tag, 0);
        Encoding.Latin1.GetBytes(title).CopyTo(tag, 3);
        Encoding.Latin1.GetBytes(artist).CopyTo(tag, 33);
        Encoding.Latin1.GetBytes(album).CopyTo(tag, 63);
        tag[125] = 0;
        tag[126] = track;
        tag[127] = 255;
        return tag;
    }

    // ---- MPEG audio ----

    /// <summary>MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, stereo: 417 bytes a frame, 1152 samples.</summary>
    public static byte[] MpegFrame()
    {
        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x00;
        return frame;
    }

    public static byte[] MpegFrames(int count) => Concat(Enumerable.Range(0, count).Select(_ => MpegFrame()).ToArray());

    /// <summary>A first frame carrying a Xing header that counts <paramref name="frames"/> frames.</summary>
    public static byte[] XingFrame(uint frames)
    {
        var frame = MpegFrame();
        Ascii("Xing").CopyTo(frame, 4 + 32);
        BE32(1).CopyTo(frame, 4 + 32 + 4);
        BE32(frames).CopyTo(frame, 4 + 32 + 8);
        return frame;
    }

    // ---- FLAC ----

    public static byte[] Flac(int sampleRate, long totalSamples, params byte[][] blocks)
    {
        var info = new byte[34];
        var packed = ((ulong)sampleRate << 44) | (1UL << 41) | (15UL << 36) | (ulong)totalSamples;
        BinaryPrimitives.WriteUInt64BigEndian(info.AsSpan(10), packed);
        var all = new List<byte[]> { FlacBlock(0, info, last: blocks.Length == 0) };
        for (var i = 0; i < blocks.Length; i++)
        {
            all.Add(blocks[i]);
        }

        return Concat([Ascii("fLaC"), .. all, new byte[64]]);
    }

    public static byte[] FlacBlock(int type, byte[] data, bool last = false) =>
        Concat([(byte)(type | (last ? 0x80 : 0)), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], data);

    public static byte[] VorbisComment(params string[] entries)
    {
        var vendor = Encoding.UTF8.GetBytes("test");
        var parts = new List<byte[]> { LE32((uint)vendor.Length), vendor, LE32((uint)entries.Length) };
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            parts.Add(LE32((uint)bytes.Length));
            parts.Add(bytes);
        }

        return Concat([.. parts]);
    }

    public static byte[] FlacPictureStructure(uint type, string mime, byte[] image) =>
        Concat(BE32(type), BE32((uint)mime.Length), Ascii(mime), BE32(4), Ascii("desc"), new byte[16], BE32((uint)image.Length), image);

    // ---- MP4 ----

    public static byte[] Atom(string type, params byte[][] payload) => Atom(Ascii(type), payload);

    public static byte[] Atom(byte[] type, params byte[][] payload)
    {
        var body = Concat(payload);
        return Concat(BE32((uint)(8 + body.Length)), type, body);
    }

    public static byte[] ItunesType(string name) => name[0] == '©' ? Concat([0xA9], Ascii(name[1..])) : Ascii(name);

    public static byte[] Mp4Data(uint type, byte[] value) => Atom("data", BE32(type), BE32(0), value);

    public static byte[] Mvhd(uint timescale, uint duration) => Atom("mvhd", new byte[4], new byte[8], BE32(timescale), BE32(duration), new byte[80]);

    // ---- Ogg ----

    /// <summary>Ogg pages for <paramref name="packets"/>, at most <paramref name="segmentsPerPage"/> lacing values a page (small, so packets span pages).</summary>
    public static byte[] OggPages(uint serial, int segmentsPerPage, params byte[][] packets)
    {
        var lacing = new List<(byte Value, byte[] Data)>();
        foreach (var packet in packets)
        {
            var pos = 0;
            while (true)
            {
                var take = Math.Min(255, packet.Length - pos);
                lacing.Add(((byte)take, packet[pos..(pos + take)]));
                pos += take;
                if (take < 255)
                {
                    break;
                }
            }
        }

        var pages = new List<byte[]>();
        for (var i = 0; i < lacing.Count; i += segmentsPerPage)
        {
            var segments = lacing.Skip(i).Take(segmentsPerPage).ToList();
            pages.Add(OggPage(serial, (uint)pages.Count, 0, segments.Select(s => s.Value).ToArray(), Concat(segments.Select(s => s.Data).ToArray())));
        }

        return Concat([.. pages]);
    }

    public static byte[] OggPage(uint serial, uint sequence, ulong granule, byte[] lacing, byte[] data) =>
        Concat(Ascii("OggS"), [0, 0], LE64(granule), LE32(serial), LE32(sequence), LE32(0), [(byte)lacing.Length], lacing, data);

    // ---- WAV ----

    public static byte[] Chunk(string id, byte[] data) =>
        Concat(Ascii(id), LE32((uint)data.Length), data, data.Length % 2 == 1 ? [0] : []);

    // ---- ASF ----

    public static byte[] AsfObject(string guid, params byte[][] payload)
    {
        var body = Concat(payload);
        return Concat(new Guid(guid).ToByteArray(), LE64((ulong)(24 + body.Length)), body);
    }

    public static byte[] Utf16Z(string text) => Concat(Encoding.Unicode.GetBytes(text), [0, 0]);
}
