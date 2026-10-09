using System.Runtime.InteropServices;
using Resonate.App.Services;

namespace Resonate.App;

/// <summary>
/// Pause on lock, a built-in plugin: the music pauses when Windows locks and
/// plays on when it unlocks, if it was this that paused it. Windows tells the
/// window through WM_WTSSESSION_CHANGE once it is registered for it
/// (<c>WTSRegisterSessionNotification</c>); while off, nothing is registered.
/// </summary>
public sealed partial class MainWindow
{
    private const uint WmWtsSessionChange = 0x02B1;
    private const nint WtsSessionLock = 0x7;
    private const nint WtsSessionUnlock = 0x8;
    private const uint NotifyForThisSession = 0;

    private IDisposable? _lockHook;
    private bool _pausedForLock;

    partial void SetUpPauseOnLock()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.PauseOnLock)
            {
                TurnPauseOnLock(_services.BuiltIns.IsOn(id));
            }
        };
        Closed += (_, _) => TurnPauseOnLock(false);
        TurnPauseOnLock(_services.BuiltIns.IsOn(BuiltInPlugins.PauseOnLock));
    }

    private void TurnPauseOnLock(bool on)
    {
        if (on == (_lockHook is not null))
        {
            return;
        }

        var hwnd = Hwnd;
        if (on)
        {
            if (WTSRegisterSessionNotification(hwnd, NotifyForThisSession))
            {
                _lockHook = WindowHook.Listen(hwnd, OnSessionMessage);
            }

            return;
        }

        _lockHook?.Dispose();
        _lockHook = null;
        WTSUnRegisterSessionNotification(hwnd);
        _pausedForLock = false;
    }

    /// <summary>Inside the window's message handling: acted on once it returns.</summary>
    private void OnSessionMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WmWtsSessionChange || (wParam != WtsSessionLock && wParam != WtsSessionUnlock))
        {
            return;
        }

        var locked = wParam == WtsSessionLock;
        DispatcherQueue.TryEnqueue(() =>
        {
            var player = _services.Player;
            if (locked && player.State.IsPlaying)
            {
                _pausedForLock = true;
                _ = player.PauseAsync();
            }
            else if (!locked && _pausedForLock)
            {
                _pausedForLock = false;
                if (!player.State.IsPlaying)
                {
                    _ = player.PlayAsync();
                }
            }
        });
    }

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSUnRegisterSessionNotification(nint hwnd);
}
