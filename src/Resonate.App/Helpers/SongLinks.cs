using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Pages.Lists;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Windows.Foundation;

namespace Resonate.App.Helpers;

/// <summary>
/// Artist and album names in a song row that open their pages, as on
/// Spotify: <c>helpers:SongLinks.To="Artists"</c> (or <c>"Album"</c>) on
/// the row's TextBlock, whose DataContext is the row's <see cref="TrackRow"/>.
/// The text itself still comes from x:Bind; the names become links only
/// while the pointer is on them, so scrolling through thousands of songs
/// makes nothing extra, and the one under the pointer is underlined in the
/// main text colour. A click reads the row it is on at that moment, so a
/// row reused for another song never opens the old song's page. Names
/// without a page (local files) stay plain text.
/// </summary>
public static class SongLinks
{
    public const string Artists = "Artists";
    public const string Album = "Album";

    public static readonly DependencyProperty ToProperty = DependencyProperty.RegisterAttached(
        "To",
        typeof(string),
        typeof(SongLinks),
        new PropertyMetadata(null, OnToChanged));

    public static string? GetTo(DependencyObject element) => (string?)element.GetValue(ToProperty);

    public static void SetTo(DependencyObject element, string? value) => element.SetValue(ToProperty, value);

    private static void OnToChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        // Set once, when the row's template is made.
        if (element is not TextBlock text || e.OldValue is not null)
        {
            return;
        }

        text.PointerEntered += OnPointerEntered;
        text.PointerMoved += OnPointerMoved;
        text.PointerExited += OnPointerLeft;
        text.PointerCanceled += OnPointerLeft;
        text.PointerCaptureLost += OnPointerLeft;
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock text && text.DataContext is TrackRow row && MakeLinks(text, row.Track))
        {
            Underline(text, e.GetCurrentPoint(text).Position);
        }
    }

    private static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock text)
        {
            Underline(text, e.GetCurrentPoint(text).Position);
        }
    }

    private static void OnPointerLeft(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock text)
        {
            Underline(text, null);
        }
    }

    /// <summary>Turns the names in <paramref name="text"/> into links; false when none has a page.</summary>
    private static bool MakeLinks(TextBlock text, TrackInfo track)
    {
        var inlines = text.Inlines;
        if (GetTo(text) == Album)
        {
            if (track.AlbumId is null || track.Album.Length == 0)
            {
                return false;
            }

            inlines.Clear();
            inlines.Add(Link(text, track.Album, 0));
            return true;
        }

        var artists = track.ArtistRefs;
        if (!artists.Any(a => a.Id is not null) || string.Join(", ", artists.Select(a => a.Name)) != track.Artists)
        {
            return false;
        }

        inlines.Clear();
        for (var i = 0; i < artists.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new Run { Text = ", " });
            }

            inlines.Add(artists[i].Id is null ? new Run { Text = artists[i].Name } : Link(text, artists[i].Name, i));
        }

        return true;
    }

    private static Hyperlink Link(TextBlock text, string name, int index)
    {
        // Drawn like the text around it until the pointer is on it.
        var link = new Hyperlink { Foreground = text.Foreground, UnderlineStyle = UnderlineStyle.None, IsTabStop = false };
        link.Inlines.Add(new Run { Text = name });
        link.Click += (_, _) => Open(text, index);
        return link;
    }

    /// <summary>Underlines the link at <paramref name="point"/> (none when null) and lets go of the others.</summary>
    private static void Underline(TextBlock text, Point? point)
    {
        Brush? primary = null;
        foreach (var inline in text.Inlines)
        {
            if (inline is not Hyperlink link)
            {
                continue;
            }

            var on = point is { } at && Covers(link, at);
            var style = on ? UnderlineStyle.Single : UnderlineStyle.None;
            if (link.UnderlineStyle != style)
            {
                link.UnderlineStyle = style;
                link.Foreground = on ? primary ??= App.Services.Theme.GetBrush("ResonateTextPrimaryBrush") : text.Foreground;
            }
        }
    }

    /// <summary>The pointer is over the link's words (one line: songs' rows never wrap).</summary>
    private static bool Covers(Hyperlink link, Point point)
    {
        var start = link.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var end = link.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        return point.X >= start.Left && point.X <= Math.Max(end.Left, end.Right);
    }

    private static void Open(TextBlock text, int index)
    {
        if (text.DataContext is not TrackRow { Track: var track })
        {
            return;
        }

        if (GetTo(text) == Album)
        {
            if (track.AlbumId is { } albumId)
            {
                App.MainWindow?.Open(AlbumSource.Prefix + albumId);
            }
        }
        else if (index < track.ArtistRefs.Count && track.ArtistRefs[index].Id is { } artistId)
        {
            App.MainWindow?.Open(TrackActions.ArtistKey(artistId));
        }
    }
}
