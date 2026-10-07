namespace Resonate.Themes.Skins;

/// <summary>The small drawing kit the built-in skin is painted with.</summary>
internal static partial class BuiltInSkin
{
    /// <summary>The colour a fraction <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static uint Mix(uint from, uint to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var red = MixChannel(from, to, 16, t);
        var green = MixChannel(from, to, 8, t);
        var blue = MixChannel(from, to, 0, t);
        return 0xFF000000u | (red << 16) | (green << 8) | blue;
    }

    private static uint MixChannel(uint from, uint to, int shift, double t)
    {
        var a = (from >> shift) & 0xFF;
        var b = (to >> shift) & 0xFF;
        return (uint)Math.Round(a + ((b - (double)a) * t));
    }

    private static void Plot(SkinImage image, int x, int y, uint colour)
    {
        if (image.Contains(x, y))
        {
            image[x, y] = colour;
        }
    }

    private static void HLine(SkinImage image, int x, int y, int width, uint colour) => image.FillRect(x, y, width, 1, colour);

    private static void VLine(SkinImage image, int x, int y, int height, uint colour) => image.FillRect(x, y, 1, height, colour);

    /// <summary>
    /// A one-pixel edge round a rectangle: <paramref name="topLeft"/> along the
    /// top and left, <paramref name="bottomRight"/> along the bottom and right,
    /// so light seems to come from the top left.
    /// </summary>
    private static void Edge(SkinImage image, int x, int y, int width, int height, uint topLeft, uint bottomRight)
    {
        HLine(image, x, y, width - 1, topLeft);
        VLine(image, x, y, height - 1, topLeft);
        HLine(image, x + 1, y + height - 1, width - 1, bottomRight);
        VLine(image, x + width - 1, y + 1, height - 1, bottomRight);
    }

    /// <summary>A sunken display: a deep fill with its rim in shadow at the top left and lit at the bottom right.</summary>
    private static void Well(SkinImage image, int x, int y, int width, int height)
    {
        image.FillRect(x, y, width, height, WellFill);
        Edge(image, x, y, width, height, WellShade, WellRim);
    }

    /// <summary>Puts the colour behind a shape into its four corner pixels, so it looks rounded.</summary>
    private static void Round(SkinImage image, int x, int y, int width, int height, uint topBehind, uint bottomBehind)
    {
        Plot(image, x, y, topBehind);
        Plot(image, x + width - 1, y, topBehind);
        Plot(image, x, y + height - 1, bottomBehind);
        Plot(image, x + width - 1, y + height - 1, bottomBehind);
    }

    /// <summary>
    /// Paints pixel art written as rows split by '/': '#' in
    /// <paramref name="ink"/>, '+' in <paramref name="second"/> (when given),
    /// anything else left as it is.
    /// </summary>
    private static void Art(SkinImage image, int x, int y, string art, uint ink, uint second = 0)
    {
        var column = x;
        var row = y;
        foreach (var c in art)
        {
            if (c == '/')
            {
                row++;
                column = x;
                continue;
            }

            if (c == '#')
            {
                Plot(image, column, row, ink);
            }
            else if (c == '+' && second != 0)
            {
                Plot(image, column, row, second);
            }

            column++;
        }
    }

    /// <summary>How many pixels wide a piece of pixel art is (its first row).</summary>
    private static int ArtWidth(string art)
    {
        var end = art.IndexOf('/', StringComparison.Ordinal);
        return end < 0 ? art.Length : end;
    }

    /// <summary>How many rows a piece of pixel art has.</summary>
    private static int ArtHeight(string art)
    {
        var rows = 1;
        foreach (var c in art)
        {
            if (c == '/')
            {
                rows++;
            }
        }

        return rows;
    }

    /// <summary>Pixel art with a one-pixel shadow under it, which keeps light glyphs crisp on a lit face.</summary>
    private static void Embossed(SkinImage image, int x, int y, string art, uint ink, uint shadow)
    {
        Art(image, x, y + 1, art, shadow);
        Art(image, x, y, art, ink);
    }

    /// <summary>Pixel art centred in a box (rounding towards the top left).</summary>
    private static (int X, int Y) Centre(string art, int x, int y, int width, int height) =>
        (x + ((width - ArtWidth(art)) / 2), y + ((height - ArtHeight(art)) / 2));

    /// <summary>Writes text in the skin's own 5x6 font, five pixels a character, as text.bmp holds it.</summary>
    private static void Write(SkinImage image, int x, int y, string text, uint ink)
    {
        foreach (var c in text)
        {
            Art(image, x, y, Glyph(c), ink);
            x += GlyphAdvance;
        }
    }

    private static int WriteWidth(string text) => (text.Length * GlyphAdvance) - 1;

    /// <summary>Writes a small label in the 3-pixel lamp font, each letter as wide as it needs.</summary>
    private static void Label(SkinImage image, int x, int y, string text, uint ink)
    {
        foreach (var c in text)
        {
            var art = LampGlyph(c);
            Art(image, x, y, art, ink);
            x += ArtWidth(art) + 1;
        }
    }

    private static int LabelWidth(string text)
    {
        var width = -1;
        foreach (var c in text)
        {
            width += ArtWidth(LampGlyph(c)) + 1;
        }

        return Math.Max(width, 0);
    }

    /// <summary>A one-pixel circle (the midpoint method), for the logo's rings.</summary>
    private static void Ring(SkinImage image, int centreX, int centreY, int radius, uint colour)
    {
        var x = radius;
        var y = 0;
        var error = 1 - radius;
        while (x >= y)
        {
            Plot(image, centreX + x, centreY + y, colour);
            Plot(image, centreX + y, centreY + x, colour);
            Plot(image, centreX - y, centreY + x, colour);
            Plot(image, centreX - x, centreY + y, colour);
            Plot(image, centreX - x, centreY - y, colour);
            Plot(image, centreX - y, centreY - x, colour);
            Plot(image, centreX + y, centreY - x, colour);
            Plot(image, centreX + x, centreY - y, colour);
            y++;
            if (error < 0)
            {
                error += (2 * y) + 1;
            }
            else
            {
                x--;
                error += (2 * (y - x)) + 1;
            }
        }
    }
}
