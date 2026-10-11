using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Resonate.App.Helpers;

/// <summary>
/// The small windows of plugins (Quick search, Desktop lyrics) and the Winamp
/// mini player: no frame at all, Windows 11's rounded corners (square for
/// Winamp), and the hairline Windows draws round a
/// window in a colour of the look's (or none) instead of its own light grey.
/// An overlapped window without border or title bar still has a dialog frame
/// (a light edge 3 px wide all round, the "weird white outline" the owner saw
/// round Quick search, 11 October 2026), and Windows puts that style back
/// whenever the window is shown, so the window instead tells Windows its
/// frame takes no room (WM_NCCALCSIZE): all of it is content.
/// </summary>
internal static unsafe partial class FramelessWindow
{
    private const uint NcCalcSize = 0x0083;
    private const uint NcDestroy = 0x0082;
    private const nuint SubclassId = 0x46524D4C; // "FRML"
    private const uint NoSize = 0x1;
    private const uint NoMove = 0x2;
    private const uint NoZOrder = 0x4;
    private const uint NoActivate = 0x10;
    private const uint FrameChanged = 0x20;
    private const uint CornerPreference = 33;
    private const uint BorderColor = 34;
    private const uint DoNotRound = 1;
    private const uint Round = 2;
    private const uint ColorNone = 0xFFFFFFFE;

    private static readonly HashSet<nint> Hooked = [];

    /// <summary>Takes the frame away (once is enough) and sets the hairline and the corners; call after the presenter is set.</summary>
    public static void Apply(Window window, global::Windows.UI.Color? border, bool rounded = true)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (Hooked.Add(hwnd))
        {
            SetWindowSubclass(hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&OnMessage, SubclassId, 0);
        }

        // Asks for the frame again, which the window now answers with none.
        _ = SetWindowPos(hwnd, 0, 0, 0, 0, 0, NoMove | NoSize | NoZOrder | NoActivate | FrameChanged);
        var round = rounded ? Round : DoNotRound;
        _ = DwmSetWindowAttribute(hwnd, CornerPreference, ref round, sizeof(uint));

        // A COLORREF is 0x00BBGGRR.
        var value = border is { } c ? (uint)(c.R | (c.G << 8) | (c.B << 16)) : ColorNone;
        _ = DwmSetWindowAttribute(hwnd, BorderColor, ref value, sizeof(uint));
    }

    [UnmanagedCallersOnly]
    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        // The whole window is content: no frame, whatever its styles say.
        if (message == NcCalcSize && wParam != 0)
        {
            return 0;
        }

        if (message == NcDestroy && Hooked.Remove(hwnd))
        {
            RemoveWindowSubclass(hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&OnMessage, SubclassId);
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hwnd, nint subclassProc, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hwnd, nint subclassProc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, int size);
}
