using System.Runtime.InteropServices;

namespace Resonate.Windows.Interop;

internal static partial class WindowMessages
{
    /// <summary>WM_CLOSE: what a window's close button sends.</summary>
    public const uint WmClose = 0x0010;

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    /// <summary>The window's class name ("Chrome_WidgetWin_0" for Spotify's main window, which is Chromium's).</summary>
    public static unsafe string ClassName(nint window)
    {
        const int Capacity = 256;
        var buffer = stackalloc char[Capacity];
        var length = GetClassName(window, buffer, Capacity);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint window, char* className, int maxCount);
}
