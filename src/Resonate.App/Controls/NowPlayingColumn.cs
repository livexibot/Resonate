using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App.Controls;

/// <summary>
/// The song that plays: its cover, its title and artists. In the Column
/// window shape (Window shapes plugin) a panel of its own, the cover as large
/// as the window allows, over the player. Beside the page (Placement Left or
/// Right) the top of the column's panel: the cover as wide as the column (or
/// as tall as the room above the controls allows) and the title and artists
/// under it, lined up on the left, with the controls and Up next below (see
/// MainWindow.PlayerPlacement.cs). Built in code; it follows the player only
/// while it is shown.
/// </summary>
internal sealed partial class NowPlayingColumn : Grid
{
    private const int CoverPixels = 480;
    private const double CoverMaxSize = 560;

    /// <summary>The smallest cover beside the page, however short the window.</summary>
    private const double CoverMinSize = 72;

    private readonly AppServices _services;
    private readonly bool _beside;
    private readonly Grid _coverHolder = new();
    private readonly Elevation? _coverShadow;
    private readonly Border _cover = new();
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly TextBlock _title = new();
    private readonly TextBlock _artists = new();
    private object? _artworkKey;
    private int _queued;

    /// <summary>Beside the page: the height the cover and the words may take, above the controls.</summary>
    private double _room = double.PositiveInfinity;

    /// <summary>Beside the page, dragged narrow: the cover alone, the song in its tooltip.</summary>
    private bool _rail;

    public NowPlayingColumn(AppServices services, bool beside = false)
    {
        _services = services;
        _beside = beside;
        var resources = Application.Current.Resources;
        Padding = beside ? new Thickness(20, 20, 20, 10) : new Thickness(24, 24, 24, 20);
        RowSpacing = beside ? 2 : 6;
        RowDefinitions.Add(new RowDefinition { Height = beside ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _image.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        _cover.Child = _image;
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = beside ? VerticalAlignment.Top : VerticalAlignment.Center;
        _coverHolder.Margin = new Thickness(0, 0, 0, beside ? 18 : 14);
        if (beside)
        {
            // The look's shadow under the cover, as under the player's own.
            _coverShadow = new Elevation { Level = ElevationLevel.Item, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            _coverHolder.Children.Add(_coverShadow);
        }

        _coverHolder.Children.Add(_cover);
        _coverHolder.SizeChanged += (_, e) => SizeCover(e.NewSize.Width, e.NewSize.Height);
        Children.Add(_coverHolder);

        var alignment = beside ? TextAlignment.Left : TextAlignment.Center;
        var placement = beside ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        _title.Style = (Style)resources[beside ? "ResonateTitleTextStyle" : "ResonateSectionTextStyle"];
        _title.TextAlignment = alignment;
        _title.HorizontalAlignment = placement;
        _title.TextWrapping = TextWrapping.Wrap;
        _title.MaxLines = 2;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(_title, 1);
        Children.Add(_title);

        _artists.Style = (Style)resources["ResonateSecondaryTextStyle"];
        _artists.TextAlignment = alignment;
        _artists.HorizontalAlignment = placement;
        _artists.TextTrimming = TextTrimming.CharacterEllipsis;
        SetRow(_artists, 2);
        Children.Add(_artists);

        if (beside)
        {
            // The artists open their pages, as in the player bar.
            _artists.HorizontalAlignment = HorizontalAlignment.Left;
            SongLinks.Attach(_artists, SongLinks.Artists, PlayingTrack.Get);

            // A title on two lines leaves the cover less room.
            _title.SizeChanged += (_, _) => SizeCover(_coverHolder.ActualWidth, _coverHolder.ActualHeight);
            _artists.SizeChanged += (_, _) => SizeCover(_coverHolder.ActualWidth, _coverHolder.ActualHeight);
        }

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

    /// <summary>
    /// Beside the page: the height above the controls. The cover shrinks so
    /// the words and the controls under it always fit.
    /// </summary>
    internal void FitTo(double room)
    {
        if (Math.Abs(room - _room) < 0.5)
        {
            return;
        }

        _room = room;
        SizeCover(_coverHolder.ActualWidth, _coverHolder.ActualHeight);
    }

    /// <summary>Beside the page, narrower than the words fit: the cover alone, with the song and its artists in its tooltip.</summary>
    internal void SetRail(bool rail)
    {
        if (!_beside || rail == _rail)
        {
            return;
        }

        _rail = rail;
        Padding = rail ? new Thickness(12, 16, 12, 6) : new Thickness(20, 20, 20, 10);
        _coverHolder.Margin = new Thickness(0, 0, 0, rail ? 10 : 18);
        _title.Visibility = rail ? Visibility.Collapsed : Visibility.Visible;
        _artists.Visibility = _title.Visibility;
        ShowTip();
        SizeCover(_coverHolder.ActualWidth, _coverHolder.ActualHeight);
    }

    private void ShowTip() =>
        ToolTipService.SetToolTip(_cover, _rail && _artists.Text.Length > 0 ? $"{_title.Text} · {_artists.Text}" : null);

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyLook();

    /// <summary>A panel like the page's, in the look's corners and outline; beside the page, the column's panel lies behind it.</summary>
    private void ApplyLook()
    {
        var theme = _services.Theme;
        var palette = theme.Palette;
        var coverCorner = new CornerRadius(theme.CoverIsRecord ? CoverMaxSize : palette.CornerMedium);
        _cover.CornerRadius = coverCorner;
        if (_coverShadow is not null)
        {
            _coverShadow.CornerRadius = coverCorner;
            return;
        }

        Background = theme.GetBrush("ResonateSurfaceBrush");
        BorderBrush = theme.GetBrush("ResonateBorderBrush");
        BorderThickness = new Thickness(palette.BorderWidth);
        CornerRadius = new CornerRadius(palette.CornerLarge);
    }

    /// <summary>
    /// The cover is square, as large as its room allows: in the window shape
    /// its row's room; beside the page the column's width, within the room
    /// above the controls left by the words.
    /// </summary>
    private void SizeCover(double width, double height)
    {
        double size;
        if (_beside)
        {
            var words = _title.ActualHeight + _artists.ActualHeight + RowSpacing * 2 + Padding.Top + Padding.Bottom + _coverHolder.Margin.Bottom;
            size = Math.Min(Math.Min(width, CoverMaxSize), Math.Max(CoverMinSize, _room - words));
        }
        else
        {
            size = Math.Min(Math.Min(width, height), CoverMaxSize);
        }

        size = Math.Max(0, Math.Floor(size));
        if (Math.Abs(_cover.Width - size) < 0.5)
        {
            return;
        }

        _cover.Width = size;
        _cover.Height = size;
        if (_coverShadow is not null)
        {
            _coverShadow.Width = size;
            _coverShadow.Height = size;
        }
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
        ShowTip();

        object key = state.ArtworkUrl ?? (object?)state.ArtworkBytes ?? "tile:" + (state.Album ?? state.Title);
        if (Equals(key, _artworkKey))
        {
            return;
        }

        _artworkKey = key;
        _image.Opacity = 0;
        var name = state.Album ?? state.Title;

        // Beside the page nothing shows while the cover loads, and the album's tile only when there is none.
        _cover.Background = name is null ? _services.Theme.GetBrush("ResonateSurfaceHoverBrush")
            : _beside && (state.ArtworkUrl is not null || state.ArtworkBytes is not null) ? null
            : Artwork.PlaceholderBrush(name);
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
        if (image is null && _cover.Background is null)
        {
            var state = _services.Player.State;
            _cover.Background = Artwork.PlaceholderBrush(state.Album ?? state.Title ?? string.Empty);
        }
    }
}
