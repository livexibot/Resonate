using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Resonate.Themes.Skins;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The pixels of one of the mini player's skinned windows: a picture in skin
/// pixels, enlarged a whole number of times into a bitmap of exactly the
/// pixels on screen and shown 1:1 by an <see cref="Image"/>, so skins stay sharp.
/// </summary>
internal sealed class SkinSurface
{
    private readonly Image _view;
    private WriteableBitmap? _bitmap;
    private byte[]? _pixels;

    public SkinSurface(Image view) => _view = view;

    /// <summary>The picture to draw into, in skin pixels; null until <see cref="Resize"/>.</summary>
    public SkinImage? Frame { get; private set; }

    /// <summary>Screen pixels per skin pixel.</summary>
    public int Scale { get; private set; } = 1;

    /// <summary>The display's scale (screen pixels per device-independent pixel).</summary>
    public double Raster { get; private set; } = 1;

    /// <summary>Makes the picture <paramref name="width"/> x <paramref name="height"/> skin pixels at a scale; true when anything changed (draw again).</summary>
    public bool Resize(int width, int height, int scale, double raster)
    {
        if (Frame is not null && Frame.Width == width && Frame.Height == height && Scale == scale && Raster.Equals(raster))
        {
            return false;
        }

        Scale = scale;
        Raster = raster;
        Frame = new SkinImage(width, height);
        _pixels = new byte[width * scale * height * scale * 4];
        _bitmap = new WriteableBitmap(width * scale, height * scale);
        _view.Source = _bitmap;
        _view.Width = _bitmap.PixelWidth / raster;
        _view.Height = _bitmap.PixelHeight / raster;
        return true;
    }

    /// <summary>Puts what was drawn into <see cref="Frame"/> on screen.</summary>
    public void Present()
    {
        if (Frame is null || _bitmap is null || _pixels is null)
        {
            return;
        }

        PixelScaler.Scale(Frame, Scale, MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan()));
        _pixels.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
    }

    /// <summary>A point on the picture (device-independent pixels from its top left) in skin pixels.</summary>
    public (double X, double Y) SkinPoint(Point position) => (position.X * Raster / Scale, position.Y * Raster / Scale);
}
