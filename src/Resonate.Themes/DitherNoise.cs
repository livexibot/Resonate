namespace Resonate.Themes;

/// <summary>
/// The grain laid over a large soft gradient (the Home stage's clouds and
/// blurred cover). The screen has only 256 steps per channel, and every
/// layer the compositor draws is rounded to them, so a dark gradient spread
/// over a 5K display shows each step as a ring, however finely the layers
/// under it were dithered (the owner saw them on 10 October 2026, after
/// <see cref="CloudMask"/>'s dither had hidden them on lighter stages). This
/// tile is white with an alpha of 0, 1 or 2 steps: drawn one texel per
/// screen pixel after everything under it, it lifts random pixels by one or
/// two steps, which dissolves the rings' edges into grain far too faint to
/// see, and brightens nothing by more than a step on average.
/// </summary>
public static class DitherNoise
{
    /// <summary>Pixels per side of the tile, repeated across the gradient.</summary>
    public const int Size = 256;

    /// <summary>The tile as RGBA pixels: white, with alpha 0, 1 or 2 (a quarter, a half and a quarter of the pixels). The same every time.</summary>
    public static byte[] Pixels(int size = Size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        var pixels = new byte[size * size * 4];
        var state = 0x6A1E_D17Bu;
        for (var i = 0; i < size * size; i++)
        {
            // Two even draws of 0 or 1 added: 0, 1 or 2, most often 1.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var at = i * 4;
            pixels[at] = 0xFF;
            pixels[at + 1] = 0xFF;
            pixels[at + 2] = 0xFF;
            pixels[at + 3] = (byte)((state & 1) + ((state >> 16) & 1));
        }

        return pixels;
    }

    /// <summary>The tile as a PNG file, for Windows' decoder.</summary>
    public static byte[] Png(int size = Size) => PngWriter.Write(Pixels(size), size, size);
}
