using Microsoft.UI.Xaml;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The right part of the bar is as wide as what it holds (see
/// <see cref="PlayerSide"/>): with too little room the buttons go above the
/// volume first, then the signal pill's word goes, and the volume slider only
/// when even that does not fit (the wheel on the speaker still sets the
/// volume). Worked out again when a button or the pill comes or goes, the
/// pill's word changes, or the bar is resized; the layout only changes when
/// the outcome does.
/// </summary>
public sealed partial class PlayerBar
{
    private (PlayerSideFit Fit, PlayerWidthClass WidthClass, bool Stacked)? _sideFit;

    /// <summary>The buttons sit above the volume (see ArrangeSide).</summary>
    private bool _sideStacked;

    /// <summary>The icon buttons in the buttons' row, which come and go.</summary>
    private FrameworkElement[]? _sideButtons;

    private void UpdateSideRoom()
    {
        if (_sideButtons is null)
        {
            // Once: everything that comes and goes on the right says so.
            _sideButtons = [PluginsButton, DeviceButton, LyricsButton, QueueButton];
            foreach (var element in _sideButtons.Append(SignalSlot))
            {
                element.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateSideRoom());
            }

            SignalSlot.SizeChanged += OnSignalSlotSizeChanged;
        }

        // The mini bar keeps its own few buttons (see ShowWidthClass).
        if (_widthClass == PlayerWidthClass.Mini)
        {
            _sideFit = null;
            if (_sideStacked)
            {
                _sideStacked = false;
                ArrangeSide();
            }

            return;
        }

        var stacked = _widthClass == PlayerWidthClass.Compact || (App.Services.Theme.ButtonsAboveVolume && _widthClass == PlayerWidthClass.Full);
        var buttons = 0;
        foreach (var button in _sideButtons)
        {
            buttons += button.Visibility == Visibility.Visible ? 1 : 0;
        }

        // The pill's word only in the full bar, while the user wants it.
        var pill = _signalPill is not null && SignalSlot.Visibility == Visibility.Visible;
        var mark = pill ? SignalMarkWidth : 0;
        var wordWanted = pill && _widthClass == PlayerWidthClass.Full && App.Services.Settings.LosslessBadgeText;
        var word = wordWanted ? _signalWordWidth + SignalWordExtra : 0;
        var outline = Bar.BorderThickness.Left + Bar.BorderThickness.Right;
        var (side, fit) = PlayerSide.Choose(_widthClass, stacked, buttons, mark, word, Bar.ActualWidth, outline);
        if (side != _sideStacked)
        {
            // ArrangeSide lays the buttons out again and comes back here with the same answer.
            _sideStacked = side;
            _sideFit = null;
            ArrangeSide();
            return;
        }

        if (_sideFit == (fit, _widthClass, side))
        {
            return;
        }

        _sideFit = (fit, _widthClass, side);
        VolumeColumn.MinWidth = fit.Width;
        NowPlayingColumn.MinWidth = fit.SongMinWidth;
        VolumeBar.Visibility = fit.Slider ? Visibility.Visible : Visibility.Collapsed;
        ShowSignalWord(fit.SignalWord);
    }

    /// <summary>The pill's word, now laid out in the look's font, may be wider or narrower than measured.</summary>
    private void OnSignalSlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_signalBadge is { Visibility: Visibility.Visible } badge && badge.ActualWidth > 0)
        {
            _signalWordWidth = badge.ActualWidth;
        }

        UpdateSideRoom();
    }
}
