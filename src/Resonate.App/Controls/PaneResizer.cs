using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Resonate.App.Controls;

/// <summary>
/// A thin strip along a pane's edge that resizes the pane by dragging, with
/// the left-right arrows cursor and a line in the accent colour while the
/// pointer is on it. It only reports how far the pointer has moved since
/// the drag started; the window decides the width. A double-click asks for
/// the default width. Built in code, so Native AOT never looks up its parts.
/// </summary>
public sealed partial class PaneResizer : Grid
{
    private readonly Rectangle _line = new()
    {
        Width = 2,
        RadiusX = 1,
        RadiusY = 1,
        HorizontalAlignment = HorizontalAlignment.Center,
        Opacity = 0,
    };

    private double _startX;
    private bool _dragging;

    public PaneResizer()
    {
        // A background, even a clear one, is what lets the strip be clicked.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        _line.Fill = App.Services.Theme.GetBrush("ResonateAccentBrush");
        _line.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Children.Add(_line);

        PointerEntered += (_, _) => _line.Opacity = 1;
        PointerExited += (_, _) =>
        {
            if (!_dragging)
            {
                _line.Opacity = 0;
            }
        };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => EndDrag(e.Pointer);
        PointerCaptureLost += (_, e) => EndDrag(e.Pointer);
        DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ResetRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised when a drag starts; the window notes the pane's width then.</summary>
    public event EventHandler? DragStarted;

    /// <summary>Raised as the pointer moves: how far right it is from where the drag started, in pixels.</summary>
    public event EventHandler<double>? Dragged;

    /// <summary>Raised when the drag ends; the window keeps the width.</summary>
    public event EventHandler? DragCompleted;

    /// <summary>Raised on a double-click: back to the default width.</summary>
    public event EventHandler? ResetRequested;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer))
        {
            return;
        }

        // Measured against the window, which stays put while the pane (and this strip) moves.
        e.Handled = true;
        _dragging = true;
        _startX = point.Position.X;
        _line.Opacity = 1;
        DragStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true;
            Dragged?.Invoke(this, e.GetCurrentPoint(null).Position.X - _startX);
        }
    }

    private void EndDrag(Pointer pointer)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCapture(pointer);
        _line.Opacity = 0;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
