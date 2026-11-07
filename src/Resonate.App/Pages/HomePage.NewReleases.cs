using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Pages;

/// <summary>
/// New releases on Home: the newest albums, EPs and singles of the artists
/// the user follows (<see cref="NewReleasesFeed"/> asks Spotify and keeps
/// them). Its cards are <c>NewReleaseCardTemplate</c> in
/// <c>NewReleasesSection</c>, like the other rows' (as many as fit, "Show
/// all" for the rest). A sign-in older than the permission to read the
/// followed artists shows Sign in again, which asks for it in the browser
/// without signing out (signing out would forget the listening history).
/// </summary>
public sealed partial class HomePage
{
    private const double NewReleaseCardWidth = 176;
    private const double NewReleasePitch = NewReleaseCardWidth + 8;

    private readonly List<NewReleaseTile> _allNewReleases = [];
    private NewReleasesView? _shownNewReleases;
    private DateOnly _newReleasesDay;
    private bool _newReleasesOn;
    private bool _newReleasesExpanded;
    private bool _signingInAgain;
    private int _newReleasesQueued;

    /// <summary>The New releases cards that fit in the row (all of them once "Show all" is pressed).</summary>
    public ObservableCollection<NewReleaseTile> NewReleaseCards { get; } = [];

    partial void OnNewReleasesNavigatedTo()
    {
        if (_newReleasesOn)
        {
            return;
        }

        _newReleasesOn = true;
        var feed = _services.NewReleases;
        feed.Changed += OnNewReleasesChanged;
        NewReleasesSection.SizeChanged += OnNewReleasesSizeChanged;
        ShowNewReleases(feed.View);
        _ = RefreshNewReleasesAsync(feed);
    }

    partial void OnNewReleasesNavigatedFrom()
    {
        if (!_newReleasesOn)
        {
            return;
        }

        _newReleasesOn = false;
        _services.NewReleases.Changed -= OnNewReleasesChanged;
        NewReleasesSection.SizeChanged -= OnNewReleasesSizeChanged;
    }

    /// <summary>
    /// For the screenshot tour: why the first New releases card did not draw,
    /// or null once it did (a picture cannot tell an empty card from no card).
    /// </summary>
    internal string? NewReleasesProblem()
    {
        if (NewReleaseCards.Count == 0 || NewReleasesSection.Visibility != Visibility.Visible)
        {
            return "it showed no cards for the demo's followed artists.";
        }

        if (NewReleasesGrid.ContainerFromIndex(0) is not { } container)
        {
            return "its first card was not made.";
        }

        if (FindRediscoverCard(container, 0) is not { } card)
        {
            return "its first card is empty.";
        }

        return card.ActualHeight < NewReleaseCardWidth
            ? $"its first card is {card.ActualHeight:0.#} px tall, less than its cover."
            : null;
    }

    /// <summary>
    /// Shows what is stored, then brings it up to date in the background. Not
    /// cancelled on leaving Home: what comes is kept, and the feed asks for
    /// little once it is up to date.
    /// </summary>
    private static async Task RefreshNewReleasesAsync(NewReleasesFeed feed)
    {
        try
        {
            await Task.Run(() => feed.RefreshAsync(CancellationToken.None));
        }
        catch (Exception)
        {
            // Offline or signed out: the releases found before stay.
        }
    }

    private void OnNewReleasesChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _newReleasesQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _newReleasesQueued, 0);
                if (_newReleasesOn)
                {
                    ShowNewReleases(_services.NewReleases.View);
                }
            });
        }
    }

    private void OnNewReleasesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            FitNewReleases();
        }
    }

    /// <summary>
    /// Shows the releases; the section stays hidden while there are none,
    /// unless the sign-in needs renewing. Cards already made are kept, so
    /// their covers do not load again while the row fills.
    /// </summary>
    private void ShowNewReleases(NewReleasesView view)
    {
        NewReleasesSignInButton.Visibility = view.NeedsSignIn ? Visibility.Visible : Visibility.Collapsed;
        NewReleasesSection.Visibility = view.NeedsSignIn || view.Releases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // "3 days ago" changes at midnight.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (ReferenceEquals(view, _shownNewReleases) && today == _newReleasesDay)
        {
            return;
        }

        _shownNewReleases = view;
        _newReleasesDay = today;
        var made = new Dictionary<string, NewReleaseTile>(StringComparer.Ordinal);
        foreach (var tile in _allNewReleases)
        {
            made.TryAdd(tile.Release.Id, tile);
        }

        _allNewReleases.Clear();
        foreach (var release in view.Releases)
        {
            var caption = NewReleasesBuilder.Caption(release, today);
            _allNewReleases.Add(made.TryGetValue(release.Id, out var kept) && kept.Caption == caption && kept.Release.ImageUrl == release.ImageUrl
                ? kept
                : new NewReleaseTile(release, caption));
        }

        FitNewReleases();
    }

    /// <summary>As many cards as fit in a row, or all of them once "Show all" is pressed.</summary>
    private void FitNewReleases()
    {
        var width = NewReleasesSection.ActualWidth > 0 ? NewReleasesSection.ActualWidth : _width;
        var fit = Math.Max(1, (int)(width / NewReleasePitch));
        ShowInOrder(NewReleaseCards, _allNewReleases, _newReleasesExpanded ? _allNewReleases.Count : fit);
        NewReleasesMoreButton.Visibility = _allNewReleases.Count > fit ? Visibility.Visible : Visibility.Collapsed;
        NewReleasesMoreButton.Content = _newReleasesExpanded ? "Show less" : "Show all";
    }

    /// <summary>
    /// Makes <paramref name="shown"/> the first <paramref name="count"/> of
    /// <paramref name="all"/>, moving as few cards as it can: a release found
    /// while the row fills goes in where it belongs, and the rest stay put.
    /// </summary>
    private static void ShowInOrder<T>(ObservableCollection<T> shown, List<T> all, int count)
        where T : class
    {
        count = Math.Min(count, all.Count);
        for (var i = 0; i < count; i++)
        {
            var wanted = all[i];
            if (i < shown.Count && ReferenceEquals(shown[i], wanted))
            {
                continue;
            }

            var at = -1;
            for (var j = i + 1; j < shown.Count; j++)
            {
                if (ReferenceEquals(shown[j], wanted))
                {
                    at = j;
                    break;
                }
            }

            if (at >= 0)
            {
                shown.RemoveAt(at);
            }

            shown.Insert(i, wanted);
        }

        while (shown.Count > count)
        {
            shown.RemoveAt(shown.Count - 1);
        }
    }

    private void OnNewReleasesMoreClick(object sender, RoutedEventArgs e)
    {
        _newReleasesExpanded = !_newReleasesExpanded;
        FitNewReleases();
    }

    /// <summary>A card opens its release, as an album opens anywhere else.</summary>
    private void OnNewReleaseClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NewReleaseTile tile && tile.Release.Id.Length > 0)
        {
            App.MainWindow?.Open(AlbumSource.Prefix + tile.Release.Id);
        }
    }

    /// <summary>
    /// Asks Spotify again in the browser, now with the permission to read the
    /// followed artists, keeping everything else. Another account coming back
    /// starts afresh, as signing out and in does.
    /// </summary>
    private async void OnNewReleasesSignInClick(object sender, RoutedEventArgs e)
    {
        if (_signingInAgain)
        {
            return;
        }

        _signingInAgain = true;
        NewReleasesSignInButton.IsEnabled = false;
        var services = _services;
        var before = services.Library.Snapshot?.User?.Id;
        try
        {
            using (var waiting = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                var token = waiting.Token;
                await Task.Run(() => services.Account.SignInAsync(token), token);
            }

            SpotifyUser? user = null;
            try
            {
                user = await Task.Run(() => services.Api.GetCurrentUserAsync(CancellationToken.None));
            }
            catch (Exception)
            {
                // Not known now: the sign-in itself worked.
            }

            if (before is not null && user is not null && user.Id != before)
            {
                services.ForgetAccount();
                App.MainWindow?.ShowSignIn();
                return;
            }

            var feed = services.NewReleases;
            feed.SignedInAgain();
            _ = RefreshNewReleasesAsync(feed);
        }
        catch (OperationCanceledException)
        {
            // Left alone in the browser too long.
        }
        catch (SpotifyAuthException ex)
        {
            App.MainWindow?.ShowMessage(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception)
        {
            // Anything else must not close Resonate (this handler is async void).
            App.MainWindow?.ShowMessage("Signing in did not work. Try again.", InfoBarSeverity.Warning);
        }
        finally
        {
            _signingInAgain = false;
            NewReleasesSignInButton.IsEnabled = true;
        }
    }
}
