using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// What a drawing draws on: the compositor, the host's root visual and its
/// property set ("p": Time, Energy, Live, the levels L0, L1 … and the peaks
/// K0, K1 …), and the host's few options.
/// </summary>
internal sealed class VisualizerCanvas(Compositor compositor, ContainerVisual root, CompositionPropertySet props)
{
    public Compositor Compositor { get; } = compositor;

    public ContainerVisual Root { get; } = root;

    public CompositionPropertySet Props { get; } = props;

    /// <summary>How much of each step a bar fills (the user's bar width, 0.2 to 0.9).</summary>
    public double Fill { get; set; } = StageBars.Fill;

    /// <summary>How far drawings around the cover may reach outside it.</summary>
    public double Reach { get; set; } = 40;

    /// <summary>The cover's corner radius, which the Pulse's rings follow.</summary>
    public double Corner { get; set; } = 8;

    /// <summary>Drawn in the player bar: smaller dots and sparks.</summary>
    public bool InBar { get; set; }
}

/// <summary>
/// One way of drawing the visualizer (<see cref="VisualizerStyle"/>) for
/// <see cref="StageVisualizer"/>, which owns the sound, the clock and when
/// anything moves. A drawing makes its visuals in a layer of its own, lays
/// them out for a size, and moves them with expressions on the host's
/// property set and its own geometry ("g"), so nothing on the interface
/// thread runs per frame and a new size only changes a few numbers. When
/// stopped it leaves nothing showing and nothing animating.
/// </summary>
internal abstract class VisualizerDrawing : IDisposable
{
    protected const string P = StageVisualizer.Props;
    protected const string G = "g";

    protected VisualizerDrawing(VisualizerCanvas canvas)
    {
        Canvas = canvas;
        Compositor = canvas.Compositor;
        Geometry = Compositor.CreatePropertySet();
        Layer = Compositor.CreateContainerVisual();
        canvas.Root.Children.InsertAtTop(Layer);
    }

    /// <summary>How many sound levels it follows; set when built.</summary>
    public int Channels { get; protected set; }

    /// <summary>The count it was last built with (-1 before the first build).</summary>
    public int Built { get; private set; } = -1;

    /// <summary>Its few broad levels are averages of the bands (lows, mids, highs), not samples.</summary>
    public virtual bool Averages => false;

    /// <summary>The host works out falling peaks (K0, K1 …) for it while it follows sound.</summary>
    public virtual bool WantsPeaks => false;

    /// <summary>Drawn around the cover (inside its box) rather than along the bottom.</summary>
    public virtual bool AroundCover => false;

    protected VisualizerCanvas Canvas { get; }

    protected Compositor Compositor { get; }

    protected CompositionPropertySet Geometry { get; }

    protected ContainerVisual Layer { get; }

    /// <summary>Whether its expressions run.</summary>
    protected bool Moving { get; private set; }

    /// <summary>How many elements a host this size wants (<paramref name="wanted"/> is the user's bar count).</summary>
    public abstract int CountFor(Vector2 size, int wanted);

    /// <summary>Makes its elements afresh for <paramref name="count"/>.</summary>
    public void Build(int count)
    {
        Stop();
        Clear();
        Built = count;
        Make(count);
    }

    /// <summary>
    /// Lays the elements out in a host <paramref name="size"/> big, with
    /// <paramref name="height"/> of room along the bottom (unused around the cover).
    /// </summary>
    public abstract void Place(Vector2 size, float height);

    /// <summary>Starts every expression. Throws when the compositor refuses one.</summary>
    public void Start()
    {
        Moving = true;
        Begin();
    }

    /// <summary>Ends every animation and leaves nothing showing.</summary>
    public void Stop()
    {
        Moving = false;
        End();
    }

    /// <summary>Paints the elements in <paramref name="colours"/>, flowing over a second when <paramref name="animate"/>.</summary>
    public abstract void Paint(IReadOnlyList<ThemeColor> colours, bool animate);

    public void Dispose()
    {
        Stop();
        Clear();
        Canvas.Root.Children.Remove(Layer);
        Layer.Dispose();
        Geometry.Dispose();
    }

    protected abstract void Make(int count);

    protected abstract void Begin();

    protected abstract void End();

    /// <summary>Removes every element and lets go of its visuals and brushes now.</summary>
    protected abstract void Clear();

    /// <summary>Level <paramref name="index"/> of <see cref="Channels"/> as an expression (0 to about 1).</summary>
    protected string Level(int index) => StageVisualizer.LevelExpression(index, Channels);

    /// <summary>An expression that can read "p" (the host's) and "g" (this drawing's geometry).</summary>
    protected ExpressionAnimation Expression(string text)
    {
        var expression = Compositor.CreateExpressionAnimation(text);
        expression.SetReferenceParameter(P, Canvas.Props);
        expression.SetReferenceParameter(G, Geometry);
        return expression;
    }

    /// <summary>Fades with the energy, so a stopping drawing sinks away with the bars.</summary>
    protected static string Fade => $"Min(1, {P}.Energy * 1.5)";

    protected static string Num(double value) => VisualizerShapes.Number(value);

    /// <summary>The colour at <paramref name="across"/> (0 to 1) between <paramref name="colours"/>.</summary>
    protected static ThemeColor ColourAt(IReadOnlyList<ThemeColor> colours, double across)
    {
        if (colours.Count == 1)
        {
            return colours[0];
        }

        var at = Math.Clamp(across, 0, 1) * (colours.Count - 1);
        var low = (int)Math.Floor(at);
        var high = Math.Min(low + 1, colours.Count - 1);
        return colours[low].Mix(colours[high], at - low);
    }

    protected void Paint(CompositionColorGradientStop stop, ThemeColor colour, bool animate)
    {
        if (animate)
        {
            stop.StartAnimation("Color", Flow(colour));
            return;
        }

        stop.StopAnimation("Color");
        stop.Color = colour.ToColor();
    }

    protected void Paint(CompositionColorBrush brush, ThemeColor colour, bool animate)
    {
        if (animate)
        {
            brush.StartAnimation("Color", Flow(colour));
            return;
        }

        brush.StopAnimation("Color");
        brush.Color = colour.ToColor();
    }

    /// <summary>A brush brightest at the top (the tip) and faint at the bottom.</summary>
    protected (CompositionLinearGradientBrush Brush, CompositionColorGradientStop Tip, CompositionColorGradientStop Base) TipBrush()
    {
        var brush = Compositor.CreateLinearGradientBrush();
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = new Vector2(0, 1);
        var tip = Compositor.CreateColorGradientStop(0, default);
        var bottom = Compositor.CreateColorGradientStop(1, default);
        brush.ColorStops.Add(tip);
        brush.ColorStops.Add(bottom);
        return (brush, tip, bottom);
    }

    /// <summary>A round dot that glows: solid in the middle, soft at its edge.</summary>
    protected (CompositionRadialGradientBrush Brush, CompositionColorGradientStop[] Stops) DotBrush(params float[] offsets)
    {
        var brush = Compositor.CreateRadialGradientBrush();
        var stops = new CompositionColorGradientStop[offsets.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            stops[i] = Compositor.CreateColorGradientStop(offsets[i], default);
            brush.ColorStops.Add(stops[i]);
        }

        return (brush, stops);
    }

    /// <summary>Lets go of a visual, its brush and the brush's parts (kept by the drawing, never read back from the visual).</summary>
    protected static void Release(Visual visual, CompositionObject? brush, params CompositionObject[] parts)
    {
        if (visual is SpriteVisual sprite)
        {
            sprite.Brush = null;
        }

        foreach (var part in parts)
        {
            part.Dispose();
        }

        brush?.Dispose();
        visual.Dispose();
    }

    private ColorKeyFrameAnimation Flow(ThemeColor colour)
    {
        var flow = Compositor.CreateColorKeyFrameAnimation();
        flow.InsertKeyFrame(1, colour.ToColor());
        flow.Duration = TimeSpan.FromSeconds(1);
        return flow;
    }
}
