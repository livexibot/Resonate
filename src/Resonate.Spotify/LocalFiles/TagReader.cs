namespace Resonate.Spotify.LocalFiles;

/// <summary>
/// Reads title, artist, album, track number, length and where the cover is
/// from a music file's header, without decoding any audio and without
/// reading the cover itself (see <see cref="ReadCover"/>). Pure C# over a
/// stream, so it runs on any OS, is tested on Linux and is safe for Native
/// AOT. Formats: MP3 (ID3v2.2 to 2.4, ID3v1), AAC (ADTS), FLAC, MP4 and M4A
/// (AAC and Apple Lossless), Ogg Vorbis and Opus, WAV and WMA.
/// </summary>
public static class TagReader
{
    /// <summary>The largest embedded cover Resonate reads.</summary>
    public const int MaxCoverBytes = 24 * 1024 * 1024;

    /// <summary>
    /// The file types listed in Local Files. Windows plays all of them with
    /// its own decoders, except Ogg and Opus, which need Microsoft's free
    /// "Web Media Extensions" from the Microsoft Store.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".mp3", ".m4a", ".mp4", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma"];

    public static bool IsSupported(ReadOnlySpan<char> path)
    {
        var extension = Path.GetExtension(path);
        foreach (var known in Extensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Opens a music file for reading without stopping other programs from writing or deleting it.</summary>
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.RandomAccess);

    public static AudioTags Read(string path)
    {
        using var stream = OpenRead(path);
        return Read(stream, path);
    }

    /// <summary>Reads the tags of <paramref name="stream"/>; <paramref name="fileName"/> gives the type and the fallback title.</summary>
    public static AudioTags Read(Stream stream, string fileName)
    {
        var tags = new TagBuilder();
        try
        {
            ReadFormat(new FileWindow(stream), tags, Path.GetExtension(fileName));
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            // A damaged or unusual header: keep what was read before it.
        }

        tags.Title ??= TagText.Clean(Path.GetFileNameWithoutExtension(fileName));
        return tags.Build();
    }

    /// <summary>The picture <paramref name="cover"/> points to, or null when it can not be read.</summary>
    public static byte[]? ReadCover(Stream stream, CoverRef cover)
    {
        try
        {
            if (cover.Encoding == CoverEncoding.InComment)
            {
                return OggTags.ReadCover(new FileWindow(stream));
            }

            if (cover.Length <= 0 || cover.Length > MaxCoverBytes || cover.Offset < 0 || cover.Offset + cover.Length > stream.Length)
            {
                return null;
            }

            var bytes = new byte[cover.Length];
            stream.Position = cover.Offset;
            stream.ReadExactly(bytes);
            return cover.Encoding == CoverEncoding.Unsynchronised ? Unsync.Decode(bytes) : bytes;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException or EndOfStreamException)
        {
            return null;
        }
    }

    private static void ReadFormat(FileWindow file, TagBuilder tags, string extension)
    {
        // Trust the bytes over the name: a renamed file still reads.
        var head = file.Read(0, 16).ToArray();
        if (head.AsSpan().StartsWith("fLaC"u8))
        {
            FlacTags.Read(file, tags);
        }
        else if (head.AsSpan().StartsWith("OggS"u8))
        {
            OggTags.Read(file, tags);
        }
        else if (RiffTags.IsWave(head))
        {
            RiffTags.Read(file, tags);
        }
        else if (Mp4Tags.IsMp4(head))
        {
            Mp4Tags.Read(file, tags);
        }
        else if (AsfTags.IsAsf(head))
        {
            AsfTags.Read(file, tags);
        }
        else if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase))
        {
            // FLAC behind an ID3v2 tag.
            FlacTags.Read(file, tags);
        }
        else
        {
            // MP3 and ADTS AAC, with or without ID3 tags.
            MpegTags.Read(file, tags);
        }
    }
}
