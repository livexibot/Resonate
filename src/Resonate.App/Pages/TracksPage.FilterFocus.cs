using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Resonate.App.ViewModels;

namespace Resonate.App.Pages;

/// <summary>
/// The filter box sits in the song list's header, and the list took the
/// keyboard whenever its songs were replaced, which the filter does after
/// each letter typed: the box lost the keyboard after one letter (the owner,
/// 18 October 2026), and Space then played or paused and Delete removed the
/// selected song. While the songs are replaced, the box keeps the keyboard
/// and its caret.
/// </summary>
public sealed partial class TracksPage
{
    // The songs are being replaced: the list must not take the keyboard from the filter box.
    private bool _filterKeepsKeyboard;

    /// <summary>Shows <paramref name="rows"/> in the list, the filter box keeping the keyboard if it has it.</summary>
    private void ShowRows(ObservableCollection<TrackRow> rows)
    {
        var how = FilterBox.FocusState;
        if (how == FocusState.Unfocused)
        {
            TrackList.ItemsSource = rows;
            return;
        }

        var (start, length) = (FilterBox.SelectionStart, FilterBox.SelectionLength);
        _filterKeepsKeyboard = true;
        TrackList.ItemsSource = rows;
        KeepFilterKeyboard();

        // The list lays out its new songs after this, and may reach for the keyboard then too.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            KeepFilterKeyboard();
            _filterKeepsKeyboard = false;
        });

        void KeepFilterKeyboard()
        {
            if (FilterBox.FocusState == FocusState.Unfocused && FilterBox.IsEnabled && FilterBox.XamlRoot is not null)
            {
                FilterBox.Focus(how);
                FilterBox.Select(start, length);
            }
        }
    }

    /// <summary>While the songs are replaced, the list's own reach for the keyboard is turned down; a click or Tab still moves it.</summary>
    private void OnFilterLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (_filterKeepsKeyboard && args.InputDevice == FocusInputDeviceKind.None)
        {
            args.TryCancel();
        }
    }
}
