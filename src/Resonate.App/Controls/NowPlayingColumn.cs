using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App.Controls;

/// <summary>
/// The Column window shape (Window shapes plugin): the song that plays, its
/// cover as large as the window allows, its title and artists, over the
/// player. Built in code; it follows the player only while it is shown.
/// </summary>
internal sealed partial class NowPlayingColumn : Grid
{
    private const int CoverPixels = 480;
    private const double CoverMaxSize = 560;

    private readonly AppServices _services;
    private readonly Grid _coverHolder = new();
    private readonly Border _cover = new();
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly TextBlock _title = new();
    private readonly TextBlock _artists = new();
    private object? _artworkKey;
    private int _queued;

    public NowPlayingColumn(AppServices services)
    {
        _services = services;
        var resources = Application.Current.Resources;
        Padding = new Thickness(24, 24, 24, 20);
        RowSpacing = 6;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _image.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        _cover.Child = _image;
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = VerticalAlignment.Center;
        _coverHolder.Margin = new Thickness(0, 0, 0, 14);
        _coverHolder.Children.Add(_cover);
        _coverHolder.SizeChanged += (_, e) => SizeCover(e.NewSize.Width, e.NewSize.Height);
        Children.Add(_coverHolder);

        _title.Style = (Style)resources["ResonateSectionTextStyle"];
        _title.TextAlignment = TextAlignment.Center;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.TextWrapping = TextWrapping.Wrap;
        _title.MaxLines = 2;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(_title, 1);
        Children.Add(_title);

        _artists.Style = (Style)resources["ResonateSecondaryTextStyle"];
        _artists.TextAlignment = TextAlignment.Center;
        _artists.HorizontalAlignment = HorizontalAlignment.Center;
        _artists.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(_artists, 2);
        Children.Add(_artists);

        Loaded += (_, _) =>
        {
            _services.Player.StateChanged += OnStateChanged;
            _services.Theme.Changed += OnThemeChanged;
            ApplyLook();
            Show(_services.Player.State);
        };
        Unloaded += (_, _) =>
        {
            _services.Player.StateChanged -= OnStateChanged;
            _services.Theme.Changed -= OnThemeChanged;
        };
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyLook();

    /// <summary>A panel like the page's, in the look's corners and outline.</summary>
    private void ApplyLook()
    {
        var theme = _services.Theme;
        var palette = theme.Palette;
        Background = theme.GetBrush("ResonateSurfaceBrush");
        BorderBrush = theme.GetBrush("ResonateBorderBrush");
        BorderThickness = new Thickness(palette.BorderWidth);
        CornerRadius = new CornerRadius(palette.CornerLarge);
        _cover.CornerRadius = new CornerRadius(theme.CoverIsRecord ? CoverMaxSize : palette.CornerMedium);
    }

    /// <summary>The cover is square, as large as its room allows.</summary>
    private void SizeCover(double width, double height)
    {
        var size = Math.Max(0, Math.Min(Math.Min(width, height), CoverMaxSize));
        _cover.Width = size;
        _cover.Height = size;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Arrives on any thread; the newest state is drawn once.
        if (Interlocked.Exchange(ref _queued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _queued, 0);
                Show(_services.Player.State);
            });
        }
    }

    private void Show(PlayerState state)
    {
        _title.Text = state.Title ?? "Nothing playing";
        _artists.Text = state.Artists ?? "Pick a song to start";

        object key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? "tile:" + (state.Album ?? state.Title);
        if (Equals(key, _artworkKey))
        {
            return;
        }

        _artworkKey = key;
        _image.Opacity = 0;
        var name = state.Album ?? state.Title;
        _cover.Background = name is null ? _services.Theme.GetBrush("ResonateSurfaceHoverBrush") : Artwork.PlaceholderBrush(name);
        if (state.ArtworkUrl is { } url)
        {
            _ = ShowCoverAsync(key, _services.Covers.GetReadyAsync(url, CoverPixels));
        }
        else if (state.ArtworkBytes is { } bytes)
        {
            _ = ShowCoverAsync(key, CoverImages.FromBytesAsync(bytes, CoverPixels));
        }
        else
        {
            _image.Source = null;
        }
    }

    private async Task ShowCoverAsync(object key, Task<(ImageSource? Image, bool Loaded)> loading)
    {
        var (image, _) = await loading;
        if (!Equals(key, _artworkKey))
        {
            return;
        }

        _image.Source = image;
        _image.Opacity = image is null ? 0 : 1;
    }
}
