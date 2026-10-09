using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Large soft glows rising from the bottom of the room, overlapping like
/// light: each follows a broad band of the sound (the lows on the left),
/// swelling taller and brighter as it gets louder, and drifts slowly
/// sideways on the compositor's clock (<see cref="VisualizerShapes.AuroraGlow"/>).
/// </summary>
internal sealed class AuroraDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private readonly List<(SpriteVisual Glow, CompositionRadialGradientBrush Brush, CompositionColorGradientStop[] Stops)> _glows = [];

    public override bool Averages => true;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.AuroraGlows(wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_glows.Count == 0)
        {
            return;
        }

        // The user's Size makes the glows wider and taller, never taller than the room.
        var width = (float)(size.X / _glows.Count * VisualizerShapes.SizeBetween(1.4, 3, Canvas.Fill));
        var tall = (float)(height * VisualizerShapes.SizeBetween(0.6, 1, Canvas.Fill));
        Geometry.InsertScalar("W", size.X);
        Geometry.InsertScalar("Bottom", size.Y);
        Geometry.InsertScalar("GW", width);
        Geometry.InsertScalar("GH", tall);
        foreach (var (glow, _, _) in _glows)
        {
            glow.Size = new Vector2(width, tall);

            // It grows upwards from the middle of its foot.
            glow.CenterPoint = new Vector3(width / 2, tall, 0);
            if (!Moving)
            {
                glow.Opacity = 0;
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _glows.Count; i++)
        {
            var colour = ColourAt(colours, VisualizerShapes.AuroraGlow(i, _glows.Count).X);
            var stops = _glows[i].Stops;
            Paint(stops[0], colour.WithAlpha(0.9), animate);
            Paint(stops[1], colour.WithAlpha(0.5), animate);
            Paint(stops[2], colour.WithAlpha(0), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        foreach (var name in (string[])["W", "Bottom", "GW", "GH"])
        {
            Geometry.InsertScalar(name, 0);
        }

        for (var i = 0; i < count; i++)
        {
            var (brush, stops) = DotBrush(0, 0.45f, 1);

            // A half glow: its brightest point on the bottom edge.
            brush.EllipseCenter = new Vector2(0.5f, 1);
            brush.EllipseRadius = new Vector2(0.5f, 1);
            var glow = Compositor.CreateSpriteVisual();
            glow.Brush = brush;
            glow.Opacity = 0;
            Layer.Children.InsertAtTop(glow);
            _glows.Add((glow, brush, stops));
        }
    }

    protected override void Begin()
    {
        for (var i = 0; i < _glows.Count; i++)
        {
            var (x, drift, speed, phase) = VisualizerShapes.AuroraGlow(i, _glows.Count);
            var level = $"Clamp({Level(i)} * 1.5, 0, 1)";
            var glow = _glows[i].Glow;
            glow.StartAnimation("Offset", Expression(
                $"Vector3({Num(x)} * {G}.W - {G}.GW / 2 + {Num(drift)} * {G}.W * Sin({P}.Time * {Num(speed)} + {Num(phase)}), {G}.Bottom - {G}.GH, 0)"));
            glow.StartAnimation("Scale", Expression($"Vector3(0.8 + 0.3 * {level}, 0.4 + 0.6 * {level}, 1)"));
            glow.StartAnimation("Opacity", Expression($"{Fade} * (0.5 + 0.5 * {level})"));
        }
    }

    protected override void End()
    {
        foreach (var (glow, _, _) in _glows)
        {
            glow.StopAnimation("Offset");
            glow.StopAnimation("Scale");
            glow.StopAnimation("Opacity");
            glow.Opacity = 0;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (glow, brush, stops) in _glows)
        {
            Release(glow, brush, stops);
        }

        _glows.Clear();
    }
}
