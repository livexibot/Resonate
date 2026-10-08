namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// How a music file is encoded, read from its header: enough to tell
/// lossless from lossy, and the rate and depth where the header says (for
/// Signal path). Like the tag reader, it trusts the bytes over the name.
/// </summary>
/// <param name="Codec">"FLAC", "WAV", "ALAC", "AIFF", "AAC", "MP3", "Vorbis", "Opus" or "WMA".</param>
/// <param name="IsLossless">True for FLAC, PCM WAV, ALAC and AIFF; false for lossy codecs; null when it can not tell.</param>
/// <param name="SampleRate">In Hz; 0 when the header does not say.</param>
/// <param name="BitsPerSample">0 when the header does not say, or for lossy codecs, which have none.</param>
public sealed record LocalAudioFormat(string Codec, bool? IsLossless, int SampleRate = 0, int BitsPerSample = 0)
{
    private const uint Moov = 0x6D6F6F76;
    private const uint Trak = 0x7472616B;
    private const uint Mdia = 0x6D646961;
    private const uint Minf = 0x6D696E66;
    private const uint Stbl = 0x7374626C;
    private const uint Stsd = 0x73747364;
    private const uint Alac = 0x616C6163;
    private const uint Mp4a = 0x6D703461;
    private const uint FlacEntry = 0x664C6143; // fLaC
    private const uint OpusEntry = 0x4F707573; // Opus

    /// <summary>How far past an ID3 tag an MP3 or AAC stream may start.</summary>
    private const int SyncSearch = 4096;

    private static readonly int[] MpegRates = [44_100, 48_000, 32_000];
    private static readonly int[] AdtsRates = [96_000, 88_200, 64_000, 48_000, 44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350];

    /// <summary>The format of the file at <paramref name="path"/>; by its name when it can not be read.</summary>
    public static LocalAudioFormat? Read(string path)
    {
        try
        {
            using var stream = TagReader.OpenRead(path);
            return Read(stream, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FromExtension(path);
        }
    }

    /// <summary>The format of <paramref name="stream"/>, falling back to <paramref name="fileName"/>'s extension.</summary>
    public static LocalAudioFormat? Read(Stream stream, string fileName)
    {
        try
        {
            return Sniff(new FileWindow(stream)) ?? FromExtension(fileName);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException or EndOfStreamException or IOException)
        {
            return FromExtension(fileName);
        }
    }

    /// <summary>A guess from the file's name alone, for files that can not be read.</summary>
    public static LocalAudioFormat? FromExtension(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".FLAC" => new("FLAC", true),
        ".WAV" => new("WAV", true),
        ".AIF" or ".AIFF" => new("AIFF", true),
        ".MP3" => new("MP3", false),
        ".AAC" => new("AAC", false),
        ".OGG" => new("Vorbis", false),
        ".OPUS" => new("Opus", false),
        ".WMA" => new("WMA", false),

        // AAC or Apple Lossless: only the file can tell.
        ".M4A" or ".MP4" => new("M4A", null),
        _ => null,
    };

    private static LocalAudioFormat? Sniff(FileWindow file)
    {
        var head = file.Read(0, 16).ToArray();
        if (Mp4Tags.IsMp4(head))
        {
            return Mp4(file);
        }

        if (RiffTags.IsWave(head))
        {
            return Wave(file);
        }

        if (head.Length >= 12 && head.AsSpan(0, 4).SequenceEqual("FORM"u8)
            && (head.AsSpan(8, 4).SequenceEqual("AIFF"u8) || head.AsSpan(8, 4).SequenceEqual("AIFC"u8)))
        {
            return new("AIFF", true);
        }

        if (AsfTags.IsAsf(head))
        {
            return new("WMA", false);
        }

        if (head.AsSpan().StartsWith("OggS"u8))
        {
            return Ogg(file);
        }

        // FLAC, MP3 and AAC may sit behind an ID3v2 tag.
        var start = MpegTags.Id3v2.TagSize(file, 0);
        return file.Matches(start, "fLaC"u8) ? Flac(file, start) : Mpeg(file, start);
    }

    private static LocalAudioFormat Flac(FileWindow file, long start)
    {
        // STREAMINFO comes first: 20 bits of sample rate, 3 of channels, 5 of bits per sample.
        var isStreamInfo = file.Read(start + 4, 1) is [var type] && (type & 0x7F) == 0;
        var info = file.Read(start + 8, 18).ToArray();
        if (!isStreamInfo || info.Length < 18)
        {
            return new("FLAC", true);
        }

        var rate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
        var bits = (((info[12] & 0x01) << 4) | (info[13] >> 4)) + 1;
        return new("FLAC", true, rate, bits);
    }

    private static LocalAudioFormat Wave(FileWindow file)
    {
        var pos = 12L;
        for (var i = 0; i < 1000 && pos + 8 <= file.Length; i++)
        {
            var size = file.U32LE(pos + 4);
            var data = pos + 8;
            if (file.Matches(pos, "fmt "u8) && size >= 16)
            {
                var tag = file.U16LE(data);
                var rate = (int)Math.Min(file.U32LE(data + 4), int.MaxValue);
                int bits = file.U16LE(data + 14);
                if (tag == 0xFFFE && size >= 26)
                {
                    // WAVE_FORMAT_EXTENSIBLE: the real format is the sub-format's first two bytes.
                    var valid = file.U16LE(data + 18);
                    bits = valid > 0 ? valid : bits;
                    tag = file.U16LE(data + 24);
                }

                // 1 is PCM and 3 is floating point; anything else (ADPCM, MP3 in a WAV) is compressed.
                return tag is 1 or 3 ? new("WAV", true, rate, bits) : new("WAV", false, rate);
            }

            pos = data + size + (size & 1);
        }

        return new("WAV", true);
    }

    private static LocalAudioFormat? Mp4(FileWindow file)
    {
        foreach (var moov in Mp4Tags.Atoms(file, 0, file.Length))
        {
            if (moov.Type != Moov)
            {
                continue;
            }

            foreach (var trak in Child(file, moov, Trak))
            {
                foreach (var stsd in Child(file, trak, Mdia).SelectMany(m => Child(file, m, Minf)).SelectMany(m => Child(file, m, Stbl)).SelectMany(s => Child(file, s, Stsd)))
                {
                    // Version and flags, the entry count, then the first sample entry.
                    foreach (var entry in Mp4Tags.Atoms(file, stsd.Data + 8, stsd.End))
                    {
                        if (SampleEntry(file, entry) is { } format)
                        {
                            return format;
                        }

                        break;
                    }
                }
            }

            break;
        }

        return new("M4A", null);
    }

    private static IEnumerable<Mp4Tags.Atom> Child(FileWindow file, Mp4Tags.Atom parent, uint type) =>
        Mp4Tags.Atoms(file, parent.Data, parent.End).Where(a => a.Type == type);

    private static LocalAudioFormat? SampleEntry(FileWindow file, Mp4Tags.Atom entry)
    {
        // An AudioSampleEntry: 6 reserved bytes, the data reference, 8 more
        // reserved, channels, sample size, 4 more, then the rate in 16.16 fixed point.
        var hasFields = entry.End - entry.Data >= 28;
        int bits = hasFields ? file.U16BE(entry.Data + 18) : 0;
        var rate = hasFields ? (int)(file.U32BE(entry.Data + 24) >> 16) : 0;
        switch (entry.Type)
        {
            case Alac:
                // The 'alac' box inside has the real depth and rate (the 16.16 field can not hold 96 kHz or more).
                if (hasFields)
                {
                    foreach (var config in Mp4Tags.Atoms(file, entry.Data + 28, entry.End))
                    {
                        if (config.Type == Alac && config.End - config.Data >= 28)
                        {
                            bits = file.Exactly(config.Data + 9, 1)[0];
                            rate = (int)Math.Min(file.U32BE(config.Data + 24), int.MaxValue);
                            break;
                        }
                    }
                }

                return new("ALAC", true, rate, bits);
            case FlacEntry:
                return new("FLAC", true, rate, bits);
            case Mp4a:
                return new("AAC", false, rate);
            case OpusEntry:
                return new("Opus", false, rate);
            default:
                return null;
        }
    }

    private static LocalAudioFormat? Ogg(FileWindow file)
    {
        // The first page's first packet names the codec.
        var segments = file.Read(26, 1);
        if (segments.Length < 1)
        {
            return null;
        }

        var packet = file.Read(27 + segments[0], 8).ToArray();
        var span = packet.AsSpan();
        return span.StartsWith("OpusHead"u8) ? new("Opus", false)
            : span.Length >= 7 && span[0] == 1 && span[1..7].SequenceEqual("vorbis"u8) ? new("Vorbis", false)
            : span.Length >= 5 && span[0] == 0x7F && span[1..5].SequenceEqual("FLAC"u8) ? new("FLAC", true)
            : null;
    }

    private static LocalAudioFormat? Mpeg(FileWindow file, long start)
    {
        var bytes = file.Read(start, SyncSearch).ToArray();
        for (var i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || (bytes[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            var version = (bytes[i + 1] >> 3) & 0x03;
            var layer = (bytes[i + 1] >> 1) & 0x03;
            if (layer == 0 && (bytes[i + 1] & 0xF0) == 0xF0)
            {
                // ADTS AAC: a 12-bit sync and layer 0.
                var index = (bytes[i + 2] >> 2) & 0x0F;
                return new("AAC", false, index < AdtsRates.Length ? AdtsRates[index] : 0);
            }

            var rateIndex = (bytes[i + 2] >> 2) & 0x03;
            if (layer == 0 || version == 1 || rateIndex == 3)
            {
                // Reserved values: not a frame header.
                continue;
            }

            // MPEG 1 (3), 2 (2) and 2.5 (0) halve the rate each step.
            var divisor = version switch { 3 => 1, 2 => 2, _ => 4 };
            return new(layer == 1 ? "MP3" : "MPEG audio", false, MpegRates[rateIndex] / divisor);
        }

        return null;
    }
}
