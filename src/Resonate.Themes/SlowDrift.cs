using System.Numerics;

namespace Resonate.Themes;

/// <summary>
/// The slow, soft motions behind the interface, worked out from the seconds
/// they have run: Liquid Glass's drifting cover and the Home stage's
/// clouds. The app shows them <see cref="FramesPerSecond"/> times a second
/// instead of handing the compositor animations that never end, which
/// redraw the window at the display's refresh rate (165 times a second on
/// the owner's display). Both are soft all the way across: between two of
/// these frames the cover moves about a screen pixel at most and a cloud a
/// few, with no edge to see jump, so they look the same.
/// </summary>
public static class SlowDrift
{
    /// <summary>How often the app shows the drift.</summary>
    public const int FramesPerSecond = 30;

    /// <summary>The cover behind Liquid Glass is drawn this much larger than the window at rest, so its soft borders never show.</summary>
    public const float CoverRestZoom = 1.18f;

    /// <summary>The cover's largest zoom, halfway through its drift.</summary>
    public const float CoverFarZoom = 1.32f;

    /// <summary>The cover turns this many degrees either way.</summary>
    public const float CoverTurn = 2.5f;

    /// <summary>How long the cover takes to zoom in (and as long to zoom out again).</summary>
    public const double CoverZoomSeconds = 28;

    /// <summary>How long the cover takes to turn one way (and as long back): not a multiple of the zoom, so the two never line up.</summary>
    public const double CoverTurnSeconds = 28 * 1.7;

    /// <summary>A cloud grows to this size halfway through its breath.</summary>
    public const float CloudBreath = 1.18f;

    // A cloud's slow loop around its place, in tenths of the field across and down.
    private static readonly Vector2[] CloudPath =
    [
        Vector2.Zero,
        new(1, -0.6f),
        new(0.3f, 1),
        new(-1, 0.4f),
        Vector2.Zero,
    ];

    /// <summary>The cover's zoom and turn (degrees) after <paramref name="seconds"/> of drift.</summary>
    public static (float Zoom, float Angle) Cover(double seconds)
    {
        var zoom = CoverRestZoom + ((CoverFarZoom - CoverRestZoom) * Ease(BackAndForth(seconds, CoverZoomSeconds)));
        var angle = -CoverTurn + (2 * CoverTurn * Ease(BackAndForth(seconds, CoverTurnSeconds)));
        return ((float)zoom, (float)angle);
    }

    /// <summary>
    /// Where a cloud is after <paramref name="seconds"/>, as a share of its
    /// reach across and down (a tenth of the field each way), on a loop of
    /// <paramref name="loopSeconds"/> that ends where it began.
    /// </summary>
    public static Vector2 CloudOffset(double seconds, double loopSeconds)
    {
        var at = Phase(seconds, loopSeconds) * (CloudPath.Length - 1);
        var leg = Math.Min((int)at, CloudPath.Length - 2);
        var along = (float)Ease(at - leg);
        return Vector2.Lerp(CloudPath[leg], CloudPath[leg + 1], along);
    }

    /// <summary>A cloud's size after <paramref name="seconds"/>: it grows and shrinks again once every <paramref name="breathSeconds"/>.</summary>
    public static float CloudScale(double seconds, double breathSeconds)
    {
        var at = Phase(seconds, breathSeconds) * 2;
        var grown = at <= 1 ? Ease(at) : 1 - Ease(at - 1);
        return (float)(1 + ((CloudBreath - 1) * grown));
    }

    /// <summary>
    /// Eases <paramref name="x"/> (0 to 1) in and out along the cubic Bézier
    /// curve (0.45, 0), (0.55, 1), the curve these drifts always used.
    /// </summary>
    public static double Ease(double x)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        // x(t) only grows, so halving the range finds t to well under a pixel's worth.
        double low = 0, high = 1, t = x;
        for (var i = 0; i < 24; i++)
        {
            t = (low + high) / 2;
            if (BezierX(t) < x)
            {
                low = t;
            }
            else
            {
                high = t;
            }
        }

        // y1 = 0 and y2 = 1: y(t) = 3t² - 2t³.
        return t * t * (3 - (2 * t));
    }

    /// <summary>0 to 1 and back again, once every two <paramref name="period"/>s.</summary>
    private static double BackAndForth(double seconds, double period)
    {
        var cycle = Phase(seconds, 2 * period) * 2;
        return cycle <= 1 ? cycle : 2 - cycle;
    }

    /// <summary>How far through its loop of <paramref name="period"/> a motion is, 0 to just under 1.</summary>
    private static double Phase(double seconds, double period)
    {
        var phase = (seconds % period) / period;
        return phase < 0 ? phase + 1 : phase;
    }

    private static double BezierX(double t)
    {
        var u = 1 - t;
        return (3 * u * u * t * 0.45) + (3 * u * t * t * 0.55) + (t * t * t);
    }
}
