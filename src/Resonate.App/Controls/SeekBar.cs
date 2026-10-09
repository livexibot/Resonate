using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Resonate.Themes;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>
/// The progress and volume bars, drawn in the theme's style: a slim line, a
/// bold bar, a glowing gradient, a hairline, or one that moves while playing
/// (a wave, two waves, a heartbeat, marching dots, a shimmer or rings from
/// the handle), which stops where it is when the music does. The bar shows
/// whatever value it is given; the player's clock moves it on one screen
/// pixel at a time (see <see cref="ValuePerPixel"/>). A glide on the
/// compositor would look the same, but would redraw the window at the
/// screen's refresh rate for as long as a song plays.
/// </summary>
public sealed partial class SeekBar : RangeBase
{
    public static readonly DependencyProperty BarStyleProperty = DependencyProperty.Register(
        nameof(BarStyle),
        typeof(ProgressStyle),
        typeof(SeekBar),
        new PropertyMetadata(ProgressStyle.Line, (d, _) => ((SeekBar)d).OnBarStyleChanged()));

    public static readonly DependencyProperty IsAdvancingProperty = DependencyProperty.Register(
        nameof(IsAdvancing),
        typeof(bool),
        typeof(SeekBar),
        new PropertyMetadata(false, (d, _) => ((SeekBar)d).OnAdvancingChanged()));

    // Shimmer: the light's width, and how long one pass and the rest after it take.
    private const double ShineWidth = 72;
    private static readonly TimeSpan ShinePass = TimeSpan.FromSeconds(1.7);
    private static readonly TimeSpan ShineRest = TimeSpan.FromSeconds(1.3);

    // Ripple: the ring's size at the handle and how far and how often it grows.
    private const double RingSize = 14;
    private const float RingGrowth = 2.6f;
    private static readonly TimeSpan RingTime = TimeSpan.FromSeconds(1.6);

    private Grid? _root;
    private Grid? _trackArea;
    private Border? _track;
    private Border? _fill;
    private Grid? _waveHost;
    private Microsoft.UI.Xaml.Shapes.Path? _wave;
    private Microsoft.UI.Xaml.Shapes.Path? _under;
    private Microsoft.UI.Xaml.Shapes.Path? _restDots;
    private Border? _shine;
    private Border? _ring;
    private Border? _thumb;
    private Visual? _thumbVisual;
    private Visual? _waveVisual;
    private Visual? _underVisual;
    private Visual? _shineVisual;
    private Visual? _ringVisual;
    private InsetClip? _fillClip;
    private InsetClip? _waveClip;
    private InsetClip? _trackClip;
    private bool _pointerOver;
    private double _waveWidth;
    private ProgressStyle? _waveStyle;
    private ProgressStyle? _rolling;
    private double _shineTrack = -1;
    private bool _ringing;
    private double _thumbOpacity = -1;
    private bool _listening;

    public SeekBar()
    {
        SizeChanged += (_, _) => Refresh();

        // The handle takes the shape of the look's buttons.
        Loaded += (_, _) => ListenToTheme(true);
        Unloaded += (_, _) => ListenToTheme(false);
    }

    /// <summary>The user started dragging.</summary>
    public event EventHandler? DragStarted;

    /// <summary>The user let go; <see cref="RangeBase.Value"/> is where they let go.</summary>
    public event EventHandler? DragCompleted;

    public ProgressStyle BarStyle
    {
        get => (ProgressStyle)GetValue(BarStyleProperty);
        set => SetValue(BarStyleProperty, value);
    }

    /// <summary>The song is playing: the moving styles move (the value is moved on by the player).</summary>
    public bool IsAdvancing
    {
        get => (bool)GetValue(IsAdvancingProperty);
        set => SetValue(IsAdvancingProperty, value);
    }

    /// <summary>How much one notch of the mouse wheel changes the value (0 for none).</summary>
    public double WheelStep { get; set; }

    public bool IsDragging { get; private set; }

    /// <summary>How much the value changes from one screen pixel to the next (0 before the bar is laid out).</summary>
    public double ValuePerPixel
    {
        get
        {
            var pixels = TrackWidth * (XamlRoot?.RasterizationScale ?? 1) * App.Services.Theme.Scale;
            return pixels > 0 ? (Maximum - Minimum) / pixels : 0;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("Root") as Grid;
        _trackArea = GetTemplateChild("TrackArea") as Grid;
        _track = GetTemplateChild("Track") as Border;
        _fill = GetTemplateChild("Fill") as Border;
        _waveHost = GetTemplateChild("WaveHost") as Grid;
        _thumb = GetTemplateChild("Thumb") as Border;
        _wave = null;
        _under = null;
        _waveVisual = null;
        _underVisual = null;
        _shineVisual = null;
        _ringVisual = null;
        _waveWidth = -1;
        _waveStyle = null;
        _rolling = null;
        _shineTrack = -1;
        _ringing = false;

        if (_track is not null)
        {
            // Only the styles that draw a line hide the played part of the track (the line draws it instead).
            var visual = ElementCompositionPreview.GetElementVisual(_track);
            _trackClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _trackClip;

            // Dots: the ones still to come, in the track's colour.
            _restDots = Dotted(App.Services.Theme.GetBrush("ResonateTrackBrush"));
            _track.Child = new Canvas { Children = { _restDots } };
        }

        if (_fill is not null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_fill);
            _fillClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _fillClip;

            // Shimmer: a soft light that runs along the bar; the fill's clip shows it only on the played part.
            _shine = new Border
            {
                Width = ShineWidth,
                Opacity = 0,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops =
                    {
                        new GradientStop { Color = global::Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 0 },
                        new GradientStop { Color = global::Windows.UI.Color.FromArgb(150, 255, 255, 255), Offset = 0.5 },
                        new GradientStop { Color = global::Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 1 },
                    },
                },
            };
            _fill.Child = new Canvas { Children = { _shine } };
            ElementCompositionPreview.SetIsTranslationEnabled(_shine, true);
            _shineVisual = ElementCompositionPreview.GetElementVisual(_shine);
        }

        if (_waveHost is not null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_waveHost);
            _waveClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _waveClip;

            // Made here, not in the template: under native AOT a template part
            // can only be cast to element types the app itself uses, and a
            // Path from the template came back as a plain element.
            _wave = new Microsoft.UI.Xaml.Shapes.Path
            {
                Stroke = App.Services.Theme.GetBrush("ResonateAccentBrush"),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };

            // Liquid's second wave, fainter, behind the first.
            _under = new Microsoft.UI.Xaml.Shapes.Path
            {
                Stroke = App.Services.Theme.GetBrush("ResonateAccent2Brush"),
                StrokeThickness = 2,
                Opacity = 0.55,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };

            // A Canvas never clips, so the lines can scroll; WaveHost's clip shows the played part.
            _waveHost.Children.Add(new Canvas { Children = { _under, _wave } });
            ElementCompositionPreview.SetIsTranslationEnabled(_wave, true);
            ElementCompositionPreview.SetIsTranslationEnabled(_under, true);
            _waveVisual = ElementCompositionPreview.GetElementVisual(_wave);
            _underVisual = ElementCompositionPreview.GetElementVisual(_under);
        }

        if (_thumb is not null)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_thumb, true);
            _thumbVisual = ElementCompositionPreview.GetElementVisual(_thumb);

            // Ripple: a ring under the handle that grows and fades while playing.
            if (_root is not null)
            {
                _ring = new Border
                {
                    Width = RingSize,
                    Height = RingSize,
                    CornerRadius = new CornerRadius(RingSize / 2),
                    BorderThickness = new Thickness(2),
                    BorderBrush = App.Services.Theme.GetBrush("ResonateAccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Opacity = 0,
                };
                _root.Children.Insert(_root.Children.IndexOf(_thumb), _ring);
                ElementCompositionPreview.SetIsTranslationEnabled(_ring, true);
                _ringVisual = ElementCompositionPreview.GetElementVisual(_ring);
                _ringVisual.CenterPoint = new Vector3((float)RingSize / 2, (float)RingSize / 2, 0);
            }
        }

        _thumbOpacity = -1;
        UpdateLook();
        Animate();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RangeBaseAutomationPeer(this);

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        ShowPosition();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        Refresh();
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerOver = true;
        UpdateLook();
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = false;
        UpdateLook();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateLook();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        UpdateLook();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (CapturePointer(e.Pointer))
        {
            _ = Focus(FocusState.Pointer);
            IsDragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
            SetValueFrom(e);
            UpdateLook();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsDragging)
        {
            SetValueFrom(e);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsDragging)
        {
            SetValueFrom(e);

            // Ended before letting go, so the capture-lost event that follows
            // has nothing to end: one release sends one seek.
            EndDrag();
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (IsDragging)
        {
            EndDrag();
        }
    }

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (WheelStep > 0)
        {
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            Value = Math.Clamp(Value + (Math.Sign(delta) * WheelStep), Minimum, Maximum);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        double? target = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Down => Value - SmallChange,
            VirtualKey.Right or VirtualKey.Up => Value + SmallChange,
            VirtualKey.PageDown => Value - LargeChange,
            VirtualKey.PageUp => Value + LargeChange,
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            _ => null,
        };

        if (target is { } value)
        {
            Value = Math.Clamp(value, Minimum, Maximum);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private double Ratio => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private double TrackWidth => _trackArea?.ActualWidth ?? 0;

    // The handle is centred on the position (moved, not laid out, so it is never clipped at the ends).
    private float ThumbOffset => (float)(_thumb?.Width ?? 0) / 2;

    private void SetValueFrom(PointerRoutedEventArgs e)
    {
        if (_trackArea is null || TrackWidth <= 0)
        {
            return;
        }

        var x = e.GetCurrentPoint(_trackArea).Position.X;
        Value = Minimum + (Math.Clamp(x / TrackWidth, 0, 1) * (Maximum - Minimum));
    }

    /// <summary>
    /// Ends a drag without <see cref="DragCompleted"/>, such as when the song
    /// changes under the pointer: a place in the old song means nothing in
    /// the new one. The owner shows the real position again.
    /// </summary>
    public void CancelDrag()
    {
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        ReleasePointerCaptures();
        UpdateLook();
        Refresh();
    }

    private void EndDrag()
    {
        IsDragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
        UpdateLook();
        Refresh();
    }

    private void ListenToTheme(bool listen)
    {
        if (listen == _listening)
        {
            return;
        }

        _listening = listen;
        if (listen)
        {
            App.Services.Theme.Changed += OnThemeChanged;
            App.Services.Theme.AnimationsChanged += OnAnimationsChanged;
            UpdateLook();
            Animate();
        }
        else
        {
            App.Services.Theme.Changed -= OnThemeChanged;
            App.Services.Theme.AnimationsChanged -= OnAnimationsChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateLook();

    private void OnAnimationsChanged(object? sender, EventArgs e) => Animate();

    private void OnAdvancingChanged() => Animate();

    private void OnBarStyleChanged()
    {
        UpdateLook();
        Refresh();
        Animate();
    }

    /// <summary>The style draws the played part as a line in WaveHost (the waves, the heartbeat, the dots).</summary>
    private bool DrawsLine => ProgressPatterns.Rolls(BarStyle);

    /// <summary>Redraws for a new size, value range or play state.</summary>
    private void Refresh()
    {
        if (DrawsLine && (Math.Abs(_waveWidth - TrackWidth) > 0.5 || _waveStyle != BarStyle))
        {
            BuildWave();
        }

        if (BarStyle == ProgressStyle.Shimmer && Math.Abs(_shineTrack - TrackWidth) > 0.5)
        {
            AnimateShine();
        }

        ShowPosition();
    }

    private void ShowPosition()
    {
        var width = (float)TrackWidth;
        var inset = width * (float)(1 - Ratio);
        _fillClip?.RightInset = inset;
        _waveClip?.RightInset = inset;
        _trackClip?.LeftInset = DrawsLine ? width - inset : 0;
        _thumbVisual?.Properties.InsertVector3("Translation", new Vector3(width - inset - ThumbOffset, 0, 0));
        _ringVisual?.Properties.InsertVector3("Translation", new Vector3(width - inset - ((float)RingSize / 2), 0, 0));
    }

    /// <summary>Sizes and colours for the bar style, and whether the handle shows.</summary>
    private void UpdateLook()
    {
        if (_trackArea is null || _track is null || _fill is null || _thumb is null || _waveHost is null)
        {
            return;
        }

        var style = BarStyle;
        var active = _pointerOver || IsDragging || FocusState == FocusState.Keyboard;
        var (height, activeHeight, thumb, thumbAlways) = style switch
        {
            ProgressStyle.Bold => (6.0, 8.0, 14.0, true),
            ProgressStyle.Gradient => (5.0, 7.0, 14.0, false),
            ProgressStyle.Shimmer => (6.0, 8.0, 14.0, false),
            ProgressStyle.Wave or ProgressStyle.Liquid or ProgressStyle.Heartbeat => (3.0, 3.0, 0.0, true),
            ProgressStyle.Dots => (4.0, 4.0, 10.0, true),
            ProgressStyle.Ripple => (4.0, 6.0, 12.0, true),
            ProgressStyle.Minimal => (2.0, 2.0, 0.0, false),
            _ => (4.0, 6.0, 12.0, false),
        };

        _trackArea.Height = active ? activeHeight : height;
        var round = style == ProgressStyle.Minimal ? 0 : _trackArea.Height / 2;
        _track.CornerRadius = new CornerRadius(round);
        _fill.CornerRadius = new CornerRadius(round);
        _fill.Background = App.Services.Theme.GetBrush(style is ProgressStyle.Gradient or ProgressStyle.Shimmer ? "ResonateAccentGradientBrush" : "ResonateAccentBrush");
        _shine?.Height = _trackArea.Height;

        // Dots draw the track as dots too; the other styles as a plain bar.
        var dots = style == ProgressStyle.Dots;
        _track.Background = dots ? null : App.Services.Theme.GetBrush("ResonateTrackBrush");
        _restDots?.Visibility = dots ? Visibility.Visible : Visibility.Collapsed;

        var line = DrawsLine;
        _waveHost.Visibility = line ? Visibility.Visible : Visibility.Collapsed;
        _fill.Visibility = line ? Visibility.Collapsed : Visibility.Visible;
        if (line && (Math.Abs(_waveWidth - TrackWidth) > 0.5 || _waveStyle != style))
        {
            BuildWave();
        }

        // The handle: a dot (or a bar for the lines), shaped like the theme's buttons.
        var buttonCorner = App.Services.Theme.Palette.CornerButton;
        var wave = line && !dots;
        if (wave)
        {
            _thumb.Width = 4;
            _thumb.Height = 16;
            _thumb.CornerRadius = new CornerRadius(Math.Min(buttonCorner, 2));
            _thumb.BorderThickness = new Thickness(0);
            _thumb.Background = App.Services.Theme.GetBrush("ResonateAccentBrush");
        }
        else
        {
            _thumb.Width = thumb;
            _thumb.Height = thumb;
            _thumb.CornerRadius = new CornerRadius(Math.Min(buttonCorner, thumb / 2));
            _thumb.Background = App.Services.Theme.GetBrush(style == ProgressStyle.Gradient ? "ResonateTextPrimaryBrush" : "ResonateAccentBrush");
            _thumb.BorderBrush = App.Services.Theme.GetBrush("ResonateAccentBrush");
            _thumb.BorderThickness = new Thickness(style == ProgressStyle.Gradient ? 3 : 0);
        }

        ShowThumb(thumb > 0 || wave ? (thumbAlways || active ? 1 : 0) : 0);
    }

    private void ShowThumb(double opacity)
    {
        if (_thumbVisual is null || Math.Abs(opacity - _thumbOpacity) < 0.01)
        {
            return;
        }

        var first = _thumbOpacity < 0;
        _thumbOpacity = opacity;
        if (first || !App.Services.Theme.AnimationsEnabled)
        {
            _thumbVisual.Opacity = (float)opacity;
            return;
        }

        var fade = _thumbVisual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, (float)opacity);
        fade.Duration = TimeSpan.FromMilliseconds(140);
        _thumbVisual.StartAnimation("Opacity", fade);
    }

    /// <summary>A dotted line in <paramref name="brush"/>: dots 4 px across, <see cref="ProgressPatterns.DotSpacing"/> apart.</summary>
    private static Microsoft.UI.Xaml.Shapes.Path Dotted(Brush brush) => new()
    {
        Stroke = brush,
        StrokeThickness = 4,
        StrokeDashArray = [0, ProgressPatterns.DotSpacing / 4],
        StrokeDashCap = PenLineCap.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// The played part's line, one period wider than the bar so it can roll
    /// without a gap: a wave (Liquid adds a second under it), a heartbeat, or
    /// dots (and the dots still to come, in the track).
    /// </summary>
    private void BuildWave()
    {
        if (_wave is null || _under is null || _waveHost is null)
        {
            return;
        }

        var style = BarStyle;
        _waveWidth = TrackWidth;
        _waveStyle = style;
        var middle = _waveHost.Height / 2;
        var dots = style == ProgressStyle.Dots;
        _wave.StrokeThickness = dots ? 4 : style == ProgressStyle.Heartbeat ? 2.5 : 3;
        _wave.StrokeDashArray = dots ? [0, ProgressPatterns.DotSpacing / 4] : [];
        _wave.StrokeDashCap = PenLineCap.Round;
        if (dots)
        {
            _wave.Data = Straight(middle, _waveWidth + ProgressPatterns.DotSpacing);
            _restDots?.Data = Straight((_trackArea?.Height ?? 4) / 2, _waveWidth);
        }
        else
        {
            _wave.Data = Polyline(ProgressPatterns.Line(style, _waveWidth + ProgressPatterns.Period(style)), middle);
        }

        var liquid = style == ProgressStyle.Liquid;
        _under.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
        if (liquid)
        {
            _under.Data = Polyline(ProgressPatterns.Line(style, _waveWidth + ProgressPatterns.UnderLength, under: true), middle);
        }
    }

    private static PathGeometry Straight(double y, double width)
    {
        var figure = new PathFigure { StartPoint = new Point(0, y), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new LineSegment { Point = new Point(Math.Max(width, 0), y) });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry Polyline(IReadOnlyList<(double X, double Height)> line, double middle)
    {
        var points = new PointCollection();
        foreach (var (x, height) in line)
        {
            points.Add(new Point(x, middle - height));
        }

        var figure = new PathFigure { StartPoint = points.Count > 0 ? points[0] : default, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment { Points = points });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>Every moving style: moving while playing, and standing still where it is when paused.</summary>
    private void Animate()
    {
        AnimateRoll();
        AnimateShine();
        AnimateRing();
    }

    private static bool MayMove => App.Services.Theme.AnimationsEnabled;

    /// <summary>
    /// The lines roll one period at a time, for ever, from the first time the
    /// style shows; a pause holds them where they are (not flattened or put
    /// back), and playing on carries on from there.
    /// </summary>
    private void AnimateRoll()
    {
        if (_waveVisual is null || _underVisual is null)
        {
            return;
        }

        var style = BarStyle;
        if (!DrawsLine || !MayMove)
        {
            if (_rolling is not null)
            {
                Stop(_waveVisual);
                Stop(_underVisual);
                _rolling = null;
            }

            return;
        }

        if (_rolling != style)
        {
            Roll(_waveVisual, -ProgressPatterns.Period(style), ProgressPatterns.RollTime(style));
            if (style == ProgressStyle.Liquid)
            {
                // The second wave starts a period to the left and rolls right, through the first.
                Roll(_underVisual, ProgressPatterns.UnderLength, ProgressPatterns.UnderRollTime, -ProgressPatterns.UnderLength);
            }
            else
            {
                Stop(_underVisual);
            }

            _rolling = style;
        }

        Hold(_waveVisual, "Translation", !IsAdvancing);
        if (style == ProgressStyle.Liquid)
        {
            Hold(_underVisual, "Translation", !IsAdvancing);
        }
    }

    private static void Roll(Visual visual, double by, TimeSpan time, double from = 0)
    {
        var compositor = visual.Compositor;
        var roll = compositor.CreateVector3KeyFrameAnimation();
        roll.InsertKeyFrame(0, new Vector3((float)from, 0, 0));
        roll.InsertKeyFrame(1, new Vector3((float)(from + by), 0, 0), compositor.CreateLinearEasingFunction());
        roll.Duration = time;
        roll.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", roll);
    }

    private static void Stop(Visual visual)
    {
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    /// <summary>Holds an animation where it is, or lets it carry on from there.</summary>
    private static void Hold(CompositionObject target, string property, bool hold)
    {
        if (target.TryGetAnimationController(property) is { } controller)
        {
            if (hold)
            {
                controller.Pause();
            }
            else
            {
                controller.Resume();
            }
        }
    }

    /// <summary>
    /// Shimmer: a light runs along the bar, then rests, again and again while
    /// playing; on pause it holds and fades, and playing on carries on.
    /// </summary>
    private void AnimateShine()
    {
        if (_shineVisual is null || _shine is null)
        {
            return;
        }

        var on = BarStyle == ProgressStyle.Shimmer && MayMove && TrackWidth > 0;
        if (!on)
        {
            if (_shineTrack >= 0)
            {
                _shineVisual.StopAnimation("Translation");
                _shineVisual.StopAnimation("Opacity");
                _shineTrack = -1;
            }

            _shine.Opacity = 0;
            return;
        }

        var compositor = _shineVisual.Compositor;
        if (Math.Abs(_shineTrack - TrackWidth) > 0.5)
        {
            _shineTrack = TrackWidth;
            var whole = ShinePass + ShineRest;
            var pass = compositor.CreateVector3KeyFrameAnimation();
            pass.InsertKeyFrame(0, new Vector3((float)-ShineWidth, 0, 0));
            pass.InsertKeyFrame((float)(ShinePass / whole), new Vector3((float)TrackWidth, 0, 0), compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(0.6f, 1)));
            pass.InsertKeyFrame(1, new Vector3((float)TrackWidth, 0, 0));
            pass.Duration = whole;
            pass.IterationBehavior = AnimationIterationBehavior.Forever;
            _shineVisual.StartAnimation("Translation", pass);
        }

        Hold(_shineVisual, "Translation", !IsAdvancing);
        _shine.Opacity = 1;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, IsAdvancing ? 1 : 0);
        fade.Duration = TimeSpan.FromMilliseconds(240);
        _shineVisual.StartAnimation("Opacity", fade);
    }

    /// <summary>Ripple: rings grow from the handle and fade while playing; on pause the last one fades out.</summary>
    private void AnimateRing()
    {
        if (_ringVisual is null || _ring is null)
        {
            return;
        }

        var on = BarStyle == ProgressStyle.Ripple && MayMove && IsAdvancing;
        if (on == _ringing)
        {
            return;
        }

        _ringing = on;
        var compositor = _ringVisual.Compositor;
        _ring.Opacity = 1;
        if (on)
        {
            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, Vector3.One);
            grow.InsertKeyFrame(1, new Vector3(RingGrowth, RingGrowth, 1), compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.6f), new Vector2(0.4f, 1)));
            grow.Duration = RingTime;
            grow.IterationBehavior = AnimationIterationBehavior.Forever;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0.7f);
            fade.InsertKeyFrame(1, 0, compositor.CreateLinearEasingFunction());
            fade.Duration = RingTime;
            fade.IterationBehavior = AnimationIterationBehavior.Forever;
            _ringVisual.StartAnimation("Scale", grow);
            _ringVisual.StartAnimation("Opacity", fade);
        }
        else
        {
            _ringVisual.StopAnimation("Scale");
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, 0);
            fade.Duration = TimeSpan.FromMilliseconds(300);
            _ringVisual.StartAnimation("Opacity", fade);
        }
    }
}
