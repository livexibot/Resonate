using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Which program a process runs, asked of that one process. Cheaper than
/// listing every process on the PC (<c>Process.GetProcessesByName</c>), which
/// reads the whole process and thread table each time.
/// </summary>
internal static class ProcessImage
{
    /// <summary>The full path of the program <paramref name="processId"/> runs, or null when Windows does not say.</summary>
    public static unsafe string? PathOf(uint processId)
    {
        var process = Processes.OpenProcess(Processes.QueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return Processes.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Processes.CloseHandle(process);
        }
    }

    /// <summary>The process runs Spotify.exe (the installer's or the Microsoft Store's).</summary>
    public static bool IsSpotify(uint processId) =>
        string.Equals(Path.GetFileName(PathOf(processId)), "Spotify.exe", StringComparison.OrdinalIgnoreCase);
}
