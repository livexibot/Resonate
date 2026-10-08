using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// Lays its children out as if it were <see cref="Factor"/> times smaller,
/// then draws them that much larger, so they fill it exactly: App size
/// (Settings, Look, Size). XAML draws text, shapes and icons sharp at the
/// size they end up on screen. Each child gets a ScaleTransform of its own,
/// so it must not set a RenderTransform itself. At 1 nothing is transformed.
/// </summary>
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
            foreach (var child in Children)
            {
                child.RenderTransform = value == 1 ? null : new ScaleTransform { ScaleX = value, ScaleY = value };
            }

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

        return new Size(width, height);
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
        // window (look switching) larger than the window.
        if (finalSize != _clipped)
        {
            _clipped = finalSize;
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, finalSize.Width, finalSize.Height) };
        }

        return finalSize;
    }
}
