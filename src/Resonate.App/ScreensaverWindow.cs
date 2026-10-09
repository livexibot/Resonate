using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Services;
using Windows.Graphics;

namespace Resonate.App;

/// <summary>
/// The screensaver's own window (Screensaver, a built-in plugin, the owner's
/// request of 9 October 2026): full screen on the display Resonate is on,
/// above every app, with the mouse pointer hidden while it shows. It holds
/// the <see cref="AwayScreen"/>, which asks for it to go at the first touch
/// of the mouse or keyboard.
/// </summary>
internal sealed partial class ScreensaverWindow : Window
{
    private static readonly nint TopMost = -1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoActivate = 0x10;

    private bool _cursorHidden;

    public ScreensaverWindow(AwayScreen screen, RectInt32 area, ElementTheme theme)
    {
        AppWindow.Title = "Resonate screensaver";
        AppWindow.IsShownInSwitchers = false;
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        // Its Brightness: the screen over black.
        screen.Opacity = Math.Clamp(App.Services.Settings.ScreensaverBrightness, 20, 100) / 100.0;
        Content = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            RequestedTheme = theme,
            Children = { screen },
        };

        // The whole display, the taskbar included.
        AppWindow.MoveAndResize(area);
        Closed += (_, _) => ShowPointer();
    }

    /// <summary>Shows it above every app, takes the keyboard when Windows allows, and hides the pointer.</summary>
    public void ShowOnTop()
    {
        Activate();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, TopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        SetForegroundWindow(hwnd);
        if (!_cursorHidden)
        {
            _cursorHidden = true;
            ShowCursor(false);
        }
    }

    private void ShowPointer()
    {
        if (_cursorHidden)
        {
            _cursorHidden = false;
            ShowCursor(true);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
}
