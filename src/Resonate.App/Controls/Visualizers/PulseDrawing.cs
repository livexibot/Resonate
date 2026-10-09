using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Two to five rings (the user's Amount) in the cover's shape around it,
/// over a soft glow: the outer ring and the glow swell with the lows, the
/// inner one with the highs and those between with the bands between; the
/// user's Size is the rings' thickness. Drawn in the cover's box (behind the
/// cover), so they move and shrink with it.
/// </summary>
internal sealed class PulseDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private readonly List<(ShapeVisual Visual, CompositionSpriteShape Shape, CompositionRoundedRectangleGeometry Outline, CompositionColorBrush Brush)> _rings = [];
    private SpriteVisual? _glow;
    private CompositionRadialGradientBrush? _glowBrush;
    private CompositionColorGradientStop[] _glowStops = [];

    public override bool AroundCover => true;

    public override bool Averages => true;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.PulseRingCount(wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_glow is null)
        {
            return;
        }

        var side = Math.Min(size.X, size.Y);
        var reach = Canvas.Reach;
        var stroke = (float)VisualizerShapes.SizeBetween(1, 5, Canvas.Fill);
        var centre = new Vector2(side / 2);

        // The glow reaches a little past the outer ring; inside the cover's edge it is hidden by the cover.
        var glow = (float)(side + (reach * 3));
        _glow.Size = new Vector2(glow);
        _glow.Offset = new Vector3(centre - new Vector2(glow / 2), 0);
        _glow.CenterPoint = new Vector3(glow / 2, glow / 2, 0);
        _glowStops[1].Offset = Math.Clamp(side / glow, 0, 0.95f);
        for (var r = 0; r < _rings.Count; r++)
        {
            var (visual, shape, outline, _) = _rings[r];
            var inset = (float)VisualizerShapes.PulseInset(r, _rings.Count, reach);
            var ring = side + (2 * inset);
            var box = ring + (stroke * 2);
            shape.StrokeThickness = stroke;
            visual.Size = new Vector2(box);
            visual.Offset = new Vector3(centre - new Vector2(box / 2), 0);
            visual.CenterPoint = new Vector3(box / 2, box / 2, 0);
            outline.Size = new Vector2(ring);
            outline.Offset = new Vector2(stroke);
            outline.CornerRadius = new Vector2((float)(Canvas.Corner + inset));
            Geometry.InsertScalar("M" + r, (float)VisualizerShapes.PulseGrowth(r, _rings.Count, side, reach));
            if (!Moving)
            {
                visual.Opacity = 0;
            }
        }

        if (!Moving)
        {
            _glow.Opacity = 0;
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0 || _glow is null)
        {
            return;
        }

        var low = ColourAt(colours, 0);
        Paint(_glowStops[0], low.WithAlpha(0.7), animate);
        Paint(_glowStops[1], low.WithAlpha(0.42), animate);
        Paint(_glowStops[2], low.WithAlpha(0), animate);
        for (var r = 0; r < _rings.Count; r++)
        {
            // The outer ring takes the lows' colour, the inner one the highs'.
            Paint(_rings[r].Brush, ColourAt(colours, 1 - (r / (double)Math.Max(1, _rings.Count - 1))).WithAlpha(0.9), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        (_glowBrush, _glowStops) = DotBrush(0, 0.8f, 1);
        _glow = Compositor.CreateSpriteVisual();
        _glow.Brush = _glowBrush;
        _glow.Opacity = 0;
        Layer.Children.InsertAtTop(_glow);
        for (var r = 0; r < count; r++)
        {
            Geometry.InsertScalar("M" + r, 0);
            var outline = Compositor.CreateRoundedRectangleGeometry();
            var brush = Compositor.CreateColorBrush();
            var shape = Compositor.CreateSpriteShape(outline);
            shape.StrokeBrush = brush;
            shape.StrokeThickness = 2;
            var visual = Compositor.CreateShapeVisual();
            visual.Shapes.Add(shape);
            visual.Opacity = 0;
            Layer.Children.InsertAtTop(visual);
            _rings.Add((visual, shape, outline, brush));
        }
    }

    protected override void Begin()
    {
        if (_glow is null)
        {
            return;
        }

        // Ring r follows the highs when inside (r = 0) and the lows when outside.
        var lows = $"Clamp({Level(0)}, 0, 1)";
        _glow.StartAnimation("Opacity", Expression($"{Fade} * (0.3 + 0.7 * {lows})"));
        _glow.StartAnimation("Scale", Expression($"Vector3(0.8 + 0.3 * {lows}, 0.8 + 0.3 * {lows}, 1)"));
        for (var r = 0; r < _rings.Count; r++)
        {
            var level = $"Clamp({Level(Channels - 1 - r)} * 1.2, 0, 1)";
            var visual = _rings[r].Visual;
            visual.StartAnimation("Opacity", Expression($"{Fade} * (0.15 + 0.85 * {level})"));
            visual.StartAnimation("Scale", Expression($"Vector3(1 + {level} * {G}.M{r}, 1 + {level} * {G}.M{r}, 1)"));
        }
    }

    protected override void End()
    {
        if (_glow is null)
        {
            return;
        }

        _glow.StopAnimation("Opacity");
        _glow.StopAnimation("Scale");
        _glow.Opacity = 0;
        foreach (var (visual, _, _, _) in _rings)
        {
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.Opacity = 0;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (visual, shape, outline, brush) in _rings)
        {
            visual.Shapes.Clear();
            shape.StrokeBrush = null;
            Release(visual, brush, shape, outline);
        }

        _rings.Clear();
        if (_glow is not null)
        {
            Release(_glow, _glowBrush, _glowStops);
            _glow = null;
            _glowBrush = null;
            _glowStops = [];
        }
    }
}
