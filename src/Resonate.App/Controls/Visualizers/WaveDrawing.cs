using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// A wave of round dots travelling across the room, as tall at each place
/// as the band there is loud (Wave), or two strands twisting around each
/// other, the near one larger and brighter (Helix). The wave's own motion
/// is the compositor's clock; only its height comes from the sound.
/// </summary>
internal sealed class WaveDrawing(VisualizerCanvas canvas, bool helix) : VisualizerDrawing(canvas)
{
    private readonly List<(SpriteVisual Dot, CompositionRadialGradientBrush Brush, CompositionColorGradientStop[] Stops, int Strand, int Index)> _dots = [];
    private int _count;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.WaveDots(size.X, helix, wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_count == 0)
        {
            return;
        }

        var pitch = size.X / _count;
        // The user's Size, never wider than the step between two dots.
        var dot = (float)Math.Min(
            pitch * 0.9,
            helix
                ? VisualizerShapes.SizeBetween(3, Canvas.InBar ? 6 : 12, Canvas.Fill)
                : VisualizerShapes.SizeBetween(2, Canvas.InBar ? 4 : 9, Canvas.Fill));
        Geometry.InsertScalar("Pitch", pitch);
        Geometry.InsertScalar("D", dot);
        Geometry.InsertScalar("Mid", size.Y - (height / 2));
        Geometry.InsertScalar("Amp", Math.Max(0, (height - dot) / 2));
        foreach (var (sprite, _, _, _, index) in _dots)
        {
            sprite.Size = new Vector2(dot, dot);
            sprite.CenterPoint = new Vector3(dot / 2, dot / 2, 0);
            if (!Moving)
            {
                sprite.Offset = new Vector3((index * pitch) + ((pitch - dot) / 2), size.Y - (height / 2) - (dot / 2), 0);
                sprite.Opacity = 0;
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        foreach (var (_, _, stops, strand, index) in _dots)
        {
            // The second strand runs through the colours the other way.
            var across = _count > 1 ? index / (double)(_count - 1) : 0;
            var colour = ColourAt(colours, strand == 0 ? across : 1 - across);
            Paint(stops[0], colour.WithAlpha(1), animate);
            Paint(stops[1], colour.WithAlpha(0.95), animate);
            Paint(stops[2], colour.WithAlpha(0), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        _count = count;
        foreach (var name in (string[])["Pitch", "D", "Mid", "Amp"])
        {
            Geometry.InsertScalar(name, 0);
        }

        for (var strand = 0; strand < (helix ? 2 : 1); strand++)
        {
            for (var i = 0; i < count; i++)
            {
                var (brush, stops) = DotBrush(0, 0.62f, 1);
                var sprite = Compositor.CreateSpriteVisual();
                sprite.Brush = brush;
                sprite.Opacity = 0;
                Layer.Children.InsertAtTop(sprite);
                _dots.Add((sprite, brush, stops, strand, i));
            }
        }
    }

    protected override void Begin()
    {
        var speed = Num(VisualizerShapes.WaveSpeed(helix));
        foreach (var (sprite, _, _, strand, i) in _dots)
        {
            var angle = $"({Num(VisualizerShapes.WavePhase(i, _count, helix))} - {P}.Time * {speed})";
            var height = $"Clamp({Level(i)} * 1.15, 0, 1)";
            var sign = strand == 0 ? "-" : "+";
            sprite.StartAnimation("Offset", Expression(
                $"Vector3({i} * {G}.Pitch + ({G}.Pitch - {G}.D) / 2, {G}.Mid - {G}.D / 2 {sign} {G}.Amp * {height} * Sin({angle}), 0)"));
            if (!helix)
            {
                sprite.StartAnimation("Opacity", Expression($"{Fade} * (0.4 + 0.6 * {height})"));
                continue;
            }

            // The strand in front is larger and brighter; the one behind smaller and fainter.
            var near = $"(0.5 {(strand == 0 ? "+" : "-")} 0.5 * Cos({angle}))";
            sprite.StartAnimation("Opacity", Expression($"{Fade} * (0.25 + 0.75 * {near})"));
            sprite.StartAnimation("Scale", Expression($"Vector3(0.5 + 0.7 * {near}, 0.5 + 0.7 * {near}, 1)"));
        }
    }

    protected override void End()
    {
        foreach (var (sprite, _, _, _, _) in _dots)
        {
            sprite.StopAnimation("Offset");
            sprite.StopAnimation("Opacity");
            sprite.StopAnimation("Scale");
            sprite.Opacity = 0;
            sprite.Scale = Vector3.One;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (sprite, brush, stops, _, _) in _dots)
        {
            Release(sprite, brush, stops);
        }

        _dots.Clear();
        _count = 0;
    }
}
