using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// A soft ribbon of light, mirrored about the middle of the room: narrow
/// columns that touch, each between two levels so the edge runs smoothly
/// from band to band, brightest along the middle and fading to nothing at
/// the top and bottom, tapering off at both ends. A fine thread stays while
/// the music is quiet.
/// </summary>
internal sealed class SilkDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    // The ribbon's light across its height, from its top edge to its middle (mirrored below).
    private static readonly (float Offset, double Alpha)[] Light = [(0, 0), (0.2f, 0.32), (0.5f, 1), (0.8f, 0.32), (1, 0)];

    private readonly List<(SpriteVisual Column, CompositionLinearGradientBrush Brush, CompositionColorGradientStop[] Stops)> _columns = [];

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.SilkColumns(size.X, wanted).Columns;

    public override void Place(Vector2 size, float height)
    {
        if (_columns.Count == 0)
        {
            return;
        }

        // The user's Size makes the ribbon taller or slimmer within the room.
        var tall = (float)(height * VisualizerShapes.SizeBetween(0.5, 1, Canvas.Fill));
        var pitch = size.X / _columns.Count;
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i].Column;

            // A little wider than the step, so no seam shows between columns.
            column.Size = new Vector2(pitch + 0.6f, tall);
            column.Offset = new Vector3(i * pitch, size.Y - (height / 2) - (tall / 2), 0);
            column.CenterPoint = new Vector3(0, tall / 2, 0);
            if (!Moving)
            {
                column.Scale = new Vector3(1, 0, 1);
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _columns.Count; i++)
        {
            var colour = ColourAt(colours, _columns.Count > 1 ? i / (double)(_columns.Count - 1) : 0);
            var stops = _columns[i].Stops;
            for (var s = 0; s < stops.Length; s++)
            {
                Paint(stops[s], colour.WithAlpha(Light[s].Alpha), animate);
            }
        }
    }

    protected override void Make(int count)
    {
        Channels = VisualizerShapes.SilkColumns(0, Canvas.Amount).Levels;
        for (var i = 0; i < count; i++)
        {
            var brush = Compositor.CreateLinearGradientBrush();
            brush.StartPoint = Vector2.Zero;
            brush.EndPoint = new Vector2(0, 1);
            var stops = new CompositionColorGradientStop[Light.Length];
            for (var s = 0; s < Light.Length; s++)
            {
                stops[s] = Compositor.CreateColorGradientStop(Light[s].Offset, default);
                brush.ColorStops.Add(stops[s]);
            }

            var column = Compositor.CreateSpriteVisual();
            column.Brush = brush;
            column.Scale = new Vector3(1, 0, 1);
            Layer.Children.InsertAtTop(column);
            _columns.Add((column, brush, stops));
        }
    }

    protected override void Begin()
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            var (level, toward) = VisualizerShapes.SilkBlend(i, _columns.Count, Channels);
            var next = Math.Min(level + 1, Channels - 1);
            var taper = Num(VisualizerShapes.SilkTaper(i, _columns.Count));
            var height = $"Clamp(Lerp({Level(level)}, {Level(next)}, {Num(toward)}) * 1.6, 0, 1) * {taper}";

            // The fine thread fades with the energy, so a stopped ribbon leaves nothing behind.
            _columns[i].Column.StartAnimation("Scale.Y", Expression($"{height} * 0.97 + 0.02 * {Fade}"));
        }
    }

    protected override void End()
    {
        foreach (var (column, _, _) in _columns)
        {
            column.StopAnimation("Scale.Y");
            column.Scale = new Vector3(1, 0, 1);
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (column, brush, stops) in _columns)
        {
            Release(column, brush, stops);
        }

        _columns.Clear();
    }
}
