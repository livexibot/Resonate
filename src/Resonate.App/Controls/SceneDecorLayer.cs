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
/// player, icicles that grow under it and frost in two corners, for Snow.
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
    private readonly ContainerVisual _sprites;
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

    public SceneDecorLayer(ThemeService theme, SceneClock clock)
    {
        _theme = theme;
        _clock = clock;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _sprites = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(this, _sprites);
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
            motions.Add(SnowMotion());
            motions.AddRange(GlintMotion(3, gathered: false));
            motions.AddRange(GlintMotion(3, gathered: true));
            motions.AddRange(FrostMotion());
            foreach (var scene in (ThemeScene[])[ThemeScene.Japan, ThemeScene.Snow])
            {
                motions.AddRange(SceneDecor.Shedding(scene).SelectMany(ShedMotion));
                _ = SceneDecor.Bough(scene);
            }

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

        Watch(_scene != ThemeScene.None && !_failed);
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

    private void Relayout()
    {
        if (_scene == ThemeScene.None)
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
                var piece = Ensure("bough", _scene, 1, () => BuildBough(art));
                Place(piece, bough.Right - (art.Width * bough.Scale), bough.Top - (art.PanelTop * bough.Scale), bough.Scale, bough.Scale);
                wanted.Add("bough");
            }

            if (_scene == ThemeScene.Japan)
            {
                LayOutPiles(frame, wanted);
            }
            else
            {
                LayOutSnow(frame, wanted);
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
            var piece = Ensure(key, (Math.Round(Math.Min(spot.Width, room)), spot.Radius), 0, () => BuildPile(index, spot));
            Place(piece, spot.X, spot.Y, spot.Mirrored ? -spot.Scale : spot.Scale, spot.Scale);
            wanted.Add(key);
        }
    }

    private void LayOutSnow(SceneFrame frame, HashSet<string> wanted)
    {
        foreach (var cap in SceneLayout.Caps(frame))
        {
            var key = $"cap{cap.Seed}";
            var piece = Ensure(key, (Math.Round(cap.Spot.Width), cap.Spot.Radius, cap.Depth), 2, () => BuildCap(cap));
            Place(piece, cap.Spot.X, cap.Spot.Y, cap.Spot.Scale, cap.Spot.Scale);
            wanted.Add(key);
        }

        if (SceneLayout.Fringe(frame) is { } fringe)
        {
            var room = (2 * ((fringe.Radius * 0.7) + 160)) + 20;
            var piece = Ensure("fringe", (Math.Round(Math.Min(fringe.Width, room)), fringe.Radius), 3, () => BuildFringe(fringe));
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
            // Lower pieces first (frost, then the bough, then snow on the edges, then what hangs under the player); the children are the pieces' shapes, in that order.
            var at = _pieces.Values.Count(p => p.Element is not null && p.Depth <= depth);
            Children.Insert(Math.Min(at, Children.Count), piece.Element);
        }

        if (piece.Holder is not null)
        {
            _sprites.Children.InsertAtTop(piece.Holder);
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

        if (piece.Holder is { } holder)
        {
            holder.Offset = new Vector3((float)x, (float)y, 0);
            holder.Scale = new Vector3((float)scaleX, (float)scaleY, 1);
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

        if (piece.Holder is not null)
        {
            _sprites.Children.Remove(piece.Holder);
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
        _sprites.Children.RemoveAll();
    }

    /// <summary>How many screen pixels one of the content's units is (for pictures that stay sharp).</summary>
    private double Pixels => (XamlRoot?.RasterizationScale ?? 1) * _theme.Scale;

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
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
        piece.Holder = holder;
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
        var cap = SceneDecor.Cap(spot.Seed, spot.Spot.Width, spot.Spot.Radius, spot.Depth, spot.Icicles, spot.IcicleLength);
        var piece = new Piece();
        var placement = new CompositeTransform();
        var outer = new Canvas { IsHitTestVisible = false, RenderTransform = placement };

        // The snow settles from a thin layer, growing up from the edge.
        var snow = new Canvas();
        snow.Children.Add(new Path { Data = Figures([cap.Shade], closed: true), Fill = new SolidColorBrush(ThemeColor.FromRgb(0x9DB9D8).ToColor()), Opacity = 0.9 });
        snow.Children.Add(new Path { Data = Figures([cap.Outline], closed: true), Fill = Gradient(ThemeColor.FromRgb(0xFFFFFF), ThemeColor.FromRgb(0xDCEAF8)) });
        snow.Children.Add(new Path { Data = Figures([cap.Crest], closed: false), Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1, Opacity = 0.9 });
        outer.Children.Add(snow);
        var settle = ElementCompositionPreview.GetElementVisual(snow);

        var growing = new List<(Visual Visual, SceneDecor.Icicle Icicle)>();
        var ice = IceBrush();
        foreach (var icicle in cap.Icicles)
        {
            var path = IciclePath(icicle, icicle.X, icicle.Y, ice);
            outer.Children.Add(path);
            growing.Add((ElementCompositionPreview.GetElementVisual(path), icicle));
        }

        // Where the snow catches the light.
        var holder = _compositor.CreateContainerVisual();
        piece.Owned.Add(holder);
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
        piece.Holder = holder;
        piece.Motions = gathered =>
        {
            var motions = new List<Motion>();
            if (!gathered)
            {
                motions.Add(new Motion(settle, SnowMotion()));
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
            settle.Scale = Vector3.One;
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
    private static (string Property, string Expression) SwayMotion()
    {
        var (a, w, b, v) = SceneDecor.BoughSway;
        return ("RotationAngleInDegrees", $"{N(a)} * Sin({Clock}.Time * {N(w)}) + {N(b)} * Sin({Clock}.Time * {N(v)} + 1.3)");
    }

    /// <summary>An icicle growing down from its root, from <paramref name="at"/> seconds into the gathering over <paramref name="over"/>.</summary>
    private static (string Property, string Expression) GrowMotion(double at, double over) =>
        ("Scale", $"Vector3(1, Clamp(({Clock}.Gather - {N(at)}) / {N(Math.Max(1, over))}, 0, 1), 1)");

    /// <summary>Snow on an edge settling from a thin layer to its full depth.</summary>
    private static (string Property, string Expression) SnowMotion()
    {
        var t = $"Clamp({Clock}.Gather / {N(SceneDecor.GatherSeconds * SceneDecor.SnowSettles)}, 0, 1)";
        return ("Scale", $"Vector3(1, 0.15 + 0.85 * {t} * {t} * (3 - 2 * {t}), 1)");
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
    /// shapes, placed by <see cref="Placement"/>, and its sprites in
    /// <see cref="Holder"/>, placed alike; what moves and how it rests.
    /// </summary>
    private sealed class Piece
    {
        public object? Key { get; set; }

        /// <summary>Which shapes it lies over: those of lower depth.</summary>
        public int Depth { get; set; }

        public FrameworkElement? Element { get; set; }

        public CompositeTransform? Placement { get; set; }

        public ContainerVisual? Holder { get; set; }

        /// <summary>What moves, before or after everything has gathered.</summary>
        public Func<bool, IEnumerable<Motion>> Motions { get; set; } = _ => [];

        /// <summary>Puts everything as it is once all has gathered.</summary>
        public Action Rest { get; set; } = () => { };

        public List<CompositionObject> Owned { get; } = [];

        public List<(CompositionObject Target, string Property)> Running { get; } = [];
    }
}
