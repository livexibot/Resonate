using System.Runtime.InteropServices;

namespace Resonate.Themes.Skins;

/// <summary>
/// A picture as opaque 32-bit pixels, <c>0xAARRGGBB</c>, rows top to bottom
/// with no padding. In memory each pixel is the bytes B, G, R, A, which is
/// what Windows' BGRA8 bitmaps take, so <see cref="AsBytes"/> can be copied
/// into one as it is.
/// </summary>
public sealed class SkinImage
{
    /// <summary>The largest side a skin picture may have; bigger ones are refused before any memory is taken.</summary>
    public const int MaxSide = 2048;

    public SkinImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxSide * 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxSide * 4);
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row after row, <see cref="Width"/> pixels each.</summary>
    public uint[] Pixels { get; }

    public uint this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }

    /// <summary>The pixels as BGRA bytes, ready for a Windows bitmap.</summary>
    public Span<byte> AsBytes() => MemoryMarshal.AsBytes(Pixels.AsSpan());

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public void Fill(uint argb) => Pixels.AsSpan().Fill(argb);

    /// <summary>Fills a rectangle; the part outside the picture is skipped.</summary>
    public void FillRect(int x, int y, int width, int height, uint argb)
    {
        var left = Math.Max(x, 0);
        var top = Math.Max(y, 0);
        var right = Math.Min(x + width, Width);
        var bottom = Math.Min(y + height, Height);
        for (var row = top; row < bottom; row++)
        {
            Pixels.AsSpan((row * Width) + left, Math.Max(right - left, 0)).Fill(argb);
        }
    }

    /// <summary>
    /// Copies the <paramref name="width"/> x <paramref name="height"/> block at
    /// (<paramref name="sourceX"/>, <paramref name="sourceY"/>) of
    /// <paramref name="source"/> to (<paramref name="x"/>, <paramref name="y"/>).
    /// Whatever falls outside either picture is skipped, so a skin whose sheet
    /// is too small draws only the part it has.
    /// </summary>
    public void Draw(SkinImage source, int sourceX, int sourceY, int width, int height, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Clip against the source, then the destination, moving both corners together.
        if (sourceX < 0)
        {
            width += sourceX;
            x -= sourceX;
            sourceX = 0;
        }

        if (sourceY < 0)
        {
            height += sourceY;
            y -= sourceY;
            sourceY = 0;
        }

        if (x < 0)
        {
            width += x;
            sourceX -= x;
            x = 0;
        }

        if (y < 0)
        {
            height += y;
            sourceY -= y;
            y = 0;
        }

        width = Math.Min(width, Math.Min(source.Width - sourceX, Width - x));
        height = Math.Min(height, Math.Min(source.Height - sourceY, Height - y));
        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (var row = 0; row < height; row++)
        {
            source.Pixels.AsSpan(((sourceY + row) * source.Width) + sourceX, width)
                .CopyTo(Pixels.AsSpan(((y + row) * Width) + x, width));
        }
    }

    /// <summary>A copy of one block, for tests and for cutting sprites out of a sheet.</summary>
    public SkinImage Crop(int x, int y, int width, int height)
    {
        var copy = new SkinImage(width, height);
        copy.Draw(this, x, y, width, height, 0, 0);
        return copy;
    }
}
