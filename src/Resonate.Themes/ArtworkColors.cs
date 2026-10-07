namespace Resonate.Themes;

/// <summary>
/// Colour work on a song's cover, shrunk to a few dozen pixels: the lively
/// colour an adaptive accent uses, and a soft blur for the glass background.
/// Pixels are 8-bit BGRA rows with no padding (what Windows' decoder gives).
/// </summary>
public static class ArtworkColors
{
    private const int HueBins = 24;

    /// <summary>
    /// The cover's most prominent lively colour, made vivid enough for an
    /// accent; null for a black-and-white or nearly empty cover.
    /// </summary>
    public static ThemeColor? PickAccent(ReadOnlySpan<byte> bgra, int width, int height)
    {
        CheckSize(bgra, width, height);

        Span<double> weight = stackalloc double[HueBins];
        Span<double> red = stackalloc double[HueBins];
        Span<double> green = stackalloc double[HueBins];
        Span<double> blue = stackalloc double[HueBins];
        weight.Clear();
        red.Clear();
        green.Clear();
        blue.Clear();

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var color = new ThemeColor(0xFF, bgra[i + 2], bgra[i + 1], bgra[i]);
            var (hue, saturation, lightness) = color.ToHsl();
            if (saturation < 0.18 || lightness < 0.10 || lightness > 0.92)
            {
                continue;
            }

            // Favour saturated, mid-light pixels: they read as "the colour of the cover".
            var w = saturation * saturation * (1 - (Math.Abs(lightness - 0.55) * 1.3));
            if (w <= 0)
            {
                continue;
            }

            var bin = (int)(hue / 360 * HueBins) % HueBins;
            weight[bin] += w;
            red[bin] += color.R * w;
            green[bin] += color.G * w;
            blue[bin] += color.B * w;
        }

        // Neighbouring bins belong together (a red spans two bins), so score a bin with its neighbours.
        var best = -1;
        var bestScore = 0.0;
        for (var bin = 0; bin < HueBins; bin++)
        {
            var score = weight[bin] + (0.5 * (weight[(bin + 1) % HueBins] + weight[(bin + HueBins - 1) % HueBins]));
            if (score > bestScore)
            {
                bestScore = score;
                best = bin;
            }
        }

        // At least a sliver of the cover must be colourful.
        var pixels = width * height;
        if (best < 0 || weight[best] < pixels * 0.004)
        {
            return null;
        }

        var average = new ThemeColor(
            0xFF,
            (byte)Math.Round(red[best] / weight[best]),
            (byte)Math.Round(green[best] / weight[best]),
            (byte)Math.Round(blue[best] / weight[best]));
        var (h, s, l) = average.ToHsl();
        return ThemeColor.FromHsl(h, Math.Max(s, 0.55), Math.Clamp(l, 0.48, 0.68));
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
}
