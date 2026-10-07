namespace Resonate.Themes.Skins;

/// <summary>Enlarges skin pictures by whole numbers, so every skin pixel stays a sharp square.</summary>
public static class PixelScaler
{
    /// <summary>
    /// Writes <paramref name="source"/> enlarged <paramref name="scale"/> times
    /// into <paramref name="destination"/>: rows of
    /// <c>source.Width * scale</c> pixels, <c>source.Height * scale</c> of them.
    /// </summary>
    public static void Scale(SkinImage source, int scale, Span<uint> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var width = source.Width * scale;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width * source.Height * scale);

        for (var y = 0; y < source.Height; y++)
        {
            var row = destination.Slice(y * scale * width, width);
            var from = source.Pixels.AsSpan(y * source.Width, source.Width);
            for (var x = 0; x < from.Length; x++)
            {
                row.Slice(x * scale, scale).Fill(from[x]);
            }

            for (var copy = 1; copy < scale; copy++)
            {
                row.CopyTo(destination.Slice(((y * scale) + copy) * width, width));
            }
        }
    }
}
