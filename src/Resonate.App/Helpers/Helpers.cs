using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    /// <summary>"3 days ago" within a month, then the date, the way Spotify shows when a song was added.</summary>
    public static string DateAdded(DateTimeOffset? added, DateTimeOffset now)
    {
        if (added is not { } time)
        {
            return string.Empty;
        }

        var age = now - time;
        return age.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => Ago((int)age.TotalMinutes, "minute"),
            < 60 * 24 => Ago((int)age.TotalHours, "hour"),
            < 60 * 24 * 7 => Ago((int)age.TotalDays, "day"),
            < 60 * 24 * 30 => Ago((int)(age.TotalDays / 7), "week"),
            _ => time.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
        };

        static string Ago(int count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
    }
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

    // One brush per gradient, shared by every tile (lists can have thousands of rows).
    private static readonly Brush?[] PlaceholderBrushes = new Brush?[Palettes.Length];

    /// <summary>The same name always gets the same gradient. Call on the interface thread.</summary>
    public static Brush PlaceholderBrush(string name)
    {
        var hash = 0u;
        foreach (var c in name)
        {
            hash = (hash * 31) + c;
        }

        var index = (int)(hash % (uint)Palettes.Length);
        if (PlaceholderBrushes[index] is { } cached)
        {
            return cached;
        }

        var (from, to) = Palettes[index];
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = Themes.ThemePreset.Hex(from), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Themes.ThemePreset.Hex(to), Offset = 1 });
        PlaceholderBrushes[index] = brush;
        return brush;
    }
}

/// <summary>
/// Finds the item a list event is about. In the published (Native AOT) app
/// an element the app never names, such as a row's ListViewItemPresenter,
/// can not be cast to FrameworkElement, so rows give themselves a background
/// (clicks then land on the row's own elements) and the selected item stands
/// in when the cast still fails.
/// </summary>
public static class ListEvents
{
    /// <summary>The item double-tapped: the one under the pointer, or the one the first tap selected.</summary>
    public static T? DoubleTapped<T>(ListViewBase list, DoubleTappedRoutedEventArgs e)
        where T : class =>
        ItemOf<T>(e.OriginalSource) ?? list.SelectedItem as T;

    /// <summary>The item a menu is for: the one under the pointer, or the selected one when the keyboard asked.</summary>
    public static T? ContextRequested<T>(ListViewBase list, ContextRequestedEventArgs args)
        where T : class =>
        ItemOf<T>(args.OriginalSource) ?? (args.TryGetPosition(list, out _) ? null : list.SelectedItem as T);

    public static T? ItemOf<T>(object? source)
        where T : class =>
        (source as FrameworkElement)?.DataContext as T;
}
