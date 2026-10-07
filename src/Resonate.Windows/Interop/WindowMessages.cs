using System.Runtime.InteropServices;

namespace Resonate.Windows.Interop;

internal static partial class WindowMessages
{
    /// <summary>WM_CLOSE: what a window's close button sends.</summary>
    public const uint WmClose = 0x0010;

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
