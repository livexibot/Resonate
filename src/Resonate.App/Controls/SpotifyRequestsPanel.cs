using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Controls;

/// <summary>
/// Settings, About, Help: how many requests Resonate sent to Spotify's Web
/// API today, by what they were for, and in total (the owner's request,
/// 10 October 2026, after the developer app's allowance ran out). Follows
/// the count while shown, at most once a second.
/// </summary>
internal sealed partial class SpotifyRequestsPanel : StackPanel
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);

    private readonly RequestCounter _counter;
    private readonly SettingRow _today = new() { Header = "Spotify requests today" };
    private readonly SettingRow _total = new();
    private readonly TextBlock _todayCount = Count();
    private readonly TextBlock _totalCount = Count();
    private readonly StackPanel _kinds = new() { Spacing = 4, Margin = new Thickness(16, 4, 16, 4) };
    private readonly DispatcherQueueTimer _timer;
    private bool _changed;

    public SpotifyRequestsPanel(RequestCounter counter)
    {
        _counter = counter;
        Spacing = 4;
        _today.Content = _todayCount;
        _total.Content = _totalCount;
        Children.Add(_today);
        Children.Add(_kinds);
        Children.Add(_total);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = Refresh;
        Loaded += (_, _) =>
        {
            _counter.Counted -= OnCounted;
            _counter.Counted += OnCounted;
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
            Show();
        };
        Unloaded += (_, _) =>
        {
            // Nothing keeps the page alive once it is closed.
            _counter.Counted -= OnCounted;
            _timer.Stop();
            _timer.Tick -= OnTick;
        };
    }

    private void OnCounted(object? sender, EventArgs e) => _changed = true;

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_changed)
        {
            _changed = false;
            Show();
        }
    }

    private void Show()
    {
        var culture = CultureInfo.CurrentCulture;
        var kinds = _counter.Today(out var today);
        _todayCount.Text = today.ToString("N0", culture);
        _total.Header = $"Total since {_counter.Since.ToString("d MMMM yyyy", culture)}";
        _totalCount.Text = _counter.Total.ToString("N0", culture);

        _kinds.Children.Clear();
        _kinds.Visibility = kinds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var style = (Style)Application.Current.Resources["ResonateSecondaryTextStyle"];
        foreach (var (kind, count) in kinds)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
            row.Children.Add(new TextBlock { Style = style, Text = KindName(kind) });
            var number = new TextBlock { Style = style, Text = count.ToString("N0", culture) };
            Grid.SetColumn(number, 1);
            row.Children.Add(number);
            _kinds.Children.Add(row);
        }
    }

    private static TextBlock Count() => new()
    {
        Style = (Style)Application.Current.Resources["ResonateBodyTextStyle"],
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string KindName(RequestKind kind) => kind switch
    {
        RequestKind.WhatPlays => "What's playing",
        RequestKind.PlayerCommands => "Play, pause, skip and volume",
        RequestKind.Devices => "Devices",
        RequestKind.Queue => "Queue",
        RequestKind.History => "Listening history",
        RequestKind.Library => "Playlists and Liked Songs",
        RequestKind.Search => "Search",
        RequestKind.AlbumsAndArtists => "Albums and artists",
        RequestKind.YourTop => "Your top",
        _ => "Account",
    };
}
