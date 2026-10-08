using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Themes.Skins;
using Windows.Graphics;
using Windows.System;

namespace Resonate.App;

/// <summary>
/// The mini player (Ctrl+M), as Spotifast has it: the classic player in a
/// small borderless window of its own while the full window is hidden. Its
/// windows stack as Winamp 2 docked them, the main window on top (with the
/// cover beside it), the equalizer and the playlist under it, each opened
/// with the main window's EQ and PL buttons and rolled up by double-clicking
/// its title bar. Every skin pixel covers a whole number of screen pixels at
/// 1x to 4x. It stays on top of other windows (the clutter bar's A), moves by
/// any part that is not a control, snapping to the screen's edges, and opens
/// where it was left. Its close button, its logo, Ctrl+M and anything that
/// needs the full window (search, a song's album) go back to it.
/// </summary>
internal sealed partial class MiniPlayerWindow : Window
{
    /// <summary>The window snaps to a screen edge within this many device-independent pixels, as Winamp's did.</summary>
    private const int SnapDistance = 10;

    /// <summary>The first time, the mini player opens this far in from the screen's bottom right corner.</summary>
    private const int CornerMargin = 24;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private readonly AppServices _services = App.Services;
    private readonly SkinLibrary _skins;
    private readonly OverlappedPresenter _presenter;
    private readonly ContentControl _host;
    private readonly StackPanel _stack;
    private readonly ClassicPlayer _main;
    private readonly ClassicEqualizer _equalizer;
    private readonly ClassicPlaylist _playlist;
    private double _raster;
    private bool _shown = true;
    private bool _closingForGood;
    private bool _movingWindow;
    private CursorPoint _moveCursor;
    private PointInt32 _moveWindow;

    public MiniPlayerWindow()
    {
        _skins = _services.Skins;
        _raster = Math.Max(GetDpiForWindow(Hwnd), 96) / 96.0;

        AppWindow.Title = App.MainWindow?.AppWindow.Title ?? "Resonate";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Resonate.ico"));

        // A presenter of its own, rather than a cast of the current one, which Native AOT may not recognise (see CLAUDE.md).
        _presenter = OverlappedPresenter.Create();
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = true;
        _presenter.IsAlwaysOnTop = _skins.MiniOnTop;
        _presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(_presenter);
        SquareCorners();

        _main = new ClassicPlayer(this);
        _equalizer = new ClassicEqualizer(this);
        _playlist = new ClassicPlaylist(this);
        _stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        _stack.Children.Add(_main);

        // Focusable, so the keys below work as soon as the window is clicked.
        _host = new ContentControl
        {
            Content = _stack,
            IsTabStop = true,
            UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            AllowDrop = true,
        };
        AddKeys(_host);
        _host.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        _host.DragOver += SkinDrop.OnDragOver;
        _host.Drop += SkinDrop.OnDrop;
        _host.Loaded += OnHostLoaded;
        Content = _host;

        _skins.MiniOptionsChanged += OnMiniOptionsChanged;
        Activated += OnActivated;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;

        Arrange(keepOnScreen: false);
        Place();
    }

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Screen pixels per skin pixel at the chosen size on this display.</summary>
    private int Scale => ClassicPlayer.PixelScale(_skins.MiniSize, _raster);

    /// <summary>Everything the window shows.</summary>
    internal FrameworkElement Root => _host;

    /// <summary>Goes back to the full window (which closes this one).</summary>
    public void Leave() => App.MainWindow?.LeaveMiniPlayer();

    public void Minimize() => _presenter.Minimize();

    /// <summary>Closes the window for good (going back to the full window, or quitting); otherwise closing it goes back.</summary>
    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    /// <summary>Brings it to the front with the keys working.</summary>
    public void ShowAndFocus()
    {
        Activate();
        _host.Focus(FocusState.Programmatic);
    }

    // Size: the cover and the main window on top, the equalizer and the playlist under the main window

    /// <summary>
    /// Shows the windows that are open and sizes the window to them exactly;
    /// unless <paramref name="keepOnScreen"/> is false (while the playlist's
    /// corner is dragged), a window that grew past the screen's bottom moves up.
    /// </summary>
    public void Arrange(bool keepOnScreen)
    {
        var scale = Scale;
        var layout = ClassicStack.Arrange(_skins.MiniStack with { PlaylistHeight = _playlist.ShownHeight });
        var cover = layout.MainHeight * scale;
        var indent = new Thickness(cover / _raster, 0, 0, 0);

        // A closed window leaves the tree, so it reads and draws nothing (the playlist reads the queue only while it is in).
        _equalizer.Margin = indent;
        _equalizer.SetScale(scale, _raster);
        Show(_equalizer, layout.EqualizerHeight > 0, 1);
        _playlist.Margin = indent;
        _playlist.SetScale(scale, _raster);
        Show(_playlist, layout.PlaylistHeight > 0, _stack.Children.Count);

        var size = new SizeInt32(cover + (ClassicStack.Width * scale), layout.Height * scale);
        if (AppWindow.ClientSize.Width != size.Width || AppWindow.ClientSize.Height != size.Height)
        {
            AppWindow.ResizeClient(size);
        }

        if (keepOnScreen && AppWindow.IsVisible)
        {
            KeepOnScreen();
        }
    }

    private void Show(UIElement window, bool open, int index)
    {
        var at = _stack.Children.IndexOf(window);
        if (open && at < 0)
        {
            _stack.Children.Insert(Math.Min(index, _stack.Children.Count), window);
        }
        else if (!open && at >= 0)
        {
            _stack.Children.RemoveAt(at);
        }
    }

    private void OnMiniOptionsChanged(object? sender, EventArgs e)
    {
        _presenter.IsAlwaysOnTop = _skins.MiniOnTop;
        Arrange(keepOnScreen: true);
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (_host.XamlRoot is { } root)
        {
            root.Changed += OnXamlRootChanged;
            OnXamlRootChanged(root, null);
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs? args)
    {
        // Moved to a display with another scale: everything is drawn again for its pixels.
        var raster = sender.RasterizationScale > 0 ? sender.RasterizationScale : 1;
        if (Math.Abs(raster - _raster) > 0.001)
        {
            _raster = raster;
            Arrange(keepOnScreen: true);
        }
    }

    // Place: where it was left, else in the screen's bottom right corner; never lost off screen

    private void Place()
    {
        var size = AppWindow.Size;
        if (_skins.Settings.MiniPlayerPlace is { } saved
            && DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, Math.Max(size.Width, 1), Math.Max(Math.Min(size.Height, 20), 1)), DisplayAreaFallback.None) is { } display)
        {
            var area = display.WorkArea;
            AppWindow.Move(new PointInt32(
                Math.Clamp(saved.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width)),
                Math.Clamp(saved.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height))));
            return;
        }

        var mainId = App.MainWindow?.AppWindow.Id;
        var work = (mainId is { } id ? DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary) : DisplayArea.Primary).WorkArea;
        var margin = (int)Math.Round(CornerMargin * _raster);
        AppWindow.Move(new PointInt32(
            Math.Max(work.X, work.X + work.Width - size.Width - margin),
            Math.Max(work.Y, work.Y + work.Height - size.Height - margin)));
    }

    private void KeepOnScreen()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var y = Math.Max(area.Y, Math.Min(position.Y, area.Y + area.Height - size.Height));
        if (y != position.Y)
        {
            AppWindow.Move(new PointInt32(position.X, y));
        }
    }

    private void SavePlace()
    {
        if (_services.IsDemo)
        {
            return;
        }

        var position = AppWindow.Position;
        var place = new WindowPlacement { X = position.X, Y = position.Y };
        if (place != _skins.Settings.MiniPlayerPlace)
        {
            _skins.Settings.MiniPlayerPlace = place;
            _services.SaveSettings();
        }
    }

    // Moving: the skinned windows call these while their title bar (or anything not a control) is dragged

    public void BeginMove()
    {
        if (GetCursorPos(out _moveCursor))
        {
            _moveWindow = AppWindow.Position;
            _movingWindow = true;
        }
    }

    public void Move()
    {
        if (!_movingWindow || !GetCursorPos(out var cursor))
        {
            return;
        }

        var size = AppWindow.Size;
        var x = _moveWindow.X + cursor.X - _moveCursor.X;
        var y = _moveWindow.Y + cursor.Y - _moveCursor.Y;
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;
        var snap = (int)Math.Round(SnapDistance * _raster);
        x = WindowSnap.Snap(x, size.Width, area.X, area.Width, snap);
        y = WindowSnap.Snap(y, size.Height, area.Y, area.Height, snap);
        var position = AppWindow.Position;
        if (x != position.X || y != position.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }
    }

    public void EndMove()
    {
        if (_movingWindow)
        {
            _movingWindow = false;
            SavePlace();
        }
    }

    // The window's life

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // The skins draw their title bars lit while the window has focus, as Winamp did.
        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        _main.SetWindowActive(active);
        _equalizer.SetWindowActive(active);
        _playlist.SetWindowActive(active);
        if (active)
        {
            _host.Focus(FocusState.Programmatic);
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Minimised or hidden: clocks and motion rest.
        var shown = sender.IsVisible && !IsIconic(Hwnd);
        if (shown != _shown)
        {
            _shown = shown;
            _main.SetWindowShown(shown);
            _playlist.SetWindowShown(shown);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4 or the taskbar's Close: back to the full window rather than a hidden app.
        if (!_closingForGood)
        {
            args.Cancel = true;
            Leave();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        SavePlace();
        _skins.MiniOptionsChanged -= OnMiniOptionsChanged;
        if (_host.XamlRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
        }

        // The skinned windows let go of the player's events when they leave the tree.
        Content = null;
    }

    // Keys: Winamp's Z X C V B, the space bar, the arrows, and Ctrl+M back to the full window

    private void AddKeys(UIElement element)
    {
        var player = _services.Player;
        Add(VirtualKey.Z, VirtualKeyModifiers.None, () => _ = player.PreviousAsync());
        Add(VirtualKey.X, VirtualKeyModifiers.None, () => _ = player.State.IsPlaying ? player.SeekAsync(TimeSpan.Zero) : player.PlayAsync());
        Add(VirtualKey.C, VirtualKeyModifiers.None, () => _ = player.TogglePlayPauseAsync());
        Add(VirtualKey.Space, VirtualKeyModifiers.None, () => _ = player.TogglePlayPauseAsync());
        Add(VirtualKey.V, VirtualKeyModifiers.None, () =>
        {
            _ = player.PauseAsync();
            _ = player.SeekAsync(TimeSpan.Zero);
        });
        Add(VirtualKey.B, VirtualKeyModifiers.None, () => _ = player.NextAsync());
        Add(VirtualKey.Up, VirtualKeyModifiers.None, () => _ = player.SetVolumeAsync(Math.Min(1, player.State.Volume + 0.05)));
        Add(VirtualKey.Down, VirtualKeyModifiers.None, () => _ = player.SetVolumeAsync(Math.Max(0, player.State.Volume - 0.05)));
        Add(VirtualKey.Left, VirtualKeyModifiers.None, () => Skip(-5));
        Add(VirtualKey.Right, VirtualKeyModifiers.None, () => Skip(5));
        Add(VirtualKey.M, VirtualKeyModifiers.Control, Leave);
        Add(VirtualKey.Escape, VirtualKeyModifiers.None, Leave);

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                action();
            };
            element.KeyboardAccelerators.Add(accelerator);
        }

        void Skip(int seconds)
        {
            var state = player.State;
            if (state.HasTrack && state.CanSeek && state.Duration > TimeSpan.Zero)
            {
                var to = state.PositionAt(DateTimeOffset.UtcNow) + TimeSpan.FromSeconds(seconds);
                _ = player.SeekAsync(TimeSpan.FromTicks(Math.Clamp(to.Ticks, 0, state.Duration.Ticks)));
            }
        }
    }

    /// <summary>Square, borderless edges, so the skin's own frame is the window's edge.</summary>
    private void SquareCorners()
    {
        var hwnd = Hwnd;
        var corner = DwmCornerDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));
        var border = unchecked((int)DwmColorNone);
        _ = DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
