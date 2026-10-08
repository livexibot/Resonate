namespace Resonate.Themes;

/// <summary>
/// The shape of one of the now-playing stage's clouds (a built-in plugin):
/// a round, soft falloff as an alpha mask the GPU stretches over the cloud
/// and tints with the cloud's colour. A radial gradient drawn by the
/// compositor shows rings on large dark areas, because the screen has only
/// 256 steps per channel and a dark cloud spreads a few of them over
/// hundreds of pixels. The mask is dithered instead: each pixel's alpha is
/// nudged by a little noise before it is rounded, so the steps dissolve
/// into grain too fine and faint to see, and the average stays exact.
/// </summary>
public static class CloudMask
{
    /// <summary>Pixels per side: enough that the noise stays finer than about 8 screen pixels on a 5K display.</summary>
    public const int Size = 512;

    /// <summary>
    /// How far (in alpha steps, triangular noise) a pixel may be nudged. A
    /// cloud adds about a quarter of a screen step per alpha step, so this
    /// spreads each screen step over about two, which hides the rings.
    /// </summary>
    public const double Dither = 4;

    /// <summary>
    /// How opaque the cloud is at <paramref name="radius"/> (0 at the centre,
    /// 1 at the edge): (1 - r²)², full in the middle, about half at half
    /// way, and fading to nothing with no edge to see.
    /// </summary>
    public static double Falloff(double radius)
    {
        if (radius >= 1)
        {
            return 0;
        }

        var inside = 1 - (radius * radius);
        return inside * inside;
    }

    /// <summary>The mask as RGBA pixels, white with the dithered falloff as alpha. The same every time.</summary>
    public static byte[] Pixels(int size = Size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        var pixels = new byte[size * size * 4];
        var noise = new Noise(0x5EED_C10D);
        var half = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            var dy = (y + 0.5 - half) / half;
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5 - half) / half;
                var alpha = Falloff(Math.Sqrt((dx * dx) + (dy * dy))) * 255;

                // Never pushed below nothing: the noise shrinks to zero at the edge, so the cloud keeps its soft rim.
                var spread = Math.Min(Dither, alpha);
                var value = alpha + (spread * noise.Triangle());
                var at = ((y * size) + x) * 4;
                pixels[at] = 0xFF;
                pixels[at + 1] = 0xFF;
                pixels[at + 2] = 0xFF;
                pixels[at + 3] = (byte)Math.Clamp(Math.Round(value), 0, 255);
            }
        }

        return pixels;
    }

    /// <summary>The mask as a PNG file, for Windows' decoder.</summary>
    public static byte[] Png(int size = Size) => PngWriter.Write(Pixels(size), size, size);

    /// <summary>A small, fixed sequence of random numbers (xorshift), so the mask is the same on every PC.</summary>
    private struct Noise(uint seed)
    {
        private uint _state = seed;

        /// <summary>From -1 to 1, most often near 0 (the sum of two even draws).</summary>
        public double Triangle() => Next() - Next();

        private double Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state / (double)uint.MaxValue;
        }
    }
}
