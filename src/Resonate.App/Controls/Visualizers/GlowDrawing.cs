using System.Numerics;
using Microsoft.UI.Composition;
using Resonate.Themes;

namespace Resonate.App.Controls.Visualizers;

/// <summary>
/// A glow round the cover (the owner's request, 11 October 2026): three soft
/// halos behind it. The widest, in the lows' colour, swells and brightens
/// with the lows; a tighter one with the mids; and a thin rim of light in the
/// highs' colour with the highs. The user's Size is how far it reaches. Each
/// halo is the blurred shadow of a tile as large as the cover and hidden
/// behind it; round covers (Vinyl) take a round glow instead. Drawn in the
/// cover's box, so it moves and shrinks with the cover; Home only.
/// </summary>
internal sealed class GlowDrawing(VisualizerCanvas canvas) : VisualizerDrawing(canvas)
{
    /// <summary>The halos from widest to tightest: how far each blurs (times the reach), how much it swells, and how bright it is at rest.</summary>
    private static readonly (float Blur, float Swell, float Calm)[] Halos =
    [
        (1.5f, 0.06f, 0.3f),
        (0.85f, 0.03f, 0.2f),
        (0.3f, 0.01f, 0.15f),
    ];

    private readonly List<Halo> _halos = [];
    private bool _round;

    public override bool AroundCover => true;

    public override bool Averages => true;

    /// <summary>The lows, the mids and the highs.</summary>
    public override int CountFor(Vector2 size, int wanted) => Halos.Length;

    public override void Place(Vector2 size, float height)
    {
        var side = Math.Min(size.X, size.Y);
        var reach = (float)(Canvas.Reach * VisualizerShapes.SizeBetween(0.6, 1.6, Canvas.Fill));
        var round = Canvas.Corner >= (side * 0.45);
        var restart = Moving && round != _round;
        if (restart)
        {
            End();
        }

        _round = round;
        for (var h = 0; h < _halos.Count; h++)
        {
            var halo = _halos[h];
            var blur = Halos[h].Blur * reach;

            // The tile sits inside the cover by about its corner, so its shadow blurs that much further to reach as far.
            Geometry.InsertScalar("B" + h, round ? blur : blur + (float)Canvas.Corner + 1);
            if (round)
            {
                // A round glow: solid to the cover's edge, fading out over the blur.
                var box = side + (2 * blur);
                halo.Tile.Size = new Vector2(box);
                halo.Tile.Offset = new Vector3(-blur, -blur, 0);
                halo.Tile.CenterPoint = new Vector3(box / 2, box / 2, 0);
                halo.Tile.Brush = halo.Round;
                halo.Tile.Shadow = null;
                halo.Edge.Offset = Math.Clamp(side / box, 0.05f, 0.95f);
            }
            else
            {
                // Inside the cover's edge (and its rounded corners) however far it swells, so only the halo shows.
                var tile = Math.Max(1, (side / (1 + Halos[h].Swell)) - (2 * (float)Canvas.Corner) - 2);
                var inset = (side - tile) / 2;
                halo.Tile.Size = new Vector2(tile);
                halo.Tile.Offset = new Vector3(inset, inset, 0);
                halo.Tile.CenterPoint = new Vector3(tile / 2, tile / 2, 0);
                halo.Tile.Brush = halo.Fill;
                halo.Tile.Shadow = halo.Shadow;
                halo.Shadow.BlurRadius = blur + inset;
            }

            if (!Moving)
            {
                halo.Tile.Opacity = 0;
            }
        }

        if (restart)
        {
            Begin();
        }
    }

    public override void Paint(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        if (colours.Count == 0)
        {
            return;
        }

        for (var h = 0; h < _halos.Count; h++)
        {
            // The widest halo takes the lows' colour, the rim the highs'.
            var colour = ColourAt(colours, h / (double)Math.Max(1, _halos.Count - 1));
            var halo = _halos[h];
            Paint(halo.Shadow, colour, animate);
            Paint(halo.Fill, colour, animate);
            Paint(halo.Stops[0], colour.WithAlpha(0.85), animate);
            Paint(halo.Edge, colour.WithAlpha(0.6), animate);
            Paint(halo.Stops[2], colour.WithAlpha(0), animate);
        }
    }

    protected override void Make(int count)
    {
        Channels = count;
        for (var h = 0; h < count; h++)
        {
            Geometry.InsertScalar("B" + h, 0);
            var shadow = Compositor.CreateDropShadow();
            shadow.Offset = Vector3.Zero;
            var fill = Compositor.CreateColorBrush();
            var (round, stops) = DotBrush(0, 0.5f, 1);
            var tile = Compositor.CreateSpriteVisual();
            tile.Opacity = 0;

            // Behind each other, the widest at the back.
            Layer.Children.InsertAtTop(tile);
            _halos.Add(new Halo(tile, fill, shadow, round, stops));
        }
    }

    protected override void Begin()
    {
        for (var h = 0; h < _halos.Count; h++)
        {
            // Halo 0 follows the lows, the last one the highs.
            var level = $"Clamp({Level(h)} * 1.15, 0, 1)";
            var (calm, swell) = (Halos[h].Calm, Halos[h].Swell);
            var halo = _halos[h];
            halo.Tile.StartAnimation("Opacity", Expression($"{Fade} * ({Num(calm)} + {Num(1 - calm)} * {level})"));
            halo.Tile.StartAnimation("Scale", Expression($"Vector3(1 + {Num(swell)} * {level}, 1 + {Num(swell)} * {level}, 1)"));
            if (!_round)
            {
                halo.Shadow.StartAnimation("BlurRadius", Expression($"{G}.B{h} * (0.75 + 0.45 * {level})"));
            }
        }
    }

    protected override void End()
    {
        foreach (var halo in _halos)
        {
            halo.Tile.StopAnimation("Opacity");
            halo.Tile.StopAnimation("Scale");
            halo.Shadow.StopAnimation("BlurRadius");
            halo.Tile.Opacity = 0;
            halo.Tile.Scale = Vector3.One;
        }
    }

    protected override void Clear()
    {
        Layer.Children.RemoveAll();
        foreach (var halo in _halos)
        {
            halo.Tile.Shadow = null;
            Release(halo.Tile, halo.Fill, halo.Shadow, halo.Round);
            foreach (var stop in halo.Stops)
            {
                stop.Dispose();
            }
        }

        _halos.Clear();
    }

    /// <summary>One halo: the tile behind the cover, its colour and shadow, and the round glow's brush.</summary>
    private sealed record Halo(SpriteVisual Tile, CompositionColorBrush Fill, DropShadow Shadow, CompositionRadialGradientBrush Round, CompositionColorGradientStop[] Stops)
    {
        public CompositionColorGradientStop Edge => Stops[1];
    }
}
