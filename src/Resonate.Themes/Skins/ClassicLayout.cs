namespace Resonate.Themes.Skins;

/// <summary>A block of skin pixels: where something is drawn, or where it can be clicked.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
}

/// <summary>Where the classic player's parts are, in skin pixels from the window's top left.</summary>
public static class ClassicLayout
{
    // Thumb travel of each slider, in pixels (Winamp's numbers, as Audacious measured them).
    internal const int VolumeTravel = 51;
    internal const int BalanceTravel = 24;
    internal const int BalanceCentre = 12;
    internal const int SeekTravel = 219;
    internal const int ShadeSeekSteps = 12;

    // Balance this close to the centre (of 1) snaps to it, so the middle is easy to find.
    private const double BalanceSnap = 0.08;

    // The title bar, in both modes.
    internal static readonly PixelRect TitleBar = new(0, 0, 275, 14);
    internal static readonly PixelRect Options = new(6, 3, 9, 9);
    internal static readonly PixelRect Minimize = new(244, 3, 9, 9);
    internal static readonly PixelRect Shade = new(254, 3, 9, 9);
    internal static readonly PixelRect Close = new(264, 3, 9, 9);

    // The clutter bar and its letters.
    internal static readonly PixelRect ClutterBar = new(10, 22, 8, 43);
    internal static readonly PixelRect ClutterOptions = new(10, 25, 8, 8);
    internal static readonly PixelRect ClutterAlwaysOnTop = new(10, 33, 8, 7);
    internal static readonly PixelRect ClutterInfo = new(10, 40, 8, 7);
    internal static readonly PixelRect ClutterDoubleSize = new(10, 47, 8, 8);
    internal static readonly PixelRect ClutterVisualiser = new(10, 55, 8, 7);

    // The status: work indicator, lamp, and the time (sign cell, then four digits; the colon is main.bmp's).
    internal static readonly PixelRect WorkIndicator = new(24, 28, 3, 9);
    internal static readonly PixelRect Lamp = new(26, 28, 9, 9);
    internal static readonly PixelRect Time = new(36, 26, 63, 13);
    internal static readonly PixelRect TimeSign = new(36, 26, 9, 13);
    internal const int TimeDigitsY = 26;

    // numbers.bmp's one-row minus lands here, inside the sign cell.
    internal const int NumbersMinusX = 38;
    internal const int NumbersMinusY = 32;

    // Text: the marquee (glyphs at y 27; the 3 px above and below count for the pointer), kbps and kHz.
    internal static readonly PixelRect MarqueeText = new(111, 27, 154, 6);
    internal static readonly PixelRect MarqueeHit = new(111, 24, 154, 12);
    internal static readonly PixelRect Kbps = new(111, 43, 15, 6);
    internal static readonly PixelRect Khz = new(156, 43, 10, 6);

    // The lamps for one or two channels.
    internal static readonly PixelRect MonoLamp = new(212, 41, 27, 12);
    internal static readonly PixelRect StereoLamp = new(239, 41, 29, 12);

    // Sliders: the track (frames are drawn over it) and the thumbs' row.
    internal static readonly PixelRect Volume = new(107, 57, 68, 13);
    internal static readonly PixelRect Balance = new(177, 57, 38, 13);
    internal static readonly PixelRect Seek = new(16, 72, 248, 10);
    internal const int SliderThumbY = 58;
    internal const int SliderThumbWidth = 14;
    internal const int SeekThumbWidth = 29;

    // The window toggles.
    internal static readonly PixelRect Equalizer = new(219, 58, 23, 12);
    internal static readonly PixelRect Playlist = new(242, 58, 23, 12);

    // Transport, shuffle and repeat (they share column 210; repeat is drawn on top and wins clicks).
    internal static readonly PixelRect Previous = new(16, 88, 23, 18);
    internal static readonly PixelRect Play = new(39, 88, 23, 18);
    internal static readonly PixelRect Pause = new(62, 88, 23, 18);
    internal static readonly PixelRect Stop = new(85, 88, 23, 18);
    internal static readonly PixelRect Next = new(108, 88, 22, 18);
    internal static readonly PixelRect Eject = new(136, 89, 22, 16);
    internal static readonly PixelRect Shuffle = new(164, 89, 47, 15);
    internal static readonly PixelRect Repeat = new(210, 89, 28, 15);
    internal static readonly PixelRect About = new(253, 91, 13, 15);

    // Shade mode: the time in text glyphs (sign, two minute digits, two second digits), the transport's
    // click areas (their art is part of the shade bar) and the small seek bar.
    internal const int ShadeTimeY = 4;
    internal static readonly PixelRect ShadeTime = new(127, 3, 31, 8);
    internal static readonly PixelRect ShadePrevious = new(169, 2, 7, 10);
    internal static readonly PixelRect ShadePlay = new(176, 2, 10, 10);
    internal static readonly PixelRect ShadePause = new(186, 2, 9, 10);
    internal static readonly PixelRect ShadeStop = new(195, 2, 9, 10);
    internal static readonly PixelRect ShadeNext = new(204, 2, 10, 10);
    internal static readonly PixelRect ShadeEject = new(215, 2, 10, 10);
    internal static readonly PixelRect ShadeSeek = new(226, 4, 17, 7);
    internal const int ShadeSeekThumbWidth = 3;

    // The visualiser, as VisualiserArea gives it.
    internal static readonly PixelRect VisualiserNormal = new(24, 43, 76, 16);
    internal static readonly PixelRect VisualiserShaded = new(79, 5, 38, 5);

    // Hit testing, first match wins.
    private static readonly (PixelRect Area, ClassicControl Control)[] TitleButtons =
    [
        (Options, ClassicControl.Options),
        (Minimize, ClassicControl.Minimize),
        (Shade, ClassicControl.Shade),
        (Close, ClassicControl.Close),
    ];

    private static readonly (PixelRect Area, ClassicControl Control)[] NormalControls =
    [
        (ClutterOptions, ClassicControl.ClutterOptions),
        (ClutterAlwaysOnTop, ClassicControl.ClutterAlwaysOnTop),
        (ClutterInfo, ClassicControl.ClutterInfo),
        (ClutterDoubleSize, ClassicControl.ClutterDoubleSize),
        (ClutterVisualiser, ClassicControl.ClutterVisualiser),
        (Time, ClassicControl.Time),
        (VisualiserNormal, ClassicControl.Visualiser),
        (MarqueeHit, ClassicControl.Marquee),
        (Volume, ClassicControl.Volume),
        (Balance, ClassicControl.Balance),
        (Equalizer, ClassicControl.Equalizer),
        (Playlist, ClassicControl.Playlist),
        (Seek, ClassicControl.Seek),
        (Previous, ClassicControl.Previous),
        (Play, ClassicControl.Play),
        (Pause, ClassicControl.Pause),
        (Stop, ClassicControl.Stop),
        (Next, ClassicControl.Next),
        (Eject, ClassicControl.Eject),
        (Repeat, ClassicControl.Repeat),
        (Shuffle, ClassicControl.Shuffle),
        (About, ClassicControl.About),
    ];

    private static readonly (PixelRect Area, ClassicControl Control)[] ShadedControls =
    [
        .. TitleButtons,
        (VisualiserShaded, ClassicControl.Visualiser),
        (ShadeTime, ClassicControl.Time),
        (ShadePrevious, ClassicControl.Previous),
        (ShadePlay, ClassicControl.Play),
        (ShadePause, ClassicControl.Pause),
        (ShadeStop, ClassicControl.Stop),
        (ShadeNext, ClassicControl.Next),
        (ShadeEject, ClassicControl.Eject),
        (ShadeSeek, ClassicControl.Seek),
    ];

    /// <summary>Where the four time digits go: minute tens, minute ones, second tens, second ones.</summary>
    internal static ReadOnlySpan<int> TimeDigitsX => [48, 60, 78, 90];

    /// <summary>Where the shade bar's time glyphs go: the sign, then two digits, the colon (the bar's art), two digits.</summary>
    internal static ReadOnlySpan<int> ShadeTimeX => [128, 134, 139, 147, 152];

    /// <summary>The visualiser: (24, 43, 76, 16), or (79, 5, 38, 5) in shade mode.</summary>
    public static (int X, int Y, int Width, int Height) VisualiserArea(bool shaded)
    {
        var area = shaded ? VisualiserShaded : VisualiserNormal;
        return (area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>The control at a point, or <see cref="ClassicControl.None"/>.</summary>
    public static ClassicControl HitTest(int x, int y, bool shaded) =>
        shaded ? HitTestShaded(x, y) : HitTestNormal(x, y);

    /// <summary>
    /// The value a slider (<see cref="ClassicControl.Volume"/>, <see cref="ClassicControl.Seek"/>:
    /// 0 to 1; <see cref="ClassicControl.Balance"/>: -1 to 1) takes when its thumb's left edge is
    /// at <paramref name="thumbLeft"/> (skin pixels from the window's left), clamped to the track.
    /// </summary>
    /// <remarks>
    /// For a press or drag, pass the pointer's x less half the thumb's width
    /// (7, or 14 for the seek bar, 1 for the shade bar's). Balance within 8 %
    /// of the centre snaps to it.
    /// </remarks>
    public static double SliderValue(ClassicControl slider, double thumbLeft, bool shaded)
    {
        if (double.IsNaN(thumbLeft))
        {
            thumbLeft = 0;
        }

        switch (slider)
        {
            case ClassicControl.Volume:
                return Math.Clamp((thumbLeft - Volume.X) / VolumeTravel, 0, 1);

            case ClassicControl.Balance:
                var balance = Math.Clamp((thumbLeft - Balance.X - BalanceCentre) / BalanceCentre, -1, 1);
                return Math.Abs(balance) < BalanceSnap ? 0 : balance;

            case ClassicControl.Seek when shaded:
                // The small thumb sits at 1 to 13 pixels into its 17-pixel track.
                return Math.Clamp((thumbLeft - ShadeSeek.X - 1) / ShadeSeekSteps, 0, 1);

            case ClassicControl.Seek:
                return Math.Clamp((thumbLeft - Seek.X) / SeekTravel, 0, 1);

            default:
                throw new ArgumentOutOfRangeException(nameof(slider), slider, "Only the volume, balance and seek bars are sliders.");
        }
    }

    /// <summary>Where a slider's thumb is drawn for a value: its left edge and width, in skin pixels.</summary>
    public static (int Left, int Width) SliderThumb(ClassicControl slider, double value, bool shaded) => slider switch
    {
        ClassicControl.Volume => (Volume.X + VolumeOffset(value), SliderThumbWidth),
        ClassicControl.Balance => (Balance.X + BalanceOffset(value), SliderThumbWidth),
        ClassicControl.Seek when shaded => (ShadeSeek.X + ShadeSeekOffset(value), ShadeSeekThumbWidth),
        ClassicControl.Seek => (Seek.X + SeekOffset(value), SeekThumbWidth),
        _ => throw new ArgumentOutOfRangeException(nameof(slider), slider, "Only the volume, balance and seek bars are sliders."),
    };

    /// <summary>The volume thumb's offset into its track, 0 to 51 (Winamp's <c>(v * 51 + 50) / 100</c> for a percentage).</summary>
    internal static int VolumeOffset(double volume) =>
        Math.Clamp(RoundHalfUp(Finite(volume) * VolumeTravel), 0, VolumeTravel);

    /// <summary>The balance thumb's offset, 0 to 24 with the centre at 12; halves round away from the centre.</summary>
    internal static int BalanceOffset(double balance)
    {
        var pixels = Finite(balance) * BalanceCentre;
        var away = RoundHalfUp(Math.Abs(pixels));
        return Math.Clamp(BalanceCentre + (pixels < 0 ? -away : away), 0, BalanceTravel);
    }

    /// <summary>The seek thumb's offset, 0 to 219: whole pixels reached so far, the last one at the end.</summary>
    internal static int SeekOffset(double position) =>
        Math.Clamp(WholePixels(Finite(position) * SeekTravel), 0, SeekTravel);

    /// <summary>The shade bar's seek thumb offset, 1 to 13 (Audacious' <c>1 + elapsed * 12 / length</c>).</summary>
    internal static int ShadeSeekOffset(double position) =>
        1 + Math.Clamp(WholePixels(Finite(position) * ShadeSeekSteps), 0, ShadeSeekSteps);

    /// <summary>Volume frame 0 to 27 for a thumb offset.</summary>
    internal static int VolumeFrame(int offset) => ((offset * 27) + 25) / VolumeTravel;

    /// <summary>Balance frame 0 (centre) to 27 (one side) for a thumb offset.</summary>
    internal static int BalanceFrame(int offset) => ((Math.Abs(offset - BalanceCentre) * 27) + 6) / BalanceCentre;

    /// <summary>JavaScript's <c>Math.round</c>, which the skin formulas were measured with: halves go up.</summary>
    internal static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>The whole pixels in a length, forgiving the rounding error of a value worked out from a pixel (109 / 219 * 219 is 108.99999...).</summary>
    private static int WholePixels(double value) => (int)Math.Floor(value + 1e-9);

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static ClassicControl HitTestNormal(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClassicRenderer.Width || y >= ClassicRenderer.Height)
        {
            return ClassicControl.None;
        }

        return TitleBar.Contains(x, y) ? Find(TitleButtons, x, y, ClassicControl.TitleBar) : Find(NormalControls, x, y, ClassicControl.None);
    }

    private static ClassicControl HitTestShaded(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClassicRenderer.Width || y >= ClassicRenderer.ShadeHeight)
        {
            return ClassicControl.None;
        }

        // The whole bar drags the window, apart from its buttons and displays.
        return Find(ShadedControls, x, y, ClassicControl.TitleBar);
    }

    private static ClassicControl Find((PixelRect Area, ClassicControl Control)[] controls, int x, int y, ClassicControl otherwise)
    {
        foreach (var (area, control) in controls)
        {
            if (area.Contains(x, y))
            {
                return control;
            }
        }

        return otherwise;
    }
}
