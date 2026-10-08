using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>
/// The grip in the gap between two panels (the sidebar and the page, the
/// page and the queue). Dragging it makes the panel beside it wider or
/// narrower, double-clicking it gives the panel its usual width back, and
/// with the keyboard the arrow keys move it. A thin line in the accent
/// colour shows while the pointer is over it. The window does the resizing
/// (see <see cref="Dragged"/>); the grip only reports how far it moved.
/// </summary>
public sealed partial class PaneSplitter : ContentControl
{
    /// <summary>How wide the grip is: wider than the gap, so it is easy to catch.</summary>
    public const double GripWidth = 12;

    /// <summary>How far one press of an arrow key moves it (four times that with Ctrl).</summary>
    public const double KeyStep = 16;

    private readonly Border _line;
    private uint? _pointer;
    private double _startX;
    private bool _over;

    public PaneSplitter()
    {
        Width = GripWidth;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        var theme = App.Services.Theme;
        _line = new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 12),
            CornerRadius = new CornerRadius(1),
            Background = theme.GetBrush("ResonateAccentBrush"),
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) },
        };

        // A see-through background, so the whole grip catches the pointer.
        Content = new Grid { Background = theme.GetBrush("ResonateTransparentBrush"), Children = { _line } };
    }

    /// <summary>A drag began: remember the panel's width now.</summary>
    public event EventHandler? DragStarted;

    /// <summary>During a drag: how far the pointer has moved right since it began (negative to the left), in view pixels.</summary>
    public event EventHandler<double>? Dragged;

    /// <summary>The drag ended: keep the width.</summary>
    public event EventHandler? DragCompleted;

    /// <summary>A double-click: back to the usual width.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>An arrow key: move this far right (negative to the left).</summary>
    public event EventHandler<double>? Stepped;

    /// <summary>What screen readers call it, such as "Resize the sidebar".</summary>
    public string Label
    {
        get => AutomationProperties.GetName(this);
        set
        {
            AutomationProperties.SetName(this, value);
            ToolTipService.SetToolTip(this, value + " (drag, or double-click for the usual size)");
        }
    }

    public bool IsDragging => _pointer is not null;

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        _over = true;
        ShowLine();
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        _over = false;
        ShowLine();
    }

    /// <summary>
    /// The grid the grip sits in: its units are the panels' widths at any App
    /// size, where the window's are not (see ScaleBox).
    /// </summary>
    private UIElement? Reference => Parent as UIElement;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(Reference);
        if (_pointer is not null || !point.Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer))
        {
            return;
        }

        e.Handled = true;

        // Measured against the panels around the grip, which do not move while it does.
        _pointer = e.Pointer.PointerId;
        _startX = point.Position.X;
        ShowLine();
        DragStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pointer != e.Pointer.PointerId)
        {
            return;
        }

        e.Handled = true;
        Dragged?.Invoke(this, e.GetCurrentPoint(Reference).Position.X - _startX);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer == e.Pointer.PointerId)
        {
            e.Handled = true;
            ReleasePointerCapture(e.Pointer);
            EndDrag();
        }
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pointer == e.Pointer.PointerId)
        {
            EndDrag();
        }
    }

    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        base.OnDoubleTapped(e);
        e.Handled = true;
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        var step = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down)
            ? KeyStep * 4
            : KeyStep;
        switch (e.Key)
        {
            case VirtualKey.Left:
                e.Handled = true;
                Stepped?.Invoke(this, -step);
                break;
            case VirtualKey.Right:
                e.Handled = true;
                Stepped?.Invoke(this, step);
                break;
            case VirtualKey.Home or VirtualKey.Enter:
                e.Handled = true;
                ResetRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        ShowLine();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        ShowLine();
    }

    private void EndDrag()
    {
        _pointer = null;
        ShowLine();
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ShowLine() =>
        _line.Opacity = _over || _pointer is not null || FocusState == FocusState.Keyboard ? 1 : 0;
}
