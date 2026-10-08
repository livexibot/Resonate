using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Themes;
using Resonate.Themes;
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

    /// <summary>
    /// A cover image at the given display width (decoded at that size, not
    /// full size), shared with every other place showing it and kept on disk
    /// (see <see cref="Services.CoverImages"/>). Call on the interface thread.
    /// </summary>
    public static ImageSource? FromUrl(string? url, int displayWidth) => App.Services.Covers.Get(url, displayWidth);

    // One brush per gradient, shared by every tile (lists can have thousands of rows).
    private static readonly Brush?[] PlaceholderBrushes = new Brush?[Palettes.Length];

    /// <summary>The same name always gets the same gradient. Call on the interface thread.</summary>
    public static Brush PlaceholderBrush(string name) => BrushAt(PaletteIndex(name));

    private static Brush BrushAt(int index)
    {
        if (PlaceholderBrushes[index] is { } cached)
        {
            return cached;
        }

        var (from, to) = ColorsAt(index);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = from.ToColor(), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = to.ToColor(), Offset = 1 });
        PlaceholderBrushes[index] = brush;
        return brush;
    }

    /// <summary>The two colours of <see cref="PlaceholderBrush"/>.</summary>
    public static (ThemeColor From, ThemeColor To) PlaceholderColors(string name) => ColorsAt(PaletteIndex(name));

    /// <summary>
    /// A placeholder for each name, as <see cref="PlaceholderBrush"/> gives,
    /// except that a name whose gradient an earlier one already took moves on
    /// to the next free one, so tiles side by side never match. Call on the
    /// interface thread.
    /// </summary>
    public static Brush[] DistinctPlaceholders(IReadOnlyList<string> names)
    {
        var taken = new bool[Palettes.Length];
        var brushes = new Brush[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            var index = PaletteIndex(names[i]);
            for (var step = 0; step < Palettes.Length && taken[index]; step++)
            {
                index = (index + 1) % Palettes.Length;
            }

            taken[index] = true;
            brushes[i] = BrushAt(index);
        }

        return brushes;
    }

    // One scrim per gradient, shared like the placeholders.
    private static readonly Brush?[] ScrimBrushes = new Brush?[Palettes.Length];

    /// <summary>
    /// A fade from clear into the deeper colour of <paramref name="name"/>'s
    /// gradient over the lower part of a card, so white text on it reads
    /// over any cover. Call on the interface thread.
    /// </summary>
    public static Brush ScrimBrush(string name)
    {
        var index = PaletteIndex(name);
        if (ScrimBrushes[index] is { } cached)
        {
            return cached;
        }

        // Darkened, so white reads at 4.5:1 over every gradient's deeper colour.
        var (_, to) = ColorsAt(index);
        var deep = to.Mix(ThemeColor.Black, 0.35);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop { Color = deep.WithAlpha(0).ToColor(), Offset = 0.15 });
        brush.GradientStops.Add(new GradientStop { Color = deep.WithAlpha(0.72).ToColor(), Offset = 0.45 });
        brush.GradientStops.Add(new GradientStop { Color = deep.WithAlpha(0.94).ToColor(), Offset = 1 });
        ScrimBrushes[index] = brush;
        return brush;
    }

    // One text colour per gradient, shared like the placeholders.
    private static readonly Brush?[] InitialsBrushes = new Brush?[Palettes.Length];

    /// <summary>
    /// White or near-black, whichever reads better over the middle of
    /// <paramref name="name"/>'s gradient: for initials drawn on a
    /// placeholder. Call on the interface thread.
    /// </summary>
    public static Brush InitialsBrush(string name)
    {
        var index = PaletteIndex(name);
        if (InitialsBrushes[index] is { } cached)
        {
            return cached;
        }

        var (from, to) = ColorsAt(index);
        var middle = from.Mix(to, 0.5);
        var ink = ThemeColor.FromRgb(0x0B0B10);
        var text = ThemeColor.ContrastRatio(ThemeColor.White, middle) >= ThemeColor.ContrastRatio(ink, middle) ? ThemeColor.White : ink;
        var brush = new SolidColorBrush(text.ToColor());
        InitialsBrushes[index] = brush;
        return brush;
    }

    private static int PaletteIndex(string name)
    {
        var hash = 0u;
        foreach (var c in name)
        {
            hash = (hash * 31) + c;
        }

        return (int)(hash % (uint)Palettes.Length);
    }

    private static (ThemeColor From, ThemeColor To) ColorsAt(int index)
    {
        var (from, to) = Palettes[index];
        return (ThemeColor.FromRgb(from), ThemeColor.FromRgb(to));
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
