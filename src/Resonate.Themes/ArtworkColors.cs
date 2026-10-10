using System.Buffers.Binary;

namespace Resonate.Themes;

/// <summary>
/// Colour work on a song's cover, shrunk to a few dozen pixels: the lively
/// colour an adaptive accent uses, a soft blur of the cover, and a wash made
/// only of the cover's colours (what the song cover backdrop shows unless
/// the user allows the blurred cover). Pixels are 8-bit BGRA rows with no
/// padding (what Windows' decoder gives).
/// </summary>
public static class ArtworkColors
{
    private const int HueBins = 24;

    // A wash's second colour sits at least this many hue bins (60 degrees) from its first.
    private const int DistinctHueBins = 4;

    // A cover of one colour gets its second accent this many degrees round the colour wheel.
    private const double SecondHueTurn = 32;

    // The wash's two pools of colour: where they sit (0 to 1 across the
    // picture), how far they spread, and how much the deep colour shows
    // everywhere. Fixed, so nothing of the cover's layout reaches the wash.
    private const double FirstPoolX = 0.3;
    private const double FirstPoolY = 0.38;
    private const double SecondPoolX = 0.7;
    private const double SecondPoolY = 0.62;
    private const double PoolSpread = 0.3;
    private const double DepthWeight = 0.22;

    // The band of shades a vivid cover keeps (HSL lightness), and how bright
    // any colour in it may be (relative luminance) for white text.
    private const double VividDarkest = 0.3;
    private const double VividLightest = 0.66;
    private const double VividBrightest = 0.3;

    /// <summary>
    /// The cover's most prominent lively colour, made vivid enough for an
    /// accent; null for a black-and-white or nearly empty cover.
    /// </summary>
    public static ThemeColor? PickAccent(ReadOnlySpan<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);
        var hues = new HueHistogram(bgra);
        var best = hues.Best();
        if (!hues.IsColourful(best, width * height))
        {
            return null;
        }

        var (h, s, l) = hues.Colour(best).ToHsl();
        return ThemeColor.FromHsl(h, Math.Max(s, 0.55), Math.Clamp(l, 0.48, 0.68));
    }

    /// <summary>
    /// The cover's two colours for a look that follows it: the most prominent
    /// lively colour and a second, clearly different one (or, for a cover of
    /// one colour, the same hue turned a little), both vivid enough for
    /// accents; null for a black-and-white or nearly empty cover.
    /// </summary>
    public static (ThemeColor Accent, ThemeColor Second)? PickAccents(ReadOnlySpan<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);
        var hues = new HueHistogram(bgra);
        var best = hues.Best();
        if (!hues.IsColourful(best, width * height))
        {
            return null;
        }

        var (h, s, l) = hues.Colour(best).ToHsl();
        var accent = ThemeColor.FromHsl(h, Math.Max(s, 0.55), Math.Clamp(l, 0.48, 0.68));
        var other = hues.Best(awayFrom: best);
        if (other >= 0 && hues.Weight(other) >= Math.Max(width * height * 0.004, hues.Weight(best) * 0.1))
        {
            var (h2, s2, l2) = hues.Colour(other).ToHsl();
            return (accent, ThemeColor.FromHsl(h2, Math.Max(s2, 0.5), Math.Clamp(l2, 0.5, 0.7)));
        }

        return (accent, ThemeColor.FromHsl(h + SecondHueTurn, Math.Max(s, 0.55), Math.Clamp(l + 0.08, 0.5, 0.74)));
    }

    /// <summary>
    /// A soft wash for the song cover backdrop, painted from three of the
    /// cover's colours (see <see cref="WashColours"/>) and nothing else: two
    /// pools of colour in fixed places over a deep base. No pixel of the
    /// cover and nothing of its layout is in it, so it never looks like the
    /// cover. Opaque BGRA, <paramref name="outWidth"/> × <paramref name="outHeight"/>.
    /// </summary>
    public static byte[] ColourWash(ReadOnlySpan<byte> bgra, int width, int height, int outWidth, int outHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(outHeight);
        var (first, second, depth) = WashColours(bgra, width, height);

        var pixels = new byte[outWidth * outHeight * 4];
        for (var y = 0; y < outHeight; y++)
        {
            var v = (y + 0.5) / outHeight;
            for (var x = 0; x < outWidth; x++)
            {
                var u = (x + 0.5) / outWidth;
                var a = Pool(u, v, FirstPoolX, FirstPoolY);
                var b = Pool(u, v, SecondPoolX, SecondPoolY);
                var total = a + b + DepthWeight;

                byte Blend(byte one, byte two, byte deep) =>
                    (byte)Math.Round(((one * a) + (two * b) + (deep * DepthWeight)) / total);

                var index = ((y * outWidth) + x) * 4;
                pixels[index] = Blend(first.B, second.B, depth.B);
                pixels[index + 1] = Blend(first.G, second.G, depth.G);
                pixels[index + 2] = Blend(first.R, second.R, depth.R);
                pixels[index + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The three colours a wash is painted with: the cover's lively colour,
    /// a second distinct one (or its average colour, or a deeper shade of
    /// the first), and a deep base. A black-and-white or empty cover gives
    /// greys at its own brightness. All are calmed (not too bright, not too
    /// loud) so they sit well under a look's tint.
    /// </summary>
    public static (ThemeColor First, ThemeColor Second, ThemeColor Depth) WashColours(ReadOnlySpan<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);
        var count = width * height;

        // Only which colours the cover has counts, not where they are: with
        // the pixels in a fixed order, any arrangement of the same colours
        // gives exactly the same result.
        var order = new uint[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = BinaryPrimitives.ReadUInt32LittleEndian(bgra.Slice(i * 4, 4));
        }

        Array.Sort(order);
        var sorted = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(sorted.AsSpan(i * 4, 4), order[i]);
        }

        var average = Average(sorted, width, height);
        var hues = new HueHistogram(sorted);
        var best = hues.Best();
        if (!hues.IsColourful(best, count))
        {
            // Black and white (or no cover): greys at the cover's own brightness.
            var grey = Calm(average, 0, 0.12, 0.14, 0.42);
            return (grey, Deeper(grey, 0.08), Deeper(grey, 0.2));
        }

        var lively = hues.Colour(best);
        var first = Calm(lively, 0.4, 0.75, 0.36, 0.54);

        var other = hues.Best(awayFrom: best);
        var second = other >= 0 && hues.Weight(other) >= Math.Max(count * 0.004, hues.Weight(best) * 0.1)
            ? Calm(hues.Colour(other), 0.3, 0.7, 0.28, 0.48)
            : Calm(average, 0, 0.6, 0.24, 0.44);
        if (Distance(first, second) < 60)
        {
            // A cover of one colour: a deeper shade of it, so the wash still has depth.
            second = Deeper(first, 0.14);
        }

        var depth = Calm(average.Mix(lively, 0.3), 0, 0.5, 0.07, 0.18);
        return (first, second, depth);
    }

    /// <summary>The cover's average colour.</summary>
    public static ThemeColor Average(ReadOnlySpan<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);
        long r = 0, g = 0, b = 0;
        var count = width * height;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            b += bgra[i];
            g += bgra[i + 1];
            r += bgra[i + 2];
        }

        return count == 0 ? ThemeColor.Black : new ThemeColor(0xFF, (byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <summary>
    /// A square diagonal gradient as BGRA pixels, standing in for a song
    /// without a cover (its tile's two colours).
    /// </summary>
    public static byte[] Gradient(ThemeColor from, ThemeColor to, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        var pixels = new byte[size * size * 4];
        var span = Math.Max(1, (size - 1) * 2);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var color = from.Mix(to, (double)(x + y) / span);
                var index = ((y * size) + x) * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 0xFF;
            }
        }

        return pixels;
    }

    /// <summary>
    /// Makes a blurred cover glow, in place: its colours stronger and its
    /// shades lifted into a band that is never near black and never too
    /// bright (by luminance, so a yellow is darkened more than a blue) for
    /// white text under a look's tint. Blurring averages a cover's
    /// colours into muddy, dark ones, and a dark cover under a dark tint
    /// would otherwise look black. Greys stay grey.
    /// </summary>
    public static void Vivid(Span<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);
        for (var i = 0; i < width * height * 4; i += 4)
        {
            var (hue, saturation, lightness) = new ThemeColor(0xFF, bgra[i + 2], bgra[i + 1], bgra[i]).ToHsl();

            // Faint colour is kept faint, so a black-and-white cover never turns a hue.
            var stronger = saturation < 0.06 ? saturation : Math.Min(1, (saturation * 1.6) + 0.12);
            var lifted = VividLightest - ((VividLightest - VividDarkest) * Math.Pow(1 - lightness, 1.3));
            var colour = ThemeColor.FromHsl(hue, stronger, lifted);

            // Bright hues (yellow, cyan) are darkened until white text still reads over them.
            while (colour.Luminance > VividBrightest && lifted > VividDarkest)
            {
                lifted -= 0.02;
                colour = ThemeColor.FromHsl(hue, stronger, lifted);
            }

            bgra[i] = colour.B;
            bgra[i + 1] = colour.G;
            bgra[i + 2] = colour.R;
            bgra[i + 3] = 0xFF;
        }
    }

    /// <summary>
    /// The lightest a see-through panel over a dimmed cover is drawn (sRGB
    /// grey 74, #4A4A4A): white text reads at 8.9:1 over it and the grey
    /// text, which <see cref="ThemePalette"/> checks against it, at 4.5:1.
    /// </summary>
    private const double BrightestPanelChannel = 74;

    // However much white the panels add, a cover is never dimmed below this luminance (sRGB grey 39).
    private const double DimmestCover = 0.02;

    /// <summary>
    /// The brightest cover <see cref="DimForWhiteText"/> leaves (its 85th
    /// percentile), as a grey: the panels, white at
    /// <paramref name="panelOpacity"/> blended over it channel by channel as
    /// XAML draws them, come out no lighter than #4A4A4A.
    /// </summary>
    public static ThemeColor BrightestCover(double panelOpacity)
    {
        var p = Math.Clamp(panelOpacity, 0, 0.9);
        var channel = (BrightestPanelChannel - (p * 255)) / (1 - p);
        var grey = (byte)Math.Clamp(Math.Floor(channel), 0, 255);
        var colour = new ThemeColor(0xFF, grey, grey, grey);
        while (colour.Luminance < DimmestCover && grey < 255)
        {
            grey++;
            colour = new ThemeColor(0xFF, grey, grey, grey);
        }

        return colour;
    }

    /// <summary>
    /// Darkens a picture just enough for the text on see-through panels over
    /// it to read: white at 8.9:1 and the grey text at 4.5:1 (see
    /// <see cref="BrightestCover"/>). Only when its brighter parts (the 85th
    /// percentile of luminance, so a few bright specks do not darken it all)
    /// are too bright, and evenly in linear light, so its colours stay its
    /// own. <paramref name="panelOpacity"/> is how much white the panels add
    /// on top. A dark picture is left as it is. Returns the factor used (1:
    /// unchanged).
    /// </summary>
    public static double DimForWhiteText(Span<byte> bgra, int width, int height, double panelOpacity)
    {
        CheckSize(bgra, width, height);
        var count = width * height;
        if (count == 0)
        {
            return 1;
        }

        // A coloured cover of the same luminance makes a darker panel than a grey one, so the grey is the bound.
        var allowed = BrightestCover(panelOpacity).Luminance;

        var luminances = new double[count];
        for (var i = 0; i < count; i++)
        {
            luminances[i] = new ThemeColor(0xFF, bgra[(i * 4) + 2], bgra[(i * 4) + 1], bgra[i * 4]).Luminance;
        }

        Array.Sort(luminances);
        var bright = luminances[Math.Min(count - 1, (int)(count * 0.85))];
        if (bright <= allowed)
        {
            return 1;
        }

        var factor = allowed / bright;
        for (var i = 0; i < count * 4; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                bgra[i + c] = ToSrgb(ToLinear(bgra[i + c]) * factor);
            }
        }

        return factor;

        static double ToLinear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static byte ToSrgb(double linear)
        {
            var c = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            return (byte)Math.Clamp(Math.Round(c * 255), 0, 255);
        }
    }

    /// <summary>
    /// Blurs in place. Three box-blur passes look like a Gaussian blur, and
    /// on a tiny image this is a fraction of a millisecond.
    /// </summary>
    public static void Blur(Span<byte> bgra, int width, int height, int radius, int passes = 3)
    {
        CheckSize(bgra, width, height);
        if (radius <= 0 || width == 0 || height == 0)
        {
            return;
        }

        var scratch = new byte[bgra.Length];
        for (var pass = 0; pass < passes; pass++)
        {
            BoxBlur(bgra, scratch, width, height, radius, horizontal: true);
            BoxBlur(scratch, bgra, width, height, radius, horizontal: false);
        }
    }

    private static void BoxBlur(ReadOnlySpan<byte> source, Span<byte> target, int width, int height, int radius, bool horizontal)
    {
        var lines = horizontal ? height : width;
        var length = horizontal ? width : height;
        var window = (radius * 2) + 1;
        Span<int> sums = stackalloc int[4];

        for (var line = 0; line < lines; line++)
        {
            int Index(int position)
            {
                position = Math.Clamp(position, 0, length - 1);
                return horizontal ? ((line * width) + position) * 4 : ((position * width) + line) * 4;
            }

            sums.Clear();
            for (var k = -radius; k <= radius; k++)
            {
                var index = Index(k);
                for (var c = 0; c < 4; c++)
                {
                    sums[c] += source[index + c];
                }
            }

            for (var position = 0; position < length; position++)
            {
                var index = Index(position);
                for (var c = 0; c < 4; c++)
                {
                    target[index + c] = (byte)((sums[c] + (window / 2)) / window);
                }

                var leaving = Index(position - radius);
                var entering = Index(position + radius + 1);
                for (var c = 0; c < 4; c++)
                {
                    sums[c] += source[entering + c] - source[leaving + c];
                }
            }
        }
    }

    private static void CheckSize(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 0 || height < 0 || bgra.Length < width * height * 4)
        {
            throw new ArgumentException("The pixel buffer is smaller than width × height × 4.", nameof(bgra));
        }
    }

    /// <summary>How strongly a pool of colour shows at (u, v): 1 at its centre, fading smoothly.</summary>
    private static double Pool(double u, double v, double x, double y)
    {
        var dx = u - x;
        var dy = v - y;
        return Math.Exp(-((dx * dx) + (dy * dy)) / (PoolSpread * PoolSpread));
    }

    /// <summary>
    /// The same hue with its lightness, then its saturation, kept within the
    /// given ranges. Its colourfulness is kept while the lightness moves, so
    /// a pale cream darkens to a soft grey-brown, not a loud brown.
    /// </summary>
    private static ThemeColor Calm(ThemeColor color, double minSaturation, double maxSaturation, double minLightness, double maxLightness)
    {
        var (h, s, l) = color.ToHsl();
        var chroma = (1 - Math.Abs((2 * l) - 1)) * s;
        var lightness = Math.Clamp(l, minLightness, maxLightness);
        var room = 1 - Math.Abs((2 * lightness) - 1);
        var saturation = room <= 0 ? 0 : chroma / room;
        return ThemeColor.FromHsl(h, Math.Clamp(saturation, minSaturation, maxSaturation), lightness);
    }

    private static ThemeColor Deeper(ThemeColor color, double by)
    {
        var (h, s, l) = color.ToHsl();
        return ThemeColor.FromHsl(h, s, Math.Max(l - by, 0.04));
    }

    private static int Distance(ThemeColor a, ThemeColor b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>
    /// The cover's colourful pixels sorted into slices of the colour wheel,
    /// counting saturated, mid-light pixels most: they read as "the colour
    /// of the cover".
    /// </summary>
    private sealed class HueHistogram
    {
        private readonly double[] _weight = new double[HueBins];
        private readonly double[] _red = new double[HueBins];
        private readonly double[] _green = new double[HueBins];
        private readonly double[] _blue = new double[HueBins];

        public HueHistogram(ReadOnlySpan<byte> bgra)
        {
            for (var i = 0; i + 3 < bgra.Length; i += 4)
            {
                var color = new ThemeColor(0xFF, bgra[i + 2], bgra[i + 1], bgra[i]);
                var (hue, saturation, lightness) = color.ToHsl();
                if (saturation < 0.18 || lightness < 0.10 || lightness > 0.92)
                {
                    continue;
                }

                var w = saturation * saturation * (1 - (Math.Abs(lightness - 0.55) * 1.3));
                if (w <= 0)
                {
                    continue;
                }

                var bin = (int)(hue / 360 * HueBins) % HueBins;
                _weight[bin] += w;
                _red[bin] += color.R * w;
                _green[bin] += color.G * w;
                _blue[bin] += color.B * w;
            }
        }

        public double Weight(int bin) => _weight[bin];

        /// <summary>
        /// The slice with the most colour, counting half of each neighbour
        /// (a red spans two slices); with <paramref name="awayFrom"/>, only
        /// slices clearly another hue than that one. -1 when there is none.
        /// </summary>
        public int Best(int awayFrom = -1)
        {
            var best = -1;
            var bestScore = 0.0;
            for (var bin = 0; bin < HueBins; bin++)
            {
                // A slice with no colour of its own cannot be the colour, whatever its neighbours hold.
                if (_weight[bin] <= 0)
                {
                    continue;
                }

                if (awayFrom >= 0)
                {
                    var apart = Math.Abs(bin - awayFrom);
                    if (Math.Min(apart, HueBins - apart) < DistinctHueBins)
                    {
                        continue;
                    }
                }

                var score = _weight[bin] + (0.5 * (_weight[(bin + 1) % HueBins] + _weight[(bin + HueBins - 1) % HueBins]));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = bin;
                }
            }

            return best;
        }

        /// <summary>Whether a slice holds enough colour to count: at least a sliver of the cover.</summary>
        public bool IsColourful(int bin, int pixels) => bin >= 0 && _weight[bin] >= pixels * 0.004;

        /// <summary>The slice's average colour, as it is on the cover.</summary>
        public ThemeColor Colour(int bin) => new(
            0xFF,
            (byte)Math.Round(_red[bin] / _weight[bin]),
            (byte)Math.Round(_green[bin] / _weight[bin]),
            (byte)Math.Round(_blue[bin] / _weight[bin]));
    }
}
