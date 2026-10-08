using System.Collections.ObjectModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Helpers;
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
/// songs not liked yet. Built in code into <c>RediscoverSection</c>, with the
/// look of the other rows (as many cards as fit, "Show all" for the rest).
/// Nothing of it shows or runs while the plugin is off.
/// </summary>
public sealed partial class HomePage
{
    private const double RediscoverCardWidth = 176;
    private const double RediscoverPitch = RediscoverCardWidth + 8;

    private readonly List<RediscoverTile> _allRediscover = [];
    private readonly ObservableCollection<RediscoverTile> _rediscover = [];
    private GridView? _rediscoverGrid;
    private Button? _rediscoverMore;
    private RediscoverPicks? _shownRediscover;
    private bool _rediscoverOn;
    private bool _rediscoverExpanded;
    private int _rediscoverQueued;

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
            _rediscover.Clear();
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
        _services.Theme.Changed += OnRediscoverThemeChanged;
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
        _services.Theme.Changed -= OnRediscoverThemeChanged;
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

    private void OnRediscoverThemeChanged(object? sender, EventArgs e)
    {
        // The cards' corners follow the look: made again.
        var picks = _shownRediscover;
        _shownRediscover = null;
        _rediscover.Clear();
        ShowRediscover(picks);
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
        if (picks is not { Cards.Count: > 0 })
        {
            RediscoverSection.Visibility = Visibility.Collapsed;
            return;
        }

        if (ReferenceEquals(picks, _shownRediscover))
        {
            return;
        }

        _shownRediscover = picks;
        BuildRediscoverSection();
        _allRediscover.Clear();
        _rediscover.Clear();
        _allRediscover.AddRange(picks.Cards.Select(c => new RediscoverTile(c)));
        RediscoverSection.Visibility = Visibility.Visible;
        FitRediscover();
    }

    /// <summary>The section's title and its row, made once per visit.</summary>
    private void BuildRediscoverSection()
    {
        if (_rediscoverGrid is not null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "Rediscover", Style = (Style)resources["ResonateSectionTextStyle"] });
        titles.Children.Add(new TextBlock
        {
            Text = "Music you chose long ago, back for another listen. New every day.",
            Style = (Style)resources["ResonateCaptionTextStyle"],
        });
        header.Children.Add(titles);

        _rediscoverMore = new Button
        {
            VerticalAlignment = VerticalAlignment.Center,
            Content = "Show all",
            Style = (Style)resources["ResonateSubtleButtonStyle"],
            Visibility = Visibility.Collapsed,
        };
        Grid.SetColumn(_rediscoverMore, 1);
        _rediscoverMore.Click += (_, _) =>
        {
            _rediscoverExpanded = !_rediscoverExpanded;
            FitRediscover();
        };
        header.Children.Add(_rediscoverMore);

        // Plain items; each card is built in code as its container is filled.
        _rediscoverGrid = new GridView
        {
            Margin = new Thickness(-4, 0, 0, 0),
            Padding = new Thickness(4, 6, 0, 0),
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            ItemsSource = _rediscover,
        };
        _rediscoverGrid.ContainerContentChanging += OnRediscoverContainerContentChanging;
        _rediscoverGrid.ItemClick += OnRediscoverClick;

        RediscoverSection.Children.Clear();
        RediscoverSection.Children.Add(header);
        RediscoverSection.Children.Add(_rediscoverGrid);
    }

    /// <summary>As many cards as fit in a row, or all of them once "Show all" is pressed.</summary>
    private void FitRediscover()
    {
        if (_rediscoverMore is null)
        {
            return;
        }

        var width = RediscoverSection.ActualWidth > 0 ? RediscoverSection.ActualWidth : _width;
        var fit = Math.Max(1, (int)(width / RediscoverPitch));
        ShowFirst(_rediscover, _allRediscover, _rediscoverExpanded ? _allRediscover.Count : fit);
        _rediscoverMore.Visibility = _allRediscover.Count > fit ? Visibility.Visible : Visibility.Collapsed;
        _rediscoverMore.Content = _rediscoverExpanded ? "Show less" : "Show all";
    }

    private void OnRediscoverContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // A new card each time (they are few), so no card ever sits in two containers.
        args.ItemContainer.Content = !args.InRecycleQueue && args.Item is RediscoverTile tile ? BuildRediscoverCard(tile.Card) : null;
        args.Handled = true;
    }

    /// <summary>
    /// A card like the other rows': the cover, a label saying why it is here,
    /// the title, the artists and a note. Songs show a play button under the
    /// pointer; albums open.
    /// </summary>
    private HoverLift BuildRediscoverCard(RediscoverCard item)
    {
        var resources = Application.Current.Resources;
        var card = new HoverLift { Width = RediscoverCardWidth, Padding = new Thickness(0, 0, 0, 10) };
        var stack = new StackPanel { Spacing = 2 };
        var cover = new Grid
        {
            Width = RediscoverCardWidth,
            Height = RediscoverCardWidth,
            Margin = new Thickness(0, 0, 0, 8),
            Background = Artwork.PlaceholderBrush(item.Title),
            CornerRadius = new CornerRadius(_services.Theme.Palette.CornerLarge),
        };
        cover.Children.Add(new Image { Source = Artwork.FromUrl(item.ImageUrl, (int)RediscoverCardWidth), Stretch = Stretch.UniformToFill });
        stack.Children.Add(cover);

        var label = new TextBlock { Text = item.Label, Style = (Style)resources["ResonateEyebrowTextStyle"], Margin = new Thickness(0, 0, 0, 2) };
        label.Foreground = _services.Theme.GetBrush("ResonateAccentBrush");
        stack.Children.Add(label);
        stack.Children.Add(Line(item.Title, "ResonateBodyTextStyle", semiBold: true));
        stack.Children.Add(Line(item.Subtitle, "ResonateSecondaryTextStyle", semiBold: false));
        stack.Children.Add(Line(item.Note, "ResonateCaptionTextStyle", semiBold: false));
        card.Children.Add(stack);

        if (item.Track is not null)
        {
            var play = new HoverReveal
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 128, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = _services.Theme.GetBrush("ResonateAccentBrush"),
                CornerRadius = new CornerRadius(20),
                IsHitTestVisible = false,
            };
            play.Children.Add(new FontIcon { Glyph = "", FontSize = 15, Foreground = _services.Theme.GetBrush("ResonateOnAccentBrush") });
            card.Children.Add(play);
        }

        var what = $"{item.Title} · {item.Subtitle}";
        ToolTipService.SetToolTip(card, what + "\n" + item.Note);
        AutomationProperties.SetName(card, $"{item.Label}: {what}, {item.Note}");
        return card;

        TextBlock Line(string text, string style, bool semiBold)
        {
            var line = new TextBlock { Text = text, Style = (Style)resources[style], MaxLines = 1, TextWrapping = TextWrapping.NoWrap };
            if (semiBold)
            {
                line.FontWeight = FontWeights.SemiBold;
            }

            return line;
        }
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
