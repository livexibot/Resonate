using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>Lays its children out in rows, left to right, starting a new row when one is full (Search's recent searches).</summary>
internal sealed partial class WrapPanel : Panel
{
    /// <summary>The space between two children in a row, and between rows.</summary>
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;
        var widest = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }

            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width), y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return finalSize;
    }
}
