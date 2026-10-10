using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// Lays its children out as if it were <see cref="Factor"/> times smaller,
/// then draws them that much larger, so they fill it exactly: App size
/// (Settings, Layout, Size). XAML draws text, shapes and icons sharp at the
/// size they end up on screen. The box itself carries the ScaleTransform,
/// so nothing may set its RenderTransform. At 1 nothing is transformed.
/// </summary>
/// <remarks>
/// Not each child: a child that asks for more room than it gets (the
/// shell, now and then, in a window a little short for it) is cut by XAML
/// to the room it got, in its parent's units and before its own transform.
/// With the transform on the child that cut stayed 1/Factor of the window,
/// so at 125 % only the top left four fifths showed (the owner's window,
/// 10 October 2026). Here the cut lies inside the box and grows with it.
/// </remarks>
public sealed partial class ScaleBox : Panel
{
    private double _factor = 1;
    private Size _clipped;

    /// <summary>How much larger the children are drawn (UIElement.Scale is something else).</summary>
    public double Factor
    {
        get => _factor;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value == _factor)
            {
                return;
            }

            _factor = value;
            RenderTransform = value == 1 ? null : new ScaleTransform { ScaleX = value, ScaleY = value };
            _clipped = default;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var scale = _factor;
        var inner = new Size(availableSize.Width / scale, availableSize.Height / scale);
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(inner);
            width = Math.Max(width, child.DesiredSize.Width * scale);
            height = Math.Max(height, child.DesiredSize.Height * scale);
        }

        // Never more than it is given, so the box itself is never cut by its parent.
        return new Size(Math.Min(width, availableSize.Width), Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var scale = _factor;
        var inner = new Rect(0, 0, finalSize.Width / scale, finalSize.Height / scale);
        foreach (var child in Children)
        {
            child.Arrange(inner);
        }

        // Layout rounds the children before they are scaled, so they may reach
        // a fraction of a pixel past the edge, which would make pictures of the
        // window (look switching) larger than the window. The clip is in the
        // box's own units, before its transform scales it to the box's size.
        if (finalSize != _clipped)
        {
            _clipped = finalSize;
            Clip = new RectangleGeometry { Rect = inner };
        }

        return finalSize;
    }
}
