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
    public static byte[]? ReadEmbedded(string audioPath)
    {
        try
        {
            using var stream = TagReader.OpenRead(audioPath);
            var tags = TagReader.Read(stream, audioPath);
            return tags.Cover is { } cover ? TagReader.ReadCover(stream, cover) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The picture next to the file (cover.jpg, folder.jpg and the like), or null.</summary>
    public static byte[]? ReadFolderImage(string audioPath)
    {
        if (FindFolderImage(audioPath) is not { } image)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(image);
            return info.Length is > 0 and <= TagReader.MaxCoverBytes ? File.ReadAllBytes(image) : null;
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
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0, IgnoreInaccessible = true };
        try
        {
            foreach (var name in FolderImageNames)
            {
                string? best = null;
                foreach (var candidate in Directory.EnumerateFiles(folder, name + ".*", options))
                {
                    var extension = Path.GetExtension(candidate);
                    var rank = Array.FindIndex(FolderImageExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
                    if (rank >= 0 && (best is null || rank < Array.FindIndex(FolderImageExtensions, e => e.Equals(Path.GetExtension(best), StringComparison.OrdinalIgnoreCase))))
                    {
                        best = candidate;
                    }
                }

                if (best is not null)
                {
                    return best;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder has no cover to offer.
        }

        return null;
    }
}
