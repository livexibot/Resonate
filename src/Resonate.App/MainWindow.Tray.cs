using System.Runtime.InteropServices;
using Resonate.App.Services;

namespace Resonate.App;

/// <summary>
/// Tray icon, a built-in plugin (as Spotifast keeps music playing from the
/// tray): an icon in the notification area while Resonate runs. Closing the
/// window hides it there instead of quitting, so the music, media keys and
/// the Summon bar's shortcut carry on; a click brings the window back, and
/// its menu plays, pauses, skips or quits. Plain Win32 (<c>Shell_NotifyIcon</c>
/// and a popup menu), its messages heard through <see cref="WindowHook"/>.
/// </summary>
public sealed partial class MainWindow
{
    private const uint TrayMessage = 0x8000 + 0x51; // WM_APP + 0x51
    private const uint TrayId = 1;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const int TrayShow = 1;
    private const int TrayPlayPause = 2;
    private const int TrayNext = 3;
    private const int TrayPrevious = 4;
    private const int TrayQuit = 5;

    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private IDisposable? _trayHook;
    private nint _trayIcon;
    private bool _quitFromTray;
    private string? _trayTip;

    private bool TrayOn => _trayHook is not null;

    partial void SetUpTray()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.TrayIcon)
            {
                TurnTray(_services.BuiltIns.IsOn(id));
            }
        };
        _services.Player.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTrayTip);
        Closed += (_, _) => TurnTray(false);
        TurnTray(_services.BuiltIns.IsOn(BuiltInPlugins.TrayIcon) && !_services.IsDemo);
    }

    /// <summary>Closing the window hides it in the tray while the icon is there (not when quitting from its menu).</summary>
    private bool HidesToTray()
    {
        if (!TrayOn || _quitFromTray)
        {
            return false;
        }

        AppWindow.Hide();
        return true;
    }

    private void TurnTray(bool on)
    {
        if (on == TrayOn)
        {
            return;
        }

        if (on)
        {
            _trayHook = WindowHook.Listen(Hwnd, OnTrayWindowMessage);
            AddTrayIcon();
            return;
        }

        RemoveTrayIcon();
        _trayHook?.Dispose();
        _trayHook = null;
    }

    private unsafe void AddTrayIcon()
    {
        if (_trayIcon == 0)
        {
            var size = GetSystemMetrics(49); // SM_CXSMICON
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Resonate.ico");
            _trayIcon = LoadImageW(0, path, 1, size, size, 0x10); // IMAGE_ICON, LR_LOADFROMFILE
        }

        var data = TrayData(NifMessage | NifIcon | NifTip);
        ShellNotifyIconW(NimAdd, &data);
    }

    private unsafe void RemoveTrayIcon()
    {
        var data = TrayData(0);
        ShellNotifyIconW(NimDelete, &data);
        if (_trayIcon != 0)
        {
            DestroyIcon(_trayIcon);
            _trayIcon = 0;
        }
    }

    /// <summary>The icon's tooltip names the song that plays.</summary>
    private unsafe void UpdateTrayTip()
    {
        if (!TrayOn)
        {
            return;
        }

        var state = _services.Player.State;
        var tip = state.Title is { } title ? $"{title} · {state.Artists}" : "Resonate";
        if (tip == _trayTip)
        {
            return;
        }

        _trayTip = tip;
        var data = TrayData(NifTip);
        ShellNotifyIconW(NimModify, &data);
    }

    private unsafe NotifyIconData TrayData(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = Hwnd,
            Id = TrayId,
            Flags = flags,
            CallbackMessage = TrayMessage,
            Icon = _trayIcon,
        };
        var tip = (_trayTip ?? "Resonate").AsSpan();
        tip = tip[..Math.Min(tip.Length, 127)];
        for (var i = 0; i < tip.Length; i++)
        {
            data.Tip[i] = tip[i];
        }

        return data;
    }

    /// <summary>Inside the window's message handling: acted on once it returns.</summary>
    private void OnTrayWindowMessage(uint message, nint wParam, nint lParam)
    {
        if (message == TaskbarCreated && TrayOn)
        {
            // Explorer started again: the icon is put back.
            DispatcherQueue.TryEnqueue(AddTrayIcon);
            return;
        }

        if (message != TrayMessage)
        {
            return;
        }

        var mouse = (uint)(lParam & 0xFFFF);
        if (mouse == WmLButtonUp)
        {
            DispatcherQueue.TryEnqueue(ShowFromTray);
        }
        else if (mouse == WmRButtonUp)
        {
            DispatcherQueue.TryEnqueue(ShowTrayMenu);
        }
    }

    private void ShowFromTray()
    {
        if (IsMiniPlayerShown)
        {
            return;
        }

        AppWindow.Show();
        if (IsIconic(Hwnd))
        {
            ShowWindowTray(Hwnd, 9); // SW_RESTORE
        }

        Activate();
        SetForegroundWindowTray(Hwnd);
    }

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var playing = _services.Player.State.IsPlaying;
            AppendMenuW(menu, 0, TrayShow, "Show Resonate");
            AppendMenuW(menu, 0x800, 0, null); // MF_SEPARATOR
            AppendMenuW(menu, 0, TrayPlayPause, playing ? "Pause" : "Play");
            AppendMenuW(menu, 0, TrayNext, "Next");
            AppendMenuW(menu, 0, TrayPrevious, "Previous");
            AppendMenuW(menu, 0x800, 0, null);
            AppendMenuW(menu, 0, TrayQuit, "Quit");
            GetCursorPos(out var point);

            // The menu closes when clicked elsewhere only if this window is in front first.
            SetForegroundWindowTray(Hwnd);
            var chosen = TrackPopupMenuEx(menu, 0x0100 | 0x0002 | 0x0080, point.X, point.Y, Hwnd, 0); // RETURNCMD, RIGHTBUTTON, NONOTIFY
            switch (chosen)
            {
                case TrayShow:
                    ShowFromTray();
                    break;
                case TrayPlayPause:
                    _ = _services.Player.TogglePlayPauseAsync();
                    break;
                case TrayNext:
                    _ = _services.Player.NextAsync();
                    break;
                case TrayPrevious:
                    _ = _services.Player.PreviousAsync();
                    break;
                case TrayQuit:
                    _quitFromTray = true;
                    Quit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrayPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellNotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nint id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out TrayPoint point);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindowTray(nint window);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowTray(nint window, int command);
}
