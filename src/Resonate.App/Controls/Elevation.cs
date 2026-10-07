using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Themes;
using Resonate.Themes;

namespace Resonate.App.Controls;

/// <summary>How much a piece of the interface stands out from what is behind it.</summary>
public enum ElevationLevel
{
    /// <summary>No shadow.</summary>
    Flat,

    /// <summary>A large panel (the sidebar, the page, a floating player).</summary>
    Panel,

    /// <summary>A small item (a cover, the play button).</summary>
    Item,

    /// <summary>The player bar: a panel's shadow when it floats, none when it is docked.</summary>
    Player,
}

/// <summary>
/// Draws the theme's shadow behind its content: a soft or deep drop shadow,
/// a glow in the accent colour, or a hard offset shadow. The blurred kinds
/// are drawn by the compositor with the content's rounded shape; the glow's
/// colour slides when the accent changes.
/// </summary>
public sealed partial class Elevation : ContentControl
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(ElevationLevel),
        typeof(Elevation),
        new PropertyMetadata(ElevationLevel.Panel, (d, _) => ((Elevation)d).UpdateShadow(animate: false)));

    private Border? _shadowHost;
    private Border? _hardShadow;
    private TranslateTransform? _hardShadowOffset;
    private ShadowVisual? _shadow;
    private bool _listening;

    public Elevation()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateShadow(animate: false);
    }

    public ElevationLevel Level
    {
        get => (ElevationLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadowHost = GetTemplateChild("ShadowHost") as Border;
        _hardShadow = GetTemplateChild("HardShadow") as Border;
        _hardShadowOffset = _hardShadow?.RenderTransform as TranslateTransform;
        _shadow?.Dispose();
        _shadow = null;
        UpdateShadow(animate: false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_listening)
        {
            _listening = true;
            App.Services.Theme.Changed += OnThemeChanged;
        }

        UpdateShadow(animate: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_listening)
        {
            _listening = false;
            App.Services.Theme.Changed -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateShadow(animate: true);

    private void UpdateShadow(bool animate)
    {
        if (_shadowHost is null || App.Services is null)
        {
            return;
        }

        var theme = App.Services.Theme;
        var palette = theme.Palette;
        var spec = Level switch
        {
            ElevationLevel.Panel => palette.PanelShadow,
            ElevationLevel.Item => palette.ItemShadow,
            ElevationLevel.Player when theme.Current.PlayerLayout == PlayerLayout.Floating => palette.PanelShadow,
            _ => ShadowSpec.None,
        };

        // A hard shadow is a solid copy of the shape, offset; it needs no blur.
        var hard = theme.Current.Shadow == ShadowStyle.Hard && spec.IsVisible;
        if (_hardShadow is not null)
        {
            _hardShadow.Visibility = hard ? Visibility.Visible : Visibility.Collapsed;
            if (_hardShadowOffset is not null)
            {
                _hardShadowOffset.X = spec.OffsetX;
                _hardShadowOffset.Y = spec.OffsetY;
            }
        }

        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        if (hard || !spec.IsVisible || size.X < 1 || size.Y < 1)
        {
            _shadow?.Hide();
            return;
        }

        _shadow ??= new ShadowVisual(_shadowHost);
        var radius = (float)Math.Min(CornerRadius.TopLeft, Math.Min(size.X, size.Y) / 2);
        _shadow.Show(size, radius, spec, animate && theme.AnimationsEnabled);
    }

    /// <summary>
    /// A shadow with no shape of its own: a drop shadow whose outline comes
    /// from a rounded rectangle mask, so it can sit behind translucent panels.
    /// </summary>
    private sealed class ShadowVisual : IDisposable
    {
        private readonly Compositor _compositor;
        private readonly SpriteVisual _sprite;
        private readonly DropShadow _shadow;
        private readonly CompositionRoundedRectangleGeometry _shape;
        private readonly ShapeVisual _shapeVisual;
        private readonly CompositionVisualSurface _surface;
        private readonly UIElement _host;

        public ShadowVisual(UIElement host)
        {
            _host = host;
            _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

            _shape = _compositor.CreateRoundedRectangleGeometry();
            var fill = _compositor.CreateSpriteShape(_shape);
            fill.FillBrush = _compositor.CreateColorBrush(Microsoft.UI.Colors.Black);
            _shapeVisual = _compositor.CreateShapeVisual();
            _shapeVisual.Shapes.Add(fill);

            _surface = _compositor.CreateVisualSurface();
            _surface.SourceVisual = _shapeVisual;

            _shadow = _compositor.CreateDropShadow();
            _shadow.Mask = _compositor.CreateSurfaceBrush(_surface);

            _sprite = _compositor.CreateSpriteVisual();
            _sprite.Shadow = _shadow;
            ElementCompositionPreview.SetElementChildVisual(host, _sprite);
        }

        public void Show(Vector2 size, float radius, ShadowSpec spec, bool animate)
        {
            _shape.Size = size;
            _shape.CornerRadius = new Vector2(radius);
            _shapeVisual.Size = size;
            _surface.SourceSize = size;
            _sprite.Size = size;

            _shadow.BlurRadius = (float)spec.BlurRadius;
            _shadow.Offset = new Vector3((float)spec.OffsetX, (float)spec.OffsetY, 0);
            _shadow.Opacity = (float)spec.Color.Opacity;

            var color = spec.Color.Opaque.ToColor();
            if (animate && _sprite.IsVisible && _shadow.Color != color)
            {
                var slide = _compositor.CreateColorKeyFrameAnimation();
                slide.InsertKeyFrame(1, color);
                slide.Duration = TimeSpan.FromMilliseconds(520);
                _shadow.StartAnimation("Color", slide);
            }
            else
            {
                _shadow.Color = color;
            }

            _sprite.IsVisible = true;
        }

        public void Hide() => _sprite.IsVisible = false;

        public void Dispose()
        {
            ElementCompositionPreview.SetElementChildVisual(_host, null);
            _sprite.Dispose();
            _shadow.Dispose();
            _surface.Dispose();
            _shapeVisual.Dispose();
            _shape.Dispose();
        }
    }
}
