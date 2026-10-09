using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls.Visualizers;
using Resonate.App.Services;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>
/// The now-playing stage's visualizer, in the cover's colours: along the
/// bottom of the stage, below the cover and the words (never under them),
/// or around the cover for the styles drawn there. Its drawing
/// (<see cref="VisualizerDrawing"/>, one per <see cref="VisualizerStyle"/>)
/// is visuals the compositor moves from one property set: while the feed
/// has sound (the local files player, or the program playing a Spotify
/// song, heard through Windows) each level follows a band of the stage's
/// spectrum, written once per new picture; otherwise (nothing heard, or
/// "Listen to Spotify" off) each sways on its own under a slow wave
/// (<see cref="StageBars"/>), worked out by the compositor with nothing on
/// the interface thread. While nothing plays, or the stage is out of
/// sight, it sinks away and every animation stops, so a still stage costs
/// nothing. Also the player bar's (<see cref="InBar"/>). Built in code.
/// </summary>
internal sealed partial class StageVisualizer : Grid
{
    internal const string Props = "p";

    // A picture older than this means the music stopped flowing: the levels rest.
    private static readonly TimeSpan StaleFrame = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Rise = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan Fall = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Blend = TimeSpan.FromMilliseconds(400);
    private static readonly string[] LevelNames = Enumerable.Range(0, StageBars.MaxCount).Select(i => "L" + i).ToArray();
    private static readonly string[] PeakNames = Enumerable.Range(0, StageBars.MaxCount).Select(i => "K" + i).ToArray();

    // At most this often the levels take a new value; a 165 Hz screen would otherwise ask nearly three times as often.
    private static readonly TimeSpan MinFrameTime = TimeSpan.FromMilliseconds(15);

    // The least room under the words worth drawing in, on the stage and in the player bar.
    private const double MinRoom = 32;
    private const double MinBarRoom = 10;

    // In the player bar the drawing takes this share of its height.
    private const double BarShare = 0.45;

    private readonly VisualiserFeed _feed;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly CompositionPropertySet _props;
    private readonly VisualizerCanvas _canvas;
    private readonly float[] _levels = new float[StageBars.MaxCount];
    private readonly float[] _peaks = new float[StageBars.MaxCount];

    // What each level heads for: the sound's newest picture, before smoothing.
    private readonly float[] _targets = new float[StageBars.MaxCount];
    private readonly DispatcherQueueTimer _rebuild;
    private VisualizerDrawing _drawing;
    private long _lastFrame;
    private AnimationController? _clock;
    private IReadOnlyList<ThemeColor> _colours = [];
    private Vector2 _size;

    // No room until the stage has measured its words, so nothing ever shows under them.
    private double _room;
    private bool _running;
    private bool _hasRoom;
    private bool _moving;
    private bool _live;
    private bool _following;
    private bool _animate;
    private bool _drawingFrames;
    private bool _wanted;
    private long _sequence = -1;
    private int _stopVersion;
    private VisualizerStyle _style = VisualizerStyle.Bars;

    public StageVisualizer(VisualiserFeed feed)
    {
        _feed = feed;
        IsHitTestVisible = false;
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _root = _compositor.CreateContainerVisual();
        _props = NewProps(_compositor);
        _canvas = new VisualizerCanvas(_compositor, _root, _props);
        _drawing = Create(_style, _canvas);
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
            // Moved to another parent (the stage puts it around the cover and back): it is still in the tree.
            if (IsLoaded)
            {
                return;
            }

            // Nothing outlives the stage: no timer, frame callback, analyser or motion holds on to it.
            _rebuild.Stop();
            _rebuild.Tick -= OnRebuild;
            SetRunning(false, live: false, _animate);
        };
    }

    /// <summary>Why the motion could not start; null when all is well.</summary>
    public static string? MotionError { get; private set; }

    /// <summary>Drawn in the player bar: in a share of its height, with smaller dots and sparks.</summary>
    public bool InBar
    {
        get => _canvas.InBar;
        init => _canvas.InBar = value;
    }

    /// <summary>How far a drawing around the cover may reach outside it (the gap to the words).</summary>
    public double Reach
    {
        get => _canvas.Reach;
        set
        {
            if (Math.Abs(value - _canvas.Reach) >= 0.5)
            {
                _canvas.Reach = value;
                Place();
            }
        }
    }

    /// <summary>The cover's corner radius, for drawings that follow its shape.</summary>
    public double CoverCorner
    {
        get => _canvas.Corner;
        set => _canvas.Corner = value;
    }

    /// <summary>Whether the style is drawn around the cover (inside its box) rather than along the bottom.</summary>
    public bool AroundCover => _drawing.AroundCover;

    /// <summary>How the sound is drawn (the look's choice); a new style takes over at once, moving if the old one moved.</summary>
    public VisualizerStyle DrawStyle
    {
        get => _style;
        set
        {
            if (value == VisualizerStyle.Off || value == _style || !Enum.IsDefined(value))
            {
                return;
            }

            var moving = _moving;
            if (moving)
            {
                EndMotion();
            }

            _drawing.Dispose();
            _style = value;
            _drawing = Create(value, _canvas);
            Build(force: true);
            if (moving && _hasRoom && !_moving)
            {
                StartMotion(rise: false);
            }

            Refresh();
        }
    }

    /// <summary>Level <paramref name="index"/> of <paramref name="count"/> as an expression: its own sway, or its band of the sound, times the energy.</summary>
    internal static string LevelExpression(int index, int count)
    {
        var synthetic = StageBars.SyntheticExpression(index, count, Props + ".Time");
        return $"({Props}.Energy * Lerp({synthetic}, {Props}.{LevelNames[index]}, {Props}.Live))";
    }

    internal static string LevelName(int index) => LevelNames[index];

    internal static string PeakName(int index) => PeakNames[index];

    /// <summary>
    /// For CI's screenshot tour, which may run with animations off: builds
    /// every style on visuals nobody sees and starts its motion, so a mistake
    /// in an expression shows up there. Null when the compositor took them all.
    /// </summary>
    public static string? CheckMotion(Compositor compositor)
    {
        var root = compositor.CreateContainerVisual();
        var props = NewProps(compositor);
        var canvas = new VisualizerCanvas(compositor, root, props);
        try
        {
            foreach (var style in Enum.GetValues<VisualizerStyle>())
            {
                if (style == VisualizerStyle.Off)
                {
                    continue;
                }

                var drawing = Create(style, canvas);
                try
                {
                    var size = new Vector2(640, 360);
                    drawing.Build(drawing.CountFor(size, 64));
                    drawing.Place(size, 120);
                    drawing.Paint([new ThemeColor(255, 255, 255, 255), new ThemeColor(255, 30, 30, 30)], animate: false);
                    drawing.Start();
                    drawing.Stop();
                }
                catch (Exception ex)
                {
                    return $"{style}: {ex.Message}";
                }
                finally
                {
                    drawing.Dispose();
                }
            }

            return null;
        }
        finally
        {
            props.Dispose();
            root.Dispose();
        }
    }

    /// <summary>
    /// Paints the drawing in <paramref name="colours"/> (the cover's, made to
    /// stand out from the page), flowing over a second when <paramref name="animate"/>.
    /// </summary>
    public void SetColours(IReadOnlyList<ThemeColor> colours, bool animate)
    {
        _colours = colours;
        _drawing.Paint(colours, animate && _animate);
    }

    /// <summary>
    /// How much height is free under the cover and the words, so the drawing
    /// never reaches them. Too little, and nothing shows.
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
    /// Whether the drawing moves (<paramref name="running"/>: music plays and
    /// it is seen), whether it follows sound (<paramref name="live"/>: a local
    /// file's, or Spotify's as heard) or sways on its own, and whether
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

    /// <summary>Settings, Layout, Home changed the bars: made again at once.</summary>
    public void ApplyOptions()
    {
        Build();
        Place();
    }

    private static CompositionPropertySet NewProps(Compositor compositor)
    {
        var props = compositor.CreatePropertySet();
        props.InsertScalar("Time", 0);
        props.InsertScalar("Energy", 0);
        props.InsertScalar("Live", 0);
        foreach (var name in LevelNames)
        {
            props.InsertScalar(name, 0);
        }

        foreach (var name in PeakNames)
        {
            props.InsertScalar(name, 0);
        }

        return props;
    }

    private static VisualizerDrawing Create(VisualizerStyle style, VisualizerCanvas canvas) => style switch
    {
        VisualizerStyle.Retro => new RetroDrawing(canvas),
        VisualizerStyle.Wave => new WaveDrawing(canvas, helix: false),
        VisualizerStyle.Helix => new WaveDrawing(canvas, helix: true),
        VisualizerStyle.Radial => new RadialDrawing(canvas),
        VisualizerStyle.Pulse => new PulseDrawing(canvas),
        VisualizerStyle.Embers => new EmbersDrawing(canvas),
        _ => new BarsDrawing(canvas, style),
    };

    /// <summary>Starts or stops what the drawing needs: its motion, the analyser and the per-frame reading of its pictures.</summary>
    private void Refresh()
    {
        var move = _running && _hasRoom && _drawing.Built > 0;
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

        if (live != _drawingFrames)
        {
            _drawingFrames = live;
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
        if (_drawing.Built < 0)
        {
            Build();
            return;
        }

        Place();
        if (_drawing.CountFor(DrawSize(), App.Services.Settings.HomeStageBars) != _drawing.Built)
        {
            _rebuild.Stop();
            _rebuild.Start();
        }
    }

    /// <summary>Makes as many elements as fit, and sets them moving if music plays.</summary>
    private void Build(bool force = false)
    {
        if (_size.X <= 0 || _size.Y <= 0 || !IsLoaded)
        {
            return;
        }

        _canvas.Fill = Math.Clamp(App.Services.Settings.HomeStageBarWidth, 20, 90) / 100.0;
        var count = _drawing.CountFor(DrawSize(), App.Services.Settings.HomeStageBars);
        if (count == _drawing.Built && !force)
        {
            Place();
            return;
        }

        // The new elements take up the motion where the old ones left it.
        var moving = _moving;
        _moving = false;
        _drawing.Build(count);
        Place();
        _drawing.Paint(_colours, animate: false);
        if (moving && _hasRoom && !_moving)
        {
            StartMotion(rise: false);
        }

        Refresh();
    }

    /// <summary>Lays the drawing out in the room it has: along the bottom, below the words, or around the cover.</summary>
    private void Place()
    {
        if (_drawing.Built < 0 || _size.X <= 0 || _size.Y <= 0)
        {
            return;
        }

        _canvas.Fill = Math.Clamp(App.Services.Settings.HomeStageBarWidth, 20, 90) / 100.0;
        double height;
        bool hasRoom;
        if (_drawing.AroundCover)
        {
            height = _size.Y;
            hasRoom = _size.X >= 48;
        }
        else if (InBar)
        {
            height = _room * BarShare;
            hasRoom = height >= MinBarRoom;
        }
        else
        {
            // The user's height and its cap (Settings, Player, Visualizer, Advanced), and never under the words.
            var settings = App.Services.Settings;
            var most = StageBars.Height(_size.Y, Math.Clamp(settings.HomeStageHeight, 10, 60) / 100.0, Math.Clamp(settings.HomeStageMaxHeight, 40, 600));
            height = Math.Min(most, _room);
            hasRoom = height >= MinRoom;
        }

        // The user's width, centred.
        var size = DrawSize();
        _root.Offset = new Vector3((_size.X - size.X) / 2, 0, 0);
        _root.IsVisible = hasRoom;
        if (hasRoom)
        {
            _drawing.Place(size, (float)height);
        }

        if (hasRoom != _hasRoom)
        {
            _hasRoom = hasRoom;
            Refresh();
        }
    }

    /// <summary>The room the drawing spans: Home's along the bottom takes the user's share of the width, centred.</summary>
    private Vector2 DrawSize()
    {
        if (InBar || _drawing.AroundCover)
        {
            return _size;
        }

        var share = Math.Clamp(App.Services.Settings.HomeStageWidth, 20, 100) / 100f;
        return new Vector2(_size.X * share, _size.Y);
    }

    /// <summary>The drawing's expressions start, and the energy rises from rest.</summary>
    private void StartMotion(bool rise = true)
    {
        if (_drawing.Built <= 0)
        {
            return;
        }

        // A stop still sinking away no longer applies.
        _stopVersion++;
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
            _drawing.Start();
            _moving = true;
            MotionError = null;
        }
        catch (Exception ex)
        {
            // A drawing the compositor refused stays at rest; the stage itself is fine.
            MotionError = $"{_style}: {ex.Message}";
            _drawing.Stop();
            return;
        }

        if (rise)
        {
            _props.InsertScalar("Energy", 0);
            AnimateScalar("Energy", 1, Rise);
        }
        else
        {
            _props.StopAnimation("Energy");
            _props.InsertScalar("Energy", 1);
        }
    }

    /// <summary>The drawing sinks away with the energy, then every animation ends, so nothing redraws while still.</summary>
    private void StopMotion()
    {
        _moving = false;
        if (!_animate)
        {
            EndMotion();
            return;
        }

        var version = ++_stopVersion;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fall = _compositor.CreateScalarKeyFrameAnimation();
        fall.InsertKeyFrame(1, 0);
        fall.Duration = Fall;
        _props.StartAnimation("Energy", fall);
        batch.End();
        batch.Completed += (_, _) =>
        {
            batch.Dispose();
            if (version == _stopVersion && !_moving)
            {
                EndMotion();
            }
        };
    }

    /// <summary>Everything stops at once: the clock, the energy and the drawing's expressions.</summary>
    private void EndMotion()
    {
        _moving = false;
        _clock?.Pause();
        _props.StopAnimation("Energy");
        _props.InsertScalar("Energy", 0);
        _drawing.Stop();
    }

    private void OnRendering(object? sender, object e)
    {
        var started = Stopwatch.GetTimestamp();
        if (_lastFrame != 0 && Stopwatch.GetElapsedTime(_lastFrame, started) < MinFrameTime)
        {
            return;
        }

        // A new picture of the sound (about 100 a second) gives each level a new value to head for.
        var settings = App.Services.Settings;
        var frame = _feed.Stage.Read();
        var atRest = frame.IsAtRest || Stopwatch.GetElapsedTime(frame.Timestamp) > StaleFrame;
        var sequence = atRest ? -2 : frame.Sequence;
        var count = Math.Min(_drawing.Channels, StageBars.MaxCount);
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
                var bands = frame.Bars.AsSpan(0, frame.BarCount);
                if (_drawing.Averages)
                {
                    StageBars.Average(bands, targets);
                }
                else
                {
                    StageBars.Resample(bands, targets);
                }

                var gain = Math.Clamp(settings.HomeStageSensitivity, 50, 200) / 100f;
                for (var i = 0; i < targets.Length; i++)
                {
                    targets[i] = Math.Min(1f, targets[i] * gain);
                }
            }
        }

        // Every frame the levels glide towards it, rising quickly and falling slowly, so they never jitter.
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastFrame == 0 ? 1 / 60f : (float)Math.Min(0.1, Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        var smoothing = Math.Clamp(settings.HomeStageSmoothing, 0, 100) / 100.0;
        var peaks = _drawing.WantsPeaks;
        for (var i = 0; i < count; i++)
        {
            var next = StageBars.Smooth(_levels[i], _targets[i], seconds, smoothing);
            if (Math.Abs(next - _levels[i]) > 0.0005f)
            {
                _levels[i] = next;
                _props.InsertScalar(LevelNames[i], next);
            }

            if (peaks)
            {
                var peak = VisualizerShapes.Peak(_peaks[i], next, seconds);
                if (Math.Abs(peak - _peaks[i]) > 0.0005f)
                {
                    _peaks[i] = peak;
                    _props.InsertScalar(PeakNames[i], peak);
                }
            }
        }
    }

    private void ClearLevels()
    {
        Array.Clear(_targets);
        _lastFrame = 0;
        Array.Clear(_levels);
        Array.Clear(_peaks);
        foreach (var name in LevelNames)
        {
            _props.InsertScalar(name, 0);
        }

        foreach (var name in PeakNames)
        {
            _props.InsertScalar(name, 0);
        }
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
