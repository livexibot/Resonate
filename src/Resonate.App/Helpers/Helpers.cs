using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Resonate.App.Helpers;

public static class Format
{
    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    public static string SongCount(int count) => count == 1 ? "1 song" : $"{count:N0} songs";
}

public static class Artwork
{
    // Soft two-colour gradients for covers that are missing or still loading.
    private static readonly (uint From, uint To)[] Palettes =
    [
        (0x8B7CFF, 0x4F46E5),
        (0xF472B6, 0x9D4EDD),
        (0x34D399, 0x0EA5E9),
        (0xFBBF24, 0xF97316),
        (0x60A5FA, 0x6366F1),
        (0xF87171, 0xDB2777),
        (0x2DD4BF, 0x059669),
        (0xA78BFA, 0xEC4899),
    ];

    /// <summary>A cover image at the given display width (decoded at that size, not full size).</summary>
    public static ImageSource? FromUrl(string? url, int displayWidth)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new BitmapImage(uri) { DecodePixelWidth = displayWidth, DecodePixelType = DecodePixelType.Logical };
    }

    /// <summary>The same name always gets the same gradient.</summary>
    public static Brush PlaceholderBrush(string name)
    {
        var hash = 0u;
        foreach (var c in name)
        {
            hash = (hash * 31) + c;
        }

        var (from, to) = Palettes[hash % (uint)Palettes.Length];
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = Themes.ThemePreset.Hex(from), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Themes.ThemePreset.Hex(to), Offset = 1 });
        return brush;
    }
}

public static class VisualTree
{
    public static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
