using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.App.ViewModels;

namespace Resonate.App.Helpers;

/// <summary>
/// Which song row of a list shows its Play and More buttons
/// (<see cref="TrackRow.IsPointerOver"/> and <see cref="TrackRow.IsSelected"/>):
/// the one under the pointer, and the selected one for the keyboard. One
/// row at a time is under the pointer, and a row whose container is reused
/// for another song lets go at once, so no row keeps its buttons by mistake.
/// The row template's root calls <see cref="Entered"/> and <see cref="Exited"/>.
/// </summary>
internal sealed class SongRowActions
{
    // The second click of a double-click on Play would pause what the first one started.
    private const long DoubleClickMilliseconds = 500;

    private TrackRow? _over;
    private TrackRow? _selected;
    private TrackRow? _played;
    private long _playedAt;

    public SongRowActions(ListViewBase list)
    {
        list.ContainerContentChanging += (_, args) => Reused(args.Item);
        list.SelectionChanged += (_, _) => Select(list.SelectedItem as TrackRow);
    }

    /// <summary>The pointer came onto a row (or onto something in it).</summary>
    public void Entered(object sender)
    {
        if (sender is FrameworkElement { DataContext: TrackRow row } && !ReferenceEquals(row, _over))
        {
            LetGo();
            _over = row;
            row.IsPointerOver = true;
        }
    }

    /// <summary>
    /// The pointer left a row, or something in it (those bubble up to the
    /// row): only leaving the row itself counts.
    /// </summary>
    public void Exited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackRow row } element
            && ReferenceEquals(row, _over)
            && (ReferenceEquals(e.OriginalSource, sender) || !Inside(element, e)))
        {
            LetGo();
        }
    }

    /// <summary>The list's selection changed (also call it after the list's songs are replaced).</summary>
    public void Select(TrackRow? row)
    {
        if (ReferenceEquals(row, _selected))
        {
            return;
        }

        _selected?.IsSelected = false;
        _selected = row;
        row?.IsSelected = true;
    }

    /// <summary>The row whose Play button was clicked; null for the second click of a double-click.</summary>
    public TrackRow? PlayClicked(object sender)
    {
        if (sender is not FrameworkElement { DataContext: TrackRow row })
        {
            return null;
        }

        var now = Environment.TickCount64;
        if (ReferenceEquals(row, _played) && now - _playedAt < DoubleClickMilliseconds)
        {
            return null;
        }

        _played = row;
        _playedAt = now;
        return row;
    }

    /// <summary>A double-click or Enter on one of a row's buttons is the button's, not a request to play the row.</summary>
    public static bool InButton(object? source, DependencyObject list)
    {
        for (var element = source as DependencyObject; element is not null && !ReferenceEquals(element, list); element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A row's container shows another song, or is put aside: the song it showed is no longer under the pointer.</summary>
    private void Reused(object? item)
    {
        if (item is TrackRow row && ReferenceEquals(row, _over))
        {
            LetGo();
        }
    }

    private void LetGo()
    {
        _over?.IsPointerOver = false;
        _over = null;
    }

    // In the row's own units, so App size does not matter.
    private static bool Inside(FrameworkElement element, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(element).Position;
        return point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight;
    }
}
