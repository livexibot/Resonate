using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The now-playing stage's visualizer: slim bars in the cover's colours
/// along the bottom of the stage, below the cover and the words (never
/// under them). Sprites the compositor moves from one property set: while
/// the feed has sound (the local files player, or the program playing a
/// Spotify song, heard through Windows) each bar follows a band of the
/// stage's spectrum, written once per new picture; otherwise (nothing
/// heard, or "Listen to Spotify" off) each bar sways on its own under a
/// slow wave (<see cref="StageBars"/>), worked out by the compositor with
/// nothing on the interface thread. While nothing plays, or the stage is
/// out of sight, the bars sink out of sight and every animation stops,
/// so a still stage costs nothing. Built in code.
/// </summary>
internal sealed partial class StageVisualizer : Grid
{
    private const string Props = "p";

    // A picture older than this means the music stopped flowing: the bars rest.
    private static readonly TimeSpan StaleFrame = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Rise = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan Fall = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Blend = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ColourChange = TimeSpan.FromSeconds(1);
    private static readonly string[] LevelNames = Enumerable.Range(0, StageBars.MaxCount).Select(i => "L" + i).ToArray();

    // The bars are brightest at their tips and fade towards the bottom of the stage.
    private const double TipAlpha = 0.92;
    private const double BaseAlpha = 0.18;

    // The least room under the words worth showing bars in.
    private const double MinRoom = 32;

    private readonly VisualiserFeed _feed;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionPropertySet _props;
    private readonly ScalarKeyFrameAnimation _fall;
    private readonly List<(SpriteVisual Sprite, CompositionColorGradientStop Tip, CompositionColorGradientStop Base)> _bars = [];
    private readonly float[] _levels = new float[StageBars.MaxCount];

    // What each bar heads for: the sound's newest picture, before smoothing.
    private readonly float[] _targets = new float[StageBars.MaxCount];
    private long _lastFrame;
    private readonly DispatcherQueueTimer _rebuild;
    private AnimationController? _clock;
    private IReadOnlyList<ThemeColor> _colours = [];
    private Vector2 _size;
    // No room until the stage has measured its words, so no bar ever shows under them.
    private double _room;
    private bool _running;
    private bool _hasRoom;
    private bool _moving;
    private bool _live;
    private bool _following;
    private bool _animate;
    private bool _drawing;
    private bool _wanted;
    private long _sequence = -1;

    public StageVisualizer(VisualiserFeed feed)
    {
        _feed = feed;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _props = _compositor.CreatePropertySet();
        _props.InsertScalar("Time", 0);
        _props.InsertScalar("Energy", 0);
        _props.InsertScalar("Live", 0);
        _props.InsertScalar("Rest", 0);
        foreach (var name in LevelNames)
        {
            _props.InsertScalar(name, 0);
        }

        // One animation sinks every bar to rest, from wherever it is.
        _fall = _compositor.CreateScalarKeyFrameAnimation();
        _fall.InsertExpressionKeyFrame(1, Props + ".Rest");
        _fall.SetReferenceParameter(Props, _props);
        _fall.Duration = Fall;

        ElementCompositionPreview.SetElementChildVisual(this, _root);

        // A window being dragged larger changes the number of bars once it settles; until then they only spread.
        _rebuild = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _rebuild.Interval = TimeSpan.FromMilliseconds(150);
        _rebuild.IsRepeating = false;
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) =>
        {
            // Loaded can come twice in a row; the handler is held once.
            _rebuild.Tick -= OnRebuild;
            _rebuild.Tick += OnRebuild;
            Build();
        };
        Unloaded += (_, _) =>
        {
            // Nothing outlives the stage: no timer, frame callback, analyser or motion holds on to it.
            _rebuild.Stop();
            _rebuild.Tick -= OnRebuild;
            SetRunning(false, live: false, _animate);
        };
    }

    /// <summary>Why a bar's motion could not start; null when all is well.</summary>
    public static string? MotionError { get; private set; }

    /// <summary>
    /// For CI's screenshot tour, which may run with animations off: starts
    /// the first and last bars' motion on a sprite nobody sees, so a mistake
    /// in the expression shows up there. Null when the compositor took it.
    /// </summary>
    public static string? CheckMotion(Compositor compositor)
    {
        var props = compositor.CreatePropertySet();
        foreach (var name in (string[])["Time", "Energy", "Live", "Rest", LevelNames[0], LevelNames[StageBars.MaxCount - 1]])
        {
            props.InsertScalar(name, 0.5f);
        }

        var sprite = compositor.CreateSpriteVisual();
        try
        {
            foreach (var index in (int[])[0, StageBars.MaxCount - 1])
            {
                var height = BarHeight(compositor, props, index, StageBars.MaxCount);
                sprite.StartAnimation("Scale.Y", height);
                sprite.StopAnimation("Scale.Y");
            }

            return MotionError;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            sprite.Dispose();
        }
    }

    /// <summary>
    /// Paints the bars in <paramref name="colours"/> (the cover's, made to
    /// stand out from the page), spread from left to right, flowing over a
    /// second when <paramref name="animate"/>.
    /// </summary>
    public void SetColours(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        _colours = colours;
        Paint(animate && _animate);
    }

    /// <summary>
    /// How much height is free under the cover and the words, so the bars
    /// never reach them. Too little, and no bars show.
    /// </summary>
    public void SetRoom(double room)
    {
        if (Math.Abs(room - _room) >= 0.5)
        {
            _room = room;
            Place();
        }
    }

    /// <summary>
    /// Whether the bars move (<paramref name="running"/>: music plays and the
    /// stage is seen), whether they follow sound (<paramref name="live"/>: a
    /// local file's, or Spotify's as heard) or sway on their own, and whether
    /// anything may animate (<paramref name="animate"/>: Windows' animations
    /// are on).
    /// </summary>
    public void SetRunning(bool running, bool live, bool animate)
    {
        _animate = animate;
        _running = running && animate;
        _live = live;
        Refresh();
    }

    /// <summary>Starts or stops what the bars need: their motion, the analyser and the per-frame reading of its pictures.</summary>
    private void Refresh()
    {
        var move = _running && _hasRoom && _bars.Count > 0;
        if (move != _wanted)
        {
            _wanted = move;
            _feed.SetWanted(this, move, stage: true);
        }

        if (move && !_moving)
        {
            StartMotion();
        }
        else if (!move && _moving)
        {
            StopMotion();
        }

        var live = _moving && _live;
        if (live != _following)
        {
            _following = live;
            if (live)
            {
                // The sound starts from silence, not from the last song's bands.
                ClearLevels();
            }

            AnimateScalar("Live", live ? 1 : 0, Blend);
        }

        if (live != _drawing)
        {
            _drawing = live;
            if (live)
            {
                _sequence = -1;
                CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }

    private void OnRebuild(DispatcherQueueTimer sender, object args) => Build();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        if (_bars.Count == 0 || BarCount() == _bars.Count)
        {
            if (_bars.Count == 0)
            {
                Build();
            }
            else
            {
                Place();
            }

            return;
        }

        Place();
        _rebuild.Stop();
        _rebuild.Start();
    }

    /// <summary>Makes as many bars as fit the width, and sets them moving if music plays.</summary>
    private void Build()
    {
        if (_size.X <= 0 || _size.Y <= 0 || !IsLoaded)
        {
            return;
        }

        var count = BarCount();
        if (count == _bars.Count)
        {
            Place();
            return;
        }

        foreach (var (sprite, _, _) in _bars)
        {
            sprite.StopAnimation("Scale.Y");
        }

        // The new bars take up the motion where the old ones left it.
        var moving = _moving;
        _moving = false;
        _root.Children.RemoveAll();

        // The old bars are gone from the stage: their sprites and brushes are let go of now, not when .NET next collects.
        foreach (var (sprite, tip, bottom) in _bars)
        {
            var brush = sprite.Brush;
            sprite.Brush = null;
            brush?.Dispose();
            tip.Dispose();
            bottom.Dispose();
            sprite.Dispose();
        }

        _bars.Clear();
        for (var i = 0; i < count; i++)
        {
            var brush = _compositor.CreateLinearGradientBrush();
            brush.StartPoint = Vector2.Zero;
            brush.EndPoint = new Vector2(0, 1);
            var tip = _compositor.CreateColorGradientStop(0, default);
            var bottom = _compositor.CreateColorGradientStop(1, default);
            brush.ColorStops.Add(tip);
            brush.ColorStops.Add(bottom);
            var sprite = _compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            _root.Children.InsertAtTop(sprite);
            _bars.Add((sprite, tip, bottom));
        }

        Place();
        Paint(animate: false);
        if (moving && _hasRoom && !_moving)
        {
            StartMotion(rise: false);
        }

        Refresh();
    }

    /// <summary>Spreads the bars over the width, as tall as the stage and the room under the words allow.</summary>
    private void Place()
    {
        if (_bars.Count == 0 || _size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        var height = (float)Math.Min(StageBars.Height(_size.Y), _room);
        var hasRoom = height >= MinRoom;
        _root.IsVisible = hasRoom;
        if (hasRoom != _hasRoom)
        {
            _hasRoom = hasRoom;
            Refresh();
        }

        if (!hasRoom)
        {
            return;
        }

        var pitch = _size.X / _bars.Count;
        var fill = Math.Clamp(App.Services.Settings.HomeStageBarWidth, 20, 90) / 100.0;
        var width = Math.Max(1f, (float)(pitch * fill));
        var rest = (float)(StageBars.RestHeight / height);
        _props.InsertScalar("Rest", rest);
        for (var i = 0; i < _bars.Count; i++)
        {
            var sprite = _bars[i].Sprite;
            sprite.Size = new Vector2(width, height);
            sprite.Offset = new Vector3((i * pitch) + ((pitch - width) / 2), _size.Y - height, 0);
            sprite.CenterPoint = new Vector3(0, height, 0);
            if (!_moving)
            {
                sprite.Scale = new Vector3(1, rest, 1);
            }
        }
    }

    /// <summary>Each bar's height becomes an expression of the clock, its band of the spectrum and the energy, which rises from rest.</summary>
    private void StartMotion(bool rise = true)
    {
        if (_bars.Count == 0)
        {
            return;
        }

        if (_clock is null)
        {
            var clock = _compositor.CreateScalarKeyFrameAnimation();
            clock.InsertKeyFrame(0, 0, _compositor.CreateLinearEasingFunction());
            clock.InsertKeyFrame(1, (float)StageBars.LoopSeconds, _compositor.CreateLinearEasingFunction());
            clock.Duration = TimeSpan.FromSeconds(StageBars.LoopSeconds);
            clock.IterationBehavior = AnimationIterationBehavior.Forever;
            _props.StartAnimation("Time", clock);
            _clock = _props.TryGetAnimationController("Time");
        }
        else
        {
            _clock.Resume();
        }

        try
        {
            for (var i = 0; i < _bars.Count; i++)
            {
                _bars[i].Sprite.StartAnimation("Scale.Y", BarHeight(_compositor, _props, i, _bars.Count));
            }

            _moving = true;
            MotionError = null;
        }
        catch (Exception ex)
        {
            // A bar the compositor refused stays at rest; the stage itself is fine.
            MotionError = ex.Message;
            StopMotion();
            return;
        }

        if (rise)
        {
            _props.InsertScalar("Energy", 0);
            AnimateScalar("Energy", 1, Rise);
        }
    }

    /// <summary>A bar's height: its own sway, or its band of the sound, times the energy, and never below rest.</summary>
    private static ExpressionAnimation BarHeight(Compositor compositor, CompositionPropertySet props, int index, int count)
    {
        var synthetic = StageBars.SyntheticExpression(index, count, Props + ".Time");
        var height = compositor.CreateExpressionAnimation(
            $"Max({Props}.Rest, {Props}.Energy * Lerp({synthetic}, {Props}.{LevelNames[index]}, {Props}.Live))");
        height.SetReferenceParameter(Props, props);
        return height;
    }

    /// <summary>The bars sink out of sight and every animation ends, so nothing redraws while still.</summary>
    private void StopMotion()
    {
        _clock?.Pause();
        _props.StopAnimation("Energy");
        _props.InsertScalar("Energy", 0);
        _moving = false;
        foreach (var (sprite, _, _) in _bars)
        {
            if (_animate)
            {
                sprite.StartAnimation("Scale.Y", _fall);
            }
            else
            {
                sprite.StopAnimation("Scale.Y");
            }
        }

        if (!_animate)
        {
            Place();
        }
    }

    private void OnRendering(object? sender, object e)
    {
        // A new picture of the sound (about 100 a second) gives each bar a new height to head for.
        var settings = App.Services.Settings;
        var frame = _feed.Stage.Read();
        var atRest = frame.IsAtRest || Stopwatch.GetElapsedTime(frame.Timestamp) > StaleFrame;
        var sequence = atRest ? -2 : frame.Sequence;
        var count = _bars.Count;
        if (sequence != _sequence)
        {
            _sequence = sequence;
            var targets = _targets.AsSpan(0, count);
            if (atRest)
            {
                targets.Clear();
            }
            else
            {
                StageBars.Resample(frame.Bars.AsSpan(0, frame.BarCount), targets);
                var gain = Math.Clamp(settings.HomeStageSensitivity, 50, 200) / 100f;
                for (var i = 0; i < targets.Length; i++)
                {
                    targets[i] = Math.Min(1f, targets[i] * gain);
                }
            }
        }

        // Every frame the bars glide towards it, rising quickly and falling slowly, so they never jitter.
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastFrame == 0 ? 1 / 60f : (float)Math.Min(0.1, Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        var smoothing = Math.Clamp(settings.HomeStageSmoothing, 0, 100) / 100.0;
        for (var i = 0; i < count; i++)
        {
            var next = StageBars.Smooth(_levels[i], _targets[i], seconds, smoothing);
            if (Math.Abs(next - _levels[i]) > 0.0005f)
            {
                _levels[i] = next;
                _props.InsertScalar(LevelNames[i], next);
            }
        }
    }

    /// <summary>The user's bar count, as many as fit.</summary>
    private int BarCount() => StageBars.Count(_size.X, App.Services.Settings.HomeStageBars);

    /// <summary>Settings, Layout, Home changed the bars: made again at once.</summary>
    public void ApplyOptions()
    {
        Build();
        Place();
    }

    private void ClearLevels()
    {
        Array.Clear(_targets);
        _lastFrame = 0;
        Array.Clear(_levels);
        foreach (var name in LevelNames)
        {
            _props.InsertScalar(name, 0);
        }
    }

    /// <summary>Each bar takes the colour at its place across the row, between the cover's colours.</summary>
    private void Paint(bool animate)
    {
        if (_colours.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            var across = _bars.Count > 1 ? i / (double)(_bars.Count - 1) : 0;
            var at = across * (_colours.Count - 1);
            var low = (int)Math.Floor(at);
            var high = Math.Min(low + 1, _colours.Count - 1);
            var colour = _colours[low].Mix(_colours[high], at - low);
            var (_, tip, bottom) = _bars[i];
            Paint(tip, colour.WithAlpha(TipAlpha), animate);
            Paint(bottom, colour.WithAlpha(BaseAlpha), animate);
        }
    }

    private void Paint(CompositionColorGradientStop stop, ThemeColor colour, bool animate)
    {
        var target = colour.ToColor();
        if (!animate)
        {
            stop.StopAnimation("Color");
            stop.Color = target;
            return;
        }

        var flow = _compositor.CreateColorKeyFrameAnimation();
        flow.InsertKeyFrame(1, target);
        flow.Duration = ColourChange;
        stop.StartAnimation("Color", flow);
    }

    private void AnimateScalar(string name, float value, TimeSpan duration)
    {
        if (!_animate)
        {
            _props.StopAnimation(name);
            _props.InsertScalar(name, value);
            return;
        }

        var move = _compositor.CreateScalarKeyFrameAnimation();
        move.InsertKeyFrame(1, value);
        move.Duration = duration;
        _props.StartAnimation(name, move);
    }
}
