using System.Numerics;
using Microsoft.UI.Composition;

namespace Resonate.App.Helpers;

/// <summary>
/// Shows a visual tree that fades out towards the bottom: the tree is drawn
/// into a surface and shown through a mask whose alpha runs from full to
/// none over its lowest share, so what lies under it shows through there.
/// Used for the soft clouds of Home's stage, which melt into the page below
/// (the owner's request, 11 October 2026). Draws at the composition's own
/// resolution, so it suits soft content, not pixel-exact drawing.
/// </summary>
internal sealed class BottomFade : IDisposable
{
    private readonly CompositionVisualSurface _surface;
    private readonly CompositionSurfaceBrush _source;
    private readonly CompositionLinearGradientBrush _gradient;
    private readonly CompositionColorGradientStop _top;
    private readonly CompositionColorGradientStop _full;
    private readonly CompositionColorGradientStop _none;
    private readonly CompositionMaskBrush _mask;

    public BottomFade(Compositor compositor, Visual content)
    {
        _surface = compositor.CreateVisualSurface();
        _surface.SourceVisual = content;
        _source = compositor.CreateSurfaceBrush(_surface);
        _source.Stretch = CompositionStretch.Fill;
        _gradient = compositor.CreateLinearGradientBrush();
        _gradient.StartPoint = Vector2.Zero;
        _gradient.EndPoint = new Vector2(0, 1);
        _top = compositor.CreateColorGradientStop(0, Microsoft.UI.Colors.White);
        _full = compositor.CreateColorGradientStop(0.6f, Microsoft.UI.Colors.White);
        _none = compositor.CreateColorGradientStop(1, Microsoft.UI.ColorHelper.FromArgb(0, 255, 255, 255));
        _gradient.ColorStops.Add(_top);
        _gradient.ColorStops.Add(_full);
        _gradient.ColorStops.Add(_none);
        _mask = compositor.CreateMaskBrush();
        _mask.Source = _source;
        _mask.Mask = _gradient;
        Sprite = compositor.CreateSpriteVisual();
        Sprite.Brush = _mask;
    }

    /// <summary>What shows the faded tree; put it where the tree was.</summary>
    public SpriteVisual Sprite { get; }

    /// <summary>The lowest share of the height that fades out (0 to 1).</summary>
    public float Share
    {
        get => 1 - _full.Offset;
        set => _full.Offset = Math.Clamp(1 - value, 0, 1);
    }

    /// <summary>The area drawn, from the tree's top left.</summary>
    public void Resize(Vector2 size)
    {
        _surface.SourceSize = size;
        Sprite.Size = size;
    }

    public void Dispose()
    {
        Sprite.Brush = null;
        _mask.Dispose();
        _gradient.Dispose();
        _top.Dispose();
        _full.Dispose();
        _none.Dispose();
        _source.Dispose();
        _surface.SourceVisual = null;
        _surface.Dispose();
        Sprite.Dispose();
    }
}
