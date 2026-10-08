using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Pages;

/// <summary>An artist: their picture, Play (Spotify plays their best-known songs), and their releases.</summary>
public sealed partial class ArtistPage : Page
{
    /// <summary>The most releases shown, read ten at a time (all Spotify allows per request).</summary>
    private const int MaxReleases = 50;

    private const int PortraitSize = 232;

    // A narrow page (a small window, or Settings or the queue open beside it), as on a playlist.
    private const double CompactWidth = 600;
    private const int CompactPortraitSize = 128;
    private const string OpenInSpotifyLabel = "Open in Spotify";

    private readonly AppServices _services = App.Services;
    private readonly CancellationTokenSource _leaving = new();
    private readonly CoverHero _hero;
    private string _artistId = string.Empty;
    private string _name = string.Empty;
    private bool _compact;

    public ArtistPage()
    {
        InitializeComponent();
        _hero = new CoverHero(Hero);
    }

    public ObservableCollection<CardItem> Albums { get; } = [];

    public ObservableCollection<CardItem> Singles { get; } = [];

    public ObservableCollection<CardItem> Compilations { get; } = [];

    private string ContextUri => "spotify:artist:" + _artistId;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _artistId = e.Parameter as string ?? string.Empty;
        PortraitFrame.Background = Artwork.PlaceholderBrush(_artistId);
        _hero.Attach();
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _hero.Detach();
        _leaving.Cancel();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            FitToWidth(e.NewSize.Width);
        }
    }

    /// <summary>A narrow page gets a smaller picture and name, and shorter buttons, so nothing is cut off.</summary>
    private void FitToWidth(double width)
    {
        var compact = width < CompactWidth;
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        var size = compact ? CompactPortraitSize : PortraitSize;
        PortraitColumn.Width = new GridLength(size);
        PortraitFrame.Width = PortraitFrame.Height = size;
        PortraitShadow.Width = PortraitShadow.Height = size;
        PortraitFrame.CornerRadius = PortraitShadow.CornerRadius = new CornerRadius(size / 2.0);
        // A smaller name, so a long one still fits on its two lines (a style, so it follows the Text size).
        NameText.Style = (Style)Application.Current.Resources[compact ? "ResonateCompactDisplayTextStyle" : "ResonateDisplayTextStyle"];

        LikedSongsButton.Content = compact ? "Liked songs" : "Liked songs by this artist";
        OpenInSpotifyText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        OpenInSpotifyIcon.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(OpenInSpotifyButton, compact ? OpenInSpotifyLabel : null);
    }

    private async Task LoadAsync()
    {
        var token = _leaving.Token;
        var api = _services.Api;
        var id = _artistId;
        LoadingRing.IsActive = true;
        try
        {
            var artistTask = Task.Run(() => api.GetArtistAsync(id, token), token);
            var albumsTask = Task.Run(() => api.GetArtistAlbumsAsync(id, 0, SpotifyWebApi.MaxArtistAlbumsLimit, token), token);

            var artist = await artistTask;
            _name = artist.Name;
            NameText.Text = artist.Name;
            PortraitFrame.Background = Artwork.PlaceholderBrush(artist.Name);
            var portrait = ImagePicker.Pick(artist.Images, 300);
            PortraitImage.Source = Artwork.FromUrl(portrait, PortraitSize);
            _hero.Show(Artwork.PlaceholderColors(artist.Name).From, portrait);
            if (artist.Genres is { Count: > 0 } genres)
            {
                GenresText.Text = string.Join(" · ", genres.Take(4));
                GenresText.Visibility = Visibility.Visible;
            }

            // The first ten show at once; the rest follow ten at a time.
            var releases = await albumsTask;
            ShowReleases(releases);
            for (var offset = releases.Items.Count; releases.HasMore && releases.Items.Count > 0 && offset < MaxReleases; offset += releases.Items.Count)
            {
                var from = offset;
                releases = await Task.Run(() => api.GetArtistAlbumsAsync(id, from, SpotifyWebApi.MaxArtistAlbumsLimit, token), token);
                ShowReleases(releases);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void ShowReleases(Page<SimplifiedAlbum> releases)
    {
        foreach (var album in releases.Items.OfType<SimplifiedAlbum>())
        {
            if (album.Id is null)
            {
                continue;
            }

            var year = album.ReleaseDate is { Length: >= 4 } date ? date[..4] : string.Empty;
            var card = new CardItem(album.Name, year, album.Uri ?? string.Empty, album.Id, ImagePicker.Pick(album.Images, 300), isPlaylist: false);
            var target = album.AlbumType switch
            {
                "single" => Singles,
                "compilation" => Compilations,
                _ => Albums,
            };
            target.Add(card);
        }

        AlbumsSection.Visibility = Albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SinglesSection.Visibility = Singles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompilationsSection.Visibility = Compilations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => _ = _services.Player.PlayContextAsync(ContextUri);

    private void OnLikedSongsClick(object sender, RoutedEventArgs e) =>
        App.MainWindow?.Open(LikedByArtistSource.Prefix + _artistId);

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CardItem { Id: { } id })
        {
            App.MainWindow?.Open(AlbumSource.Prefix + id);
        }
    }

    private void OnOpenInSpotifyClick(object sender, RoutedEventArgs e) => TrackActions.OpenInSpotify(ContextUri);
}
