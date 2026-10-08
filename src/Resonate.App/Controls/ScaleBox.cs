using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// Lays its children out as if it were <see cref="Scale"/> times smaller,
/// then draws them that much larger, so they fill it exactly: App size
/// (Settings, Look, Size). XAML draws text, shapes and icons sharp at the
/// size they end up on screen. Each child gets a ScaleTransform of its own,
/// so it must not set a RenderTransform itself. At 1 nothing is transformed.
/// </summary>
public sealed partial class ScaleBox : Panel
{
    private double _scale = 1;

    public double Scale
    {
        get => _scale;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value == _scale)
            {
                return;
            }

            _scale = value;
            foreach (var child in Children)
            {
                child.RenderTransform = value == 1 ? null : new ScaleTransform { ScaleX = value, ScaleY = value };
            }

            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var scale = Scale;
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
        var scale = Scale;
        var inner = new Rect(0, 0, finalSize.Width / scale, finalSize.Height / scale);
        foreach (var child in Children)
        {
            child.Arrange(inner);
        }

        return finalSize;
    }
}
