namespace Resonate.Themes.Skins;

/// <summary>Which of the mini player's windows show, and how: the main window, then the equalizer and the playlist under it.</summary>
public readonly record struct ClassicStackState(
    bool MainShaded,
    bool EqualizerOpen,
    bool EqualizerShaded,
    bool PlaylistOpen,
    bool PlaylistShaded,
    int PlaylistHeight);

/// <summary>Where each window of the mini player sits, in skin pixels from the top: its top and its height (0 when closed).</summary>
public readonly record struct ClassicStackLayout(
    int MainHeight,
    int EqualizerTop,
    int EqualizerHeight,
    int PlaylistTop,
    int PlaylistHeight)
{
    /// <summary>The whole stack's height.</summary>
    public int Height => PlaylistTop + PlaylistHeight;
}

/// <summary>
/// The mini player's windows stacked as Winamp's docked them: one column 275
/// skin pixels wide, the main window on top.
/// </summary>
public static class ClassicStack
{
    public const int Width = ClassicRenderer.Width;

    public static ClassicStackLayout Arrange(ClassicStackState state)
    {
        var main = state.MainShaded ? ClassicRenderer.ShadeHeight : ClassicRenderer.Height;
        var equalizer = !state.EqualizerOpen ? 0 : state.EqualizerShaded ? EqualizerLayout.ShadeHeight : EqualizerLayout.Height;
        var playlist = !state.PlaylistOpen ? 0 : state.PlaylistShaded ? PlaylistLayout.ShadeHeight : PlaylistLayout.SnapHeight(state.PlaylistHeight);
        return new ClassicStackLayout(main, main, equalizer, main + equalizer, playlist);
    }
}

/// <summary>
/// Winamp's windows snapped to the screen's edges when dragged near them;
/// the mini player does the same, along one axis at a time.
/// </summary>
public static class WindowSnap
{
    /// <summary>
    /// Where a window <paramref name="size"/> long goes when dragged to
    /// <paramref name="position"/> on a screen from <paramref name="areaStart"/>,
    /// <paramref name="areaSize"/> long: against an edge within
    /// <paramref name="distance"/> of it, otherwise where it was dragged.
    /// </summary>
    public static int Snap(int position, int size, int areaStart, int areaSize, int distance)
    {
        var areaEnd = areaStart + areaSize;
        if (Math.Abs(position - areaStart) <= distance)
        {
            return areaStart;
        }

        return Math.Abs(position + size - areaEnd) <= distance ? areaEnd - size : position;
    }
}

/// <summary>
/// Winamp's ten equalizer sliders over a six-band equalizer (the Spotify
/// app's: 60 Hz, 150 Hz, 400 Hz, 1 kHz, 2.4 kHz and 15 kHz). Each slider
/// stands for the band nearest its frequency, so sliders that share a band
/// move together.
/// </summary>
public static class EqualizerSliders
{
    /// <summary>The band each of the ten sliders (60 Hz to 16 kHz) moves.</summary>
    private static readonly int[] Bands = [0, 1, 2, 2, 3, 4, 4, 5, 5, 5];

    /// <summary>The six-band equalizer's band for slider <paramref name="slider"/> (0 to 9).</summary>
    public static int BandFor(int slider) => Bands[Math.Clamp(slider, 0, Bands.Length - 1)];

    /// <summary>The ten sliders' gains for six band gains (missing bands are 0).</summary>
    public static double[] FromBands(IReadOnlyList<double> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);
        var sliders = new double[EqualizerLayout.BandCount];
        for (var i = 0; i < sliders.Length; i++)
        {
            var band = Bands[i];
            sliders[i] = band < gains.Count ? gains[band] : 0;
        }

        return sliders;
    }
}
