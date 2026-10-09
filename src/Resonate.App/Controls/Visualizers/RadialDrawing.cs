using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Bars standing out all around the cover, hugging its square edge: the
/// lows at the bottom, the highs at the top, left and right mirrored. Drawn
/// in the cover's box (behind the cover), so they move and shrink with it.
/// </summary>
internal sealed class RadialDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private const double TipAlpha = 0.95;
    private const double BaseAlpha = 0.35;

    private readonly List<(SpriteVisual Sprite, CompositionLinearGradientBrush Brush, CompositionColorGradientStop Tip, CompositionColorGradientStop Base)> _bars = [];

    public override bool AroundCover => true;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.RadialCount(Math.Min(size.X, size.Y), wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_bars.Count == 0)
        {
            return;
        }

        var side = Math.Min(size.X, size.Y);
        var length = (float)Math.Max(4, Canvas.Reach - VisualizerShapes.RadialGap);
        var width = Math.Max(1.5f, (float)(side * 4 / _bars.Count * Canvas.Fill * 0.8));
        for (var i = 0; i < _bars.Count; i++)
        {
            var sprite = _bars[i].Sprite;
            var (x, y, degrees) = VisualizerShapes.RadialPlace(i, _bars.Count, side, VisualizerShapes.RadialGap);

            // The bar's foot is its bottom middle: it turns about it and grows outwards from it.
            sprite.Size = new Vector2(width, length);
            sprite.CenterPoint = new Vector3(width / 2, length, 0);
            sprite.Offset = new Vector3((float)x - (width / 2), (float)y - length, 0);
            sprite.RotationAngleInDegrees = (float)degrees;
            if (!Moving)
            {
                sprite.Scale = new Vector3(1, 0, 1);
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            var channel = VisualizerShapes.RadialChannel(i, _bars.Count);
            var colour = ColourAt(colours, channel / (double)Math.Max(1, Channels - 1));
            Paint(_bars[i].Tip, colour.WithAlpha(TipAlpha), animate);
            Paint(_bars[i].Base, colour.WithAlpha(BaseAlpha), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = VisualizerShapes.RadialChannels(count);
        for (var i = 0; i < count; i++)
        {
            var (brush, tip, bottom) = TipBrush();
            var sprite = Compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.Scale = new Vector3(1, 0, 1);
            Layer.Children.InsertAtTop(sprite);
            _bars.Add((sprite, brush, tip, bottom));
        }
    }

    protected override void Begin()
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            // The whole scale, not Scale.Y alone: a turned sprite ignored its Y channel's animation.
            var channel = VisualizerShapes.RadialChannel(i, _bars.Count);
            _bars[i].Sprite.StartAnimation("Scale", Expression($"Vector3(1, Clamp({Level(channel)}, 0, 1), 1)"));
        }
    }

    protected override void End()
    {
        foreach (var (sprite, _, _, _) in _bars)
        {
            sprite.StopAnimation("Scale");
            sprite.Scale = new Vector3(1, 0, 1);
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (sprite, brush, tip, bottom) in _bars)
        {
            Release(sprite, brush, tip, bottom);
        }

        _bars.Clear();
    }
}
