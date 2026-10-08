namespace Resonate.Themes.Skins;

/// <summary>A part of the classic equalizer window that reacts to the pointer.</summary>
public enum EqualizerControl
{
    None,
    TitleBar,
    Shade,
    Close,
    On,
    Auto,
    Presets,
    Graph,
    Preamp,

    /// <summary>One of the ten band sliders (which one comes with the hit).</summary>
    Band,

    /// <summary>The rolled-up bar's little volume slider.</summary>
    ShadeVolume,

    /// <summary>The rolled-up bar's little balance slider.</summary>
    ShadeBalance,
}

/// <summary>
/// Everything the classic equalizer window shows at one moment: Winamp's
/// ten bands (decibels, -12 to 12, low to high), the preamp, the switches
/// and the pointer. The renderer turns it into pixels.
/// </summary>
public sealed record EqualizerView
{
    /// <summary>The ON switch is lit.</summary>
    public bool On { get; init; }

    /// <summary>Ten gains in decibels, one per slider; missing ones are 0.</summary>
    public IReadOnlyList<double> Bands { get; init; } = new double[EqualizerLayout.BandCount];

    /// <summary>The preamp in decibels (-12 to 12).</summary>
    public double Preamp { get; init; }

    /// <summary>The control under a pressed pointer, drawn pressed.</summary>
    public EqualizerControl Pressed { get; init; }

    /// <summary>The band slider held, when <see cref="Pressed"/> is <see cref="EqualizerControl.Band"/>.</summary>
    public int PressedBand { get; init; } = -1;

    /// <summary>The window has focus: its title bar is drawn active.</summary>
    public bool WindowActive { get; init; } = true;

    /// <summary>Rolled up into the 275 x 14 bar.</summary>
    public bool Shaded { get; init; }

    /// <summary>0 to 1, for the rolled-up bar.</summary>
    public double Volume { get; init; } = 1;

    /// <summary>-1 to 1, for the rolled-up bar.</summary>
    public double Balance { get; init; }

    public bool Equals(EqualizerView? other) =>
        other is not null
        && On == other.On
        && Preamp.Equals(other.Preamp)
        && Pressed == other.Pressed
        && PressedBand == other.PressedBand
        && WindowActive == other.WindowActive
        && Shaded == other.Shaded
        && Volume.Equals(other.Volume)
        && Balance.Equals(other.Balance)
        && Bands.SequenceEqual(other.Bands);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(On);
        hash.Add(Preamp);
        hash.Add(Pressed);
        hash.Add(PressedBand);
        hash.Add(Shaded);
        foreach (var band in Bands)
        {
            hash.Add(band);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Where the equalizer window's parts are, in skin pixels from its top left (Winamp's places).</summary>
public static class EqualizerLayout
{
    public const int Width = 275;
    public const int Height = 116;
    public const int ShadeHeight = 14;

    /// <summary>Winamp's ten bands: 60, 170, 310 and 600 Hz, 1, 3, 6, 12, 14 and 16 kHz.</summary>
    public const int BandCount = 10;

    /// <summary>The gain at the top of a slider (and minus it at the bottom).</summary>
    public const double RangeDb = 12;

    /// <summary>A slider's thumb is 11 pixels tall in a track of 63.</summary>
    internal const int ThumbSize = 11;
    internal const int SliderTravel = 63 - ThumbSize;
    internal const int SliderTop = 38;

    /// <summary>Volume's mini thumb is 3 pixels wide.</summary>
    internal const int ShadeThumbWidth = 3;

    // The graph's bands sit 12 pixels apart, starting 2 in from its edge.
    internal const int GraphStep = 12;
    internal const int GraphInset = 2;

    internal static readonly PixelRect TitleBar = new(0, 0, Width, 14);
    internal static readonly PixelRect Shade = new(254, 3, 9, 9);
    internal static readonly PixelRect Close = new(264, 3, 9, 9);
    internal static readonly PixelRect On = new(14, 18, 26, 12);
    internal static readonly PixelRect Auto = new(40, 18, 32, 12);
    internal static readonly PixelRect Presets = new(217, 18, 44, 12);
    internal static readonly PixelRect Graph = new(86, 17, 113, 19);
    internal static readonly PixelRect Preamp = new(21, SliderTop, 14, 63);
    internal static readonly PixelRect ShadeVolume = new(61, 4, 97, 7);
    internal static readonly PixelRect ShadeBalance = new(164, 4, 43, 7);

    /// <summary>What Winamp printed under each slider.</summary>
    public static IReadOnlyList<string> BandLabels { get; } = ["60", "170", "310", "600", "1K", "3K", "6K", "12K", "14K", "16K"];

    /// <summary>The slider of band <paramref name="band"/> (0 to 9), the ten of them 18 pixels apart.</summary>
    internal static PixelRect BandSlider(int band) => new(78 + (18 * band), SliderTop, 14, 63);

    /// <summary>The control at a point and, for a band slider, which band (otherwise -1).</summary>
    public static (EqualizerControl Control, int Band) HitTest(int x, int y, bool shaded)
    {
        if (x < 0 || y < 0 || x >= Width || y >= (shaded ? ShadeHeight : Height))
        {
            return (EqualizerControl.None, -1);
        }

        if (Shade.Contains(x, y))
        {
            return (EqualizerControl.Shade, -1);
        }

        if (Close.Contains(x, y))
        {
            return (EqualizerControl.Close, -1);
        }

        if (shaded)
        {
            // The whole bar drags the window, apart from its buttons and sliders.
            return ShadeVolume.Contains(x, y) ? (EqualizerControl.ShadeVolume, -1)
                : ShadeBalance.Contains(x, y) ? (EqualizerControl.ShadeBalance, -1)
                : (EqualizerControl.TitleBar, -1);
        }

        if (TitleBar.Contains(x, y))
        {
            return (EqualizerControl.TitleBar, -1);
        }

        if (On.Contains(x, y))
        {
            return (EqualizerControl.On, -1);
        }

        if (Auto.Contains(x, y))
        {
            return (EqualizerControl.Auto, -1);
        }

        if (Presets.Contains(x, y))
        {
            return (EqualizerControl.Presets, -1);
        }

        if (Graph.Contains(x, y))
        {
            return (EqualizerControl.Graph, -1);
        }

        if (Preamp.Contains(x, y))
        {
            return (EqualizerControl.Preamp, -1);
        }

        for (var band = 0; band < BandCount; band++)
        {
            if (BandSlider(band).Contains(x, y))
            {
                return (EqualizerControl.Band, band);
            }
        }

        return (EqualizerControl.None, -1);
    }

    /// <summary>A gain as a fraction of a slider's travel: 0 at the bottom (-12 dB), 1 at the top (+12 dB).</summary>
    public static double Fraction(double gainDb) =>
        double.IsFinite(gainDb) ? Math.Clamp((gainDb + RangeDb) / (2 * RangeDb), 0, 1) : 0.5;

    /// <summary>The gain for a fraction of a slider's travel (see <see cref="Fraction"/>).</summary>
    public static double Decibels(double fraction) =>
        (Math.Clamp(double.IsFinite(fraction) ? fraction : 0.5, 0, 1) * 2 * RangeDb) - RangeDb;

    /// <summary>The top of a slider's thumb for a gain, in skin pixels from the window's top.</summary>
    public static int ThumbTop(double gainDb) => SliderTop + ClassicLayout.RoundHalfUp((1 - Fraction(gainDb)) * SliderTravel);

    /// <summary>
    /// The gain a slider takes when its thumb's top is at <paramref name="thumbTop"/>
    /// (skin pixels from the window's top). For a press, pass the pointer's y
    /// less where it holds the thumb (5 for its middle). The middle snaps to 0 dB.
    /// </summary>
    public static double GainAt(double thumbTop)
    {
        var fraction = 1 - ((thumbTop - SliderTop) / SliderTravel);
        var gain = Decibels(fraction);
        return Math.Abs(gain) < RangeDb / SliderTravel ? 0 : gain;
    }

    /// <summary>A slider frame (0 to 27) for a gain.</summary>
    internal static int SliderFrame(double gainDb) =>
        ClassicLayout.RoundHalfUp(Fraction(gainDb) * (StackSprites.EqSliderFrames - 1));

    /// <summary>The value (0 to 1) the little volume slider takes when its thumb's left is at <paramref name="thumbLeft"/>.</summary>
    public static double ShadeVolumeAt(double thumbLeft) =>
        Math.Clamp((thumbLeft - ShadeVolume.X) / (ShadeVolume.Width - ShadeThumbWidth), 0, 1);

    /// <summary>The little volume thumb's left edge for a volume of 0 to 1.</summary>
    internal static int ShadeVolumeThumb(double volume) =>
        ShadeVolume.X + ClassicLayout.RoundHalfUp(Math.Clamp(double.IsFinite(volume) ? volume : 0, 0, 1) * (ShadeVolume.Width - ShadeThumbWidth));

    /// <summary>The little balance thumb's left edge for a balance of -1 to 1.</summary>
    internal static int ShadeBalanceThumb(double balance) =>
        ShadeBalance.X + ClassicLayout.RoundHalfUp((Math.Clamp(double.IsFinite(balance) ? balance : 0, -1, 1) + 1) / 2 * (ShadeBalance.Width - ShadeThumbWidth));

    /// <summary>
    /// A smooth curve through evenly spaced values (Catmull-Rom), at
    /// <paramref name="t"/> measured in values from the first; kept inside 0 to 1.
    /// </summary>
    public static double Spline(IReadOnlyList<double> values, double t)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0.5;
        }

        var last = values.Count - 1;
        var i = Math.Clamp((int)Math.Floor(t), 0, Math.Max(last - 1, 0));
        var u = Math.Clamp(t - i, 0, 1);
        double At(int index) => values[Math.Clamp(index, 0, last)];
        var (p0, p1, p2, p3) = (At(i - 1), At(i), At(i + 1), At(i + 2));
        var value = 0.5 * ((2 * p1) + ((-p0 + p2) * u) + (((2 * p0) - (5 * p1) + (4 * p2) - p3) * u * u) + ((-p0 + (3 * p1) - (3 * p2) + p3) * u * u * u));
        return Math.Clamp(value, 0, 1);
    }
}

/// <summary>Draws the classic equalizer window from a skin, as Winamp 2 did: 275 x 116, or the 275 x 14 bar rolled up.</summary>
public static class EqualizerRenderer
{
    private const uint Black = 0xFF000000;

    /// <summary>Draws the window into <paramref name="target"/>, at least 275 wide and 116 (or, rolled up, 14) tall.</summary>
    public static void Render(Skin skin, EqualizerView view, SkinImage target)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(target);
        var height = view.Shaded ? EqualizerLayout.ShadeHeight : EqualizerLayout.Height;
        if (target.Width < EqualizerLayout.Width || target.Height < height)
        {
            throw new ArgumentException($"The equalizer needs a picture of {EqualizerLayout.Width} x {height} pixels.", nameof(target));
        }

        target.FillRect(0, 0, EqualizerLayout.Width, height, Black);
        if (view.Shaded)
        {
            RenderShaded(skin, view, target);
        }
        else
        {
            RenderNormal(skin, view, target);
        }
    }

    private static void RenderNormal(Skin skin, EqualizerView view, SkinImage target)
    {
        target.Draw(skin, StackSprites.EqBackground, 0, 0);
        target.Draw(skin, view.WindowActive ? StackSprites.EqTitleBarActive : StackSprites.EqTitleBarInactive, 0, 0);
        target.Draw(skin, view.Pressed == EqualizerControl.Close ? StackSprites.EqClosePressed : StackSprites.EqClose, EqualizerLayout.Close.X, EqualizerLayout.Close.Y);
        if (view.Pressed == EqualizerControl.Shade)
        {
            target.Draw(skin, StackSprites.EqShadePressed, EqualizerLayout.Shade.X, EqualizerLayout.Shade.Y);
        }

        target.Draw(skin, StackSprites.EqOnToggle.For(view.On, view.Pressed == EqualizerControl.On), EqualizerLayout.On.X, EqualizerLayout.On.Y);

        // AUTO is a one-shot here (it lays the bands flat), so it never stays lit.
        target.Draw(skin, StackSprites.EqAutoToggle.For(false, view.Pressed == EqualizerControl.Auto), EqualizerLayout.Auto.X, EqualizerLayout.Auto.Y);
        target.Draw(skin, view.Pressed == EqualizerControl.Presets ? StackSprites.EqPresetsPressed : StackSprites.EqPresets, EqualizerLayout.Presets.X, EqualizerLayout.Presets.Y);

        DrawGraph(skin, view, target);
        DrawSlider(skin, target, EqualizerLayout.Preamp, view.Preamp, view.Pressed == EqualizerControl.Preamp);
        for (var band = 0; band < EqualizerLayout.BandCount; band++)
        {
            var gain = band < view.Bands.Count ? view.Bands[band] : 0;
            DrawSlider(skin, target, EqualizerLayout.BandSlider(band), gain, view.Pressed == EqualizerControl.Band && view.PressedBand == band);
        }
    }

    private static void RenderShaded(Skin skin, EqualizerView view, SkinImage target)
    {
        target.Draw(skin, view.WindowActive ? StackSprites.EqShadeBarActive : StackSprites.EqShadeBarInactive, 0, 0);

        var volume = Math.Clamp(double.IsFinite(view.Volume) ? view.Volume : 0, 0, 1);
        var volumeLook = Third(volume, StackSprites.EqShadeVolumeLow, StackSprites.EqShadeVolumeMiddle, StackSprites.EqShadeVolumeHigh);
        target.Draw(skin, volumeLook, EqualizerLayout.ShadeVolumeThumb(volume), EqualizerLayout.ShadeVolume.Y);

        var balance = (Math.Clamp(double.IsFinite(view.Balance) ? view.Balance : 0, -1, 1) + 1) / 2;
        var balanceLook = Third(balance, StackSprites.EqShadeBalanceLeft, StackSprites.EqShadeBalanceMiddle, StackSprites.EqShadeBalanceRight);
        target.Draw(skin, balanceLook, EqualizerLayout.ShadeBalanceThumb(view.Balance), EqualizerLayout.ShadeBalance.Y);

        if (view.Pressed == EqualizerControl.Shade)
        {
            target.Draw(skin, StackSprites.EqUnshadePressed, EqualizerLayout.Shade.X, EqualizerLayout.Shade.Y);
        }

        target.Draw(skin, view.Pressed == EqualizerControl.Close ? StackSprites.EqShadeClosePressed : StackSprites.EqShadeClose, EqualizerLayout.Close.X, EqualizerLayout.Close.Y);
    }

    /// <summary>Which of a little slider's three looks goes with a value from 0 to 1.</summary>
    internal static Sprite Third(double value, Sprite low, Sprite middle, Sprite high) =>
        value < 1.0 / 3 ? low : value < 2.0 / 3 ? middle : high;

    private static void DrawSlider(Skin skin, SkinImage target, PixelRect area, double gainDb, bool pressed)
    {
        target.Draw(skin, StackSprites.EqSliderFrame(EqualizerLayout.SliderFrame(gainDb)), area.X, area.Y);
        target.Draw(skin, pressed ? StackSprites.EqThumbPressed : StackSprites.EqThumb, area.X + 1, EqualizerLayout.ThumbTop(gainDb));
    }

    /// <summary>The curve through the bands, each row in the colour the skin gives it, over the preamp's line.</summary>
    private static void DrawGraph(Skin skin, EqualizerView view, SkinImage target)
    {
        var area = EqualizerLayout.Graph;
        target.Draw(skin, StackSprites.EqGraph, area.X, area.Y);
        var rows = area.Height;
        int RowOf(double fraction) => ClassicLayout.RoundHalfUp((1 - Math.Clamp(fraction, 0, 1)) * (rows - 1));

        target.Draw(skin, StackSprites.EqPreampLine, area.X, area.Y + RowOf(EqualizerLayout.Fraction(view.Preamp)));

        var colours = skin.Sheet(SkinSheet.EqMain);
        var line = StackSprites.EqGraphColours;
        uint ColourAt(int row)
        {
            var y = line.Y + Math.Clamp(row, 0, line.Height - 1);
            return colours.Contains(line.X, y) ? colours[line.X, y] | Black : 0xFFFFFFFF;
        }

        var points = new double[EqualizerLayout.BandCount];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = EqualizerLayout.Fraction(i < view.Bands.Count ? view.Bands[i] : 0);
        }

        var width = EqualizerLayout.GraphStep * (EqualizerLayout.BandCount - 1);
        var last = RowOf(points[0]);
        for (var x = 0; x <= width; x++)
        {
            var row = RowOf(EqualizerLayout.Spline(points, x / (double)EqualizerLayout.GraphStep));
            var (top, bottom) = row < last ? (row, last) : (last, row);
            for (var r = top; r <= bottom; r++)
            {
                var px = area.X + EqualizerLayout.GraphInset + x;
                var py = area.Y + r;
                if (target.Contains(px, py))
                {
                    target[px, py] = ColourAt(r);
                }
            }

            last = row;
        }
    }
}
