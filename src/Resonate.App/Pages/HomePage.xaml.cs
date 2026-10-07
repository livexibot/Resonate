using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
using Resonate.Spotify.Auth;
using Resonate.Spotify.History;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

/// <summary>
/// Home: listening stats for the past day and week, the daily mixes, and
/// the songs played lately. What is stored shows at once; Spotify is asked
/// for what is new in the background.
/// </summary>
public sealed partial class HomePage : Page
{
    private const int RecentCount = 6;

    /// <summary>A second click on the same song this soon is the rest of a double-click, not another play.</summary>
    private const int DoubleClickMilliseconds = 500;

    private readonly AppServices _services = App.Services;
    private CancellationTokenSource _leaving = new();
    private HomeContent? _shownContent;
    private PlayRecord? _shownNewest;
    private bool _refreshing;
    private int _updateQueued;
    private RecentCard? _lastPlayed;
    private long _lastPlayedAt;

    public HomePage()
    {
        InitializeComponent();
    }

    public ObservableCollection<MixCard> Mixes { get; } = [];

    public ObservableCollection<RecentCard> Recent { get; } = [];

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
        ShowRecent(plays, now);

        var content = home.Content;
        if (content != _shownContent)
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
        if (e.ClickedItem is not RecentCard card
            || (card == _lastPlayed && Environment.TickCount64 - _lastPlayedAt < DoubleClickMilliseconds))
        {
            return;
        }

        _lastPlayed = card;
        _lastPlayedAt = Environment.TickCount64;
        Play(card);
    }

    /// <summary>Plays the song inside its album (as Search does), among the other recent songs if Spotify refuses the album.</summary>
    private void Play(RecentCard card)
    {
        var tracks = Recent.Select(r => r.Track).ToList();
        var index = tracks.IndexOf(card.Track);
        _ = _services.Player.PlayAsync(new PlayRequest(tracks, index, card.Track.AlbumUri, card.Track.Album));
    }

    private void OnRecentContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((args.OriginalSource as FrameworkElement)?.DataContext is RecentCard card)
        {
            TrackActions.ShowMenu(TrackActions.BuildMenu(card.Track, new TrackMenuOptions { Play = () => Play(card) }), RecentGrid, args);
        }
    }
}
