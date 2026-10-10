using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.History;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

/// <summary>
/// Rediscover, a built-in plugin: one row on Home of music the user chose
/// long ago. Songs liked on this day years ago and albums released on it,
/// songs liked long ago and not played for months, and favourite albums with
/// songs not liked yet. Its cards are <c>RediscoverCardTemplate</c> in
/// <c>RediscoverSection</c>, like the other rows' (as many as fit, "Show all"
/// for the rest); cards built in code came out empty in the published app
/// (v0.15). Nothing of it shows or runs while the plugin is off.
/// </summary>
public sealed partial class HomePage
{
    private const double RediscoverCardWidth = 176;
    private const double RediscoverPitch = RediscoverCardWidth + 8;

    private readonly List<RediscoverTile> _allRediscover = [];
    private RediscoverPicks? _shownRediscover;
    private bool _rediscoverOn;
    private bool _rediscoverExpanded;
    private int _rediscoverQueued;

    /// <summary>Rediscover's cards that fit in the row (all of them once "Show all" is pressed).</summary>
    public ObservableCollection<RediscoverTile> Rediscovered { get; } = [];

    partial void OnRediscoverNavigatedTo()
    {
        // Turned on or off in Settings, which can be open beside Home.
        _services.BuiltIns.Changed += OnRediscoverPluginChanged;
        if (_services.BuiltIns.IsOn(BuiltInPlugins.Rediscover))
        {
            StartRediscover();
        }
    }

    partial void OnRediscoverNavigatedFrom()
    {
        _services.BuiltIns.Changed -= OnRediscoverPluginChanged;
        StopRediscover();
    }

    /// <summary>
    /// For the screenshot tour: why Rediscover's first card did not draw, or
    /// null once it did. A picture cannot tell an empty card from no card,
    /// and the cards once came out empty in the published app (v0.15).
    /// </summary>
    internal string? RediscoverProblem()
    {
        if (!_rediscoverOn)
        {
            return "Rediscover is not on.";
        }

        if (Rediscovered.Count == 0 || RediscoverSection.Visibility != Visibility.Visible)
        {
            return "it showed no cards for the demo's songs.";
        }

        if (RediscoverGrid.ContainerFromIndex(0) is not { } container)
        {
            return "its first card was not made.";
        }

        if (FindRediscoverCard(container, 0) is not { } card)
        {
            return "its first card is empty.";
        }

        return card.ActualHeight < RediscoverCardWidth
            ? $"its first card is {card.ActualHeight:0.#} px tall, less than its cover."
            : null;
    }

    // The card's own type (the app's), found among its container's children, so the check holds in the published app too.
    private static HoverLift? FindRediscoverCard(DependencyObject parent, int depth)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is HoverLift card)
            {
                return card;
            }

            if (depth < 4 && FindRediscoverCard(child, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnRediscoverPluginChanged(object? sender, string id)
    {
        if (id != BuiltInPlugins.Rediscover)
        {
            return;
        }

        if (_services.BuiltIns.IsOn(id))
        {
            StartRediscover();
        }
        else
        {
            StopRediscover();
            _shownRediscover = null;
            _allRediscover.Clear();
            Rediscovered.Clear();
            RediscoverSection.Visibility = Visibility.Collapsed;
        }
    }

    private void StartRediscover()
    {
        if (_rediscoverOn)
        {
            return;
        }

        _rediscoverOn = true;
        var feed = BuiltInFeeds.Rediscover(_services);
        feed.Changed += OnRediscoverChanged;
        RediscoverSection.SizeChanged += OnRediscoverSizeChanged;
        ShowRediscover(feed.Picks);
        _ = RefreshRediscoverAsync(feed);
    }

    private void StopRediscover()
    {
        if (!_rediscoverOn)
        {
            return;
        }

        _rediscoverOn = false;
        BuiltInFeeds.Rediscover(_services).Changed -= OnRediscoverChanged;
        RediscoverSection.SizeChanged -= OnRediscoverSizeChanged;
    }

    /// <summary>
    /// Brings the cards up to date in the background. Not cancelled on leaving
    /// Home: it asks for a few albums at most, and what came is kept.
    /// </summary>
    private static async Task RefreshRediscoverAsync(RediscoverFeed feed)
    {
        try
        {
            await Task.Run(() => feed.RefreshAsync(CancellationToken.None));
        }
        catch (Exception)
        {
            // Offline or signed out: the cards picked before stay.
        }
    }

    private void OnRediscoverChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _rediscoverQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _rediscoverQueued, 0);
                if (_rediscoverOn)
                {
                    ShowRediscover(BuiltInFeeds.Rediscover(_services).Picks);
                }
            });
        }
    }

    private void OnRediscoverSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            FitRediscover();
        }
    }

    /// <summary>Shows the day's cards; the section stays hidden while there are none.</summary>
    private void ShowRediscover(RediscoverPicks? picks)
    {
        // Only the kinds the user keeps (the plugin's settings).
        var settings = _services.Settings;
        var cards = picks?.Cards.Where(c => c.Kind switch
        {
            RediscoverKind.OnThisDay or RediscoverKind.AlbumBirthday => settings.RediscoverOnThisDay,
            RediscoverKind.GatheringDust => settings.RediscoverDust,
            _ => settings.RediscoverDeepCuts,
        }).ToList() ?? [];
        if (picks is null || cards.Count == 0)
        {
            RediscoverSection.Visibility = Visibility.Collapsed;
            return;
        }

        if (ReferenceEquals(picks, _shownRediscover))
        {
            return;
        }

        _shownRediscover = picks;
        _allRediscover.Clear();
        Rediscovered.Clear();
        _allRediscover.AddRange(cards.Select(c => new RediscoverTile(c)));
        RediscoverSection.Visibility = Visibility.Visible;
        FitRediscover();
    }

    /// <summary>As many cards as fit in a row, or all of them once "Show all" is pressed.</summary>
    private void FitRediscover()
    {
        var width = RediscoverSection.ActualWidth > 0 ? RediscoverSection.ActualWidth : _width;
        var fit = Math.Max(1, (int)(width / RediscoverPitch));
        ShowFirst(Rediscovered, _allRediscover, _rediscoverExpanded ? _allRediscover.Count : fit);
        RediscoverMoreButton.Visibility = _allRediscover.Count > fit ? Visibility.Visible : Visibility.Collapsed;
        RediscoverMoreButton.Content = _rediscoverExpanded ? "Show less" : "Show all";
    }

    private void OnRediscoverMoreClick(object sender, RoutedEventArgs e)
    {
        _rediscoverExpanded = !_rediscoverExpanded;
        FitRediscover();
    }

    private void OnRediscoverClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RediscoverTile tile)
        {
            OpenRediscover(tile);
        }
    }

    /// <summary>
    /// A song liked on this day plays inside Liked Songs; a dusty song plays
    /// the day's dusty songs from it; an album opens.
    /// </summary>
    private void OpenRediscover(RediscoverTile tile)
    {
        var item = tile.Card;
        if (item.Track is not { } track)
        {
            if (item.AlbumId is { } albumId)
            {
                App.MainWindow?.Open(AlbumSource.Prefix + albumId);
            }

            return;
        }

        if (IsSecondClick(tile))
        {
            return;
        }

        if (item.Kind == RediscoverKind.GatheringDust)
        {
            _ = _services.Player.PlayAsync(new PlayRequest(item.Tracks, 0, null, "Gathering dust"));
            return;
        }

        var liked = _services.Library.Snapshot?.User?.Id is { } userId ? $"spotify:user:{userId}:collection" : null;
        _ = _services.Player.PlayAsync(liked is not null
            ? new PlayRequest([track], 0, liked, "Liked Songs")
            : new PlayRequest([track], 0, track.AlbumUri, track.Album));
    }
}
