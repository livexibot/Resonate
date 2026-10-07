using System.Runtime.InteropServices;

namespace Resonate.Windows.LocalAudio;

/// <summary>Folders as Windows knows them, which the user may have moved (Downloads to another drive, say).</summary>
public static partial class WindowsFolders
{
    // FOLDERID_Downloads.
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>The user's Downloads folder, or null when Windows can not say.</summary>
    public static string? Downloads()
    {
        var result = SHGetKnownFolderPath(DownloadsId, 0, 0, out var path);
        try
        {
            return result == 0 && path != 0 ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);
}
