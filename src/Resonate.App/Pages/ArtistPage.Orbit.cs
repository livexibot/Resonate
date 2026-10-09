using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Controls;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages;

/// <summary>
/// Artist orbit, a built-in plugin: an "In your orbit" section with the
/// artists who keep this one company in the user's own playlists, listening
/// sessions and liked songs (see <see cref="ArtistOrbit"/>). Clicking one
/// opens their page, so Back retraces the path. Built in code into
/// <c>OrbitSection</c>; hidden while the plugin is off or nobody keeps the
/// artist company.
/// </summary>
public sealed partial class ArtistPage
{
    /// <summary>Pictures asked of Spotify per orbit at most (one GET /artists/{id} each, then remembered).</summary>
    private const int MaxOrbitPictureRequests = 10;

    private ArtistOrbitPanel? _orbit;
    private bool _orbitStarted;
    private string? _orbitName;
    private string? _orbitPortrait;

    private void OnOrbitNavigatedTo()
    {
        // Turned on or off in Settings, which can be open beside the page.
        _services.BuiltIns.Changed += OnOrbitPluginChanged;
        if (_services.BuiltIns.IsOn(BuiltInPlugins.ArtistOrbit))
        {
            _ = ShowOrbitAsync();
        }
    }

    private void OnOrbitNavigatedFrom() => _services.BuiltIns.Changed -= OnOrbitPluginChanged;

    /// <summary>The artist is known: they sit in the middle.</summary>
    private void ShowOrbitArtist(string name, string? portrait)
    {
        _orbitName = name;
        _orbitPortrait = portrait;
        _orbit?.SetCentre(name, portrait);
    }

    private void OnOrbitPluginChanged(object? sender, string id)
    {
        if (id != BuiltInPlugins.ArtistOrbit)
        {
            return;
        }

        if (_services.BuiltIns.IsOn(id))
        {
            _ = ShowOrbitAsync();
        }
        else
        {
            _orbitStarted = false;
            _orbit = null;
            OrbitSection.Children.Clear();
            OrbitSection.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowOrbitAsync()
    {
        if (_orbitStarted)
        {
            return;
        }

        _orbitStarted = true;
        var token = _leaving.Token;
        var artistId = _artistId;
        ArtistOrbitView view;
        try
        {
            var orbit = await BuiltInFeeds.OrbitAsync(_services);
            var settings = _services.Settings;
            var companions = Math.Clamp(settings.RelatedArtistsCount, 3, ArtistOrbit.MaxCompanions);
            var albums = settings.RelatedArtistsAlbums ? ArtistOrbit.MaxAlbums : 0;
            view = await Task.Run(() => orbit.For(artistId, companions, albums), token);
        }
        catch (Exception)
        {
            // Left the page, or nothing to work from yet (offline on a first start).
            return;
        }

        if (token.IsCancellationRequested || !_orbitStarted || view.Companions.Count == 0)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "In your orbit", Style = (Style)resources["ResonateSectionTextStyle"] });

        var name = _orbitName ?? view.ArtistName ?? string.Empty;
        _orbit = new ArtistOrbitPanel(_services);
        _orbit.SetCentre(name, _orbitPortrait);
        _orbit.ArtistClicked += (_, id) => App.MainWindow?.Open(TrackActions.ArtistKey(id));
        _orbit.AlbumClicked += (_, id) => App.MainWindow?.Open(AlbumSource.Prefix + id);
        OrbitSection.Children.Clear();
        OrbitSection.Children.Add(titles);
        OrbitSection.Children.Add(_orbit);
        OrbitSection.Visibility = Visibility.Visible;
        _orbit.Show(view);
        await LoadOrbitPicturesAsync(_orbit, view, token);
    }

    /// <summary>The companions' pictures: those Home already knows at once, the rest asked for one by one and remembered.</summary>
    private async Task LoadOrbitPicturesAsync(ArtistOrbitPanel orbit, ArtistOrbitView view, CancellationToken token)
    {
        var home = _services.Home;
        var asked = 0;
        foreach (var companion in view.Companions)
        {
            if (home.KnownArtistImage(companion.Id) is { } known)
            {
                orbit.SetPicture(companion.Id, known);
                continue;
            }

            if (asked++ >= MaxOrbitPictureRequests)
            {
                break;
            }

            try
            {
                var id = companion.Id;
                var url = await Task.Run(() => home.GetArtistImageAsync(id, token), token);
                if (token.IsCancellationRequested || orbit != _orbit)
                {
                    return;
                }

                orbit.SetPicture(id, url);
            }
            catch (Exception)
            {
                // Left the page, or Spotify did not answer: the initials stay.
                return;
            }
        }
    }

    /// <summary>For the screenshot tour: scrolls the orbit into view at once.</summary>
    internal void ShowOrbitForTour() =>
        OrbitSection.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
}
