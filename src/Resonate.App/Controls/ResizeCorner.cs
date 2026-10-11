using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Resonate.App.Controls;

/// <summary>
/// A small square in one corner of a borderless window (the Winamp mini
/// player) that shows the diagonal resize pointer; the window resizes itself
/// while it is dragged (see MiniPlayerWindow).
/// </summary>
internal sealed partial class ResizeCorner : Grid
{
    /// <summary>How far from the corner it reaches, in the window's units.</summary>
    private const double Reach = 10;

    public ResizeCorner(bool left, bool top)
    {
        Left = left;
        Top = top;
        Width = Reach;
        Height = Reach;
        HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        // See-through, but it takes the pointer.
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(left == top
            ? InputSystemCursorShape.SizeNorthwestSoutheast
            : InputSystemCursorShape.SizeNortheastSouthwest);
    }

    /// <summary>On the window's left side (else its right).</summary>
    public bool Left { get; }

    /// <summary>On the window's top (else its bottom).</summary>
    public bool Top { get; }
}
