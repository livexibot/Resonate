using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Slim bars in a row: rising from the bottom (Bars), growing both ways from
/// the middle of the room (Mirror), or hairlines (Lines). Each is brightest
/// at its tip and fades towards the bottom.
/// </summary>
internal sealed class BarsDrawing(VisualizerCanvas canvas, VisualizerStyle style) : VisualizerDrawing(canvas)
{
    private const double TipAlpha = 0.92;
    private const double BaseAlpha = 0.18;

    private readonly List<(SpriteVisual Sprite, CompositionLinearGradientBrush Brush, CompositionColorGradientStop Tip, CompositionColorGradientStop Base)> _bars = [];

    public override int CountFor(Vector2 size, int wanted) => StageBars.Count(size.X, wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_bars.Count == 0)
        {
            return;
        }

        var pitch = size.X / _bars.Count;
        var width = style == VisualizerStyle.Lines
            ? Math.Clamp(pitch * 0.18f, 1f, 2.5f)
            : Math.Max(1f, (float)(pitch * Canvas.Fill));
        for (var i = 0; i < _bars.Count; i++)
        {
            var sprite = _bars[i].Sprite;
            var x = (i * pitch) + ((pitch - width) / 2);
            sprite.Size = new Vector2(width, height);
            sprite.Offset = new Vector3(x, size.Y - height, 0);

            // Mirror grows both ways from the middle of the room; the others rise from the bottom.
            sprite.CenterPoint = new Vector3(0, style == VisualizerStyle.Mirror ? height / 2 : height, 0);

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
            var colour = ColourAt(colours, _bars.Count > 1 ? i / (double)(_bars.Count - 1) : 0);
            Paint(_bars[i].Tip, colour.WithAlpha(TipAlpha), animate);
            Paint(_bars[i].Base, colour.WithAlpha(BaseAlpha), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
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
            _bars[i].Sprite.StartAnimation("Scale.Y", Expression($"Max(0, {Level(i)})"));
        }
    }

    protected override void End()
    {
        foreach (var (sprite, _, _, _) in _bars)
        {
            sprite.StopAnimation("Scale.Y");
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
