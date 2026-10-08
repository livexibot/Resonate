namespace Resonate.Spotify.LocalFiles;

/// <summary>A music file's cover: the one inside the file, or else the picture saved next to it.</summary>
public static class LocalCovers
{
    // The names rippers, iTunes and Windows Media Player give a folder's cover, best first.
    private static readonly string[] FolderImageNames = ["cover", "folder", "front", "album", "albumart", "albumartlarge"];
    private static readonly string[] FolderImageExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>The embedded cover's bytes, or the folder's picture; null when there is neither.</summary>
    public static byte[]? Read(string audioPath) => ReadEmbedded(audioPath) ?? ReadFolderImage(audioPath);

    /// <summary>The cover inside the file, or null.</summary>
    public static byte[]? ReadEmbedded(string audioPath) =>
        TryReadEmbedded(new LocalFile { Path = audioPath }, out var cover) ? cover : null;

    /// <summary>
    /// The cover inside <paramref name="file"/> (null when it has none), read
    /// from where the scan found it when it still looks like a picture there.
    /// False when the file could not be read (locked, or not downloaded), so
    /// the caller can tell that from a file without a cover.
    /// </summary>
    public static bool TryReadEmbedded(LocalFile file, out byte[]? cover)
    {
        try
        {
            using var stream = TagReader.OpenRead(file.Path);
            if (file.Cover is { } known && TagReader.ReadCover(stream, known) is { } bytes && LooksLikeImage(bytes))
            {
                cover = bytes;
                return true;
            }

            stream.Position = 0;
            var tags = TagReader.Read(stream, file.Path);
            cover = tags.Cover is { } found ? TagReader.ReadCover(stream, found) : null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cover = null;
            return false;
        }
    }

    /// <summary>Whether <paramref name="bytes"/> start like a JPEG, PNG, BMP or GIF picture.</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> bytes) => TagText.SniffImage(bytes) is not null;

    /// <summary>The picture next to the file (cover.jpg, folder.jpg and the like), or null.</summary>
    public static byte[]? ReadFolderImage(string audioPath) =>
        FindFolderImage(audioPath) is { } image ? ReadPicture(image) : null;

    /// <summary>A picture file's bytes, or null when it is empty, too large or can not be read.</summary>
    public static byte[]? ReadPicture(string imagePath)
    {
        try
        {
            var info = new FileInfo(imagePath);
            return info.Length is > 0 and <= TagReader.MaxCoverBytes ? File.ReadAllBytes(imagePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The path of the folder's cover picture for <paramref name="audioPath"/>, or null.</summary>
    public static string? FindFolderImage(string audioPath)
    {
        var folder = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        // Windows Media Player hides its Folder.jpg, so hidden files count here.
        // One look through the folder finds the best name and type.
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        string? best = null;
        var bestRank = int.MaxValue;
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(folder, "*", options))
            {
                var name = Array.FindIndex(FolderImageNames, n => n.Equals(Path.GetFileNameWithoutExtension(candidate), StringComparison.OrdinalIgnoreCase));
                var type = Array.FindIndex(FolderImageExtensions, e => e.Equals(Path.GetExtension(candidate), StringComparison.OrdinalIgnoreCase));
                var rank = (name * FolderImageExtensions.Length) + type;
                if (name >= 0 && type >= 0 && rank < bestRank)
                {
                    (best, bestRank) = (candidate, rank);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder has no cover to offer.
        }

        return best;
    }
}
