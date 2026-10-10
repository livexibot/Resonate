using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.Themes;
using Resonate.Windows;

namespace Resonate.App.Controls;

/// <summary>
/// A faint grain over a large soft gradient, so its colour steps never show
/// as rings (see <see cref="DitherNoise"/>). The tile is drawn one texel per
/// screen pixel (display scaling and App size counted), repeated by as many
/// sprites as cover the layer, nearest-neighbour so the grain stays one
/// pixel fine. Static: nothing moves, so it costs nothing once drawn.
/// </summary>
internal sealed partial class DitherLayer : Grid
{
    // The tile, decoded once for every layer.
    private static Task<LoadedImageSurface?>? _loading;

    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionSurfaceBrush _brush;
    private readonly List<SpriteVisual> _tiles = [];
    private Vector2 _size;
    private double _laidOutScale;
    private Vector2 _laidOutSize;
    private XamlRoot? _xamlRoot;

    public DitherLayer()
    {
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _shape = _compositor.CreateRoundedRectangleGeometry();
        _root.Clip = _compositor.CreateGeometricClip(_shape);
        _brush = _compositor.CreateSurfaceBrush();
        _brush.Stretch = CompositionStretch.Fill;
        _brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.NearestNeighbor;
        ElementCompositionPreview.SetElementChildVisual(this, _root);
        _ = UseTileAsync();

        SizeChanged += (_, e) =>
        {
            _size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            _shape.Size = _size;
            LayOut();
        };
        Loaded += (_, _) =>
        {
            App.Services.Theme.SizeChanged -= OnAppSizeChanged;
            App.Services.Theme.SizeChanged += OnAppSizeChanged;
            if (_xamlRoot is not null)
            {
                _xamlRoot.Changed -= OnXamlRootChanged;
            }

            _xamlRoot = XamlRoot;
            if (_xamlRoot is not null)
            {
                _xamlRoot.Changed += OnXamlRootChanged;
            }

            LayOut();
        };
        Unloaded += (_, _) =>
        {
            App.Services.Theme.SizeChanged -= OnAppSizeChanged;
            if (_xamlRoot is not null)
            {
                _xamlRoot.Changed -= OnXamlRootChanged;
                _xamlRoot = null;
            }
        };
    }

    /// <summary>The layer's rounded corners, matching the card it lies on (0 for square).</summary>
    public float CornerRadiusValue
    {
        get => _shape.CornerRadius.X;
        set => _shape.CornerRadius = new Vector2(value, value);
    }

    private void OnAppSizeChanged(object? sender, EventArgs e) => LayOut();

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => LayOut();

    private static Task<LoadedImageSurface?> LoadTile() => _loading ??= LoadTileAsync();

    private static async Task<LoadedImageSurface?> LoadTileAsync()
    {
        try
        {
            var png = await Task.Run(() => DitherNoise.Png());
            var stream = await ImageStreams.FromBytesAsync(png);
            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            var loaded = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.LoadCompleted += (_, e) =>
            {
                stream.Dispose();
                loaded.TrySetResult(e.Status);
            };

            return await loaded.Task == LoadedImageSourceLoadStatus.Success ? surface : null;
        }
        catch (Exception)
        {
            // No grain rather than none of the stage.
            return null;
        }
    }

    private async Task UseTileAsync()
    {
        if (await LoadTile() is { } surface)
        {
            _brush.Surface = surface;
        }
    }

    /// <summary>As many tiles as cover the layer, each one texel per screen pixel.</summary>
    private void LayOut()
    {
        if (_size.X <= 0 || _size.Y <= 0 || XamlRoot is not { } root)
        {
            return;
        }

        var scale = root.RasterizationScale * App.Services.Theme.Scale;
        if (Math.Abs(scale - _laidOutScale) < 0.001 && _size == _laidOutSize)
        {
            return;
        }

        _laidOutScale = scale;
        _laidOutSize = _size;
        var side = (float)(DitherNoise.Size / scale);
        var columns = (int)Math.Ceiling(_size.X / side);
        var rows = (int)Math.Ceiling(_size.Y / side);
        var count = columns * rows;
        while (_tiles.Count < count)
        {
            var tile = _compositor.CreateSpriteVisual();
            tile.Brush = _brush;
            _tiles.Add(tile);
            _root.Children.InsertAtTop(tile);
        }

        while (_tiles.Count > count)
        {
            var last = _tiles[^1];
            _tiles.RemoveAt(_tiles.Count - 1);
            _root.Children.Remove(last);
            last.Dispose();
        }

        for (var i = 0; i < count; i++)
        {
            var tile = _tiles[i];
            tile.Size = new Vector2(side, side);
            tile.Offset = new Vector3(i % columns * side, i / columns * side, 0);
        }
    }
}
