using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Themes;
using Windows.Graphics;

namespace Resonate.App;

/// <summary>
/// Window shapes, a built-in plugin: instead of a separate mini player the
/// window rearranges itself by its size, once the user lets go of a resize,
/// snaps or maximises it (never during a live drag): Full, Compact (the
/// sidebar folds into a rail), Column (a big cover over the player) and
/// Strip (just the player, which can stay above other windows). A button in
/// the title bar picks a shape and sizes the window for it. The player's
/// slot is never rebuilt or moved out of the shell; panels are only hidden,
/// and come back as they were. The shapes themselves are worked out in
/// <see cref="WindowShapes"/>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A resize that is not a drag (snapping, maximising, the menu) settles this long before the shape follows.</summary>
    private static readonly TimeSpan ShapeSettleDelay = TimeSpan.FromMilliseconds(150);

    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int SwRestore = 9;

    private readonly List<(UIElement Element, Visibility Visibility)> _hiddenByShape = [];
    private WindowShape _shape = WindowShape.Full;
    private IDisposable? _shapeHook;
    private DispatcherQueueTimer? _shapeTimer;
    private bool _inSizeMove;
    private Button? _shapeButton;
    private ShapeRail? _rail;
    private NowPlayingColumn? _column;
    private Grid? _stripControls;
    private Button? _pinButton;
    private GridLength _titleRowHeight;
    private (bool Queue, bool Settings, bool Lyrics)? _panesBeforeShape;

    /// <summary>The last size the window had in the Full shape, and the last before it became a strip (inside, device-independent pixels).</summary>
    private (double Width, double Height)? _fullSize;
    private (double Width, double Height)? _sizeBeforeStrip;

    /// <summary>The window shows no page or sidebar (Column and Strip): the player sits under everything.</summary>
    private bool ShapeHidesPanels => _shape is WindowShape.Column or WindowShape.Strip;

    private bool WindowShapesOn => _shapeHook is not null;

    partial void SetUpWindowShapes()
    {
        _services.BuiltIns.Changed += (_, id) =>
        {
            if (id == BuiltInPlugins.WindowShapes)
            {
                TurnWindowShapes(_services.BuiltIns.IsOn(id), starting: false);
            }
        };

        // Signing out shows the sign-in page in the usual window; signing in gives the shape back.
        ShellGrid.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (WindowShapesOn)
            {
                UpdateShape(animate: false);
            }
        });
        QueueOpenChanged += (_, _) => OnShapePaneChanged();
        ContentFrame.Navigated += (_, _) => _rail?.Select(_currentKey);

        if (_services.BuiltIns.IsOn(BuiltInPlugins.WindowShapes))
        {
            TurnWindowShapes(true, starting: true);
        }
    }

    private void TurnWindowShapes(bool on, bool starting)
    {
        if (on == WindowShapesOn)
        {
            return;
        }

        if (on)
        {
            _shapeHook = WindowHook.Listen(Hwnd, OnShapeMessage);
            AppWindow.Changed += OnShapeWindowChanged;
            SetShapeMinimumSize(WindowShapes.MinimumWidth, WindowShapes.MinimumHeight);
            if (starting)
            {
                RestoreSmallPlacement();
            }

            if (_services.Settings.WindowShapesButton)
            {
                AddShapeButton();
            }

            UpdateShape(animate: false);
            return;
        }

        _shapeHook?.Dispose();
        _shapeHook = null;
        AppWindow.Changed -= OnShapeWindowChanged;
        _shapeTimer?.Stop();
        _inSizeMove = false;
        var wasShaped = _shape != WindowShape.Full;
        ApplyShape(WindowShape.Full, animate: false);
        RemoveShapeButton();
        if (wasShaped && !IsZoomed(Hwnd))
        {
            ResizeWindow(WindowShapes.PickedSize(WindowShape.Full, ScreenSize(), _fullSize));
        }

        UpdateMinimumSize(grow: false);
    }

    // ---- When the shape changes ----

    /// <summary>Inside the window's message handling: a drag starts or ends.</summary>
    private void OnShapeMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WindowHook.WmEnterSizeMove)
        {
            _inSizeMove = true;
            _shapeTimer?.Stop();
        }
        else if (message == WindowHook.WmExitSizeMove)
        {
            _inSizeMove = false;
            DispatcherQueue.TryEnqueue(() => UpdateShape(animate: true));
        }
    }

    /// <summary>Sizes that change without a drag (snapping, maximising, the menu) settle first.</summary>
    private void OnShapeWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_inSizeMove || !(args.DidSizeChange || args.DidPresenterChange))
        {
            return;
        }

        if (_shapeTimer is null)
        {
            _shapeTimer = DispatcherQueue.CreateTimer();
            _shapeTimer.Interval = ShapeSettleDelay;
            _shapeTimer.IsRepeating = false;
            _shapeTimer.Tick += (_, _) => UpdateShape(animate: true);
        }

        _shapeTimer.Stop();
        _shapeTimer.Start();
    }

    /// <summary>Takes the shape the window's size calls for (Full while signing in or with the plugin off).</summary>
    private void UpdateShape(bool animate)
    {
        var hwnd = Hwnd;
        if (IsIconic(hwnd))
        {
            // Minimised: it comes back in the shape it had.
            return;
        }

        var size = ClientSizeInDips();
        var shape = WindowShapesOn && ShellGrid.Visibility == Visibility.Visible
            ? WindowShapes.For(size.Width, size.Height)
            : WindowShape.Full;

        // The shapes the user left out (the plugin's settings) give way to the next larger one.
        var settings = _services.Settings;
        if (shape == WindowShape.Strip && !settings.WindowShapesStrip)
        {
            shape = settings.WindowShapesColumn ? WindowShape.Column : WindowShape.Compact;
        }

        if (shape == WindowShape.Column && !settings.WindowShapesColumn)
        {
            shape = WindowShape.Compact;
        }
        if (shape != WindowShape.Strip)
        {
            _sizeBeforeStrip = size;
        }

        if (shape == WindowShape.Full && !IsZoomed(hwnd))
        {
            _fullSize = size;
        }

        ApplyShape(shape, animate);
        if (shape == WindowShape.Strip)
        {
            FitStrip();
        }
    }

    private void ApplyShape(WindowShape shape, bool animate)
    {
        if (shape == _shape)
        {
            return;
        }

        var previous = _shape;
        _shape = shape;

        // Undo the shape before, then make this one.
        if (previous == WindowShape.Strip)
        {
            LeaveStrip();
        }

        if (_rail is not null)
        {
            Sidebar.Children.Remove(_rail);
            _rail = null;
        }

        if (_column is not null)
        {
            ShellGrid.Children.Remove(_column);
            _column = null;
        }

        foreach (var (element, visibility) in _hiddenByShape)
        {
            element.Visibility = visibility;
        }

        _hiddenByShape.Clear();

        switch (shape)
        {
            case WindowShape.Compact:
                EnterCompact();
                break;
            case WindowShape.Column:
                EnterColumn();
                break;
            case WindowShape.Strip:
                EnterStrip();
                break;
        }

        SidebarColumn.MaxWidth = shape switch
        {
            WindowShape.Compact => WindowShapes.RailWidth,
            WindowShape.Column or WindowShape.Strip => 0,
            _ => double.PositiveInfinity,
        };

        if (!ShapeHidesPanels && _panesBeforeShape is { } panes)
        {
            // Back to a shape with the page: the queue or Settings opens again if it was open.
            _panesBeforeShape = null;
            if (panes.Queue)
            {
                ShowQueue(true);
            }
            else if (panes.Settings)
            {
                ShowSettings(true);
            }
            else if (panes.Lyrics)
            {
                ShowLyrics(true);
            }
        }

        PlacePanesAsSheet();
        ApplyPlayerPlacement();
        if (animate)
        {
            GlideIntoShape();
        }
    }

    private void EnterCompact()
    {
        foreach (var child in Sidebar.Children)
        {
            Hide(child);
        }

        Hide(SidebarSplitter);
        _rail = new ShapeRail(NavItems, Playlists, Open, item => _ = PlayPlaylistAsync(item, shuffle: null), ToggleSettings);
        Grid.SetRowSpan(_rail, Sidebar.RowDefinitions.Count);
        Sidebar.Children.Add(_rail);
        _rail.Select(_currentKey);
    }

    private void EnterColumn()
    {
        HidePanels();
        _column = new NowPlayingColumn(_services);
        Grid.SetRow(_column, PlayerPlacement.PanelsRow);
        Grid.SetColumnSpan(_column, ToTheLastColumn);
        // Under the weather that passes behind the player, as the page is.
        ShellGrid.Children.Insert(ShellGrid.Children.IndexOf(WeatherBehindPlayer), _column);
    }

    /// <summary>Hides the sidebar and the page, closing the queue, Settings or the lyrics for now.</summary>
    private void HidePanels()
    {
        _panesBeforeShape ??= (QueuePane.IsOpen, SettingsPane.IsOpen, LyricsPane.IsOpen);
        ShowQueue(false);
        ShowSettings(false);
        ShowLyrics(false);
        Hide(Sidebar);
        Hide(SidebarElevation);
        Hide(SidebarSplitter);
        Hide(ContentPanel);

        // The page's shadow, which has no name of its own.
        foreach (var child in ShellGrid.Children)
        {
            if (child is Elevation elevation && !ReferenceEquals(elevation, SidebarElevation))
            {
                Hide(elevation);
            }
        }
    }

    private void Hide(UIElement element)
    {
        _hiddenByShape.Add((element, element.Visibility));
        element.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// In a column the queue, Settings or the lyrics open over the cover, across the
    /// window; in a strip, opening one gives the window its size back.
    /// </summary>
    private void OnShapePaneChanged()
    {
        if (!WindowShapesOn)
        {
            return;
        }

        var open = QueuePane.IsOpen || SettingsPane.IsOpen || LyricsPane.IsOpen;
        if (open && ShapeHidesPanels)
        {
            // The user opened it here: it is theirs now, not one to reopen later.
            _panesBeforeShape = null;
        }

        if (open && _shape == WindowShape.Strip)
        {
            ExpandStrip();
            return;
        }

        PlacePanesAsSheet();
    }

    private void PlacePanesAsSheet()
    {
        var open = QueuePane.IsOpen || SettingsPane.IsOpen || LyricsPane.IsOpen;
        var sheet = _shape == WindowShape.Column && open;
        foreach (var pane in new FrameworkElement[] { QueuePane, SettingsPane, LyricsPane })
        {
            Grid.SetColumn(pane, sheet ? 0 : 2);
            Grid.SetColumnSpan(pane, sheet ? ToTheLastColumn : 1);
            Canvas.SetZIndex(pane, sheet ? 1 : 0);
        }

        RightSplitter.Visibility = !sheet && open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A short rise and fade as the window settles into its shape; none while Windows' animations are off.</summary>
    private void GlideIntoShape()
    {
        if (!_services.Theme.AnimationsEnabled)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(ShellGrid, true);
        var visual = ElementCompositionPreview.GetElementVisual(ShellGrid);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1));
        var duration = TimeSpan.FromMilliseconds(240);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.5f);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = duration;
        visual.StartAnimation("Opacity", fade);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 12, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = duration;
        visual.StartAnimation("Translation", rise);
    }

    // ---- The strip ----

    private void EnterStrip()
    {
        HidePanels();
        Hide(AppTitleBar);
        _titleRowHeight = RootGrid.RowDefinitions[0].Height;
        RootGrid.RowDefinitions[0].Height = new GridLength(0);

        // The strip's own buttons take the place of the caption buttons.
        _presenter?.SetBorderAndTitleBar(true, false);
        _presenter?.IsAlwaysOnTop = _services.Settings.WindowShapesPinned;

        _stripControls = BuildStripControls();
        PlayerSlot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PlayerSlot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_stripControls, 1);
        PlayerSlot.Children.Add(_stripControls);
        PlayerSlot.SizeChanged += OnStripPlayerSizeChanged;
    }

    private void LeaveStrip()
    {
        PlayerSlot.SizeChanged -= OnStripPlayerSizeChanged;
        if (_stripControls is not null)
        {
            PlayerSlot.Children.Remove(_stripControls);
            PlayerSlot.ColumnDefinitions.Clear();
            _stripControls = null;
            _pinButton = null;
        }

        RootGrid.RowDefinitions[0].Height = _titleRowHeight;
        _presenter?.IsAlwaysOnTop = false;
        _presenter?.SetBorderAndTitleBar(true, true);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyCaptionButtonColors();
    }

    /// <summary>Pin and close above, a handle to move the window and the way back below.</summary>
    private Grid BuildStripControls()
    {
        var controls = new Grid { Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _pinButton = StripButton(string.Empty, string.Empty);
        _pinButton.Click += (_, _) =>
        {
            _services.Settings.WindowShapesPinned = !_services.Settings.WindowShapesPinned;
            _services.SaveSettings();
            _presenter?.IsAlwaysOnTop = _services.Settings.WindowShapesPinned;
            ShowPin();
        };
        ShowPin();
        controls.Children.Add(_pinButton);

        var close = StripButton("", "Close");
        close.Click += (_, _) => Quit();
        Grid.SetColumn(close, 1);
        controls.Children.Add(close);

        // Dragging the handle moves the window, as a title bar would.
        var handle = new Border
        {
            Width = 30,
            Height = 30,
            Background = _services.Theme.GetBrush("ResonateTransparentBrush"),
            Child = new FontIcon { Glyph = "", FontSize = 12, Foreground = _services.Theme.GetBrush("ResonateTextTertiaryBrush") },
        };
        AutomationProperties.SetName(handle, "Move");
        ToolTipService.SetToolTip(handle, "Drag to move");
        handle.PointerPressed += OnStripHandlePressed;
        Grid.SetRow(handle, 1);
        controls.Children.Add(handle);

        var expand = StripButton("", "Back to the full window");
        expand.Click += (_, _) => ExpandStrip();
        Grid.SetRow(expand, 1);
        Grid.SetColumn(expand, 1);
        controls.Children.Add(expand);
        return controls;
    }

    private static Button StripButton(string glyph, string name)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
            Width = 30,
            Height = 30,
            FontSize = 12,
            Content = glyph,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private void ShowPin()
    {
        if (_pinButton is not { } pin)
        {
            return;
        }

        var pinned = _services.Settings.WindowShapesPinned;
        pin.Content = pinned ? "" : "";
        var name = pinned ? "Stop keeping on top" : "Keep on top of other windows";
        AutomationProperties.SetName(pin, name);
        ToolTipService.SetToolTip(pin, name);
        if (pinned)
        {
            pin.Foreground = _services.Theme.GetBrush("ResonateAccentBrush");
        }
        else
        {
            pin.ClearValue(Control.ForegroundProperty);
        }
    }

    private void OnStripHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            ReleaseCapture();
            SendMessage(Hwnd, WmNcLButtonDown, HtCaption, 0);
        }
    }

    /// <summary>The window as it was before it became a strip, or the usual window.</summary>
    private void ExpandStrip() =>
        ResizeWindow(_sizeBeforeStrip is { } before && WindowShapes.For(before.Width, before.Height) != WindowShape.Strip
            ? before
            : WindowShapes.PickedSize(WindowShape.Full, ScreenSize(), _fullSize));

    private void OnStripPlayerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The bar grows or shrinks with its width; the strip follows once a drag ends.
        if (!_inSizeMove && e.NewSize.Height != e.PreviousSize.Height)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FitStrip);
        }
    }

    /// <summary>The strip is exactly as tall as its player.</summary>
    private void FitStrip()
    {
        if (_shape != WindowShape.Strip || _inSizeMove || PlayerSlot.ActualHeight <= 0 || IsZoomed(Hwnd))
        {
            return;
        }

        var height = PlayerSlot.ActualHeight + PlayerSlot.Margin.Top + PlayerSlot.Margin.Bottom + ShellGrid.Padding.Top + ShellGrid.Padding.Bottom;
        var scale = GetDpiForWindow(Hwnd) / 96.0;
        var wanted = (int)Math.Ceiling(height * scale);
        var client = AppWindow.ClientSize;
        if (Math.Abs(client.Height - wanted) > 1)
        {
            AppWindow.ResizeClient(new SizeInt32(client.Width, wanted));
        }
    }

    /// <summary>The plugin's settings changed: the title bar's button and the shapes it may take.</summary>
    internal void FollowShapeOptions()
    {
        if (!WindowShapesOn)
        {
            return;
        }

        RemoveShapeButton();
        if (_services.Settings.WindowShapesButton)
        {
            AddShapeButton();
        }

        UpdateShape(animate: true);
    }

    // ---- Picking a shape ----

    /// <summary>A button in the title bar, left of Settings and the mini player button, that picks a shape.</summary>
    private void AddShapeButton()
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var (shape, name) in new[]
            {
                (WindowShape.Full, "Full"),
                (WindowShape.Compact, "Compact"),
                (WindowShape.Column, "Column"),
                (WindowShape.Strip, "Strip"),
            })
            {
                var item = new RadioMenuFlyoutItem { Text = name, GroupName = "shape", IsChecked = shape == _shape };
                item.Click += (_, _) => PickShape(shape);
                menu.Items.Add(item);
            }
        };

        _shapeButton = new Button
        {
            Style = (Style)Application.Current.Resources["ResonateIconButtonStyle"],
            Width = 40,
            Height = 32,
            FontSize = 13,
            Content = "",
            Flyout = menu,
        };
        AutomationProperties.SetName(_shapeButton, "Window shape");
        ToolTipService.SetToolTip(_shapeButton, "Window shape");
        _shapeButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        TitleBarButtons.Children.Insert(0, _shapeButton);
    }

    private void RemoveShapeButton()
    {
        if (_shapeButton is null)
        {
            return;
        }

        TitleBarButtons.Children.Remove(_shapeButton);
        _shapeButton = null;
        UpdateTitleBarPassthrough();
    }

    /// <summary>Sizes the window for <paramref name="shape"/>; the shape follows once the size settles.</summary>
    private void PickShape(WindowShape shape)
    {
        if (shape == _shape)
        {
            return;
        }

        ResizeWindow(WindowShapes.PickedSize(shape, ScreenSize(), shape == WindowShape.Full ? _fullSize : null));
    }

    /// <summary>Gives the window's inside this size (device-independent pixels), restored if it was maximised, and keeps it on its screen.</summary>
    private void ResizeWindow((double Width, double Height) size)
    {
        var hwnd = Hwnd;
        if (IsZoomed(hwnd) || IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)));

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var position = AppWindow.Position;
        var outer = AppWindow.Size;
        var x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - outer.Width));
        var y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - outer.Height));
        if (x != position.X || y != position.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }
    }

    private (double Width, double Height) ClientSizeInDips()
    {
        var scale = GetDpiForWindow(Hwnd) / 96.0;
        var size = AppWindow.ClientSize;
        return (size.Width / scale, size.Height / scale);
    }

    /// <summary>The work area of the window's screen, in device-independent pixels.</summary>
    private (double Width, double Height) ScreenSize()
    {
        var scale = GetDpiForWindow(Hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        return (area.Width / scale, area.Height / scale);
    }

    /// <summary>How small the window may be made, in device-independent pixels.</summary>
    private void SetShapeMinimumSize(double width, double height)
    {
        if (_presenter is null)
        {
            return;
        }

        var scale = GetDpiForWindow(Hwnd) / 96.0;
        _presenter.PreferredMinimumWidth = (int)Math.Round(width * scale);
        _presenter.PreferredMinimumHeight = (int)Math.Round(height * scale);
    }

    /// <summary>
    /// At start-up the window was placed before the plugin let it be small,
    /// so a strip or column left last time comes back here.
    /// </summary>
    private void RestoreSmallPlacement()
    {
        if (_services.IsDemo || _services.Settings.Window is not { Maximized: false } saved)
        {
            return;
        }

        var size = AppWindow.Size;
        if (saved.Width >= size.Width && saved.Height >= size.Height)
        {
            return;
        }

        var top = new RectInt32(saved.X, saved.Y, saved.Width, Math.Min(saved.Height, KeepOnScreen / 2));
        if (DisplayArea.GetFromRect(top, DisplayAreaFallback.None) is not null)
        {
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
