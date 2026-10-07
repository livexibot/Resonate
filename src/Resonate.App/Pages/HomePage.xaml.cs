using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Auth;
using Resonate.Spotify.History;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.Pages;

/// <summary>
/// Home: listening stats for the past day and week, Spotify's own top
/// artists and songs, the daily mixes, and the songs played lately. What is
/// stored shows at once; Spotify is asked for what is new in the background.
/// </summary>
public sealed partial class HomePage : Page
{
    private const int RecentCount = 6;

    /// <summary>Top artists and songs shown until "Show top 10" is pressed.</summary>
    private const int TopShownFirst = 5;

    /// <summary>A second click on the same song this soon is the rest of a double-click, not another play.</summary>
    private const int DoubleClickMilliseconds = 500;

    private readonly AppServices _services = App.Services;
    private CancellationTokenSource _leaving = new();
    private HomeContent? _shownContent;
    private PlayRecord? _shownNewest;
    private bool _refreshing;
    private int _updateQueued;
    private object? _lastPlayed;
    private long _lastPlayedAt;
    private TopRange _topRange;
    private TopOnSpotify? _shownTop;
    private int _shownTopCount;
    private bool _topExpanded;
    private bool _selectingRange;

    public HomePage()
    {
        InitializeComponent();
    }

    public ObservableCollection<MixCard> Mixes { get; } = [];

    public ObservableCollection<RecentCard> Recent { get; } = [];

    public ObservableCollection<TopArtistRow> TopArtists { get; } = [];

    public ObservableCollection<TopSongRow> TopSongs { get; } = [];

    /// <summary>"past 4 weeks", "past 6 months" or "past year", as Spotify measures them (roughly).</summary>
    public static string RangeName(TopRange range) => range switch
    {
        TopRange.ShortTerm => "past 4 weeks",
        TopRange.MediumTerm => "past 6 months",
        _ => "past year",
    };

    /// <summary>"Good morning" until noon, "Good afternoon" until six, then "Good evening".</summary>
    public static string Greeting(int hour) => hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 18 => "Good afternoon",
        _ => "Good evening",
    };

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _leaving = new CancellationTokenSource();
        GreetingText.Text = Greeting(DateTime.Now.Hour);
        PermissionNote.Visibility = _services.Account.MissingScopes.Any(s => s is "user-read-recently-played" or "user-top-read")
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectTopRange(_services.Settings.HomeTopRange);

        _services.Home.Changed += OnHomeChanged;
        _services.Home.History.Changed += OnHomeChanged;
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _services.Home.Changed -= OnHomeChanged;
        _services.Home.History.Changed -= OnHomeChanged;
        _leaving.Cancel();
    }

    private async Task LoadAsync()
    {
        var token = _leaving.Token;
        var home = _services.Home;
        _refreshing = true;
        try
        {
            if (!home.IsLoaded)
            {
                // The first visit reads the stored history and mixes (a few milliseconds, off this thread).
                await Task.Run(home.LoadStored, token);
            }

            // What is stored, at once (on later visits, before the first frame).
            Show();
            await Task.Run(() => home.RefreshAsync(token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (SpotifyAuthException)
        {
            // Handled by the SignedOut event.
        }
        catch (Exception)
        {
            // Offline or refused: what is stored stays, and nothing needs the user's attention.
        }
        finally
        {
            _refreshing = false;
        }

        if (!token.IsCancellationRequested)
        {
            Show();
        }
    }

    private void OnHomeChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                if (!_leaving.IsCancellationRequested)
                {
                    Show();
                }
            });
        }
    }

    /// <summary>Shows what is known now; parts that did not change are left alone, so nothing flickers.</summary>
    private void Show()
    {
        var home = _services.Home;
        if (!home.IsLoaded)
        {
            return;
        }

        Sections.Visibility = Visibility.Visible;
        var plays = home.History.Plays;
        var now = DateTimeOffset.UtcNow;
        ShowStats(plays, now);
        ShowTop();
        ShowRecent(plays, now);

        // New top lists alone leave the mix cards as they are.
        var content = home.Content;
        if (content?.Mixes != _shownContent?.Mixes || content?.OnRepeat != _shownContent?.OnRepeat)
        {
            _shownContent = content;
            Mixes.Clear();
            foreach (var mix in content?.Mixes ?? [])
            {
                Mixes.Add(new MixCard(DailyMixSource.KeyFor(mix.Number), mix.Title, mix.Subtitle, mix.ImageUrl, DailyMixSource.Glyph));
            }

            if (content?.OnRepeat.Count > 0)
            {
                Mixes.Add(new MixCard(OnRepeatSource.ListKey, "On repeat", "Your most played lately", null, OnRepeatSource.Glyph));
            }
        }

        ShowMixesNote();
    }

    private void ShowStats(IReadOnlyList<PlayRecord> plays, DateTimeOffset now)
    {
        var day = ListeningStats.Summarize(plays, now, ListeningStats.Day);
        var week = ListeningStats.Summarize(plays, now, ListeningStats.Week);
        var added = new List<StatCard>();
        if (DayStats.Content is not StatCard shownDay || shownDay.Summary != day)
        {
            var card = new StatCard("PAST 24 HOURS", day, "Nothing played in the past 24 hours.");
            DayStats.Content = card;
            added.Add(card);
        }

        if (WeekStats.Content is not StatCard shownWeek || shownWeek.Summary != week)
        {
            var card = new StatCard("PAST 7 DAYS", week, "Nothing played in the past week.");
            WeekStats.Content = card;
            added.Add(card);
        }

        if (added.Count > 0)
        {
            _ = ShowArtistImagesAsync(added);
        }

        // A new listener: say why the numbers are small, once the first look at Spotify is done.
        HistoryNote.Visibility = plays.Count < ListeningHistory.PageLimit && !_refreshing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One card after the other, so the same artist on both is asked for once.</summary>
    private async Task ShowArtistImagesAsync(List<StatCard> cards)
    {
        var home = _services.Home;
        var token = _leaving.Token;
        foreach (var card in cards)
        {
            if (card.Summary.TopArtist?.Id is not { } id)
            {
                continue;
            }

            try
            {
                card.ShowArtistImage(home.KnownArtistImage(id) ?? await Task.Run(() => home.GetArtistImageAsync(id, token), token));
            }
            catch (Exception)
            {
                // The card keeps its colours.
            }
        }
    }

    /// <summary>For the screenshot tour: picks <paramref name="range"/> as a click would, and scrolls the lists into view.</summary>
    internal void PickTopRange(TopRange range)
    {
        TopRangeBar.SelectedItem = TopRangeBar.Items[(int)range];
        TopRangeBar.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
    }

    /// <summary>Picks the time range in the bar without treating it as the user's choice.</summary>
    private void SelectTopRange(TopRange range)
    {
        _topRange = range;
        _selectingRange = true;
        try
        {
            TopRangeBar.SelectedItem = TopRangeBar.Items[Math.Clamp((int)range, 0, TopRangeBar.Items.Count - 1)];
        }
        finally
        {
            _selectingRange = false;
        }
    }

    /// <summary>Spotify's top lists for the chosen time range; rebuilt only when they or the number shown change.</summary>
    private void ShowTop()
    {
        var top = _services.Home.Content?.TopFor(_topRange);
        var count = _topExpanded ? HomeFeed.TopSize : TopShownFirst;
        if (top != _shownTop || count != _shownTopCount)
        {
            _shownTop = top;
            _shownTopCount = count;
            TopArtists.Clear();
            TopSongs.Clear();
            var rank = 0;
            foreach (var artist in top?.Artists.Take(count) ?? [])
            {
                TopArtists.Add(new TopArtistRow(artist, ++rank));
            }

            rank = 0;
            foreach (var song in top?.Songs.Take(count) ?? [])
            {
                TopSongs.Add(new TopSongRow(song, ++rank));
            }
        }

        var any = TopArtists.Count > 0 || TopSongs.Count > 0;
        TopLists.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        TopNote.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        TopNote.Text = top is not null
            ? $"Spotify has no top artists or songs for the {RangeName(_topRange)} yet. They appear as you listen."
            : _refreshing ? "Asking Spotify…" : "Spotify has not shared your top artists and songs yet.";

        var more = top is not null && (top.Artists.Count > TopShownFirst || top.Songs.Count > TopShownFirst);
        TopMoreButton.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
        TopMoreButton.Content = _topExpanded ? "Show top 5" : "Show top 10";
    }

    private void OnTopRangeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = TopRangeBar.Items.IndexOf(TopRangeBar.SelectedItem);
        if (_selectingRange || index < 0 || (TopRange)index == _topRange)
        {
            return;
        }

        _topRange = (TopRange)index;
        _services.Settings.HomeTopRange = _topRange;
        _services.SaveSettings();
        ShowTop();
    }

    private void OnTopMoreClick(object sender, RoutedEventArgs e)
    {
        _topExpanded = !_topExpanded;
        ShowTop();
    }

    private void OnTopArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TopArtistRow row)
        {
            App.MainWindow?.Open(TrackActions.ArtistKey(row.Artist.Id));
        }
    }

    private void OnTopSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TopSongRow row && !IsSecondClick(row))
        {
            Play(row);
        }
    }

    /// <summary>Plays the top songs of the chosen time range in rank order, from <paramref name="row"/>'s song.</summary>
    private void Play(TopSongRow row)
    {
        if (_shownTop is not { } top)
        {
            return;
        }

        var index = top.Songs.IndexOf(row.Track);
        var name = $"Your top songs, {RangeName(top.Range)}";
        _ = _services.Player.PlayAsync(new PlayRequest(top.Songs, Math.Max(0, index), null, name));
    }

    private void OnTopSongContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TopSongRow>(TopSongList, args) is { } row)
        {
            TrackActions.ShowMenu(TrackActions.BuildMenu(row.Track, new TrackMenuOptions { Play = () => Play(row) }), TopSongList, args);
        }
    }

    /// <summary>Whether this click on <paramref name="item"/> is the second half of a double-click (see <see cref="DoubleClickMilliseconds"/>).</summary>
    private bool IsSecondClick(object item)
    {
        var now = Environment.TickCount64;
        if (item == _lastPlayed && now - _lastPlayedAt < DoubleClickMilliseconds)
        {
            return true;
        }

        _lastPlayed = item;
        _lastPlayedAt = now;
        return false;
    }

    private void ShowRecent(IReadOnlyList<PlayRecord> plays, DateTimeOffset now)
    {
        var newest = plays.Count > 0 ? plays[0] : null;
        if (newest != _shownNewest)
        {
            _shownNewest = newest;
            Recent.Clear();
            foreach (var play in ListeningStats.RecentSongs(plays, RecentCount))
            {
                Recent.Add(new RecentCard(play.ToTrack(), play.PlayedAt, now));
            }
        }

        RecentNote.Visibility = Recent.Count == 0 && !_refreshing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMixesNote()
    {
        var empty = Mixes.Count == 0;
        MixesNote.Text = _refreshing
            ? "Making your mixes…"
            : "Your daily mixes appear here once you have liked some songs. Each is built around an artist you love, from your Liked Songs, and changes every day.";
        MixesNote.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        MixGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnMixClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MixCard card)
        {
            App.MainWindow?.Open(card.Key);
        }
    }

    private void OnRecentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentCard card && !IsSecondClick(card))
        {
            Play(card);
        }
    }

    /// <summary>
    /// Plays the song inside its album, as Search does. The other recent
    /// songs are not sent with it: Resonate would take them for the album's
    /// songs, and shuffle would not work once the album moves past them.
    /// </summary>
    private void Play(RecentCard card) =>
        _ = _services.Player.PlayAsync(new PlayRequest([card.Track], 0, card.Track.AlbumUri, card.Track.Album));

    private void OnRecentContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<RecentCard>(RecentGrid, args) is { } card)
        {
            TrackActions.ShowMenu(TrackActions.BuildMenu(card.Track, new TrackMenuOptions { Play = () => Play(card) }), RecentGrid, args);
        }
    }
}
