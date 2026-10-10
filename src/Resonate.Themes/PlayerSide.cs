namespace Resonate.Themes;

/// <summary>What the right part of the player bar shows, and how wide that is.</summary>
/// <param name="Slider">The volume slider shows; without it the wheel on the speaker still sets the volume.</param>
/// <param name="SignalWord">The signal pill shows its word; without it only its mark.</param>
/// <param name="Width">How wide the right part is with that: the least its column may be.</param>
/// <param name="SongMinWidth">The least the song's column may be: less than usual only when even the least of the right part does not fit the narrowest bar of its kind.</param>
public readonly record struct PlayerSideFit(bool Slider, bool SignalWord, double Width, double SongMinWidth);

/// <summary>
/// The right part of the player bar (the signal pill, the plugin, device,
/// lyrics and queue buttons, the speaker and the volume slider) is as wide as
/// what it holds, so nothing in it is cut off. A full bar short of room puts
/// the buttons above the volume first (the owner's choice of 9 October 2026,
/// instead of leaving the slider out), then leaves out the signal pill's word
/// (its mark stays), and leaves out the slider only when even that does not
/// fit. The numbers are the player bar's own (see PlayerBar.ShowWidthClass
/// and ArrangeSide in the app).
/// </summary>
public static class PlayerSide
{
    /// <summary>An icon button in one row with the volume.</summary>
    public const double ButtonSize = 36;

    /// <summary>The space between the buttons in one row, and before the speaker.</summary>
    public const double ButtonSpacing = 4;

    /// <summary>An icon button in the row above the volume.</summary>
    public const double StackedButtonSize = 32;

    /// <summary>The space between the buttons above the volume.</summary>
    public const double StackedButtonSpacing = 6;

    /// <summary>The space between the speaker and the slider.</summary>
    public const double SliderSpacing = 4;

    /// <summary>The volume slider's width.</summary>
    public static double SliderWidth(PlayerWidthClass widthClass) => widthClass == PlayerWidthClass.Full ? 112 : 96;

    /// <summary>The song's usual least width.</summary>
    public static double SongMinWidth(PlayerWidthClass widthClass) => widthClass switch
    {
        PlayerWidthClass.Full => 220,
        PlayerWidthClass.Compact => 150,
        _ => 0,
    };

    /// <summary>The playback controls' least width.</summary>
    public static double ControlsMinWidth(PlayerWidthClass widthClass) => widthClass switch
    {
        PlayerWidthClass.Full => 320,
        PlayerWidthClass.Compact => 216,
        _ => 0,
    };

    /// <summary>The bar's padding on each side.</summary>
    public static double Padding(PlayerWidthClass widthClass) => widthClass switch
    {
        PlayerWidthClass.Full => 20,
        PlayerWidthClass.Compact => 16,
        _ => 14,
    };

    /// <summary>The space between the bar's three parts.</summary>
    public static double ColumnSpacing(PlayerWidthClass widthClass) => widthClass switch
    {
        PlayerWidthClass.Full => 24,
        PlayerWidthClass.Compact => 16,
        _ => 12,
    };

    /// <summary>
    /// The most the right part can have in a bar <paramref name="barWidth"/>
    /// wide while the song and the controls keep their least.
    /// </summary>
    /// <param name="outline">The bar's outline on the left and the right together.</param>
    public static double Room(PlayerWidthClass widthClass, double barWidth, double outline) =>
        barWidth - outline - (2 * Padding(widthClass)) - (2 * ColumnSpacing(widthClass)) - SongMinWidth(widthClass) - ControlsMinWidth(widthClass);

    /// <summary>
    /// Whether the buttons go above the volume, and what the right part then
    /// shows: above it when <paramref name="stacked"/> asks for that (a
    /// compact bar, or the user's switch), else in one row while everything
    /// fits there, else above the volume while the slider and the pill's mark
    /// fit that way, and in one row without the slider only as a last resort.
    /// </summary>
    public static (bool Stacked, PlayerSideFit Fit) Choose(PlayerWidthClass widthClass, bool stacked, int buttons, double signalMark, double signalWord, double barWidth, double outline)
    {
        if (stacked)
        {
            return (true, Fit(widthClass, true, buttons, signalMark, signalWord, barWidth, outline));
        }

        var row = Fit(widthClass, false, buttons, signalMark, signalWord, barWidth, outline);
        if (row.Slider && (row.SignalWord || signalWord <= 0))
        {
            return (false, row);
        }

        var above = Fit(widthClass, true, buttons, signalMark, signalWord, barWidth, outline);
        return above.Width <= Room(widthClass, barWidth, outline) + 0.5 ? (true, above) : (false, row);
    }

    /// <summary>What the right part of a bar <paramref name="barWidth"/> wide shows.</summary>
    /// <param name="stacked">The buttons sit in a row above the speaker and the slider.</param>
    /// <param name="buttons">The icon buttons shown in the buttons' row (plugins, device, lyrics, queue).</param>
    /// <param name="signalMark">The signal pill's width with only its mark, or 0 without the pill.</param>
    /// <param name="signalWord">What the pill's word adds to that, or 0 when the word is not wanted.</param>
    /// <param name="outline">The bar's outline on the left and the right together.</param>
    public static PlayerSideFit Fit(PlayerWidthClass widthClass, bool stacked, int buttons, double signalMark, double signalWord, double barWidth, double outline)
    {
        var size = stacked ? StackedButtonSize : ButtonSize;
        var spacing = stacked ? StackedButtonSpacing : ButtonSpacing;
        var items = buttons + (signalMark > 0 ? 1 : 0);
        var row = (buttons * size) + signalMark + (spacing * Math.Max(items - 1, 0));
        var slider = SliderSpacing + SliderWidth(widthClass);

        // In one row the speaker (and the slider) follow the buttons; above the volume the wider row counts.
        double Width((bool Slider, bool Word) shown)
        {
            var buttonsRow = row + (shown.Word ? signalWord : 0);
            var volumeRow = size + (shown.Slider ? slider : 0);
            return stacked ? Math.Max(buttonsRow, volumeRow) : buttonsRow + ButtonSpacing + volumeRow;
        }

        (bool Slider, bool Word)[] tries = stacked
            ? [(true, true), (true, false)]
            : [(true, true), (false, true), (false, false)];
        var room = Room(widthClass, barWidth, outline);
        var chosen = tries[^1];
        foreach (var shown in tries)
        {
            if (Width(shown) <= room + 0.5)
            {
                chosen = shown;
                break;
            }
        }

        // The least it can show takes what it lacks at the narrowest bar from the song,
        // the same amount at every width, so a resize never moves it.
        var songMin = SongMinWidth(widthClass);
        if (Width(chosen) <= Width(tries[^1]) + 0.5)
        {
            var narrowest = widthClass == PlayerWidthClass.Full ? PlayerPlacement.FullWidth : PlayerPlacement.CompactWidth;
            songMin = Math.Max(0, songMin - Math.Max(0, Width(chosen) - Room(widthClass, narrowest, outline)));
        }

        return new PlayerSideFit(chosen.Slider, chosen.Word && signalWord > 0, Width(chosen), songMin);
    }
}
