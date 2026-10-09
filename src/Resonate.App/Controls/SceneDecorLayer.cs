using System.Diagnostics;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Helpers;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Resonate.App.Controls;

/// <summary>
/// A special look's decorations over the panels (<see cref="SceneDecor"/>,
/// placed by <see cref="SceneLayout"/>): a cherry branch over the page's top
/// right corner that sheds a petal now and then, and petals that gather in
/// piles on the player's shoulders and the page's edge, for Japan; a snowy
/// spruce bough in the same place, snow that settles on the panels and the
/// player, icicles that grow under it and frost in two corners, for Snow;
/// drops of mercury that swell under the panels' and the player's bottom
/// edges and fall, and a sheen of light that sweeps across the player, for
/// Liquid Chrome; neon brackets on the panels' and the player's corners that
/// flicker and glitch now and then, and a scan line passing down the page,
/// for Cyberpunk.
/// Shapes are XAML paths, drawn once; pictures are composition sprites;
/// everything that moves is an expression on the scene's clock
/// (<see cref="SceneClock"/>), which ticks at a capped rate only while the
/// window shows and Windows' animations are on. Otherwise every animation
/// stops, and the decorations rest as they are once all has gathered. Never
/// in the way of a click; if anything here fails, the decorations go and
/// the window stays as it was.
/// </summary>
internal sealed partial class SceneDecorLayer : Canvas
{
    private const string Clock = "c";

    // A snowy edge being dragged wider is drawn again at most this often, and once more when it settles.
    private static readonly TimeSpan RebuildEvery = TimeSpan.FromMilliseconds(120);

    private static readonly ThemeColor Ice = ThemeColor.FromRgb(0xD4EAFB);
    private static readonly ThemeColor FrostColour = ThemeColor.FromRgb(0xEAF5FF);

    private readonly ThemeService _theme;
    private readonly SceneClock _clock;
    private readonly Compositor _compositor;
    private readonly Dictionary<string, Piece> _pieces = [];
    private readonly DispatcherQueueTimer _later;
    private MainWindow? _window;
    private ThemeScene _scene;
    private SceneFrame? _frame;
    private bool _moving;
    private bool _watching;
    private bool _failed;
    private long _lastRebuild;
    private bool _mayRebuild;
    private bool _rebuilt;
    private XamlRoot? _root;
    private double _rasterization;

    public SceneDecorLayer(ThemeService theme, SceneClock clock)
    {
        _theme = theme;
        _clock = clock;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _later = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _later.Interval = RebuildEvery;
        _later.IsRepeating = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// For CI's screenshot tour: starts every kind of motion of both scenes on
    /// a visual nobody sees, so a mistake in an expression shows, and lays
    /// out both scenes' shapes. Null when all is well.
    /// </summary>
    public static string? CheckMotion(Compositor compositor)
    {
        var clock = compositor.CreatePropertySet();
        clock.InsertScalar("Time", 1);
        clock.InsertScalar("Gather", 1);
        var sprite = compositor.CreateSpriteVisual();
        try
        {
            var motions = new List<(string Property, string Expression)>();
            var pile = SceneDecor.Pile(1, 600, 9, 76, 10, 32, true, false);
            motions.AddRange(PetalMotion(pile[^1], 12, 0, gathered: false));
            motions.AddRange(PetalMotion(pile[^1], 12, 0, gathered: true));
            motions.AddRange(PetalMotion(pile[0], SceneDecor.Landed, 1, gathered: false));
            motions.Add(SwayMotion());
            motions.Add(GrowMotion(60, 30));
            motions.Add(SnowMotion(1, SceneDecor.SettlingLayers + 1));
            motions.AddRange(GlintMotion(3, gathered: false));
            motions.AddRange(GlintMotion(3, gathered: true));
            motions.AddRange(FrostMotion());
            foreach (var scene in (ThemeScene[])[ThemeScene.Japan, ThemeScene.Snow])
            {
                motions.AddRange(SceneDecor.Shedding(scene).SelectMany(ShedMotion));
                _ = SceneDecor.Bough(scene);
            }

            motions.AddRange(DripMotion(new SceneLayout.Drip(0, 0, 10, SceneMotion.Frequency(9), 0.3, 1)));
            motions.AddRange(SweepMotion(SceneMotion.Frequency(9), 0.2, 400, 120));
            motions.AddRange(ScanMotion(SceneMotion.Frequency(12), 0.4, 600));
            motions.AddRange(HudMotion(2).Select(m => (m.Property == "Translation" ? "Offset" : m.Property, m.Expression)));
            _ = SceneDecor.Cap(2, 900, 18, 14, 9, 20);
            _ = SceneDecor.Frost(5, 18, SceneLayout.FrostReach);
            foreach (var (property, expression) in motions)
            {
                var animation = compositor.CreateExpressionAnimation(expression);
                animation.SetReferenceParameter(Clock, clock);
                sprite.StartAnimation(property, animation);
                sprite.StopAnimation(property);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            sprite.Dispose();
            clock.Dispose();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can come twice in a row; each handler is held once.
        _theme.Changed -= OnThemeChanged;
        _theme.Changed += OnThemeChanged;
        _clock.GatheredNow -= OnGathered;
        _clock.GatheredNow += OnGathered;
        _later.Tick -= OnLater;
        _later.Tick += OnLater;
        _theme.AnimationsChanged -= OnAnimationsChanged;
        _theme.AnimationsChanged += OnAnimationsChanged;
        if (_root is not null)
        {
            _root.Changed -= OnRootChanged;
        }

        _root = XamlRoot;
        if (_root is not null)
        {
            _root.Changed += OnRootChanged;
            _rasterization = _root.RasterizationScale;
        }

        _window = App.MainWindow;
        if (_window is not null)
        {
            _window.ShownChanged -= OnShownChanged;
            _window.ShownChanged += OnShownChanged;
        }

        Show();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _clock.GatheredNow -= OnGathered;
        _later.Stop();
        _later.Tick -= OnLater;
        _theme.AnimationsChanged -= OnAnimationsChanged;
        if (_root is not null)
        {
            _root.Changed -= OnRootChanged;
            _root = null;
        }

        if (_window is not null)
        {
            _window.ShownChanged -= OnShownChanged;
            _window = null;
        }

        Watch(false);
        Move(false);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Show();

    private void OnShownChanged(object? sender, EventArgs e) => Guard(Refresh);

    private void OnAnimationsChanged(object? sender, EventArgs e) => Guard(Refresh);

    /// <summary>On a display with another scale, the pictures are drawn again at its sharpness.</summary>
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale == _rasterization)
        {
            return;
        }

        _rasterization = sender.RasterizationScale;
        _frame = null;
        Guard(Relayout);
    }

    private void OnLayoutUpdated(object? sender, object e) => Guard(Relayout);

    private void OnLater(DispatcherQueueTimer sender, object args)
    {
        // A rebuild put off while the window was being resized.
        _frame = null;
        Guard(Relayout);
    }

    /// <summary>Once everything has gathered, the piles stop following their flight and only stir now and then.</summary>
    private void OnGathered(object? sender, EventArgs e) => Guard(() =>
    {
        if (_moving)
        {
            Move(false);
            Move(true);
        }
    });

    /// <summary>The look's decorations: made afresh when the scene changes, then placed, and moving or resting as the window allows.</summary>
    private void Show()
    {
        var scene = _theme.Current.Scene;
        if (scene != _scene)
        {
            // A new scene starts afresh, even after the last one failed.
            _failed = false;
            Guard(() =>
            {
                Move(false);
                Clear();
            });
            _scene = scene;
            _frame = null;
            _clock.Restart();
        }

        Watch(Decorated && !_failed);
        Guard(Relayout);
        Guard(Refresh);
    }

    /// <summary>Runs <paramref name="action"/>; if it fails, the decorations go rather than break the window.</summary>
    private void Guard(Action action)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Scene decorations stopped: {ex}");
            _failed = true;
            Watch(false);
            _later.Stop();
            try
            {
                Move(false);
                Clear();
            }
            catch (Exception)
            {
                // Already broken; nothing more to tidy.
            }
        }
    }

    /// <summary>Follows the panels and the player while a scene shows.</summary>
    private void Watch(bool watch)
    {
        if (watch == _watching)
        {
            return;
        }

        _watching = watch;
        if (watch)
        {
            LayoutUpdated += OnLayoutUpdated;
        }
        else
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void Refresh() =>
        Move(_scene != ThemeScene.None && _pieces.Count > 0 && (_window?.IsShown ?? true) && _theme.AnimationsEnabled);

    /// <summary>Starts or stops every piece's motion; stopped pieces rest as they are once all has gathered.</summary>
    private void Move(bool move)
    {
        if (move == _moving)
        {
            return;
        }

        _moving = move;
        _clock.Want(this, move);
        foreach (var piece in _pieces.Values)
        {
            if (move)
            {
                Start(piece);
            }
            else
            {
                Stop(piece);
            }
        }
    }

    private void Start(Piece piece)
    {
        foreach (var (target, property, expression) in piece.Motions(_clock.Gathered))
        {
            var animation = _compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter(Clock, _clock.Props);
            target.StartAnimation(property, animation);
            piece.Running.Add((target, property));
        }
    }

    private static void Stop(Piece piece)
    {
        foreach (var (target, property) in piece.Running)
        {
            target.StopAnimation(property);
        }

        piece.Running.Clear();
        piece.Rest();
    }

    // ------------------------------------------------------------ layout

    /// <summary>Whether the scene has decorations: Synthwave's and Afterhours' are all in their scenery and weather.</summary>
    private bool Decorated => _scene is ThemeScene.Japan or ThemeScene.Snow or ThemeScene.LiquidChrome or ThemeScene.Cyberpunk;

    private void Relayout()
    {
        if (!Decorated)
        {
            return;
        }

        var frame = _window?.SceneFrameFor(this);
        if (frame == _frame)
        {
            return;
        }

        _frame = frame;

        // Every piece that changed shape is drawn again in this pass, or none of them until a moment later.
        _mayRebuild = Stopwatch.GetElapsedTime(_lastRebuild) >= RebuildEvery;
        _rebuilt = false;
        var wanted = new HashSet<string>();
        if (frame is not null)
        {
            if (SceneDecor.Bough(_scene) is { } art && SceneLayout.Bough(frame) is { } bough)
            {
                var piece = Ensure("bough", (_scene, Sharpness), 2, () => BuildBough(art));
                Place(piece, bough.Right - (art.Width * bough.Scale), bough.Top - (art.PanelTop * bough.Scale), bough.Scale, bough.Scale);
                wanted.Add("bough");
            }

            switch (_scene)
            {
                case ThemeScene.Japan:
                    LayOutPiles(frame, wanted);
                    break;
                case ThemeScene.Snow:
                    LayOutSnow(frame, wanted);
                    break;
                case ThemeScene.LiquidChrome:
                    LayOutChrome(frame, wanted);
                    break;
                case ThemeScene.Cyberpunk:
                    LayOutHud(frame, wanted);
                    break;
            }
        }

        foreach (var key in _pieces.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            Remove(key);
        }

        if (_rebuilt)
        {
            _lastRebuild = Stopwatch.GetTimestamp();
        }

        Refresh();
    }

    private void LayOutPiles(SceneFrame frame, HashSet<string> wanted)
    {
        foreach (var (index, spot) in SceneLayout.PileSpots(frame))
        {
            var kind = SceneLayout.Piles[index];

            // A pile only changes shape when its edge is short; beyond that its petals stay put.
            var room = (2 * (spot.Radius + kind.Length)) + 12;
            var key = $"pile{index}";
            var piece = Ensure(key, (Math.Round(Math.Min(spot.Width, room)), spot.Radius, Sharpness), 1, () => BuildPile(index, spot));
            Place(piece, spot.X, spot.Y, spot.Mirrored ? -spot.Scale : spot.Scale, spot.Scale);
            wanted.Add(key);
        }
    }

    private void LayOutSnow(SceneFrame frame, HashSet<string> wanted)
    {
        foreach (var cap in SceneLayout.Caps(frame))
        {
            var key = $"cap{cap.Seed}";
            var low = string.Join(';', cap.Low.Select(l => $"{Math.Round(l.From)},{Math.Round(l.To)},{Math.Round(l.Depth, 1)}"));
            var piece = Ensure(key, (Math.Round(cap.Spot.Width), cap.Spot.Radius, cap.Depth, low, Sharpness), 1, () => BuildCap(cap));
            Place(piece, cap.Spot.X, cap.Spot.Y, cap.Spot.Scale, cap.Spot.Scale);
            wanted.Add(key);
        }

        if (SceneLayout.Fringe(frame) is { } fringe)
        {
            // Its right-hand icicles lie from the right end, so any change of width draws it again.
            var piece = Ensure("fringe", (Math.Round(fringe.Width), fringe.Radius), 3, () => BuildFringe(fringe));
            Place(piece, fringe.X, fringe.Y, fringe.Scale, fringe.Scale);
            wanted.Add("fringe");
        }

        foreach (var frost in SceneLayout.Frost(frame))
        {
            var key = $"frost{frost.Seed}";
            var piece = Ensure(key, frost.Radius, 0, () => BuildFrost(frost));
            Place(piece, frost.X, frost.Y, frost.FlipX ? -frost.Scale : frost.Scale, frost.FlipY ? -frost.Scale : frost.Scale);
            wanted.Add(key);
        }
    }

    private void LayOutChrome(SceneFrame frame, HashSet<string> wanted)
    {
        var drips = SceneLayout.Drips(frame);
        for (var i = 0; i < drips.Count; i++)
        {
            var drip = drips[i];
            var key = $"drip{i}";
            var piece = Ensure(key, (drip.Size, drip.Frequency, drip.Phase, Sharpness), 1, () => BuildDrip(drip));
            Place(piece, drip.X, drip.Y, drip.Scale, drip.Scale);
            wanted.Add(key);
        }

        var s = Math.Max(0.1, frame.Scale);
        if (frame.Player is { } player && player.Width / s >= 140 && player.Height / s >= 24)
        {
            var (width, height) = (Math.Round(player.Width / s), Math.Round(player.Height / s));
            var piece = Ensure("sheen", (width, height, frame.PlayerRadius), 2, () => BuildSheen(width, height, frame.PlayerRadius));
            Place(piece, player.X, player.Y, s, s);
            wanted.Add("sheen");
        }
    }

    private void LayOutHud(SceneFrame frame, HashSet<string> wanted)
    {
        var s = Math.Max(0.1, frame.Scale);
        var look = _theme.Current;
        var framed = SceneLayout.Framed(frame);
        for (var i = 0; i < framed.Count; i++)
        {
            var (box, player) = framed[i];
            var (width, height) = (Math.Round(box.Width / s), Math.Round(box.Height / s));
            var colour = player ? look.Accent2 : look.Accent;
            var key = $"hud{i}";
            var seed = i;
            var piece = Ensure(key, (width, height, colour), 2, () => BuildBrackets(width, height, colour, seed));
            Place(piece, box.X, box.Y, s, s);
            wanted.Add(key);
        }

        var page = frame.Page;
        if (page.Width / s >= 200 && page.Height / s >= 160)
        {
            var (width, height) = (Math.Round(page.Width / s), Math.Round(page.Height / s));
            var piece = Ensure("scan", (width, height, frame.PanelRadius, look.Accent), 0, () => BuildScan(width, height, frame.PanelRadius, look.Accent));
            Place(piece, page.X, page.Y, s, s);
            wanted.Add("scan");
        }
    }

    /// <summary>
    /// The piece called <paramref name="name"/>, built for <paramref name="key"/>:
    /// the one there if it was built for the same, else a new one (and while
    /// the window is being resized, the old one a moment longer).
    /// </summary>
    private Piece Ensure(string name, object key, int depth, Func<Piece> build)
    {
        var old = _pieces.GetValueOrDefault(name);
        if (old is not null && Equals(old.Key, key))
        {
            return old;
        }

        if (old is not null && !_mayRebuild)
        {
            _later.Stop();
            _later.Start();
            return old;
        }

        if (old is not null)
        {
            Remove(name);
            _rebuilt = true;
        }

        var piece = build();
        piece.Key = key;
        piece.Depth = depth;
        if (piece.Element is not null)
        {
            // Lower pieces first (frost, then petals and snow on the edges, then the bough in front of them, then what hangs under the player); the children are the pieces, in that order.
            var at = _pieces.Values.Count(p => p.Element is not null && p.Depth <= depth);
            Children.Insert(Math.Min(at, Children.Count), piece.Element);
        }

        _pieces[name] = piece;
        piece.Rest();
        if (_moving)
        {
            Start(piece);
        }

        return piece;
    }

    private static void Place(Piece piece, double x, double y, double scaleX, double scaleY)
    {
        if (piece.Placement is { } placement)
        {
            placement.TranslateX = x;
            placement.TranslateY = y;
            placement.ScaleX = scaleX;
            placement.ScaleY = scaleY;
        }

    }

    private void Remove(string name)
    {
        if (!_pieces.Remove(name, out var piece))
        {
            return;
        }

        foreach (var (target, property) in piece.Running)
        {
            target.StopAnimation(property);
        }

        piece.Running.Clear();
        if (piece.Element is not null)
        {
            Children.Remove(piece.Element);
        }

        // Pictures let go of by the shape they rode on before they are closed.
        if (piece.Sprites is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(piece.Sprites, null);
        }

        foreach (var owned in piece.Owned)
        {
            owned.Dispose();
        }
    }

    private void Clear()
    {
        foreach (var name in _pieces.Keys.ToList())
        {
            Remove(name);
        }

        Children.Clear();
    }

    /// <summary>How many screen pixels one of the content's units is (for pictures that stay sharp).</summary>
    private double Pixels => (XamlRoot?.RasterizationScale ?? 1) * _theme.Scale;

    /// <summary><see cref="Pixels"/> in quarters, in the keys of pieces with pictures, so they are drawn again when it changes.</summary>
    private double Sharpness => Math.Round(Pixels * 4) / 4;

    // ------------------------------------------------------------ the bough

    private Piece BuildBough(SceneDecor.BoughArt art)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();

        // Cut off at its right edge (where the page or the window ends), free everywhere else.
        var outer = new Canvas
        {
            IsHitTestVisible = false,
            RenderTransform = placement,
            Clip = new RectangleGeometry { Rect = new Rect(-4000, -4000, 4000 + art.Width, 8000) },
        };
        var inner = new Canvas();
        outer.Children.Add(inner);
        foreach (var fill in art.Fills)
        {
            inner.Children.Add(new Path { Data = Figures(fill.Figures, closed: true), Fill = Gradient(fill.Top, fill.Bottom), Opacity = fill.Opacity });
        }

        var growing = new List<(Visual Visual, SceneDecor.Icicle Icicle)>();
        var ice = IceBrush();
        foreach (var icicle in art.Icicles)
        {
            var path = IciclePath(icicle, icicle.X, icicle.Y, ice);
            inner.Children.Add(path);
            growing.Add((ElementCompositionPreview.GetElementVisual(path), icicle));
        }

        // Blossoms, buds and glints over the wood, and what it sheds: sprites that sway with it.
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
        ElementCompositionPreview.SetElementChildVisual(inner, holder);
        piece.Sprites = inner;
        var pixels = Pixels;
        var glints = new List<(SpriteVisual Sprite, int Index)>();
        foreach (var spot in art.Sprites)
        {
            var sprite = Sprite(spot.Sprite, spot.Size, pixels, piece);
            sprite.Offset = new Vector3(spot.Centre.X - ((float)spot.Size / 2), spot.Centre.Y - ((float)spot.Size / 2), 0);
            sprite.RotationAngleInDegrees = (float)spot.Angle;
            sprite.Scale = new Vector3((float)spot.Squash, 1, 1);
            sprite.Opacity = (float)spot.Opacity;
            holder.Children.InsertAtTop(sprite);
            if (spot.Sprite == SceneSprite.Glint)
            {
                glints.Add((sprite, glints.Count));
            }
        }

        var shed = new List<(SpriteVisual Sprite, SceneDecor.Shed Shed)>();
        foreach (var falling in SceneDecor.Shedding(_scene))
        {
            var sprite = falling.Sprite is { } picture ? Sprite(picture, falling.Size, pixels, piece) : Dot(falling.Size, piece);
            sprite.Opacity = 0;
            holder.Children.InsertAtTop(sprite);
            shed.Add((sprite, falling));
        }

        var sway = ElementCompositionPreview.GetElementVisual(inner);
        sway.CenterPoint = new Vector3(art.Pivot, 0);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = gathered =>
        {
            var motions = new List<Motion> { new(sway, SwayMotion()) };
            if (!gathered)
            {
                motions.AddRange(growing.Select(g => new Motion(g.Visual, GrowMotion(g.Icicle.GrowAt, g.Icicle.GrowFor))));
            }

            foreach (var (sprite, index) in glints)
            {
                motions.AddRange(GlintMotion(index, gathered: true).Select(m => new Motion(sprite, m)));
            }

            foreach (var (sprite, falling) in shed)
            {
                motions.AddRange(ShedMotion(falling).Select(m => new Motion(sprite, m)));
            }

            return motions;
        };
        piece.Rest = () =>
        {
            sway.RotationAngleInDegrees = 0;
            foreach (var (visual, _) in growing)
            {
                visual.Scale = Vector3.One;
            }

            foreach (var (sprite, _) in glints)
            {
                sprite.Opacity = 0.7f;
                sprite.Scale = Vector3.One;
            }

            foreach (var (sprite, _) in shed)
            {
                sprite.Opacity = 0;
            }
        };
        return piece;
    }

    // ------------------------------------------------------------ petals

    private Piece BuildPile(int index, SceneLayout.Spot spot)
    {
        var kind = SceneLayout.Piles[index];
        var petals = SceneDecor.Pile(kind.Seed, spot.Width, spot.Radius, kind.Length, kind.Height, kind.Count, kind.Blossoms, spot.Mirrored);
        var landings = SceneLayout.PileLandings[index];
        var piece = new Piece();

        // The petals ride on a shape of their own, placed like the others, so the bough can lie in front of them.
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Sprites = outer;
        if (petals.Count == 0)
        {
            return piece;
        }

        // A soft shadow under the pile, darker as it grows.
        var left = petals.Min(p => p.X);
        var right = petals.Max(p => p.X);
        var shadowBrush = _compositor.CreateRadialGradientBrush();
        piece.Owned.Add(shadowBrush);
        var dark = _compositor.CreateColorGradientStop(0, ThemeColor.FromRgb(0x000000).WithAlpha(0.32).ToColor());
        var clear = _compositor.CreateColorGradientStop(1, ThemeColor.FromRgb(0x000000).WithAlpha(0).ToColor());
        piece.Owned.Add(dark);
        piece.Owned.Add(clear);
        shadowBrush.ColorStops.Add(dark);
        shadowBrush.ColorStops.Add(clear);
        var shadow = _compositor.CreateSpriteVisual();
        piece.Owned.Add(shadow);
        shadow.Brush = shadowBrush;
        var width = (float)Math.Max(12, (right - left) * 1.15);
        shadow.Size = new Vector2(width, 10);
        shadow.Offset = new Vector3((float)((left + right) / 2) - (width / 2), -2, 0);
        holder.Children.InsertAtTop(shadow);

        var pixels = Pixels;
        var sprites = new List<(SpriteVisual Sprite, SceneDecor.PilePetal Petal, double Lands)>(petals.Count);
        for (var i = 0; i < petals.Count; i++)
        {
            var petal = petals[i];
            var sprite = Sprite(petal.Sprite, petal.Size, pixels, piece);
            holder.Children.InsertAtTop(sprite);
            sprites.Add((sprite, petal, i < landings.Length ? landings[i] : SceneDecor.Landed));
        }

        piece.Motions = gathered =>
        {
            var motions = new List<Motion>();
            if (!gathered)
            {
                motions.Add(new Motion(shadow, ("Opacity", $"0.5 + 0.5 * Clamp({Clock}.Gather / {N(SceneDecor.GatherSeconds * 0.5)}, 0, 1)")));
            }

            foreach (var (sprite, petal, lands) in sprites)
            {
                motions.AddRange(PetalMotion(petal, lands, index, gathered).Select(m => new Motion(sprite, m)));
            }

            return motions;
        };
        piece.Rest = () =>
        {
            shadow.Opacity = 1;
            foreach (var (sprite, petal, _) in sprites)
            {
                var half = (float)petal.Size / 2;
                sprite.Offset = new Vector3((float)petal.X - half, (float)petal.Y - half, 0);
                sprite.RotationAngleInDegrees = (float)petal.Angle;
                sprite.Scale = new Vector3((float)petal.Squash, 1, 1);
                sprite.Opacity = 1;
            }
        };
        return piece;
    }

    // ------------------------------------------------------------ snow

    private Piece BuildCap(SceneLayout.CapSpot spot)
    {
        var cap = SceneDecor.Cap(spot.Seed, spot.Spot.Width, spot.Spot.Radius, spot.Depth, spot.Icicles, spot.IcicleLength, spot.Low);
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };

        // The snow settles through thinner layers, each lying on the edge and round its corners, then the whole of it.
        var fill = Gradient(ThemeColor.FromRgb(0xFFFFFF), ThemeColor.FromRgb(0xDCEAF8));
        var layers = new List<Visual>(cap.Settling.Count + 1);
        foreach (var thin in cap.Settling)
        {
            var path = new Path { Data = Figures([thin], closed: true), Fill = fill };
            outer.Children.Add(path);
            layers.Add(ElementCompositionPreview.GetElementVisual(path));
        }

        var snow = new Canvas();
        snow.Children.Add(new Path { Data = Figures([cap.Shade], closed: true), Fill = new SolidColorBrush(ThemeColor.FromRgb(0x9DB9D8).ToColor()), Opacity = 0.9 });
        snow.Children.Add(new Path { Data = Figures([cap.Outline], closed: true), Fill = fill });
        snow.Children.Add(new Path { Data = Figures([cap.Crest], closed: false), Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1, Opacity = 0.9 });
        outer.Children.Add(snow);
        layers.Add(ElementCompositionPreview.GetElementVisual(snow));

        var growing = new List<(Visual Visual, SceneDecor.Icicle Icicle)>();
        var ice = IceBrush();
        foreach (var icicle in cap.Icicles)
        {
            var path = IciclePath(icicle, icicle.X, icicle.Y, ice);
            outer.Children.Add(path);
            growing.Add((ElementCompositionPreview.GetElementVisual(path), icicle));
        }

        // Where the snow catches the light, riding on the snow's own shape.
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Sprites = outer;
        var pixels = Pixels;
        var glints = new List<(SpriteVisual Sprite, int Index)>();
        foreach (var at in cap.Glints)
        {
            var sprite = Sprite(SceneSprite.Glint, 11, pixels, piece);
            sprite.Offset = new Vector3(at.X - 5.5f, at.Y - 5.5f, 0);
            holder.Children.InsertAtTop(sprite);
            glints.Add((sprite, (spot.Seed * 31) + glints.Count));
        }

        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = gathered =>
        {
            var motions = new List<Motion>();
            if (!gathered)
            {
                motions.AddRange(layers.Select((layer, index) => new Motion(layer, SnowMotion(index, layers.Count))));
                motions.AddRange(growing.Select(g => new Motion(g.Visual, GrowMotion(g.Icicle.GrowAt, g.Icicle.GrowFor))));
            }

            foreach (var (sprite, index) in glints)
            {
                motions.AddRange(GlintMotion(index, gathered).Select(m => new Motion(sprite, m)));
            }

            return motions;
        };
        piece.Rest = () =>
        {
            // Settled: only the whole of it shows (the thinner layers lie under it).
            for (var i = 0; i < layers.Count; i++)
            {
                layers[i].Opacity = i == layers.Count - 1 ? 1 : 0;
            }

            foreach (var (visual, _) in growing)
            {
                visual.Scale = Vector3.One;
            }

            foreach (var (sprite, _) in glints)
            {
                sprite.Opacity = 0.6f;
                sprite.Scale = Vector3.One;
            }
        };
        return piece;
    }

    private Piece BuildFringe(SceneLayout.Spot spot)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var growing = new List<(Visual Visual, SceneDecor.Icicle Icicle)>();
        var ice = IceBrush();
        foreach (var icicle in SceneDecor.Fringe(4, spot.Width, spot.Radius, 8, 13))
        {
            // Measured up from the bottom edge; hanging down from there.
            var path = IciclePath(icicle, icicle.X, -icicle.Y, ice);
            outer.Children.Add(path);
            growing.Add((ElementCompositionPreview.GetElementVisual(path), icicle));
        }

        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = gathered => gathered ? [] : [.. growing.Select(g => new Motion(g.Visual, GrowMotion(g.Icicle.GrowAt, g.Icicle.GrowFor)))];
        piece.Rest = () =>
        {
            foreach (var (visual, _) in growing)
            {
                visual.Scale = Vector3.One;
            }
        };
        return piece;
    }

    private Piece BuildFrost(SceneLayout.FrostSpot spot)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();

        // Drawn as a top left corner, flipped into place, and kept inside its panel.
        var reach = SceneLayout.FrostReach + 20;
        var outer = new Canvas
        {
            IsHitTestVisible = false,
            RenderTransform = placement,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, reach, reach) },
        };
        var inner = new Canvas();
        var haze = new RadialGradientBrush { Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = 1, RadiusY = 1 };
        haze.GradientStops.Add(new GradientStop { Offset = 0, Color = FrostColour.WithAlpha(0.16).ToColor() });
        haze.GradientStops.Add(new GradientStop { Offset = 1, Color = FrostColour.WithAlpha(0).ToColor() });
        inner.Children.Add(new Rectangle { Width = reach, Height = reach, Fill = haze });

        // Fine lines in a few strengths, each strength one path.
        var strokes = SceneDecor.Frost(spot.Seed, spot.Radius, SceneLayout.FrostReach);
        foreach (var group in strokes.GroupBy(s => (s.Width, Opacity: Math.Round(s.Opacity * 20) / 20)))
        {
            inner.Children.Add(new Path
            {
                Data = Figures(group.Select(s => s.Points), closed: false),
                Stroke = new SolidColorBrush(FrostColour.ToColor()),
                StrokeThickness = group.Key.Width,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = group.Key.Opacity,
            });
        }

        outer.Children.Add(inner);
        var spread = ElementCompositionPreview.GetElementVisual(inner);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = gathered => gathered ? [] : [.. FrostMotion().Select(m => new Motion(spread, m))];
        piece.Rest = () =>
        {
            spread.Opacity = 1;
            spread.Scale = Vector3.One;
        };
        return piece;
    }

    // ------------------------------------------------------------ liquid chrome

    /// <summary>A drop of mercury hanging from an edge: it swells, stretches, lets go and falls, then gathers again.</summary>
    private Piece BuildDrip(SceneLayout.Drip drip)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Sprites = outer;
        var bead = Sprite(SceneSprite.ChromeBead, drip.Size, Pixels, piece);

        // It grows down from the edge.
        var half = (float)drip.Size / 2;
        bead.CenterPoint = new Vector3(half, 0, 0);
        holder.Children.InsertAtTop(bead);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = _ => [.. DripMotion(drip).Select(m => new Motion(bead, m))];
        piece.Rest = () =>
        {
            bead.Offset = new Vector3(-half, -2, 0);
            bead.Scale = new Vector3(0.7f, 0.7f, 1);
            bead.Opacity = 1;
        };
        return piece;
    }

    /// <summary>A band of light sweeping across the player now and then, as over polished metal, inside its rounded edge.</summary>
    private Piece BuildSheen(double width, double height, double radius)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var holder = _compositor.CreateContainerVisual();
        holder.Size = new Vector2((float)width, (float)height);
        var shape = _compositor.CreateRoundedRectangleGeometry();
        shape.Size = holder.Size;
        shape.CornerRadius = new Vector2((float)Math.Min(radius, height / 2));
        var clip = _compositor.CreateGeometricClip(shape);
        holder.Clip = clip;
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Sprites = outer;

        var bandWidth = (float)Math.Max(90, height * 2.4);
        var brush = _compositor.CreateLinearGradientBrush();
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = new Vector2(1, 0);
        CompositionColorGradientStop[] stops =
        [
            _compositor.CreateColorGradientStop(0, ThemeColor.White.WithAlpha(0).ToColor()),
            _compositor.CreateColorGradientStop(0.42f, ThemeColor.FromRgb(0xE8EEFF).WithAlpha(0.07).ToColor()),
            _compositor.CreateColorGradientStop(0.5f, ThemeColor.White.WithAlpha(0.2).ToColor()),
            _compositor.CreateColorGradientStop(0.58f, ThemeColor.FromRgb(0xF6E4FF).WithAlpha(0.07).ToColor()),
            _compositor.CreateColorGradientStop(1, ThemeColor.White.WithAlpha(0).ToColor()),
        ];
        foreach (var stop in stops)
        {
            brush.ColorStops.Add(stop);
        }

        var band = _compositor.CreateSpriteVisual();
        band.Size = new Vector2(bandWidth, (float)(height * 3));
        band.CenterPoint = new Vector3(bandWidth / 2, (float)(height * 1.5), 0);
        band.RotationAngleInDegrees = 20;
        band.Brush = brush;
        band.Opacity = 0;
        holder.Children.InsertAtTop(band);
        piece.Owned.AddRange([holder, shape, clip, brush, .. stops, band]);

        var frequency = SceneMotion.Frequency(9);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = _ => [.. SweepMotion(frequency, 0.15, width, height).Select(m => new Motion(band, m))];
        piece.Rest = () => band.Opacity = 0;
        return piece;
    }

    // ------------------------------------------------------------ cyberpunk

    /// <summary>A HUD's neon brackets on a box's four corners, with a tick beside the top left one and a faint glow, flickering and now and then glitching.</summary>
    private Piece BuildBrackets(double width, double height, ThemeColor colour, int seed)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var holder = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Sprites = outer;
        piece.Owned.Add(holder);

        const float thick = 2;
        const float outside = 5;
        var arm = (float)Math.Clamp(Math.Min(width, height) / 5, 8, 22);
        var (w, h) = ((float)width, (float)height);
        var bars = new List<(float X, float Y, float W, float H)>();
        foreach (var (x, y, right, bottom) in (ReadOnlySpan<(float, float, bool, bool)>)[(-outside, -outside, false, false), (w + outside, -outside, true, false), (-outside, h + outside, false, true), (w + outside, h + outside, true, true)])
        {
            var left = right ? x - arm : x;
            var top = bottom ? y - arm : y;
            bars.Add((left, bottom ? y - thick : y, arm, thick));
            bars.Add((right ? x - thick : x, top, thick, arm));
        }

        bars.Add((-outside + arm + 4, -outside, 7, thick));
        bars.Add((-outside + arm + 14, -outside, 3, thick));

        // Two wider, fainter copies under each bar for a glow, then the bar itself.
        foreach (var (grow, alpha) in (ReadOnlySpan<(float, double)>)[(4, 0.08), (2, 0.18), (0, 0.95)])
        {
            var brush = _compositor.CreateColorBrush(colour.Mix(ThemeColor.White, grow == 0 ? 0.25 : 0).WithAlpha(alpha).ToColor());
            piece.Owned.Add(brush);
            foreach (var bar in bars)
            {
                var sprite = _compositor.CreateSpriteVisual();
                sprite.Size = new Vector2(bar.W + (2 * grow), bar.H + (2 * grow));
                sprite.Offset = new Vector3(bar.X - grow, bar.Y - grow, 0);
                sprite.Brush = brush;
                holder.Children.InsertAtTop(sprite);
                piece.Owned.Add(sprite);
            }
        }

        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = _ => [.. HudMotion(seed).Select(m => new Motion(holder, m.Property == "Translation" ? "Offset" : m.Property, m.Expression))];
        piece.Rest = () =>
        {
            holder.Offset = Vector3.Zero;
            holder.Opacity = 1;
        };
        return piece;
    }

    /// <summary>A faint line of light passing down the page now and then, trailing a glow, inside its edge.</summary>
    private Piece BuildScan(double width, double height, double radius, ThemeColor colour)
    {
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };
        var holder = _compositor.CreateContainerVisual();
        holder.Size = new Vector2((float)width, (float)height);
        var shape = _compositor.CreateRoundedRectangleGeometry();
        shape.Size = holder.Size;
        shape.CornerRadius = new Vector2((float)radius);
        var clip = _compositor.CreateGeometricClip(shape);
        holder.Clip = clip;
        ElementCompositionPreview.SetElementChildVisual(outer, holder);
        piece.Sprites = outer;

        var brush = _compositor.CreateLinearGradientBrush();
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = new Vector2(0, 1);
        CompositionColorGradientStop[] stops =
        [
            _compositor.CreateColorGradientStop(0, colour.WithAlpha(0).ToColor()),
            _compositor.CreateColorGradientStop(0.88f, colour.WithAlpha(0.05).ToColor()),
            _compositor.CreateColorGradientStop(0.97f, colour.WithAlpha(0.14).ToColor()),
            _compositor.CreateColorGradientStop(0.985f, colour.Mix(ThemeColor.White, 0.5).WithAlpha(0.32).ToColor()),
            _compositor.CreateColorGradientStop(1, colour.WithAlpha(0).ToColor()),
        ];
        foreach (var stop in stops)
        {
            brush.ColorStops.Add(stop);
        }

        var band = _compositor.CreateSpriteVisual();
        band.Size = new Vector2((float)width, ScanHeight);
        band.Brush = brush;
        band.Opacity = 0;
        holder.Children.InsertAtTop(band);
        piece.Owned.AddRange([holder, shape, clip, brush, .. stops, band]);

        var frequency = SceneMotion.Frequency(12);
        piece.Element = outer;
        piece.Placement = placement;
        piece.Motions = _ => [.. ScanMotion(frequency, 0.4, height).Select(m => new Motion(band, m))];
        piece.Rest = () => band.Opacity = 0;
        return piece;
    }

    // ------------------------------------------------------------ drawing

    private SpriteVisual Sprite(SceneSprite picture, double size, double pixels, Piece piece)
    {
        var sprite = _compositor.CreateSpriteVisual();
        piece.Owned.Add(sprite);
        var side = (float)size;
        sprite.Size = new Vector2(side);
        sprite.CenterPoint = new Vector3(side / 2, side / 2, 0);
        sprite.Brush = SceneSpriteBrushes.Get(_compositor, picture, size * pixels);
        return sprite;
    }

    /// <summary>A soft dot of snow.</summary>
    private SpriteVisual Dot(double size, Piece piece)
    {
        var brush = _compositor.CreateRadialGradientBrush();
        var middle = _compositor.CreateColorGradientStop(0, ThemeColor.White.ToColor());
        var edge = _compositor.CreateColorGradientStop(1, ThemeColor.FromRgb(0xEAF4FF).WithAlpha(0).ToColor());
        brush.ColorStops.Add(middle);
        brush.ColorStops.Add(edge);
        piece.Owned.Add(middle);
        piece.Owned.Add(edge);
        piece.Owned.Add(brush);
        var sprite = _compositor.CreateSpriteVisual();
        piece.Owned.Add(sprite);
        var side = (float)size;
        sprite.Size = new Vector2(side);
        sprite.CenterPoint = new Vector3(side / 2, side / 2, 0);
        sprite.Brush = brush;
        return sprite;
    }

    private static PathGeometry Figures(IEnumerable<IReadOnlyList<Vector2>> figures, bool closed)
    {
        // Shapes that overlap in one path fill together rather than cancel out.
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var points in figures)
        {
            if (points.Count < 2)
            {
                continue;
            }

            var line = new PolyLineSegment();
            for (var i = 1; i < points.Count; i++)
            {
                line.Points.Add(new Point(points[i].X, points[i].Y));
            }

            var figure = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y), IsClosed = closed, IsFilled = closed };
            figure.Segments.Add(line);
            geometry.Figures.Add(figure);
        }

        return geometry;
    }

    private static LinearGradientBrush Gradient(ThemeColor top, ThemeColor bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = top.ToColor() });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = bottom.ToColor() });
        return brush;
    }

    /// <summary>Clear ice: nearly white at the root, fading to a pale blue at the tip.</summary>
    private static LinearGradientBrush IceBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = ThemeColor.FromRgb(0xF6FBFF).WithAlpha(0.97).ToColor() });
        brush.GradientStops.Add(new GradientStop { Offset = 0.55, Color = Ice.WithAlpha(0.8).ToColor() });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = ThemeColor.FromRgb(0xA9D3F3).WithAlpha(0.55).ToColor() });
        return brush;
    }

    private static Path IciclePath(SceneDecor.Icicle icicle, double x, double y, Brush ice)
    {
        var path = new Path { Data = Figures([SceneDecor.IcicleOutline(icicle)], closed: true), Fill = ice };
        SetLeft(path, x);
        SetTop(path, y);
        return path;
    }

    // ------------------------------------------------------------ motion

    private static string N(double value) => value < 0 ? $"({VisualizerShapes.Number(value)})" : VisualizerShapes.Number(value);

    /// <summary>
    /// A pile's petal: falling onto its place over <see cref="SceneDecor.FlightSeconds"/>
    /// before <paramref name="lands"/> (seconds into the gathering), turning,
    /// fluttering and swaying as it comes; then still, but for the petals on
    /// top, which shiver when a breeze passes along the pile.
    /// </summary>
    private static IEnumerable<(string Property, string Expression)> PetalMotion(SceneDecor.PilePetal p, double lands, int pile, bool gathered)
    {
        var half = p.Size / 2;
        var phase = p.Breeze * Math.Tau;
        var (gustSpeed, gustAlong, shiver) = SceneDecor.Gust;

        // A breeze's strength here, 0 almost always: a short swell as it passes.
        var gust = $"Pow(Max(0, Sin({Clock}.Time * {N(gustSpeed)} - {N(p.X * gustAlong)} + {N(pile * 2.1)})), 24)";
        var stir = $"{N(6 + (8 * p.Breeze))} * {gust} * Sin({Clock}.Time * {N(shiver)} + {N(phase)})";
        if (gathered || lands <= SceneDecor.Landed)
        {
            if (p.Stirs)
            {
                yield return ("RotationAngleInDegrees", $"{N(p.Angle)} + {stir}");
            }

            yield break;
        }

        // 0 when it sets off, 1 once it has landed.
        var t = $"(({Clock}.Gather - {N(lands)}) / {N(SceneDecor.FlightSeconds)} + 1)";
        var at = $"Clamp({t}, 0, 1)";
        var rest = $"(1 - {at})";
        yield return ("Offset", $"Vector3({N(p.X)} + {N(p.FromX - p.X)} * {rest} * {rest} + 12 * {rest} * Sin({at} * 8 + {N(phase)}) - {N(half)}, {N(p.FromY)} + {N(p.Y - p.FromY)} * {at} * {at} * (3 - 2 * {at}) - {N(half)}, 0)");
        var stirred = p.Stirs ? $" + Floor({at}) * {stir}" : string.Empty;
        yield return ("RotationAngleInDegrees", $"{N(p.Angle)} + {N(p.Spin)} * {rest} * {rest}{stirred}");
        yield return ("Scale", $"Vector3({N(p.Squash)} + (0.1 + 0.9 * Abs(Cos({at} * 14 + {N(phase)})) - {N(p.Squash)}) * {rest}, 1, 1)");
        yield return ("Opacity", $"Clamp({t} * 6, 0, 1)");
    }

    /// <summary>The bough's slow sway about where it comes in.</summary>
    // How tall the scan line's glow is, in the content's units.
    private const float ScanHeight = 140;

    /// <summary>
    /// A drop of mercury: it gathers under the edge and swells for most of
    /// its period, stretches, lets go and falls <see cref="SceneLayout.DripFall"/>,
    /// gone by the time it lands.
    /// </summary>
    private static IEnumerable<(string Property, string Expression)> DripMotion(SceneLayout.Drip drip)
    {
        var half = drip.Size / 2;
        var u = $"Mod({Clock}.Time * {N(drip.Frequency)} + {N(drip.Phase)}, 1)";
        var g = $"Clamp({u} / 0.7, 0, 1)";
        var grow = $"(0.25 + 0.75 * {g} * {g} * (3 - 2 * {g}))";
        var drop = $"Clamp(({u} - 0.76) / 0.24, 0, 1)";
        var stretch = $"Clamp(({u} - 0.6) / 0.16, 0, 1) * (1 - {drop})";
        yield return ("Offset", $"Vector3({N(-half)}, -2 + {N(SceneLayout.DripFall)} * {drop} * {drop}, 0)");
        yield return ("Scale", $"Vector3({grow} * (1 - 0.14 * {stretch}), {grow} * (1 + 0.4 * {stretch}), 1)");
        yield return ("Opacity", $"Clamp({u} * 25, 0, 1) * (1 - {drop} * {drop})");
    }

    /// <summary>A band of light across a box <paramref name="width"/> wide, in the first <paramref name="share"/> of each period, eased.</summary>
    private static IEnumerable<(string Property, string Expression)> SweepMotion(double frequency, double share, double width, double height)
    {
        var bandWidth = Math.Max(90, height * 2.4);
        var u = $"Mod({Clock}.Time * {N(frequency)} + 0.5, 1)";
        var q = $"Clamp({u} / {N(share)}, 0, 1)";
        yield return ("Offset", $"Vector3({N(-bandWidth * 1.3)} + {N(width + (bandWidth * 2.6))} * {q} * {q} * (3 - 2 * {q}), {N(-height)}, 0)");
        yield return ("Opacity", $"Clamp({q} * 30, 0, 1) * Clamp((1 - {q}) * 30, 0, 1)");
    }

    /// <summary>A scan line down a box <paramref name="height"/> tall, in the first <paramref name="share"/> of each period.</summary>
    private static IEnumerable<(string Property, string Expression)> ScanMotion(double frequency, double share, double height)
    {
        var u = $"Mod({Clock}.Time * {N(frequency)} + 0.3, 1)";
        var q = $"Clamp({u} / {N(share)}, 0, 1)";
        yield return ("Offset", $"Vector3(0, {N(-ScanHeight)} + {N(height + ScanHeight)} * {q}, 0)");
        yield return ("Opacity", $"Clamp({q} * 20, 0, 1) * Clamp((1 - {q}) * 20, 0, 1)");
    }

    /// <summary>A HUD bracket's flicker and glitch, each box on its own beat.</summary>
    private static IEnumerable<(string Property, string Expression)> HudMotion(int seed)
    {
        // Spread out by the golden ratio, so no two boxes keep time.
        double Spread(int salt) => ((seed + 1) * 0.6180339887 * salt) % 1;
        var flicker = 6 + (3 * Spread(1));
        var glitch = 11 + (7 * Spread(2));
        var motion = SceneMotion.Parse(FormattableString.Invariant(
            $"flicker p={flicker:0.##} ph={Spread(3):0.###} w=0.015 lo=0.3; glitch p={glitch:0.##} ph={Spread(5):0.###} w=0.02 dx=3"));
        yield return ("Translation", motion.Translation!);
        yield return ("Opacity", motion.Opacity!);
    }

    private static (string Property, string Expression) SwayMotion()
    {
        var (a, w, b, v) = SceneDecor.BoughSway;
        return ("RotationAngleInDegrees", $"{N(a)} * Sin({Clock}.Time * {N(w)}) + {N(b)} * Sin({Clock}.Time * {N(v)} + 1.3)");
    }

    /// <summary>An icicle growing down from its root, from <paramref name="at"/> seconds into the gathering over <paramref name="over"/>.</summary>
    private static (string Property, string Expression) GrowMotion(double at, double over) =>
        ("Scale", $"Vector3(1, Clamp(({Clock}.Gather - {N(at)}) / {N(Math.Max(1, over))}, 0, 1), 1)");

    /// <summary>Snow on an edge settling from a thin layer to its full depth.</summary>
    /// <summary>
    /// Layer <paramref name="index"/> of <paramref name="count"/> of an edge's
    /// snow fading in its turn, thinnest first, so the snow thickens while
    /// every layer keeps to the edge and its corners.
    /// </summary>
    private static (string Property, string Expression) SnowMotion(int index, int count)
    {
        var t = $"Clamp({Clock}.Gather / {N(SceneDecor.GatherSeconds * SceneDecor.SnowSettles)}, 0, 1)";
        return ("Opacity", $"Clamp({t} * {count} - {index}, 0, 1)");
    }

    /// <summary>A glint twinkling now and then; on an edge's snow, only once the snow has settled.</summary>
    private static IEnumerable<(string Property, string Expression)> GlintMotion(int index, bool gathered)
    {
        var (speed, phase) = SceneDecor.Twinkle(index);
        var sparkle = $"Pow(Max(0, Sin({Clock}.Time * {N(speed)} + {N(phase)})), 10)";
        var settled = gathered ? string.Empty : $"Clamp(({Clock}.Gather / {N(SceneDecor.GatherSeconds * SceneDecor.SnowSettles)} - 0.85) * 6.67, 0, 1) * ";
        yield return ("Opacity", $"{settled}(0.25 + 0.75 * {sparkle})");
        yield return ("Scale", $"Vector3(0.55 + 0.45 * {sparkle}, 0.55 + 0.45 * {sparkle}, 1)");
    }

    /// <summary>Frost spreading out of its corner over the gathering.</summary>
    private static IEnumerable<(string Property, string Expression)> FrostMotion()
    {
        var t = $"Clamp({Clock}.Gather / {N(SceneDecor.GatherSeconds)}, 0, 1)";
        yield return ("Opacity", $"Clamp({Clock}.Gather / {N(SceneDecor.GatherSeconds * 0.6)}, 0, 1)");
        yield return ("Scale", $"Vector3(0.55 + 0.45 * {t} * (2 - {t}), 0.55 + 0.45 * {t} * (2 - {t}), 1)");
    }

    /// <summary>Something falling from the bough: a petal drifting away on the wind, or a few bits of snow dropping.</summary>
    private static IEnumerable<(string Property, string Expression)> ShedMotion(SceneDecor.Shed s)
    {
        var half = s.Size / 2;
        var f = $"(Mod({Clock}.Time + {N(s.Phase)}, {N(s.Period)}) / {N(s.Active)})";
        if (s.Sprite is not null)
        {
            var phase = s.Phase % Math.Tau;
            yield return ("Offset", $"Vector3({N(s.From.X - half)} + {N(s.Drift)} * {f} - 25 * Sin({f} * 7 + {N(phase)}), {N(s.From.Y - half)} + {N(s.Fall)} * {f} * (0.55 + 0.45 * {f}), 0)");
            yield return ("RotationAngleInDegrees", $"{N(phase * 57)} + {N(s.Spin)} * {f}");
            yield return ("Scale", $"Vector3(0.3 + 0.7 * Abs(Cos({f} * {N(s.Flutter)} + {N(phase)})), 1, 1)");
            yield return ("Opacity", $"{f} < 1 ? Clamp({f} * 10, 0, 1) * Clamp((1 - {f}) * 5, 0, 1) : 0");
            yield break;
        }

        yield return ("Offset", $"Vector3({N(s.From.X - half)} + {N(s.Drift)} * {f}, {N(s.From.Y - half)} + {N(s.Fall)} * {f} * {f}, 0)");
        yield return ("Opacity", $"{f} < 1 ? 0.9 * Clamp({f} * 20, 0, 1) * Clamp((1 - {f}) * 4, 0, 1) : 0");
    }

    /// <summary>One expression on one property of one visual.</summary>
    private readonly record struct Motion(CompositionObject Target, string Property, string Expression)
    {
        public Motion(CompositionObject target, (string Property, string Expression) motion)
            : this(target, motion.Property, motion.Expression)
        {
        }
    }

    /// <summary>
    /// One decoration (the bough, a pile, the snow on an edge...): its XAML
    /// shapes, placed by <see cref="Placement"/>, with its pictures riding on
    /// <see cref="Sprites"/> among them; what moves and how it rests.
    /// </summary>
    private sealed class Piece
    {
        public object? Key { get; set; }

        /// <summary>Which shapes it lies over: those of lower depth.</summary>
        public int Depth { get; set; }

        public FrameworkElement? Element { get; set; }

        public CompositeTransform? Placement { get; set; }

        /// <summary>The shape its pictures ride on, as its child visual.</summary>
        public UIElement? Sprites { get; set; }

        /// <summary>What moves, before or after everything has gathered.</summary>
        public Func<bool, IEnumerable<Motion>> Motions { get; set; } = _ => [];

        /// <summary>Puts everything as it is once all has gathered.</summary>
        public Action Rest { get; set; } = () => { };

        public List<CompositionObject> Owned { get; } = [];

        public List<(CompositionObject Target, string Property)> Running { get; } = [];
    }
}
