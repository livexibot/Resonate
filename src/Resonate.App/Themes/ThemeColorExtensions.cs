using Microsoft.UI.Xaml.Media;
using Resonate.Themes;
using Windows.Foundation;
using Windows.UI;

namespace Resonate.App.Themes;

/// <summary>Turns the theme model's colours and angles into what XAML draws with.</summary>
public static class ThemeColorExtensions
{
    public static Color ToColor(this ThemeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static ThemeColor ToThemeColor(this Color color) => new(color.A, color.R, color.G, color.B);

    public static SolidColorBrush ToBrush(this ThemeColor color) => new(color.ToColor());

    /// <summary>Where a gradient at <paramref name="degrees"/> (0 is left to right, 90 top to bottom) starts and ends.</summary>
    public static (Point Start, Point End) GradientPoints(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var (dx, dy) = (Math.Cos(radians) / 2, Math.Sin(radians) / 2);
        return (new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
    }
}
