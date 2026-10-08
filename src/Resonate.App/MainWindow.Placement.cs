using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Resonate.App.Services;
using Windows.Graphics;

namespace Resonate.App;

/// <summary>
/// The window opens where it was left: the same size and place, maximised
/// if it was. A place on a screen that is no longer there opens centred.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The window's usual size, in pixels at 100 % scaling.</summary>
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 820;

    /// <summary>At least this much of the window stays on its screen, so it can always be grabbed.</summary>
    private const int KeepOnScreen = 120;

    /// <summary>The smallest window, in pixels at 100 % scaling and the usual App size: room for the page beside the sidebar and a mini player.</summary>
    private const int MinimumWidth = 760;
    private const int MinimumHeight = 540;

    private const int SwMaximize = 3;

    /// <summary>Moving or resizing sends many changes; the place is saved once the window settles.</summary>
    private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromSeconds(1);

    private DispatcherQueueTimer? _placementTimer;

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>
    /// Keeps the window from being made smaller than <see cref="MinimumWidth"/>
    /// by <see cref="MinimumHeight"/> (more at a larger App size, see
    /// UpdateMinimumSize). Called first thing, before the title bar is set up
    /// and the window is placed, so nothing set later is lost.
    /// </summary>
    private void SetMinimumSize()
    {
        // A presenter of its own, rather than a cast of the current one, which
        // Native AOT may not recognise (see CLAUDE.md); it is the same kind.
        _presenter = OverlappedPresenter.Create();
        UpdateMinimumSize(grow: false);
        AppWindow.SetPresenter(_presenter);
    }

    /// <summary>Puts the window where it was left (before it is first shown).</summary>
    private void RestorePlacement()
    {
        var saved = _services.IsDemo ? null : _services.Settings.Window;
        if (saved is null || !TryPlace(saved))
        {
            PlaceWindow(DefaultWidth, DefaultHeight);
        }

        if (saved is { Maximized: true })
        {
            ShowWindow(Hwnd, SwMaximize);
        }
    }

    private bool TryPlace(WindowPlacement saved)
    {
        if (saved.Width < KeepOnScreen || saved.Height < KeepOnScreen)
        {
            return false;
        }

        // The top of the window must be on a screen, or it could not be moved back.
        var top = new RectInt32(saved.X, saved.Y, saved.Width, KeepOnScreen / 2);
        if (DisplayArea.GetFromRect(top, DisplayAreaFallback.None) is not { } display)
        {
            return false;
        }

        var area = display.WorkArea;
        var width = Math.Min(saved.Width, area.Width);
        var height = Math.Min(saved.Height, area.Height);
        var x = Math.Clamp(saved.X, area.X - width + KeepOnScreen, area.X + area.Width - KeepOnScreen);
        var y = Math.Clamp(saved.Y, area.Y, area.Y + area.Height - KeepOnScreen);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        return true;
    }

    /// <summary>Notes where the window is, and saves it once it settles.</summary>
    private void NotePlacement(AppWindowChangedEventArgs args)
    {
        if (_services.IsDemo || !(args.DidPositionChange || args.DidSizeChange) || !AppWindow.IsVisible)
        {
            return;
        }

        var hwnd = Hwnd;
        if (IsIconic(hwnd))
        {
            // Minimised: it comes back where it was.
            return;
        }

        var previous = _services.Settings.Window;
        WindowPlacement placement;
        if (IsZoomed(hwnd))
        {
            // Maximised: the size and place to go back to stay as they were.
            placement = (previous ?? new WindowPlacement()) with { Maximized = true };
        }
        else
        {
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            placement = new WindowPlacement { X = position.X, Y = position.Y, Width = size.Width, Height = size.Height };
        }

        if (placement == previous)
        {
            return;
        }

        _services.Settings.Window = placement;
        if (_placementTimer is null)
        {
            _placementTimer = DispatcherQueue.CreateTimer();
            _placementTimer.Interval = PlacementSaveDelay;
            _placementTimer.IsRepeating = false;
            _placementTimer.Tick += (_, _) => _services.SaveSettings();
        }

        _placementTimer.Stop();
        _placementTimer.Start();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);
}
