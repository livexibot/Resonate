using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Controls;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.App.ViewModels;
using Resonate.Spotify.Auth;
using Resonate.Spotify.History;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Pages;

/// <summary>A part of Home the screenshot tour scrolls to.</summary>
internal enum HomeSection
{
    Overview,
    Mixes,
    Top,
    Recent,
}

/// <summary>
/// Home: a greeting with what plays on a wash of its cover's colours,
/// listening stats for the past day and week with a chart of each, the daily
/// mixes, Spotify's own top artists and songs, and the songs played lately.
/// What is stored shows at once; Spotify is asked for what is new in the
/// background. Nothing on the page moves by itself once it has settled
/// (idle use is measured here), apart from the cards rising under the pointer.
/// </summary>
public sealed partial class HomePage : Page
{
    private const int RecentCount = 12;

    /// <summary>Top artists and songs shown until "Show top 10" is pressed.</summary>
    private const int TopShownFirst = 5;

    /// <summary>A second click on the same song this soon is the rest of a double-click, not another play.</summary>
    private const int DoubleClickMilliseconds = 500;

    // Widths of the page's content where its layout changes (see Fit). The
    // window's width alone would not do: the queue can take part of it.
    // From WideWidth the stats sit beside the greeting and all ten top
    // artists and songs show; below StackedWidth the stat cards stack.
    private const double WideWidth = 1480;
    private const double StackedWidth = 540;

    // What plays sits beside the greeting from this width of the greeting's card.
    private const double HeroSideBySideWidth = 700;
    private const double NowPlayingWidth = 360;

    // A card's width and the room the grid leaves after it, for the rows
    // that show only the cards that fit (a little more than the grid's own
    // gap, so a card never wraps onto a second row).
    private const double MixPitch = 176 + 8;

    private const string SunGlyph = "";
    private const string MoonGlyph = "";
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    private static readonly TimeSpan WashFade = TimeSpan.FromMilliseconds(600);

    // The content's width at the last visit, so later visits fit their rows before the first frame.
    private static double _lastWidth;

    private readonly AppServices _services = App.Services;
    private readonly List<MixCard> _allMixes = [];
    private readonly List<RecentCard> _allRecent = [];
    private CancellationTokenSource _leaving = new();
    private HomeContent? _shownContent;
    private PlayRecord? _shownNewest;
    private int _coverSizeShown;
    private PlayRecord? _lastPlay;

    // Whether the history has been shown, so _lastPlay is known.
    private bool _historyShown;
    private bool _refreshing;
    private int _updateQueued;
    private int _playerQueued;
    private object? _lastPlayed;
    private long _lastPlayedAt;
    private TopRange _topRange;
    private TopOnSpotify? _shownTop;
    private int _shownTopCount;
    private bool _shownTopSplit;
    private bool _topExpanded;
    private bool _mixesExpanded;
    private bool _recentExpanded;
    private bool _selectingRange;
    private double _width;
    private HomeLayout _layout;
    private bool _heroSideBySide;
    private object? _nowKey;
    private object? _coverKey;
    private object? _washKey;
    private ThemeColor? _washLight;
    private int _washVersion;
    private ImageSource? _shownWash;

    public HomePage()
    {
        InitializeComponent();

        // The playing song's artists open their pages.
        SongLinks.Attach(NowArtists, SongLinks.Artists, PlayingTrack.Get);
    }

    private enum HomeLayout
    {
        Unknown,

        /// <summary>The stat cards one above the other.</summary>
        Stacked,

        /// <summary>The stats under the greeting, side by side.</summary>
        Medium,

        /// <summary>The stats beside the greeting; the top lists in full.</summary>
        Wide,
    }

    public ObservableCollection<MixCard> Mixes { get; } = [];

    public ObservableCollection<RecentCard> Recent { get; } = [];

    public ObservableCollection<TopArtistRow> TopArtists { get; } = [];

    public ObservableCollection<TopSongRow> TopSongs { get; } = [];

    /// <summary>Top songs 6 to 10, beside 1 to 5 on a wide window.</summary>
    public ObservableCollection<TopSongRow> TopSongsMore { get; } = [];

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
        PermissionNote.Visibility = _services.Account.MissingScopes.Any(s => s is "user-read-recently-played" or "user-top-read")
            ? Visibility.Visible
            : Visibility.Collapsed;
        SelectTopRange(_services.Settings.HomeTopRange);
        ShowWashFade();
        if (_lastWidth > 0)
        {
            Fit(_lastWidth);
        }

        ShowGreeting();
        ShowScrim();
        ShowNowPlaying();

        _services.Home.Changed += OnHomeChanged;
        _services.Home.History.Changed += OnHomeChanged;
        _services.Player.StateChanged += OnPlayerChanged;
        _services.Theme.Changed += OnThemeChanged;
        _coverSizeShown = AppScale.Nearest(_services.Settings.CoverSize, AppScale.CoverSizes);
        TrackColumns.OptionsChanged += OnCoverOptionsChanged;
        OnStageNavigatedTo();
        OnRediscoverNavigatedTo();
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _services.Home.Changed -= OnHomeChanged;
        _services.Home.History.Changed -= OnHomeChanged;
        _services.Player.StateChanged -= OnPlayerChanged;
        _services.Theme.Changed -= OnThemeChanged;
        TrackColumns.OptionsChanged -= OnCoverOptionsChanged;
        OnStageNavigatedFrom();
        OnRediscoverNavigatedFrom();
        _leaving.Cancel();
    }

    /// <summary>Cover size changed in Settings (open beside Home): the top songs and recently played are made again at the new size.</summary>
    private void OnCoverOptionsChanged(object? sender, EventArgs e)
    {
        // Every song list option comes here; only a new Cover size touches Home.
        var size = AppScale.Nearest(_services.Settings.CoverSize, AppScale.CoverSizes);
        if (size == _coverSizeShown)
        {
            return;
        }

        _coverSizeShown = size;
        _shownTop = null;
        _shownNewest = null;
        Show();
    }

    // The built-in plugins on Home (see BuiltInPlugins), each in its own HomePage.<Name>.cs.
    partial void OnStageNavigatedTo();

    partial void OnStageNavigatedFrom();

    /// <summary>After the greeting's card shows what plays: the Home stage, when on, shows it instead.</summary>
    partial void OnStageNowPlayingShown();

    partial void OnRediscoverNavigatedTo();

    partial void OnRediscoverNavigatedFrom();

    /// <summary>For the screenshot tour: scrolls <paramref name="section"/> to the top of the page at once.</summary>
    internal void ScrollTo(HomeSection section)
    {
        FrameworkElement target = section switch
        {
            HomeSection.Mixes => MixesSection,
            HomeSection.Top => TopSection,
            HomeSection.Recent => RecentSection,
            _ => Overview,
        };
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.02 });
    }

    /// <summary>For the screenshot tour: picks <paramref name="range"/> as a click would, and scrolls the lists into view.</summary>
    internal void PickTopRange(TopRange range)
    {
        TopRangeBar.SelectedItem = TopRangeBar.Items[(int)range];
        TopRangeBar.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
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

    private void OnPlayerChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads, sometimes in bursts; look at the newest one once.
        if (Interlocked.Exchange(ref _playerQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _playerQueued, 0);
                if (!_leaving.IsCancellationRequested)
                {
                    ShowNowPlaying();
                }
            });
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // A light look gets a lighter wash and a light scrim; colours from the
        // shared brushes follow by themselves.
        ShowWashFade();
        ShowScrim();
        _nowKey = null;
        ShowNowPlaying();
    }

    /// <summary>A new wash fades in over <see cref="WashFade"/>; with Windows' animations off it simply appears.</summary>
    private void ShowWashFade() =>
        HeroWash.OpacityTransition = _services.Theme.AnimationsEnabled ? HeroWash.OpacityTransition ?? new ScalarTransition { Duration = WashFade } : null;

    /// <summary>Shows what is known now; parts that did not change are left alone, so nothing flickers.</summary>
    private void Show()
    {
        var home = _services.Home;
        if (!home.IsLoaded)
        {
            return;
        }

        _historyShown = true;
        StatsGrid.Visibility = Visibility.Visible;
        Sections.Visibility = Visibility.Visible;
        var plays = home.History.Plays;
        var now = DateTimeOffset.UtcNow;
        ShowGreeting();
        ShowToday(plays, now);
        ShowStats(plays, now);
        ShowTop();
        ShowMixes(home.Content);
        ShowRecent(plays, now);
        ShowNowPlaying();
        ShowMixesNote();
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
        {
            Fit(e.NewSize.Width - Body.Padding.Left - Body.Padding.Right);
        }
    }

    /// <summary>
    /// Lays the page out for the width of its content. Only crossing one of
    /// the widths above moves anything; the rows of cards take as many as fit.
    /// </summary>
    private void Fit(double width)
    {
        if (width <= 0)
        {
            return;
        }

        _width = width;
        _lastWidth = width;
        var layout = width >= WideWidth ? HomeLayout.Wide : width >= StackedWidth ? HomeLayout.Medium : HomeLayout.Stacked;
        if (layout != _layout)
        {
            _layout = layout;
            Arrange();
            ShowTop();
        }

        var heroWidth = layout == HomeLayout.Wide ? (width - Overview.ColumnSpacing) / 2 : width;
        ArrangeHero(heroWidth >= HeroSideBySideWidth);
        FitRows();
    }

    private void Arrange()
    {
        // The stats beside the greeting on a wide window, under it otherwise.
        var wide = _layout == HomeLayout.Wide;
        Grid.SetColumnSpan(Hero, wide ? 1 : 2);
        Grid.SetRow(StatsGrid, wide ? 0 : 1);
        Grid.SetColumn(StatsGrid, wide ? 1 : 0);
        Grid.SetColumnSpan(StatsGrid, wide ? 1 : 2);
        Overview.RowSpacing = wide ? 0 : 16;

        // The two cards side by side, or one above the other where there is little room.
        var stacked = _layout == HomeLayout.Stacked;
        Grid.SetRow(WeekStats, stacked ? 1 : 0);
        Grid.SetColumn(WeekStats, stacked ? 0 : 1);
        StatsGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        StatsGrid.ColumnSpacing = stacked ? 0 : 16;
        StatsGrid.RowSpacing = stacked ? 16 : 0;
    }

    /// <summary>What plays beside the greeting when the card is wide enough, under it otherwise.</summary>
    private void ArrangeHero(bool sideBySide)
    {
        _heroSideBySide = sideBySide;
        Grid.SetColumnSpan(HeroGreeting, sideBySide ? 1 : 2);
        Grid.SetRow(NowPlaying, sideBySide ? 0 : 1);
        Grid.SetColumn(NowPlaying, sideBySide ? 1 : 0);
        Grid.SetColumnSpan(NowPlaying, sideBySide ? 1 : 2);
        NowPlaying.Width = sideBySide ? NowPlayingWidth : double.NaN;
        HeroContent.RowSpacing = sideBySide || NowPlaying.Visibility == Visibility.Collapsed ? 0 : 22;
    }

    /// <summary>The rows of mixes and songs: as many cards as fit, or all of them once "Show all" is pressed.</summary>
    private void FitRows()
    {
        var mixesFit = Fitting(MixPitch);
        ShowFirst(Mixes, _allMixes, _mixesExpanded ? _allMixes.Count : mixesFit);
        MixesMoreButton.Visibility = _allMixes.Count > mixesFit ? Visibility.Visible : Visibility.Collapsed;
        MixesMoreButton.Content = _mixesExpanded ? "Show less" : "Show all";

        var recentFit = Fitting(RecentCard.CoverSize + 8);
        ShowFirst(Recent, _allRecent, _recentExpanded ? _allRecent.Count : recentFit);
        RecentMoreButton.Visibility = _allRecent.Count > recentFit ? Visibility.Visible : Visibility.Collapsed;
        RecentMoreButton.Content = _recentExpanded ? "Show less" : "Show all";
    }

    private int Fitting(double pitch) => Math.Max(1, (int)(_width / pitch));

    /// <summary>Makes <paramref name="shown"/> the first <paramref name="count"/> of <paramref name="all"/>, touching only the cards at its end.</summary>
    private static void ShowFirst<T>(ObservableCollection<T> shown, List<T> all, int count)
    {
        count = Math.Min(count, all.Count);
        while (shown.Count > count)
        {
            shown.RemoveAt(shown.Count - 1);
        }

        while (shown.Count < count)
        {
            shown.Add(all[shown.Count]);
        }
    }

    private void OnMixesMoreClick(object sender, RoutedEventArgs e)
    {
        _mixesExpanded = !_mixesExpanded;
        FitRows();
    }

    private void OnRecentMoreClick(object sender, RoutedEventArgs e)
    {
        _recentExpanded = !_recentExpanded;
        FitRows();
    }

    private void ShowGreeting()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var name = HomeText.FirstName(_services.Library.Snapshot?.User?.DisplayName);
        GreetingText.Text = name is null ? Greeting(now.Hour) : $"{Greeting(now.Hour)}, {name}";
        DateText.Text = (now.ToString("dddd", culture) + ", " + now.ToString(culture.DateTimeFormat.MonthDayPattern, culture)).ToUpper(culture);
        DayGlyph.Glyph = now.Hour is >= 6 and < 19 ? SunGlyph : MoonGlyph;
    }

    /// <summary>One line about today: "83 minutes · 26 songs today · mostly Mira Sol".</summary>
    private void ShowToday(IReadOnlyList<PlayRecord> plays, DateTimeOffset now)
    {
        var today = ListeningStats.Summarize(plays, now, now - ListeningStats.StartOfDay(now, TimeZoneInfo.Local));

        // With nothing played ever, no line at all (no explanatory sentences, the owner's rule).
        TodayText.Visibility = plays.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (today.Songs == 0)
        {
            TodayText.Text = plays.Count == 0 ? string.Empty : "Nothing played yet today.";
            return;
        }

        var text = $"{MinutesText(today.Listened.TotalMilliseconds)} · {Format.SongCount(today.Songs)} today";
        if (today.TopArtist is { } artist && today.TopArtistPlays > 1)
        {
            text += " · mostly " + artist.Name;
        }

        TodayText.Text = text;
    }

    /// <summary>
    /// The scrim keeps the greeting readable over any wash: the look's
    /// background, strongest behind the words. The song's card sits on its
    /// own nearly solid panel, so its smaller, fainter lines read over even
    /// a bright cover's colours.
    /// </summary>
    private void ShowScrim()
    {
        var palette = _services.Theme.Palette;
        var background = palette.Background.Opaque;
        var scrim = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        scrim.GradientStops.Add(new GradientStop { Color = background.WithAlpha(0.85).ToColor(), Offset = 0 });
        scrim.GradientStops.Add(new GradientStop { Color = background.WithAlpha(0.75).ToColor(), Offset = 0.5 });
        scrim.GradientStops.Add(new GradientStop { Color = background.WithAlpha(0.4).ToColor(), Offset = 1 });
        HeroScrim.Background = scrim;
        NowPlaying.Background = new SolidColorBrush(palette.Surface.Over(background).WithAlpha(0.88).ToColor());
    }

    /// <summary>What plays now, or what played last while nothing is loaded; changes only when the song, its state or its cover do.</summary>
    private void ShowNowPlaying()
    {
        var state = _services.Player.State;
        if (!state.HasTrack && !_historyShown)
        {
            // What played last is not known yet: wait for it rather than show the look's colours first.
            return;
        }

        var play = state.HasTrack ? null : _lastPlay;
        object key = state.HasTrack
            ? (state.TrackUri, state.Title, state.IsPlaying, state.SourceName, (object?)state.ArtworkUrl ?? state.ArtworkBytes)
            : (object?)play ?? "nothing";
        if (Equals(key, _nowKey))
        {
            return;
        }

        _nowKey = key;

        // The Home stage (always on) shows the song, so the card's own corner for it stays hidden and loads no cover; its wash still paints the card.
        if (_services.BuiltIns.IsOn(BuiltInPlugins.HomeStage))
        {
            string? url = null;
            byte[]? bytes = null;
            string? name = null;
            if (state.HasTrack)
            {
                (url, bytes, name) = (state.ArtworkUrl, state.ArtworkBytes, state.Album ?? state.Title ?? string.Empty);
            }
            else if (play is not null)
            {
                (url, name) = (play.ImageUrl, play.Album.Length > 0 ? play.Album : play.Title);
            }

            NowPlaying.Visibility = Visibility.Collapsed;
            ShowWash(url, bytes, name);
            OnStageNowPlayingShown();
            HeroContent.RowSpacing = 0;
            return;
        }

        if (state.HasTrack)
        {
            NowPlaying.Visibility = Visibility.Visible;
            NowLabel.Text = state.IsPlaying ? "NOW PLAYING" : "PAUSED";
            NowTitle.Text = state.Title;
            NowArtists.Text = state.Artists ?? string.Empty;
            NowSource.Text = state.SourceName is { Length: > 0 } source ? "From " + source : state.Album ?? string.Empty;
            HeroPlayGlyph.Glyph = state.IsPlaying ? PauseGlyph : PlayGlyph;
            HeroPlayText.Text = state.IsPlaying ? "Pause" : "Resume";
            var name = state.Album ?? state.Title ?? string.Empty;
            ShowCover(state.ArtworkUrl, state.ArtworkBytes, name);
            ShowWash(state.ArtworkUrl, state.ArtworkBytes, name);
        }
        else if (play is not null)
        {
            NowPlaying.Visibility = Visibility.Visible;
            NowLabel.Text = "LAST PLAYED";
            NowTitle.Text = play.Title;
            NowArtists.Text = string.Join(", ", play.Artists.Select(a => a.Name));
            NowSource.Text = PlaylistName(play.ContextUri) is { } playlist ? "From " + playlist : play.Album;
            HeroPlayGlyph.Glyph = PlayGlyph;
            HeroPlayText.Text = "Play";
            var name = play.Album.Length > 0 ? play.Album : play.Title;
            ShowCover(play.ImageUrl, null, name);
            ShowWash(play.ImageUrl, null, name);
        }
        else
        {
            NowPlaying.Visibility = Visibility.Collapsed;
            ShowWash(null, null, null);
        }

        OnStageNowPlayingShown();
        HeroContent.RowSpacing = _heroSideBySide || NowPlaying.Visibility == Visibility.Collapsed ? 0 : 22;
    }

    private string? PlaylistName(string? contextUri) =>
        contextUri is null ? null : _services.Library.Snapshot?.Playlists.FirstOrDefault(p => p.Uri == contextUri)?.Name;

    private void ShowCover(string? url, byte[]? bytes, string name)
    {
        // The cover's address or bytes, or, without one, the album's tile.
        object key = url ?? (object?)bytes ?? "tile:" + name;
        if (Equals(key, _coverKey))
        {
            return;
        }

        _coverKey = key;
        NowCover.Background = Artwork.PlaceholderBrush(name);
        if (url is not null)
        {
            NowImage.Source = Artwork.FromUrl(url, 104);
        }
        else if (bytes is not null)
        {
            NowImage.Source = null;
            _ = LoadCoverBytesAsync(bytes);
        }
        else
        {
            NowImage.Source = null;
        }
    }

    private async Task LoadCoverBytesAsync(byte[] bytes)
    {
        // Through Windows' own stream, like every cover made from bytes (see CoverImages).
        var (image, loaded) = await CoverImages.FromBytesAsync(bytes, 104);
        if (loaded && ReferenceEquals(_coverKey, bytes) && !_leaving.IsCancellationRequested)
        {
            NowImage.Source = image;
        }
    }

    /// <summary>
    /// The greeting's background: a wash of the cover's colours while the
    /// look's "Colours follow the cover" is on, otherwise (and with no song at
    /// all) of the look's accents, lighter on a light look. It is made off
    /// this thread's back, and fades over once; nothing moves afterwards.
    /// </summary>
    private void ShowWash(string? url, byte[]? bytes, string? name)
    {
        var palette = _services.Theme.Palette;
        ThemeColor? light = palette.IsLight ? palette.Background.Opaque : null;
        var look = _services.Theme.Current;
        if (!StageColours.FollowsCover(look))
        {
            (url, bytes, name) = (null, null, null);
        }

        object key = url ?? (object?)bytes ?? (object?)name ?? (look.Accent, look.Accent2);
        if (Equals(key, _washKey) && light == _washLight)
        {
            return;
        }

        _washKey = key;
        _washLight = light;
        _ = LoadWashAsync(url, bytes, name, light, ++_washVersion);
    }

    private async Task LoadWashAsync(string? url, byte[]? bytes, string? name, ThemeColor? light, int version)
    {
        var token = _leaving.Token;
        ImageSource wash;
        try
        {
            wash = await _services.Artwork.GetWashAsync(url, bytes, name, light, token);
        }
        catch (Exception)
        {
            // Left the page, or the picture could not be made: the card keeps its surface.
            return;
        }

        if (version != _washVersion || token.IsCancellationRequested || ReferenceEquals(wash, _shownWash))
        {
            return;
        }

        if (_shownWash is not null && HeroWash.OpacityTransition is not null)
        {
            // Out with the old colours first, then in with the new.
            HeroWash.Opacity = 0;
            try
            {
                await Task.Delay(WashFade, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (version != _washVersion)
            {
                return;
            }
        }

        _shownWash = wash;
        HeroWashBrush.ImageSource = wash;
        HeroWash.Opacity = 1;
    }

    private void OnHeroPlayClick(object sender, RoutedEventArgs e)
    {
        if (_services.Player.State.HasTrack)
        {
            // The player shows the new state at once; the card follows it.
            _ = _services.Player.TogglePlayPauseAsync();
        }
        else if (_lastPlay is { } play)
        {
            var track = play.ToTrack();
            _ = _services.Player.PlayAsync(new PlayRequest([track], 0, track.AlbumUri, track.Album));
        }
    }

    private void ShowStats(IReadOnlyList<PlayRecord> plays, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.Local;
        var day = ListeningStats.Summarize(plays, now, ListeningStats.Day);
        var week = ListeningStats.Summarize(plays, now, ListeningStats.Week);
        var hourBars = HourBars(ListeningStats.Hourly(plays, now, zone), ListeningStats.HourStarts(now, zone));
        var dayBars = DayBars(ListeningStats.Daily(plays, now, zone), now);
        var trend = Trend(ListeningStats.Change(plays, now, ListeningStats.Week));
        var added = new List<StatCard>();
        if (DayStats.Content is not StatCard shownDay || shownDay.Summary != day || !shownDay.Bars.SameAs(hourBars))
        {
            var card = new StatCard("PAST 24 HOURS", day, hourBars, "Nothing played in the past 24 hours.");
            DayStats.Content = card;
            added.Add(card);
        }

        if (WeekStats.Content is not StatCard shownWeek || shownWeek.Summary != week || !shownWeek.Bars.SameAs(dayBars) || shownWeek.TrendTip != (trend?.Full ?? string.Empty))
        {
            var card = new StatCard("PAST 7 DAYS", week, dayBars, "Nothing played in the past week.", trend);
            WeekStats.Content = card;
            added.Add(card);
        }

        if (added.Count > 0)
        {
            _ = ShowArtistImagesAsync(added);
        }
    }

    /// <summary>The past day's chart: a bar per clock hour, this hour last and in the accent, a time under every sixth.</summary>
    private static BarSeries HourBars(long[] listened, DateTimeOffset[] starts)
    {
        var culture = CultureInfo.CurrentCulture;
        var pattern = culture.DateTimeFormat.ShortTimePattern.Contains('t', StringComparison.Ordinal) ? "h tt" : "HH:mm";
        var count = listened.Length;
        var values = new double[count];
        var labels = new string?[count];
        var tips = new string[count];
        for (var i = 0; i < count; i++)
        {
            var hour = starts[i];
            var time = hour.ToString(pattern, culture);
            values[i] = listened[i];
            labels[i] = hour.Hour % 6 == 0 ? time : null;
            tips[i] = (i == count - 1 ? "This hour" : "From " + time) + " · " + MinutesText(listened[i]);
        }

        return new BarSeries(values, count - 1, labels, 6, tips);
    }

    /// <summary>The past week's chart: a bar per day, today last and in the accent.</summary>
    private static BarSeries DayBars(long[] listened, DateTimeOffset now)
    {
        var names = CultureInfo.CurrentCulture.DateTimeFormat;
        var today = now.ToLocalTime().Date;
        var count = listened.Length;
        var values = new double[count];
        var labels = new string?[count];
        var tips = new string[count];
        for (var i = 0; i < count; i++)
        {
            var day = today.AddDays(i - (count - 1));
            values[i] = listened[i];
            labels[i] = names.GetAbbreviatedDayName(day.DayOfWeek);
            tips[i] = (i == count - 1 ? "Today" : names.GetDayName(day.DayOfWeek)) + " · " + MinutesText(listened[i]);
        }

        return new BarSeries(values, count - 1, labels, 1, tips);
    }

    private static string MinutesText(double milliseconds)
    {
        var minutes = (int)Math.Round(milliseconds / 60_000);
        return minutes == 1 ? "1 minute" : $"{minutes:N0} minutes";
    }

    /// <summary>"▲ 23%" on the card and "▲ 23% on the week before" in its tip, or null when there is nothing fair to compare with.</summary>
    private static (string Short, string Full)? Trend(double? change) => change switch
    {
        null => null,
        >= 0.005 => ($"▲ {change.Value:P0}", $"▲ {change.Value:P0} on the week before"),
        <= -0.005 => ($"▼ {-change.Value:P0}", $"▼ {-change.Value:P0} on the week before"),
        _ => ($"{0:P0}", "As much as the week before"),
    };

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

    /// <summary>
    /// Spotify's top lists for the chosen time range; rebuilt only when they,
    /// the number shown or the layout change. A wide window shows all ten,
    /// the songs in two columns.
    /// </summary>
    private void ShowTop()
    {
        var top = _services.Home.Content?.TopFor(_topRange);
        var wide = _layout == HomeLayout.Wide;
        var count = wide || _topExpanded ? HomeFeed.TopSize : TopShownFirst;
        if (top != _shownTop || count != _shownTopCount || wide != _shownTopSplit)
        {
            _shownTop = top;
            _shownTopCount = count;
            _shownTopSplit = wide;
            TopArtists.Clear();
            TopSongs.Clear();
            TopSongsMore.Clear();
            var rank = 0;
            foreach (var artist in top?.Artists.Take(count) ?? [])
            {
                TopArtists.Add(new TopArtistRow(artist, ++rank));
            }

            rank = 0;
            foreach (var song in top?.Songs.Take(count) ?? [])
            {
                var row = new TopSongRow(song, ++rank);
                (wide && rank > TopShownFirst ? TopSongsMore : TopSongs).Add(row);
            }
        }

        var split = TopSongsMore.Count > 0;
        TopSongListMore.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        TopSongsGrid.ColumnDefinitions[1].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TopSongsGrid.ColumnSpacing = split ? 24 : 0;
        TopSongsCard.Visibility = TopSongs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var any = TopArtists.Count > 0 || TopSongs.Count > 0;
        TopLists.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        TopNote.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        TopNote.Text = top is not null
            ? $"Spotify has no top artists or songs for the {RangeName(_topRange)} yet. They appear as you listen."
            : _refreshing ? "Asking Spotify…" : "Spotify has not shared your top artists and songs yet.";

        var more = !wide && top is not null && (top.Artists.Count > TopShownFirst || top.Songs.Count > TopShownFirst);
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

    private void OnTopSongContextRequested(UIElement sender, ContextRequestedEventArgs args) => ShowTopSongMenu(TopSongList, args);

    private void OnTopSongMoreContextRequested(UIElement sender, ContextRequestedEventArgs args) => ShowTopSongMenu(TopSongListMore, args);

    private void ShowTopSongMenu(ListView list, ContextRequestedEventArgs args)
    {
        if (ListEvents.ContextRequested<TopSongRow>(list, args) is { } row)
        {
            TrackActions.ShowMenu(TrackActions.BuildMenu(row.Track, new TrackMenuOptions { Play = () => Play(row) }), list, args);
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

    /// <summary>The mix cards, made again only when the mixes or "On repeat" change (new top lists alone leave them).</summary>
    private void ShowMixes(HomeContent? content)
    {
        if (content?.Mixes == _shownContent?.Mixes && content?.OnRepeat == _shownContent?.OnRepeat)
        {
            return;
        }

        _shownContent = content;
        _allMixes.Clear();
        Mixes.Clear();
        foreach (var mix in content?.Mixes ?? [])
        {
            var number = mix.Number.ToString(CultureInfo.CurrentCulture);
            _allMixes.Add(new MixCard(DailyMixSource.KeyFor(mix.Number), mix.Title, mix.Subtitle, "DAILY MIX", number, DailyMixSource.Glyph, mix.ImageUrl, mix.Tracks));
        }

        if (content is { OnRepeat.Count: > 0 })
        {
            _allMixes.Add(new MixCard(OnRepeatSource.ListKey, "On repeat", "Your most played lately", "ON REPEAT", null, OnRepeatSource.Glyph, null, content.OnRepeat));
        }

        FitRows();
    }

    private void ShowMixesNote()
    {
        var empty = _allMixes.Count == 0;
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

    private void ShowRecent(IReadOnlyList<PlayRecord> plays, DateTimeOffset now)
    {
        var newest = plays.Count > 0 ? plays[0] : null;
        if (newest != _shownNewest)
        {
            _shownNewest = newest;
            _allRecent.Clear();
            Recent.Clear();
            var recent = ListeningStats.RecentSongs(plays, RecentCount);
            _lastPlay = recent.Count > 0 ? recent[0] : null;
            foreach (var play in recent)
            {
                _allRecent.Add(new RecentCard(play.ToTrack(), play.PlayedAt, now));
            }

            FitRows();
        }

        RecentNote.Visibility = _allRecent.Count == 0 && !_refreshing ? Visibility.Visible : Visibility.Collapsed;
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
