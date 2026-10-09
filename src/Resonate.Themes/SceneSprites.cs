namespace Resonate.Themes;

/// <summary>A small picture the special looks draw many times (see <see cref="SceneSprites"/>).</summary>
public enum SceneSprite
{
    /// <summary>A sakura petal, pink: notched at its tip, deeper at its base.</summary>
    PetalPink,

    /// <summary>A deeper pink petal.</summary>
    PetalDeep,

    /// <summary>A pale petal.</summary>
    PetalPale,

    /// <summary>A petal nearly white, as Somei Yoshino blossoms are.</summary>
    PetalWhite,

    /// <summary>A whole cherry blossom: five petals, a deep centre and stamens.</summary>
    Blossom,

    /// <summary>A snow crystal with six branched arms.</summary>
    Crystal,

    /// <summary>A snow crystal with six plain arms around a plate.</summary>
    CrystalPlate,

    /// <summary>A four-pointed sparkle, for ice and snow catching the light.</summary>
    Glint,

    /// <summary>A bead of liquid chrome: a mirrored sphere with the studio's light above, a dark horizon and a pearl glow below.</summary>
    ChromeBead,

    /// <summary>A drop of rain on a window: nearly clear, a darker rim, the city's warm light caught in its lower half and a bright spot near its top.</summary>
    Droplet,
}

/// <summary>
/// The special looks' little pictures (petals, blossoms, snow crystals,
/// sparkles, chrome beads and raindrops), drawn in code as PNG files so Windows' own decoder can take
/// them (<see cref="PngWriter"/>), like the stage's cloud mask. Each is
/// <see cref="Size"/> pixels square with its shape centred, sampled 6 x 6
/// times per pixel for smooth edges, and the same every time.
/// </summary>
public static class SceneSprites
{
    /// <summary>Pixels per side: sharp at the largest a petal is drawn on a 5K display.</summary>
    public const int Size = 96;

    private const int Samples = 6;

    // Where a petal's base and tip lie, in the square from -1 to 1 (the tip up).
    private const double PetalBase = 0.92;
    private const double PetalWidth = 0.74;

    private static readonly ThemeColor PetalHeart = ThemeColor.FromRgb(0xD9567F);
    private static readonly ThemeColor White = ThemeColor.FromRgb(0xFFFFFF);

    /// <summary>The picture as a PNG file, <paramref name="size"/> pixels square.</summary>
    public static byte[] Png(SceneSprite sprite, int size = Size) => PngWriter.Write(Pixels(sprite, size), size, size);

    /// <summary>The picture as RGBA pixels, not premultiplied.</summary>
    public static byte[] Pixels(SceneSprite sprite, int size = Size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        Func<double, double, Rgba> draw = sprite switch
        {
            SceneSprite.PetalPink => (x, y) => Petal(x, y, ThemeColor.FromRgb(0xF6B3C8)),
            SceneSprite.PetalDeep => (x, y) => Petal(x, y, ThemeColor.FromRgb(0xF094B2)),
            SceneSprite.PetalPale => (x, y) => Petal(x, y, ThemeColor.FromRgb(0xFAD0DD)),
            SceneSprite.PetalWhite => (x, y) => Petal(x, y, ThemeColor.FromRgb(0xFDE9F0)),
            SceneSprite.Blossom => Blossom,
            SceneSprite.Crystal => (x, y) => Crystal(x, y, branched: true),
            SceneSprite.CrystalPlate => (x, y) => Crystal(x, y, branched: false),
            SceneSprite.Glint => Glint,
            SceneSprite.ChromeBead => ChromeBead,
            SceneSprite.Droplet => Droplet,
            _ => throw new ArgumentOutOfRangeException(nameof(sprite)),
        };

        var pixels = new byte[size * size * 4];
        var step = 2.0 / size;
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                // Premultiplied sums, so edges blend towards the shape's own colour.
                double r = 0, g = 0, b = 0, a = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    var y = -1 + ((py + ((sy + 0.5) / Samples)) * step);
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var x = -1 + ((px + ((sx + 0.5) / Samples)) * step);
                        var c = draw(x, y);
                        r += c.R * c.A;
                        g += c.G * c.A;
                        b += c.B * c.A;
                        a += c.A;
                    }
                }

                var at = ((py * size) + px) * 4;
                var n = Samples * Samples;
                if (a <= 0)
                {
                    continue;
                }

                pixels[at] = Byte(r / a);
                pixels[at + 1] = Byte(g / a);
                pixels[at + 2] = Byte(b / a);
                pixels[at + 3] = Byte(a / n);
            }
        }

        return pixels;
    }

    /// <summary>
    /// How wide a petal is (half its width, 0 to <see cref="PetalWidth"/>) at
    /// <paramref name="t"/> along it, 0 at its base and 1 at its tip: a narrow
    /// claw, widest about two thirds along, then rounding to the tip.
    /// </summary>
    internal static double PetalHalfWidth(double t)
    {
        if (t is < 0 or > 1)
        {
            return 0;
        }

        const double widest = 0.64;
        if (t <= widest)
        {
            // Towards the base the sides run nearly straight to a narrow claw.
            var down = (widest - t) / widest;
            return PetalWidth * Math.Max(Math.Pow(Math.Max(0, 1 - Math.Pow(down, 1.8)), 0.75), 0.08);
        }

        var up = (t - widest) / (1.02 - widest);
        return PetalWidth * Math.Sqrt(Math.Max(0, 1 - (up * up)));
    }

    /// <summary>Whether a point (across, along) is inside a petal: in its outline and out of the notch at its tip.</summary>
    internal static bool InPetal(double across, double t)
    {
        var half = PetalHalfWidth(t);
        if (Math.Abs(across) > half)
        {
            return false;
        }

        // The notch: a V cut into the middle of the tip.
        const double depth = 0.15;
        var into = t - (1 - depth);
        return into <= 0 || Math.Abs(across) > 0.2 * into / depth;
    }

    private static Rgba Petal(double x, double y, ThemeColor colour)
    {
        // The tip at the top, the base at the bottom.
        var t = (PetalBase - y) / (2 * PetalBase);
        if (!InPetal(x, t))
        {
            return default;
        }

        return PetalColour(colour, x / Math.Max(PetalHalfWidth(t), 1e-6), t);
    }

    /// <summary>A petal's colour at a point: deeper towards its base, paler at its tip, with faint veins and a slightly deeper rim.</summary>
    private static Rgba PetalColour(ThemeColor colour, double across, double t)
    {
        var deep = colour.Mix(PetalHeart, 0.55);
        var pale = colour.Mix(White, 0.45);
        var c = deep.Mix(colour, Smooth(0, 0.42, t)).Mix(pale, Smooth(0.55, 1, t));

        // Three faint veins from the base.
        var veins = 0.0;
        foreach (var at in (ReadOnlySpan<double>)[-0.5, 0, 0.5])
        {
            var d = (across - at) / 0.05;
            veins += Math.Exp(-(d * d));
        }

        c = c.Mix(deep, Math.Min(0.22, veins * 0.16 * (1 - t)));

        // A slightly deeper rim gives the petal its edge on a pale background.
        c = c.Mix(deep, 0.2 * Smooth(0.82, 1, Math.Abs(across)));
        return new Rgba(c.R / 255.0, c.G / 255.0, c.B / 255.0, 0.97);
    }

    private static Rgba Blossom(double x, double y)
    {
        var r = Math.Sqrt((x * x) + (y * y));
        var angle = Math.Atan2(y, x);

        // The centre and its stamens over the petals.
        if (r < 0.15)
        {
            var heart = ThemeColor.FromRgb(0xB8325F).Mix(ThemeColor.FromRgb(0xE2779B), r / 0.15);
            return Solid(heart, 1);
        }

        for (var k = 0; k < 11; k++)
        {
            var a = (k * Math.Tau / 11) + 0.2;
            var length = 0.36 + (0.06 * Math.Sin(k * 2.3));
            var (ax, ay) = (Math.Cos(a) * length, Math.Sin(a) * length);
            if (Distance(x, y, ax, ay) < 0.04)
            {
                return Solid(ThemeColor.FromRgb(0xF3C64F), 1);
            }

            if (Segment(x, y, Math.Cos(a) * 0.12, Math.Sin(a) * 0.12, ax, ay) < 0.011)
            {
                return Solid(ThemeColor.FromRgb(0xE88AAA), 1);
            }
        }

        // Five petals, each turned to point away from the centre.
        var best = default(Rgba);
        for (var k = 0; k < 5; k++)
        {
            var petalAngle = (k * Math.Tau / 5) - (Math.PI / 2);
            var turn = angle - petalAngle;
            var along = Math.Cos(turn) * r;
            var across = Math.Sin(turn) * r;
            var t = along / 0.98;
            if (InPetal(across / 0.62, t))
            {
                var c = PetalColour(ThemeColor.FromRgb(0xF8C2D3), across / 0.62 / Math.Max(PetalHalfWidth(t), 1e-6), t);
                if (c.A > best.A)
                {
                    best = c;
                }
            }
        }

        return best;
    }

    private static Rgba Crystal(double x, double y, bool branched)
    {
        var distance = double.MaxValue;
        for (var k = 0; k < 6; k++)
        {
            var a = (k * Math.Tau / 6) - (Math.PI / 2);
            var (dx, dy) = (Math.Cos(a), Math.Sin(a));
            distance = Math.Min(distance, Segment(x, y, 0, 0, dx * 0.86, dy * 0.86));
            if (branched)
            {
                foreach (var (at, length) in (ReadOnlySpan<(double, double)>)[(0.34, 0.3), (0.56, 0.22), (0.74, 0.13)])
                {
                    var (bx, by) = (dx * at, dy * at);
                    foreach (var side in (ReadOnlySpan<double>)[-1, 1])
                    {
                        var b = a + (side * Math.PI / 3);
                        distance = Math.Min(distance, Segment(x, y, bx, by, bx + (Math.Cos(b) * length), by + (Math.Sin(b) * length)));
                    }
                }
            }
            else
            {
                // Two little crossbars on each arm.
                foreach (var at in (ReadOnlySpan<double>)[0.5, 0.7])
                {
                    var (bx, by) = (dx * at, dy * at);
                    var half = 0.14 * (1.1 - at);
                    distance = Math.Min(distance, Segment(x, y, bx - (dy * half), by + (dx * half), bx + (dy * half), by - (dx * half)));
                }
            }
        }

        // A hexagonal plate in the middle.
        var hexagon = Hexagon(x, y) - (branched ? 0.13 : 0.24);
        distance = Math.Min(distance, Math.Max(hexagon, 0));

        const double arm = 0.045;
        var ice = ThemeColor.FromRgb(0xF4FAFF);
        if (distance < arm)
        {
            // A faint blue line down each arm's middle, as clear ice shows.
            return Solid(ice.Mix(ThemeColor.FromRgb(0xBFDDF7), 0.35 * Math.Exp(-distance / 0.01)), 1);
        }

        // A soft glow around the crystal.
        var glow = 0.28 * Math.Exp(-(distance - arm) / 0.06);
        return glow < 0.004 ? default : Solid(ThemeColor.FromRgb(0xE4F1FF), glow);
    }

    private static Rgba Glint(double x, double y)
    {
        double Ray(double along, double across, double length, double width)
        {
            var fade = Math.Max(0, 1 - (Math.Abs(along) / length));
            return fade * fade * fade * Math.Exp(-Math.Abs(across) / width);
        }

        var d = Math.Sqrt(0.5);
        var light = Math.Max(Ray(x, y, 1, 0.03), Ray(y, x, 1, 0.03));
        light = Math.Max(light, 0.45 * Math.Max(Ray((x + y) * d, (x - y) * d, 0.5, 0.025), Ray((x - y) * d, (x + y) * d, 0.5, 0.025)));
        light = Math.Max(light, Math.Exp(-((x * x) + (y * y)) / 0.018));
        return light < 0.004 ? default : Solid(ThemeColor.FromRgb(0xF6FBFF), Math.Min(1, light));
    }

    private static Rgba ChromeBead(double x, double y)
    {
        const double radius = 0.9;
        var r = Math.Sqrt((x * x) + (y * y)) / radius;
        if (r > 1)
        {
            return default;
        }

        // The sphere's surface tilts away towards its rim, so what it mirrors is squeezed there: the sky above, the floor below.
        var tilt = y / radius;
        var bent = Math.Sign(tilt) * Math.Pow(Math.Abs(tilt), 0.7);
        var stops = (ReadOnlySpan<(double At, uint Rgb)>)
        [
            (-1, 0xF7F9FD), (-0.6, 0xD3DAE6), (-0.18, 0x7C8597), (-0.02, 0x1E222B), (0.12, 0x333A48),
            (0.42, 0xA9B3C6), (0.7, 0xE6D8F1), (0.86, 0xC9E6F6), (1, 0x8E95A8),
        ];
        var c = ThemeColor.FromRgb(stops[^1].Rgb);
        for (var i = 1; i < stops.Length; i++)
        {
            if (bent <= stops[i].At)
            {
                var t = (bent - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
                c = ThemeColor.FromRgb(stops[i - 1].Rgb).Mix(ThemeColor.FromRgb(stops[i].Rgb), Smooth(0, 1, t));
                break;
            }
        }

        // Darker towards the rim, then a thin bright edge of light below, and the softbox's reflection above left.
        c = c.Mix(ThemeColor.FromRgb(0x14161C), 0.55 * Smooth(0.72, 1, r));
        c = c.Mix(ThemeColor.FromRgb(0xFFE8FA), 0.8 * Smooth(0.86, 0.97, r) * Smooth(0.2, 0.7, y / radius));
        var (hx, hy) = ((x + 0.32) / 0.3, (y + 0.42) / 0.16);
        c = c.Mix(White, 0.95 * Math.Exp(-((hx * hx) + (hy * hy)) * 2));
        var (sx, sy) = ((x - 0.34) / 0.08, (y + 0.22) / 0.06);
        c = c.Mix(White, 0.9 * Math.Exp(-((sx * sx) + (sy * sy)) * 3));
        return Solid(c, Smooth(1, 0.96, r));
    }

    private static Rgba Droplet(double x, double y)
    {
        // A little fuller below than above, as a drop hangs on glass.
        var angle = Math.Atan2(y, x);
        var radius = 0.62 * (1 + (0.1 * Math.Sin(angle)));
        var r = Math.Sqrt((x * x) + (y * y)) / radius;
        if (r > 1)
        {
            return default;
        }

        var down = Math.Sin(angle) * r;
        var rim = Smooth(0.7, 1, r);

        // Nearly clear, its rim dark above and lit below, the city's light caught as a warm crescent inside its lower edge.
        var c = ThemeColor.FromRgb(0xC9D6E6);
        var alpha = 0.16;
        c = c.Mix(ThemeColor.FromRgb(0x0E1118), rim * Smooth(0.2, -0.6, down));
        alpha += 0.5 * rim * Smooth(0.3, -0.5, down);
        // The light comes from the city below and to one side, so it gathers in the lower right of the rim.
        var side = Math.Cos(angle - 1.1);
        var crescent = Smooth(0.62, 0.86, r) * Smooth(0.99, 0.9, r) * Smooth(0.35, 0.95, side) * 0.8;
        c = c.Mix(ThemeColor.FromRgb(0xFFD7A3), crescent);
        alpha = Math.Max(alpha, 0.85 * crescent);

        // A small streak of light near the top left, along the curve of the rim.
        var (u, v) = ((x * 0.8) + (y * 0.6), (y * 0.8) - (x * 0.6));
        var (hx, hy) = ((u + 0.32) / 0.07, (v + 0.12) / 0.15);
        var spot = Math.Exp(-((hx * hx) + (hy * hy)) * 2) * 0.9;
        c = c.Mix(White, spot);
        alpha = Math.Max(alpha, spot);
        return Solid(c, Math.Min(1, alpha) * Smooth(1, 0.94, r));
    }

    private static double Hexagon(double x, double y)
    {
        // Distance to the centre in a hexagon's own measure (1 at its corners' radius).
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        return Math.Max((ax * 0.866) + (ay * 0.5), ay);
    }

    private static double Distance(double x, double y, double px, double py) => Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));

    /// <summary>The distance from a point to a line segment.</summary>
    private static double Segment(double x, double y, double x1, double y1, double x2, double y2)
    {
        var (dx, dy) = (x2 - x1, y2 - y1);
        var length = (dx * dx) + (dy * dy);
        var t = length <= 0 ? 0 : Math.Clamp((((x - x1) * dx) + ((y - y1) * dy)) / length, 0, 1);
        return Distance(x, y, x1 + (t * dx), y1 + (t * dy));
    }

    private static double Smooth(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static Rgba Solid(ThemeColor c, double alpha) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0, alpha);

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private readonly record struct Rgba(double R, double G, double B, double A);
}
