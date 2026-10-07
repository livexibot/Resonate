namespace Resonate.Themes.Skins;

/// <summary>Where the classic player's parts are, in skin pixels from the window's top left.</summary>
public static class ClassicLayout
{
    /// <summary>The visualiser: (24, 43, 76, 16), or (79, 5, 38, 5) in shade mode.</summary>
    public static (int X, int Y, int Width, int Height) VisualiserArea(bool shaded) =>
        shaded ? (79, 5, 38, 5) : (24, 43, 76, 16);

    /// <summary>The control at a point, or <see cref="ClassicControl.None"/>.</summary>
    public static ClassicControl HitTest(int x, int y, bool shaded) => throw new NotImplementedException();

    /// <summary>
    /// The value a slider (<see cref="ClassicControl.Volume"/>, <see cref="ClassicControl.Seek"/>:
    /// 0 to 1; <see cref="ClassicControl.Balance"/>: -1 to 1) takes when its thumb's left edge is
    /// at <paramref name="thumbLeft"/> (skin pixels from the window's left), clamped to the track.
    /// </summary>
    public static double SliderValue(ClassicControl slider, double thumbLeft, bool shaded) => throw new NotImplementedException();

    /// <summary>Where a slider's thumb is drawn for a value: its left edge and width, in skin pixels.</summary>
    public static (int Left, int Width) SliderThumb(ClassicControl slider, double value, bool shaded) => throw new NotImplementedException();
}
