using System.Runtime.InteropServices;

namespace Resonate.App.Services;

/// <summary>
/// Messages Windows sends a window that WinUI does not pass on, such as a
/// global shortcut (WM_HOTKEY), the end of a resize (WM_EXITSIZEMOVE), the
/// tray icon's clicks, a session change or a taskbar button's click.
/// Listening subclasses the window (SetWindowSubclass, on the window's own
/// thread, the interface thread); the subclass is removed again when the
/// last listener stops, so nothing hooks into Windows while no plugin needs it.
/// Listeners run inside the window's message handling: they must be quick,
/// and should queue real work on the dispatcher.
/// </summary>
internal static unsafe partial class WindowHook
{
    public const uint WmHotkey = 0x0312;
    public const uint WmEnterSizeMove = 0x0231;
    public const uint WmExitSizeMove = 0x0232;

    private const uint WmNcDestroy = 0x0082;

    private const nuint SubclassId = 0x5245534F; // "RESO"

    private static readonly Dictionary<nint, List<Action<uint, nint, nint>>> Listeners = [];

    /// <summary>
    /// Calls <paramref name="listener"/> with every message the window gets
    /// (the message and its two parameters; it picks the ones it wants) until
    /// the result is disposed.
    /// Call on the window's thread.
    /// </summary>
    public static IDisposable Listen(nint hwnd, Action<uint, nint, nint> listener)
    {
        if (!Listeners.TryGetValue(hwnd, out var list))
        {
            list = [];
            Listeners[hwnd] = list;
            SetWindowSubclass(hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&OnMessage, SubclassId, 0);
        }

        list.Add(listener);
        return new Subscription(hwnd, listener);
    }

    private static void Stop(nint hwnd, Action<uint, nint, nint> listener)
    {
        if (!Listeners.TryGetValue(hwnd, out var list) || !list.Remove(listener) || list.Count > 0)
        {
            return;
        }

        Listeners.Remove(hwnd);
        RemoveWindowSubclass(hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&OnMessage, SubclassId);
    }

    [UnmanagedCallersOnly]
    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        // Every message: each listener picks its own. Until 9 October 2026 only
        // these three were passed on, so the tray icon's clicks and Pause on
        // lock's session changes never arrived.
        if (Listeners.TryGetValue(hwnd, out var list))
        {
            // By index, with no copy per message; a listener that stops itself only shifts the rest.
            for (var i = 0; i < list.Count; i++)
            {
                try
                {
                    list[i](message, wParam, lParam);
                }
                catch (Exception)
                {
                    // An exception must never cross back into Windows.
                }
            }
        }

        if (message == WmNcDestroy && Listeners.Remove(hwnd))
        {
            // The window is going away: the subclass goes first, as Windows asks.
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

    private sealed class Subscription(nint hwnd, Action<uint, nint, nint> listener) : IDisposable
    {
        private bool _stopped;

        public void Dispose()
        {
            if (!_stopped)
            {
                _stopped = true;
                Stop(hwnd, listener);
            }
        }
    }
}
