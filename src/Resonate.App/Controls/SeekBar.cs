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
/// bold bar, a glowing gradient, a moving wave, or a hairline. The bar shows
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

    private const double Wavelength = 22;
    private const double WaveAmplitude = 3.2;

    private Grid? _trackArea;
    private Border? _track;
    private Border? _fill;
    private Grid? _waveHost;
    private Microsoft.UI.Xaml.Shapes.Path? _wave;
    private Border? _thumb;
    private Visual? _thumbVisual;
    private Visual? _waveVisual;
    private InsetClip? _fillClip;
    private InsetClip? _waveClip;
    private InsetClip? _trackClip;
    private bool _pointerOver;
    private double _waveWidth;
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

    /// <summary>The song is playing: the wave style rolls (the value is moved on by the player).</summary>
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
        _trackArea = GetTemplateChild("TrackArea") as Grid;
        _track = GetTemplateChild("Track") as Border;
        _fill = GetTemplateChild("Fill") as Border;
        _waveHost = GetTemplateChild("WaveHost") as Grid;
        _thumb = GetTemplateChild("Thumb") as Border;
        _wave = null;
        _waveVisual = null;
        _waveWidth = -1;

        if (_track is not null)
        {
            // Only the wave style hides the played part of the track (the wave draws it instead).
            var visual = ElementCompositionPreview.GetElementVisual(_track);
            _trackClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _trackClip;
        }

        if (_fill is not null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_fill);
            _fillClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _fillClip;
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

            // A Canvas never clips, so the wave can scroll; WaveHost's clip shows the played part.
            _waveHost.Children.Add(new Canvas { Children = { _wave } });
            ElementCompositionPreview.SetIsTranslationEnabled(_wave, true);
            _waveVisual = ElementCompositionPreview.GetElementVisual(_wave);
        }

        if (_thumb is not null)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_thumb, true);
            _thumbVisual = ElementCompositionPreview.GetElementVisual(_thumb);
        }

        _thumbOpacity = -1;
        UpdateLook();
        AnimateWave();
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
            ReleasePointerCapture(e.Pointer);
            EndDrag();
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
            UpdateLook();
        }
        else
        {
            App.Services.Theme.Changed -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateLook();

    private void OnAdvancingChanged() => AnimateWave();

    private void OnBarStyleChanged()
    {
        UpdateLook();
        Refresh();
        AnimateWave();
    }

    /// <summary>Redraws for a new size, value range or play state.</summary>
    private void Refresh()
    {
        if (BarStyle == ProgressStyle.Wave && Math.Abs(_waveWidth - TrackWidth) > 0.5)
        {
            BuildWave();
        }

        ShowPosition();
    }

    private void ShowPosition()
    {
        var width = (float)TrackWidth;
        var inset = width * (float)(1 - Ratio);
        _fillClip?.RightInset = inset;
        _waveClip?.RightInset = inset;
        _trackClip?.LeftInset = BarStyle == ProgressStyle.Wave ? width - inset : 0;
        _thumbVisual?.Properties.InsertVector3("Translation", new Vector3(width - inset - ThumbOffset, 0, 0));
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
            ProgressStyle.Wave => (3.0, 3.0, 0.0, true),
            ProgressStyle.Minimal => (2.0, 2.0, 0.0, false),
            _ => (4.0, 6.0, 12.0, false),
        };

        _trackArea.Height = active ? activeHeight : height;
        var round = style == ProgressStyle.Minimal ? 0 : _trackArea.Height / 2;
        _track.CornerRadius = new CornerRadius(round);
        _fill.CornerRadius = new CornerRadius(round);
        _fill.Background = App.Services.Theme.GetBrush(style == ProgressStyle.Gradient ? "ResonateAccentGradientBrush" : "ResonateAccentBrush");

        var wave = style == ProgressStyle.Wave;
        _waveHost.Visibility = wave ? Visibility.Visible : Visibility.Collapsed;
        _fill.Visibility = wave ? Visibility.Collapsed : Visibility.Visible;
        if (wave && Math.Abs(_waveWidth - TrackWidth) > 0.5)
        {
            BuildWave();
        }

        // The handle: a dot (or a bar for the wave), shaped like the theme's buttons.
        var buttonCorner = App.Services.Theme.Palette.CornerButton;
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

    /// <summary>A sine wave one wavelength wider than the bar, so it can scroll without a gap.</summary>
    private void BuildWave()
    {
        if (_wave is null || _waveHost is null)
        {
            return;
        }

        _waveWidth = TrackWidth;
        var middle = _waveHost.Height / 2;
        var points = new PointCollection();
        for (double x = 0; x <= _waveWidth + Wavelength; x += 1.5)
        {
            points.Add(new Point(x, middle - (Math.Sin(x / Wavelength * Math.PI * 2) * WaveAmplitude)));
        }

        var figure = new PathFigure { StartPoint = points.Count > 0 ? points[0] : default, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment { Points = points });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _wave.Data = geometry;
        _waveVisual?.CenterPoint = new Vector3(0, (float)middle, 0);
    }

    /// <summary>The wave rolls while playing and flattens when paused.</summary>
    private void AnimateWave()
    {
        if (_waveVisual is null)
        {
            return;
        }

        var compositor = _waveVisual.Compositor;
        var rolling = BarStyle == ProgressStyle.Wave && IsAdvancing && App.Services.Theme.AnimationsEnabled;
        if (rolling)
        {
            var roll = compositor.CreateVector3KeyFrameAnimation();
            roll.InsertKeyFrame(0, Vector3.Zero);
            roll.InsertKeyFrame(1, new Vector3((float)-Wavelength, 0, 0), compositor.CreateLinearEasingFunction());
            roll.Duration = TimeSpan.FromMilliseconds(1100);
            roll.IterationBehavior = AnimationIterationBehavior.Forever;
            _waveVisual.StartAnimation("Translation", roll);
        }
        else
        {
            _waveVisual.StopAnimation("Translation");
        }

        var amplitude = compositor.CreateVector3KeyFrameAnimation();
        amplitude.InsertKeyFrame(1, new Vector3(1, IsAdvancing ? 1 : 0.18f, 1));
        amplitude.Duration = TimeSpan.FromMilliseconds(320);
        _waveVisual.StartAnimation("Scale", amplitude);
    }
}
