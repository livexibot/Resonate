namespace Resonate.Themes;

/// <summary>
/// Colours for the now-playing stage (the Home stage and the away screen,
/// built-in plugins): the cover's main colours, found by k-means in OKLab
/// (where equal distances look equally different), and each colour made
/// safe for the look's text, so the clouds and the blurred cover behind the
/// title never make it hard to read. Pixels are 8-bit BGRA rows with no
/// padding, as Windows' decoder gives them.
/// </summary>
public static class StageColours
{
    /// <summary>How many colours the stage's clouds use.</summary>
    public const int CloudCount = 5;

    private const int Iterations = 10;

    // A little above the palette's own 7:1 and 4.5:1, because colours blended
    // over each other in sRGB can come out slightly darker than either.
    private const double PrimaryContrast = 7.4;
    private const double SecondaryContrast = 4.8;

    // The visualizer's bars against the page, a little above WCAG's 3:1 for graphics, for the same reason.
    private const double BarContrast = 3.2;

    /// <summary>
    /// The cover's <paramref name="count"/> main colours, most of the cover
    /// first. Always that many: a cover with fewer colours repeats them. The
    /// same pixels always give the same colours.
    /// </summary>
    public static IReadOnlyList<ThemeColor> Palette(ReadOnlySpan<byte> bgra, int width, int height, int count = CloudCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (bgra.Length < width * height * 4)
        {
            throw new ArgumentException("The pixels do not fill the picture.", nameof(bgra));
        }

        var points = new List<Lab>(width * height);
        for (var i = 0; i < width * height; i++)
        {
            var p = i * 4;
            if (bgra[p + 3] >= 0x80)
            {
                points.Add(Lab.From(new ThemeColor(0xFF, bgra[p + 2], bgra[p + 1], bgra[p])));
            }
        }

        if (points.Count == 0)
        {
            return Enumerable.Repeat(ThemeColor.Black, count).ToList();
        }

        // Starting centres, the same every time: the colour nearest the
        // average, then each time the colour farthest from those picked.
        var mean = Lab.Mean(points);
        var centres = new List<Lab> { points.MinBy(p => p.DistanceTo(mean)) };
        var nearest = points.Select(p => p.DistanceTo(centres[0])).ToArray();
        while (centres.Count < count)
        {
            var far = 0;
            for (var i = 1; i < points.Count; i++)
            {
                if (nearest[i] > nearest[far])
                {
                    far = i;
                }
            }

            if (nearest[far] <= 1e-9)
            {
                break;
            }

            centres.Add(points[far]);
            for (var i = 0; i < points.Count; i++)
            {
                nearest[i] = Math.Min(nearest[i], points[i].DistanceTo(points[far]));
            }
        }

        var owner = new int[points.Count];
        var sizes = new int[centres.Count];
        for (var round = 0; round < Iterations; round++)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var best = 0;
                var bestDistance = double.MaxValue;
                for (var c = 0; c < centres.Count; c++)
                {
                    var distance = points[i].DistanceTo(centres[c]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = c;
                    }
                }

                owner[i] = best;
            }

            Array.Clear(sizes);
            var sums = new Lab[centres.Count];
            for (var i = 0; i < points.Count; i++)
            {
                sizes[owner[i]]++;
                sums[owner[i]] = sums[owner[i]].Plus(points[i]);
            }

            for (var c = 0; c < centres.Count; c++)
            {
                if (sizes[c] > 0)
                {
                    centres[c] = sums[c].Times(1.0 / sizes[c]);
                }
            }
        }

        var found = Enumerable.Range(0, centres.Count)
            .Where(c => sizes[c] > 0)
            .OrderByDescending(c => sizes[c])
            .ThenBy(c => c)
            .Select(c => centres[c].ToColor())
            .ToList();
        var colours = new List<ThemeColor>(count);
        for (var i = 0; i < count; i++)
        {
            colours.Add(found[i % found.Count]);
        }

        return colours;
    }

    /// <summary>
    /// Whether the stage, and the greeting on Home, take the playing song's
    /// cover colours: only while <paramref name="look"/>'s "Colours follow
    /// the cover" is on; otherwise they keep the look's own (the owner's
    /// choice, 9 October 2026).
    /// </summary>
    public static bool FollowsCover(ThemeDefinition look) => look.AdaptiveAccent;

    /// <summary>
    /// The colours the stage paints its clouds and bars in: the cover's
    /// (<paramref name="cover"/>) while it follows the cover and they are
    /// known, otherwise the look's two accents as <paramref name="palette"/>
    /// shows them (as the player bar's visualizer does). Opaque.
    /// </summary>
    public static IReadOnlyList<ThemeColor> Pick(bool followsCover, IReadOnlyList<ThemeColor>? cover, ThemePalette palette) =>
        followsCover && cover is { Count: > 0 } ? cover : [palette.Accent.Opaque, palette.Accent2.Opaque];

    /// <summary>
    /// The stage's soft background while it keeps the look's colours, in place
    /// of the blurred cover: the look's two accents as a diagonal gradient,
    /// <paramref name="size"/> square, BGRA.
    /// </summary>
    public static byte[] LookPicture(ThemePalette palette, int size) =>
        ArtworkColors.Gradient(palette.Accent.Opaque, palette.Accent2.Opaque, size);

    /// <summary>
    /// <paramref name="colour"/>, darkened on a dark look (lightened on a
    /// light one) just enough that the look's main text keeps 7:1 and its
    /// secondary text 4.5:1 over it, so a cloud or a blurred cover of that
    /// colour can sit behind any text. Opaque.
    /// </summary>
    public static ThemeColor ForText(ThemeColor colour, ThemePalette palette)
    {
        var toward = palette.IsLight ? ThemeColor.White : ThemeColor.Black;
        var opaque = colour.Opaque;
        for (var step = 0; step <= 20; step++)
        {
            var shown = opaque.Mix(toward, step / 20.0);
            if (Readable(shown, palette))
            {
                return shown;
            }
        }

        // Text that reads over neither black nor white (a custom look): the look's own page.
        return palette.Surface.Over(palette.Background).Opaque;
    }

    /// <summary>
    /// <paramref name="colour"/> for the visualizer's bars, which never sit
    /// under text: lightened on a dark page (darkened on a light one) just
    /// enough to stand out from <paramref name="page"/> at 3:1, the contrast
    /// WCAG asks of graphics. Opaque.
    /// </summary>
    public static ThemeColor ForBars(ThemeColor colour, ThemeColor page)
    {
        var background = page.Opaque;
        var toward = background.IsLight ? ThemeColor.Black : ThemeColor.White;
        var opaque = colour.Opaque;
        for (var step = 0; step <= 20; step++)
        {
            var shown = opaque.Mix(toward, step / 20.0);
            if (ThemeColor.ContrastRatio(shown, background) >= BarContrast)
            {
                return shown;
            }
        }

        return toward;
    }

    /// <summary>Makes every pixel of a BGRA picture safe for the look's text (see <see cref="ForText"/>).</summary>
    public static void ForText(Span<byte> bgra, ThemePalette palette)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var safe = ForText(new ThemeColor(0xFF, bgra[i + 2], bgra[i + 1], bgra[i]), palette);
            bgra[i] = safe.B;
            bgra[i + 1] = safe.G;
            bgra[i + 2] = safe.R;
            bgra[i + 3] = 0xFF;
        }
    }

    /// <summary>
    /// Whether the look's main and secondary text read over <paramref name="background"/>
    /// (7:1 and 4.5:1, with a little to spare).
    /// </summary>
    public static bool Readable(ThemeColor background, ThemePalette palette) =>
        ThemeColor.ContrastRatio(palette.TextPrimary.Over(background), background) >= PrimaryContrast
        && ThemeColor.ContrastRatio(palette.TextSecondary.Over(background), background) >= SecondaryContrast;

    /// <summary>
    /// How far the away screen's text moves from its place, in pixels, in
    /// the given minute: a slow walk around a small square, so nothing stays
    /// lit on the same pixels of an OLED screen. Zero at minute zero.
    /// </summary>
    public static (double X, double Y) BurnInShift(long minute, double range)
    {
        // Eight places around the square, then back to the middle.
        ReadOnlySpan<sbyte> xs = [0, 1, 1, 0, -1, -1, -1, 0, 1];
        ReadOnlySpan<sbyte> ys = [0, 0, 1, 1, 1, 0, -1, -1, -1];
        var step = (int)(((minute % xs.Length) + xs.Length) % xs.Length);
        return (xs[step] * range, ys[step] * range);
    }

    /// <summary>A colour in OKLab.</summary>
    private readonly record struct Lab(double L, double A, double B)
    {
        public static Lab From(ThemeColor colour)
        {
            var r = Linear(colour.R);
            var g = Linear(colour.G);
            var b = Linear(colour.B);
            var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
            var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
            var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
            return new Lab(
                (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
                (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
                (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
        }

        public static Lab Mean(List<Lab> points)
        {
            var sum = default(Lab);
            foreach (var point in points)
            {
                sum = sum.Plus(point);
            }

            return sum.Times(1.0 / points.Count);
        }

        public double DistanceTo(Lab other)
        {
            var dl = L - other.L;
            var da = A - other.A;
            var db = B - other.B;
            return (dl * dl) + (da * da) + (db * db);
        }

        public Lab Plus(Lab other) => new(L + other.L, A + other.A, B + other.B);

        public Lab Times(double factor) => new(L * factor, A * factor, B * factor);

        public ThemeColor ToColor()
        {
            var l = L + (0.3963377774 * A) + (0.2158037573 * B);
            var m = L - (0.1055613458 * A) - (0.0638541728 * B);
            var s = L - (0.0894841775 * A) - (1.2914855480 * B);
            l *= l * l;
            m *= m * m;
            s *= s * s;
            return new ThemeColor(
                0xFF,
                Encode((4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s)),
                Encode((-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s)),
                Encode((-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s)));
        }

        private static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static byte Encode(double linear)
        {
            linear = Math.Clamp(linear, 0, 1);
            var c = linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            return (byte)Math.Round(Math.Clamp(c, 0, 1) * 255);
        }
    }
}
