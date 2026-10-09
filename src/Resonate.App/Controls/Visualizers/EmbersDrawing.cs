using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Sparks drifting up through the room, each at its own pace and sway,
/// fading in at the bottom and out at the top; each follows one band of the
/// sound, glowing brighter and larger as it gets louder. Their path is the
/// compositor's clock alone (<see cref="VisualizerShapes.Ember"/>).
/// </summary>
internal sealed class EmbersDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private readonly List<(SpriteVisual Spark, CompositionRadialGradientBrush Brush, CompositionColorGradientStop[] Stops, double Across)> _sparks = [];

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.EmberCount(size.X);

    public override void Place(Vector2 size, float height)
    {
        var dot = Canvas.InBar ? 7f : 14f;
        Geometry.InsertScalar("W", size.X);
        Geometry.InsertScalar("Bottom", size.Y);
        Geometry.InsertScalar("Travel", Math.Max(0, height));
        Geometry.InsertScalar("D", dot);
        for (var i = 0; i < _sparks.Count; i++)
        {
            var spark = _sparks[i].Spark;
            var side = (float)(dot * VisualizerShapes.Ember(i).Size);
            spark.Size = new Vector2(side, side);
            spark.CenterPoint = new Vector3(side / 2, side / 2, 0);
            if (!Moving)
            {
                spark.Opacity = 0;
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        foreach (var (_, _, stops, across) in _sparks)
        {
            var colour = ColourAt(colours, across);
            Paint(stops[0], colour.WithAlpha(1), animate);
            Paint(stops[1], colour.WithAlpha(0.75), animate);
            Paint(stops[2], colour.WithAlpha(0), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = VisualizerShapes.EmberChannels;
        foreach (var name in (string[])["W", "Bottom", "Travel", "D"])
        {
            Geometry.InsertScalar(name, 0);
        }

        for (var i = 0; i < count; i++)
        {
            var (brush, stops) = DotBrush(0, 0.4f, 1);
            var spark = Compositor.CreateSpriteVisual();
            spark.Brush = brush;
            spark.Opacity = 0;
            Layer.Children.InsertAtTop(spark);
            _sparks.Add((spark, brush, stops, VisualizerShapes.Ember(i).X));
        }
    }

    protected override void Begin()
    {
        for (var i = 0; i < _sparks.Count; i++)
        {
            var spark = _sparks[i].Spark;
            var e = VisualizerShapes.Ember(i);
            var size = $"({G}.D * {Num(e.Size)})";

            // How far up its rise the spark is, 0 to 1, a whole number of rises per loop.
            var up = $"Mod({P}.Time * {Num(e.Rise)} + {Num(e.Start)}, 1)";
            var level = $"Clamp({Level(e.Channel)}, 0, 1)";
            spark.StartAnimation("Offset", Expression(
                $"Vector3({Num(e.X)} * ({G}.W - {size}) + {Num(e.Drift)} * Sin({P}.Time * {Num(e.DriftSpeed)} + {Num(e.DriftPhase)}), {G}.Bottom - {size} - {up} * ({G}.Travel - {size}), 0)"));
            spark.StartAnimation("Opacity", Expression($"{Fade} * Sin({up} * 3.14159) * (0.3 + 0.7 * Clamp({level} * 1.3, 0, 1))"));
            spark.StartAnimation("Scale", Expression($"Vector3(0.6 + 0.9 * {level}, 0.6 + 0.9 * {level}, 1)"));
        }
    }

    protected override void End()
    {
        foreach (var (spark, _, _, _) in _sparks)
        {
            spark.StopAnimation("Offset");
            spark.StopAnimation("Opacity");
            spark.StopAnimation("Scale");
            spark.Opacity = 0;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (spark, brush, stops, _) in _sparks)
        {
            Release(spark, brush, stops);
        }

        _sparks.Clear();
    }
}
