using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// A hi-fi's meter: chunky columns of segments lit from the bottom, the
/// unlit ones faintly there, and a peak segment above each column that
/// falls back slowly. The cover's colours run up the column, as a hi-fi's
/// run from green to red. Every column shares two gradient brushes (lit
/// and unlit segments); a clip shows as many lit segments as the level
/// reaches, a whole segment at a time.
/// </summary>
internal sealed class RetroDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    private const double LitAlpha = 0.95;
    private const double UnlitAlpha = 0.09;

    private readonly List<(SpriteVisual Unlit, SpriteVisual Lit, InsetClip Clip, SpriteVisual Peak)> _columns = [];
    private readonly List<(CompositionColorGradientStop Stop, int Segment, double Alpha)> _stops = [];
    private CompositionLinearGradientBrush? _litBrush;
    private CompositionLinearGradientBrush? _unlitBrush;
    private CompositionColorBrush? _peakBrush;
    private IReadOnlyList<ThemeColor> _colours = [];
    private int _segments;
    private float _height;

    public override bool WantsPeaks => true;

    public override int CountFor(Vector2 size, int wanted) => VisualizerShapes.RetroColumns(size.X, wanted);

    public override void Place(Vector2 size, float height)
    {
        if (_columns.Count == 0)
        {
            return;
        }

        var segments = VisualizerShapes.RetroSegments(height);
        if (segments != _segments)
        {
            _segments = segments;
            MakeBrushes();
        }

        _height = height;
        var pitch = size.X / _columns.Count;
        var width = Math.Max(2f, (float)(pitch * Canvas.Fill));
        var segment = height / segments;
        Geometry.InsertScalar("Pitch", pitch);
        Geometry.InsertScalar("Width", width);
        Geometry.InsertScalar("Top", size.Y - height);
        Geometry.InsertScalar("H", height);
        Geometry.InsertScalar("N", segments);
        Geometry.InsertScalar("Seg", segment);
        for (var i = 0; i < _columns.Count; i++)
        {
            var (unlit, lit, clip, peak) = _columns[i];
            var offset = new Vector3((i * pitch) + ((pitch - width) / 2), size.Y - height, 0);
            unlit.Size = lit.Size = new Vector2(width, height);
            unlit.Offset = lit.Offset = offset;
            peak.Size = new Vector2(width, (float)(segment * (1 - VisualizerShapes.RetroGap)));
            if (!Moving)
            {
                clip.TopInset = height;
                peak.Opacity = 0;
                unlit.Opacity = 0;
            }
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        _colours = colours;
        var top = Math.Max(1, _segments - 1);
        foreach (var (stop, segment, alpha) in _stops)
        {
            Paint(stop, ColourAt(colours, segment / (double)top).WithAlpha(alpha), animate);
        }

        if (_peakBrush is not null)
        {
            Paint(_peakBrush, ColourAt(colours, 1), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        foreach (var name in (string[])["Pitch", "Width", "Top", "H", "N", "Seg"])
        {
            Geometry.InsertScalar(name, name == "N" ? 1 : 0);
        }

        _peakBrush ??= Compositor.CreateColorBrush();
        for (var i = 0; i < count; i++)
        {
            var unlit = Compositor.CreateSpriteVisual();
            var lit = Compositor.CreateSpriteVisual();
            var clip = Compositor.CreateInsetClip();
            lit.Clip = clip;
            var peak = Compositor.CreateSpriteVisual();
            peak.Brush = _peakBrush;
            peak.Opacity = 0;
            unlit.Opacity = 0;
            unlit.Brush = _unlitBrush;
            lit.Brush = _litBrush;
            Layer.Children.InsertAtTop(unlit);
            Layer.Children.InsertAtTop(lit);
            Layer.Children.InsertAtTop(peak);
            _columns.Add((unlit, lit, clip, peak));
        }
    }

    protected override void Begin()
    {
        var round = Num(VisualizerShapes.RetroRound);
        for (var i = 0; i < _columns.Count; i++)
        {
            var (unlit, _, clip, peak) = _columns[i];
            var lit = $"Floor(Clamp({Level(i)}, 0, 1) * {G}.N + {round})";
            clip.StartAnimation("TopInset", Expression($"{G}.H * (1 - Min({G}.N, {lit}) / {G}.N)"));
            unlit.StartAnimation("Opacity", Expression(Fade));

            // The peak follows the sound only: the host's falling peak, a segment above the lit ones.
            var heard = $"Floor(Clamp({P}.{StageVisualizer.LevelName(i)} * {P}.Energy, 0, 1) * {G}.N + {round})";
            var top = $"Min({G}.N, Floor(Clamp({P}.{StageVisualizer.PeakName(i)}, 0, 1) * {G}.N + {round}))";
            peak.StartAnimation("Offset", Expression(
                $"Vector3({i} * {G}.Pitch + ({G}.Pitch - {G}.Width) / 2, {G}.Top + {G}.H * (1 - {top} / {G}.N) + {G}.Seg * {Num(VisualizerShapes.RetroGap)}, 0)"));
            peak.StartAnimation("Opacity", Expression($"({P}.Live > 0.5 && {top} > {heard}) ? {Fade} : 0"));
        }
    }

    protected override void End()
    {
        foreach (var (unlit, _, clip, peak) in _columns)
        {
            clip.StopAnimation("TopInset");
            clip.TopInset = Math.Max(_height, 1);
            unlit.StopAnimation("Opacity");
            unlit.Opacity = 0;
            peak.StopAnimation("Offset");
            peak.StopAnimation("Opacity");
            peak.Opacity = 0;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var (unlit, lit, clip, peak) in _columns)
        {
            lit.Clip = null;
            Release(unlit, null);
            Release(lit, null, clip);
            Release(peak, null);
        }

        _columns.Clear();
        ReleaseBrushes();
        _segments = 0;
        _peakBrush?.Dispose();
        _peakBrush = null;
    }

    /// <summary>The lit and unlit brushes for this many segments: hard stops, a dark gap at the top of each segment.</summary>
    private void MakeBrushes()
    {
        ReleaseBrushes();
        _litBrush = SegmentBrush(LitAlpha);
        // In the player bar the unlit segments would only clutter the controls.
        _unlitBrush = SegmentBrush(Canvas.InBar ? 0 : UnlitAlpha);
        foreach (var (unlit, lit, _, _) in _columns)
        {
            unlit.Brush = _unlitBrush;
            lit.Brush = _litBrush;
        }

        Paint(_colours, animate: false);
    }

    private CompositionLinearGradientBrush SegmentBrush(double alpha)
    {
        var brush = Compositor.CreateLinearGradientBrush();
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = new Vector2(0, 1);
        var n = _segments;
        var gap = (float)VisualizerShapes.RetroGap;
        for (var t = 0; t < n; t++)
        {
            // From the top: segment t is the (n - 1 - t)th from the bottom.
            var segment = n - 1 - t;
            var from = t / (float)n;
            var lit = (t + gap) / n;
            var to = (t + 1) / (float)n;
            foreach (var (offset, a) in ((float, double)[])[(from, 0), (lit, 0), (lit, alpha), (to, alpha)])
            {
                var stop = Compositor.CreateColorGradientStop(offset, default);
                brush.ColorStops.Add(stop);
                _stops.Add((stop, segment, a));
            }
        }

        return brush;
    }

    private void ReleaseBrushes()
    {
        foreach (var (unlit, lit, _, _) in _columns)
        {
            unlit.Brush = null;
            lit.Brush = null;
        }

        foreach (var (stop, _, _) in _stops)
        {
            stop.Dispose();
        }

        _stops.Clear();
        _litBrush?.Dispose();
        _unlitBrush?.Dispose();
        _litBrush = null;
        _unlitBrush = null;
    }
}
