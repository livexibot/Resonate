using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// Rounded capsules growing both ways from the middle of the room, like a
/// modern voice waveform: each a rounded rectangle in one shape visual,
/// whose height (never less than its width, so a quiet band is a dot) and
/// place follow its band, so the ends stay round at every height.
/// </summary>
internal sealed class PillsDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private const double Alpha = 0.95;

    private readonly List<(CompositionSpriteShape Shape, CompositionRoundedRectangleGeometry Pill, CompositionColorBrush Brush)> _pills = [];
    private ShapeVisual? _visual;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.PillCount(size.X, wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_visual is null || _pills.Count == 0)
        {
            return;
        }

        var pitch = size.X / _pills.Count;
        var width = (float)Math.Clamp(pitch * Canvas.Fill, 2, pitch * 0.95);
        _visual.Size = size;
        Geometry.InsertScalar("Pitch", pitch);
        Geometry.InsertScalar("W", width);
        Geometry.InsertScalar("Mid", size.Y - (height / 2));
        Geometry.InsertScalar("H", height);
        for (var i = 0; i < _pills.Count; i++)
        {
            var pill = _pills[i].Pill;
            pill.CornerRadius = new Vector2(width / 2);
            if (!Moving)
            {
                pill.Size = new Vector2(width, width);
                pill.Offset = new Vector2((i * pitch) + ((pitch - width) / 2), size.Y - (height / 2) - (width / 2));
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _pills.Count; i++)
        {
            Paint(_pills[i].Brush, ColourAt(colours, _pills.Count > 1 ? i / (double)(_pills.Count - 1) : 0).WithAlpha(Alpha), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        foreach (var name in (string[])["Pitch", "W", "Mid", "H"])
        {
            Geometry.InsertScalar(name, 0);
        }

        _visual = Compositor.CreateShapeVisual();
        _visual.Opacity = 0;
        Layer.Children.InsertAtTop(_visual);
        for (var i = 0; i < count; i++)
        {
            var pill = Compositor.CreateRoundedRectangleGeometry();
            var brush = Compositor.CreateColorBrush();
            var shape = Compositor.CreateSpriteShape(pill);
            shape.FillBrush = brush;
            _visual.Shapes.Add(shape);
            _pills.Add((shape, pill, brush));
        }
    }

    protected override void Begin()
    {
        if (_visual is null)
        {
            return;
        }

        _visual.StartAnimation("Opacity", Expression(Fade));
        for (var i = 0; i < _pills.Count; i++)
        {
            var height = $"Max({G}.W, Clamp({Level(i)}, 0, 1) * {G}.H)";
            var pill = _pills[i].Pill;
            pill.StartAnimation("Size", Expression($"Vector2({G}.W, {height})"));
            pill.StartAnimation("Offset", Expression($"Vector2({i} * {G}.Pitch + ({G}.Pitch - {G}.W) / 2, {G}.Mid - {height} / 2)"));
        }
    }

    protected override void End()
    {
        if (_visual is null)
        {
            return;
        }

        _visual.StopAnimation("Opacity");
        _visual.Opacity = 0;
        foreach (var (_, pill, _) in _pills)
        {
            pill.StopAnimation("Size");
            pill.StopAnimation("Offset");
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        if (_visual is not null)
        {
            _visual.Shapes.Clear();
        }

        foreach (var (shape, pill, brush) in _pills)
        {
            shape.FillBrush = null;
            shape.Dispose();
            pill.Dispose();
            brush.Dispose();
        }

        _pills.Clear();
        _visual?.Dispose();
        _visual = null;
    }
}
