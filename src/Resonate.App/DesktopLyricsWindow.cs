using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Services;
using Windows.Graphics;

namespace Resonate.App;

/// <summary>
/// Desktop lyrics, a built-in plugin: the line being sung and the next one in
/// a small frosted window that stays on top of every app, never in the task
/// switcher, never taking the keyboard. Dragged anywhere by itself, it opens
/// where it was left (above the taskbar the first time). Without synced
/// lyrics it shows the song and its artists.
/// </summary>
internal sealed partial class DesktopLyricsWindow : Window
{
    private const double BaseWidth = 860;
    private const double BaseHeight = 104;

    private readonly AppServices _services;
    private readonly OverlappedPresenter _presenter;
    private readonly TextBlock _line;
    private readonly TextBlock _next;
    private readonly Grid _root;
    private (string?, string?, string?, string?)? _shown;
    private bool _moving;
    private CursorPoint _moveCursor;
    private PointInt32 _moveWindow;

    public DesktopLyricsWindow(AppServices services)
    {
        _services = services;
        AppWindow.Title = "Resonate lyrics";
        AppWindow.IsShownInSwitchers = false;
        _presenter = OverlappedPresenter.Create();
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsAlwaysOnTop = true;
        _presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(_presenter);
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var resources = Application.Current.Resources;
        _line = new TextBlock
        {
            Style = (Style)resources["ResonateTitleTextStyle"],
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _next = new TextBlock
        {
            Style = (Style)resources["ResonateSecondaryTextStyle"],
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _root = new Grid
        {
            Padding = new Thickness(24, 10, 24, 10),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            RequestedTheme = App.MainWindow?.Content is FrameworkElement main ? main.ActualTheme : ElementTheme.Dark,
            Children =
            {
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children = { _line, _next },
                },
            },
        };
        _root.PointerPressed += (_, e) =>
        {
            if (GetCursorPos(out _moveCursor))
            {
                _moveWindow = AppWindow.Position;
                _moving = true;
                _root.CapturePointer(e.Pointer);
            }
        };
        _root.PointerMoved += (_, _) => Move();
        _root.PointerReleased += (_, e) =>
        {
            _root.ReleasePointerCapture(e.Pointer);
            EndMove();
        };
        _root.PointerCaptureLost += (_, _) => EndMove();
        Content = _root;
        ApplySize();
    }

    /// <summary>Shows the window without taking the keyboard from the app in front.</summary>
    public void ShowQuietly() => AppWindow.Show(activateWindow: false);

    /// <summary>The sung line and the next one, or the song when there are no synced lyrics; null hides the text.</summary>
    public void Show((string? Line, string? Next, string Title, string? Artists)? lyrics)
    {
        var shown = lyrics is { } l ? (l.Line, l.Next, l.Title, l.Artists) : default((string?, string?, string?, string?)?);
        if (Equals(shown, _shown))
        {
            return;
        }

        _shown = shown;
        if (lyrics is not { } now)
        {
            _line.Text = string.Empty;
            _next.Text = string.Empty;
            return;
        }

        if (now.Line is null && now.Next is null)
        {
            _line.Text = now.Title;
            _next.Text = now.Artists ?? string.Empty;
        }
        else
        {
            _line.Text = now.Line ?? now.Next ?? string.Empty;
            _next.Text = now.Line is null ? string.Empty : now.Next ?? string.Empty;
        }
    }

    /// <summary>Sizes the window and its text for the user's Size, and puts it where it was left.</summary>
    public void ApplySize()
    {
        var size = Math.Clamp(_services.Settings.DesktopLyricsSize, 60, 250) / 100.0;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        _line.FontSize = 26 * size;
        _next.FontSize = 16 * size;
        var width = (int)Math.Round(BaseWidth * size * scale);
        var height = (int)Math.Round(BaseHeight * size * scale);
        if (_services.Settings.DesktopLyricsPlace is { } place)
        {
            AppWindow.MoveAndResize(new RectInt32(place.X, place.Y, width, height));
            return;
        }

        // The first time: centred just above the taskbar of the main screen.
        var area = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - width) / 2), area.Y + area.Height - height - (int)Math.Round(24 * scale), width, height));
    }

    private void Move()
    {
        if (!_moving || !GetCursorPos(out var cursor))
        {
            return;
        }

        AppWindow.Move(new PointInt32(_moveWindow.X + cursor.X - _moveCursor.X, _moveWindow.Y + cursor.Y - _moveCursor.Y));
    }

    private void EndMove()
    {
        if (!_moving)
        {
            return;
        }

        _moving = false;
        var position = AppWindow.Position;
        _services.Settings.DesktopLyricsPlace = new WindowPlacement { X = position.X, Y = position.Y };
        _services.SaveSettings();
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
}
