using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;

namespace Resonate.App.Controls;

/// <summary>
/// An artist's orbit (the built-in plugin Artist orbit): the artist in the
/// middle, the albums of theirs the user likes on an inner ring, and the
/// artists who keep them company on an outer ring, joined to the middle by
/// lines as strong as the bond. Fixed geometry on two ellipses that use the
/// page's width; no physics. When it appears, the rings bloom out of the
/// middle on composition springs (once; nothing moves afterwards), and a
/// satellite grows a little under the pointer. Still when Windows'
/// animations are off. Built in code, so Native AOT never looks up a
/// XAML-created type.
/// </summary>
internal sealed partial class ArtistOrbitPanel : Grid
{
    private const double Widest = 960;
    private const double Tallest = 520;

    /// <summary>Narrower than this, the orbit gets smaller pictures.</summary>
    private const double SmallWidth = 600;

    /// <summary>Narrower than this, the inner ring of albums is left out (it would touch the outer one).</summary>
    private const double AlbumsWidth = 400;

    /// <summary>The middle picture's size on a wide page, for loading it before the first layout.</summary>
    private const int CentreSize = 124;

    private readonly AppServices _services;
    private readonly Canvas _canvas = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Ellipse _outerRing = new() { StrokeThickness = 1, StrokeDashArray = [2, 4] };
    private readonly Ellipse _innerRing = new() { StrokeThickness = 1 };
    private readonly Ellipse _halo = new() { StrokeThickness = 2 };
    private readonly Grid _centre = new();
    private readonly TextBlock _centreInitials = new();
    private readonly Image _centreImage = new() { Stretch = Stretch.UniformToFill };
    private readonly List<OrbitNode> _companions = [];
    private readonly List<OrbitNode> _albums = [];
    private readonly List<Line> _spokes = [];
    private double _width;
    private OrbitShape _shape;
    private bool _bloomPending;

    public ArtistOrbitPanel(AppServices services)
    {
        _services = services;
        var border = services.Theme.GetBrush("ResonateBorderBrush");
        _outerRing.Stroke = border;
        _innerRing.Stroke = border;
        _halo.Stroke = services.Theme.GetBrush("ResonateAccentSoftBrush");
        _centreInitials.HorizontalAlignment = HorizontalAlignment.Center;
        _centreInitials.VerticalAlignment = VerticalAlignment.Center;
        _centreInitials.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _centre.Children.Add(_centreInitials);
        _centre.Children.Add(_centreImage);
        _canvas.Children.Add(_outerRing);
        _canvas.Children.Add(_innerRing);
        _canvas.Children.Add(_halo);
        _canvas.Children.Add(_centre);
        Children.Add(_canvas);

        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width)
            {
                LayOut(e.NewSize.Width);
            }
        };
        Loaded += (_, _) => _services.Theme.Changed += OnThemeChanged;
        Unloaded += (_, _) => _services.Theme.Changed -= OnThemeChanged;
    }

    /// <summary>A companion was clicked: its artist ID.</summary>
    public event EventHandler<string>? ArtistClicked;

    /// <summary>An album on the inner ring was clicked: its ID.</summary>
    public event EventHandler<string>? AlbumClicked;

    /// <summary>The artist in the middle: their initials until the picture loads.</summary>
    public void SetCentre(string name, string? imageUrl)
    {
        _centre.Background = Artwork.PlaceholderBrush(name);
        _centreInitials.Foreground = Artwork.InitialsBrush(name);
        _centreInitials.Text = HomeText.Initials(name);
        _centreImage.Source = Artwork.FromUrl(imageUrl, CentreSize);
        AutomationProperties.SetName(_centre, name);
    }

    /// <summary>Lays out the orbit and lets it bloom out of the middle.</summary>
    public void Show(ArtistOrbitView view)
    {
        foreach (var node in _companions.Concat(_albums))
        {
            _canvas.Children.Remove(node);
        }

        foreach (var spoke in _spokes)
        {
            _canvas.Children.Remove(spoke);
        }

        _companions.Clear();
        _albums.Clear();
        _spokes.Clear();
        var strongest = view.Companions.Count > 0 ? view.Companions.Max(c => c.Strength) : 1;
        foreach (var companion in view.Companions)
        {
            var node = OrbitNode.Artist(companion, _services);
            node.Tapped += (_, _) => ArtistClicked?.Invoke(this, companion.Id);
            _companions.Add(node);

            // Behind the rings' satellites: the stronger the bond, the bolder the line.
            var spoke = new Line
            {
                Stroke = _services.Theme.GetBrush("ResonateBorderBrush"),
                StrokeThickness = 1 + (1.5 * companion.Strength / strongest),
                IsHitTestVisible = false,
            };
            _spokes.Add(spoke);
            _canvas.Children.Insert(0, spoke);
        }

        foreach (var album in view.Albums)
        {
            var node = OrbitNode.Album(album, _services);
            node.Tapped += (_, _) => AlbumClicked?.Invoke(this, album.Id);
            _albums.Add(node);
            _canvas.Children.Add(node);
        }

        foreach (var node in _companions)
        {
            _canvas.Children.Add(node);
        }

        // Blooms once the satellites have their places (at once when the width is known).
        _bloomPending = _services.Theme.AnimationsEnabled;
        LayOut(ActualWidth > 0 ? ActualWidth : _width);
    }

    /// <summary>A companion's picture, once it is known.</summary>
    public void SetPicture(string artistId, string? imageUrl)
    {
        foreach (var node in _companions)
        {
            if (node.Id == artistId)
            {
                node.SetPicture(imageUrl);
            }
        }
    }

    /// <summary>The sizes and the two ellipses for a width: wide pages get a wide orbit, narrow ones a smaller one.</summary>
    private static OrbitShape ShapeFor(double width)
    {
        var w = Math.Clamp(width, 280, Widest);
        var small = w < SmallWidth;

        // Tall enough that the outer ring's top and bottom clear the inner ring.
        var h = small ? 360 : Math.Clamp(w * 0.55, 460, Tallest);
        var centre = small ? 84.0 : CentreSize;
        var portrait = small ? 48.0 : 64.0;
        var label = small ? 84.0 : 112.0;
        var cover = small ? 36.0 : 52.0;

        // The outer ring keeps its satellites and their names inside; the inner ring clears the middle.
        var outerX = (w / 2) - (label / 2) - 4;
        var outerY = (h / 2) - (portrait / 2) - 26;
        var innerX = Math.Max((centre / 2) + (cover / 2) + 18, outerX * 0.5);
        var innerY = Math.Max((centre / 2) + (cover / 2) + 10, outerY * 0.55);
        return new OrbitShape(w, h, centre, portrait, label, cover, outerX, outerY, innerX, innerY, w >= AlbumsWidth);
    }

    private void LayOut(double width)
    {
        if (width <= 0)
        {
            return;
        }

        _width = width;
        var g = ShapeFor(width);
        _shape = g;
        _canvas.Width = g.Width;
        _canvas.Height = g.Height;
        var cx = g.Width / 2;
        var cy = g.Height / 2;

        Place(_outerRing, cx - g.OuterX, cy - g.OuterY, g.OuterX * 2, g.OuterY * 2);
        Place(_innerRing, cx - g.InnerX, cy - g.InnerY, g.InnerX * 2, g.InnerY * 2);
        _innerRing.Visibility = _albums.Count > 0 && g.ShowsAlbums ? Visibility.Visible : Visibility.Collapsed;
        Place(_halo, cx - (g.Centre / 2) - 6, cy - (g.Centre / 2) - 6, g.Centre + 12, g.Centre + 12);
        Place(_centre, cx - (g.Centre / 2), cy - (g.Centre / 2), g.Centre, g.Centre);
        _centre.CornerRadius = new CornerRadius(g.Centre / 2);
        _centreInitials.FontSize = g.Centre / 3;

        for (var i = 0; i < _companions.Count; i++)
        {
            var angle = (-Math.PI / 2) + (2 * Math.PI * i / _companions.Count);
            var x = cx + (g.OuterX * Math.Cos(angle));
            var y = cy + (g.OuterY * Math.Sin(angle));
            _companions[i].PlaceAt(x, y, g.Portrait, g.Label);
            var spoke = _spokes[i];
            spoke.X1 = cx;
            spoke.Y1 = cy;
            spoke.X2 = x;
            spoke.Y2 = y;
        }

        for (var i = 0; i < _albums.Count; i++)
        {
            // Half a step round from the outer ring, so covers and portraits do not line up.
            var angle = (-Math.PI / 2) + (Math.PI / _albums.Count) + (2 * Math.PI * i / _albums.Count);
            _albums[i].PlaceAt(cx + (g.InnerX * Math.Cos(angle)), cy + (g.InnerY * Math.Sin(angle)), g.Cover, 0);
            _albums[i].Visibility = g.ShowsAlbums ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateCorners();
        if (_bloomPending)
        {
            _bloomPending = false;
            Bloom();
        }
    }

    private static void Place(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = width;
        element.Height = height;
    }

    /// <summary>The satellites spring out from the middle, the albums first, each a moment after the one before.</summary>
    private void Bloom()
    {
        var cx = (float)(_shape.Width / 2);
        var cy = (float)(_shape.Height / 2);
        var delay = 0;
        foreach (var node in _albums.Concat(_companions))
        {
            node.Bloom(new Vector2(cx, cy), TimeSpan.FromMilliseconds(delay));
            delay += 28;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateCorners();

    private void UpdateCorners()
    {
        var corner = new CornerRadius(_services.Theme.Palette.CornerSmall);
        foreach (var album in _albums)
        {
            album.Face.CornerRadius = corner;
        }
    }

    private readonly record struct OrbitShape(
        double Width,
        double Height,
        double Centre,
        double Portrait,
        double Label,
        double Cover,
        double OuterX,
        double OuterY,
        double InnerX,
        double InnerY,
        bool ShowsAlbums);
}

/// <summary>
/// One satellite of an <see cref="ArtistOrbitPanel"/>: a round portrait with
/// the artist's name under it, or an album cover. Its own type, made with
/// <c>new</c>, so it is safe to recognise under Native AOT.
/// </summary>
internal sealed partial class OrbitNode : Grid
{
    private const float HoverGrow = 1.1f;

    private static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(180);

    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock? _initials;
    private readonly TextBlock? _name;
    private double _size;
    private bool _over;

    private OrbitNode(string id, string name, Brush placeholder, Brush? initialsBrush, string? imageUrl, bool round)
    {
        Id = id;
        IsRound = round;
        Background = App.Services.Theme.GetBrush("ResonateTransparentBrush");
        RowSpacing = 6;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Face = new Grid { Background = placeholder, HorizontalAlignment = HorizontalAlignment.Center };
        if (initialsBrush is not null)
        {
            _initials = new TextBlock
            {
                Text = HomeText.Initials(name),
                Foreground = initialsBrush,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Face.Children.Add(_initials);
        }

        Face.Children.Add(_image);
        Children.Add(Face);
        if (round)
        {
            _name = new TextBlock
            {
                Text = name,
                Style = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"],
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
            };
            SetRow(_name, 1);
            Children.Add(_name);
        }

        ImageUrl = imageUrl;
        PointerEntered += (_, _) => SetOver(true);
        PointerExited += (_, _) => SetOver(false);
        PointerCanceled += (_, _) => SetOver(false);
        PointerCaptureLost += (_, _) => SetOver(false);
    }

    public string Id { get; }

    public bool IsRound { get; }

    /// <summary>The picture: round for an artist, a rounded square for an album.</summary>
    public Grid Face { get; }

    private string? ImageUrl { get; set; }

    public static OrbitNode Artist(OrbitCompanion companion, AppServices services)
    {
        var node = new OrbitNode(companion.Id, companion.Name, Artwork.PlaceholderBrush(companion.Name), Artwork.InitialsBrush(companion.Name), services.Home.KnownArtistImage(companion.Id), round: true);
        ToolTipService.SetToolTip(node, companion.Name + "\n" + companion.Description);
        AutomationProperties.SetName(node, companion.Name + ", " + companion.Description.Replace('\n', ','));
        return node;
    }

    public static OrbitNode Album(OrbitAlbum album, AppServices services)
    {
        var liked = album.LikedSongs == 1 ? "1 liked song" : $"{album.LikedSongs} liked songs";
        var node = new OrbitNode(album.Id, album.Name, Artwork.PlaceholderBrush(album.Name), null, album.ImageUrl, round: false);
        ToolTipService.SetToolTip(node, album.Name + "\n" + liked);
        AutomationProperties.SetName(node, album.Name + ", " + liked);
        node.Face.CornerRadius = new CornerRadius(services.Theme.Palette.CornerSmall);
        return node;
    }

    public void SetPicture(string? imageUrl)
    {
        ImageUrl = imageUrl;
        if (_size > 0)
        {
            _image.Source = Artwork.FromUrl(imageUrl, (int)_size);
        }
    }

    /// <summary>Centres the picture on (<paramref name="x"/>, <paramref name="y"/>), with the name under it.</summary>
    public void PlaceAt(double x, double y, double size, double labelWidth)
    {
        var resized = Math.Abs(size - _size) > 0.5;
        _size = size;
        Face.Width = Face.Height = size;
        if (IsRound)
        {
            Face.CornerRadius = new CornerRadius(size / 2);
        }

        if (_initials is not null)
        {
            _initials.FontSize = Math.Round(size / 3);
        }

        var width = Math.Max(size, labelWidth);
        Width = width;
        if (_name is not null)
        {
            _name.MaxWidth = labelWidth;
        }

        Canvas.SetLeft(this, x - (width / 2));
        Canvas.SetTop(this, y - (size / 2));
        Face.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0);
        if (resized)
        {
            _image.Source = Artwork.FromUrl(ImageUrl, (int)size);
        }
    }

    /// <summary>Springs out from <paramref name="from"/> (in the orbit's coordinates) to where it sits.</summary>
    public void Bloom(Vector2 from, TimeSpan delay)
    {
        var left = (float)Canvas.GetLeft(this);
        var top = (float)Canvas.GetTop(this);
        var centre = new Vector2(left + (float)(Width / 2), top + (float)(_size / 2));
        var start = new Vector3(from - centre, 0);

        ElementCompositionPreview.SetIsTranslationEnabled(this, true);
        var visual = ElementCompositionPreview.GetElementVisual(this);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)(Width / 2), (float)(_size / 2), 0);

        var move = compositor.CreateSpringVector3Animation();
        move.InitialValue = start;
        move.FinalValue = Vector3.Zero;
        move.DampingRatio = 0.68f;
        move.Period = TimeSpan.FromMilliseconds(60);
        move.DelayTime = delay;
        move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Translation", move);

        var grow = compositor.CreateSpringVector3Animation();
        grow.InitialValue = new Vector3(0.3f, 0.3f, 1);
        grow.FinalValue = Vector3.One;
        grow.DampingRatio = 0.68f;
        grow.Period = TimeSpan.FromMilliseconds(60);
        grow.DelayTime = delay;
        grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Scale", grow);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);
    }

    private void SetOver(bool over)
    {
        if (over == _over)
        {
            return;
        }

        _over = over;
        if (App.Services.Theme.AnimationsEnabled)
        {
            Face.ScaleTransition ??= new Vector3Transition { Duration = HoverDuration };
            Face.Scale = over ? new Vector3(HoverGrow, HoverGrow, 1) : Vector3.One;
        }
        else if (Face.Scale != Vector3.One)
        {
            Face.ScaleTransition = null;
            Face.Scale = Vector3.One;
        }
    }
}
