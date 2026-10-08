using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Resonate.App.Controls;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.App.ViewModels;
using Resonate.Plugins;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;
using Windows.Graphics;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App;

public sealed partial class MainWindow : Window
{
    public const string HomeKey = "home";
    public const string SearchKey = "search";
    public const string LikedSongsKey = LikedSongsSource.ListKey;
    public const string LocalFilesKey = "local";
    public const string DjKey = "dj";
    public const string SettingsKey = "settings";
    public const string ArtistPrefix = "artist:";

    private const int HistoryLimit = 50;

    /// <summary>The sidebar's usual width, and how narrow and wide it can be dragged.</summary>
    private const double SidebarDefaultWidth = 272;
    private const double SidebarMinWidth = 200;
    private const double SidebarMaxWidth = 520;

    /// <summary>How narrow and wide the queue pane can be dragged.</summary>
    private const double QueueMinWidth = 280;
    private const double QueueMaxWidth = 600;

    /// <summary>How wide the Settings pane can be dragged (its narrowest is <see cref="SettingsPane.MinimumWidth"/>).</summary>
    private const double SettingsMaxWidth = 960;

    /// <summary>The page keeps at least this much room when a panel is dragged wider.</summary>
    private const double PageMinWidth = 380;

    /// <summary>The update bar's width in the page's corner, when the page is wide enough.</summary>
    private const double UpdateBarWidth = 380;

    /// <summary>
    /// How often the listening history is saved while Resonate is open.
    /// Spotify only shares the last 50 songs played (at least 100 minutes
    /// of music), so this keeps every play even without opening Home.
    /// </summary>
    private static readonly TimeSpan ListeningHistoryInterval = TimeSpan.FromMinutes(30);

    private readonly AppServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _messageTimer;
    private readonly List<string> _history = [];
    private readonly ColumnDefinition _paneColumn = new() { Width = new GridLength(Controls.QueuePanel.PaneWidth) };
    private string? _currentKey;
    private bool _syncingSelection;
    private bool _backgroundStarted;
    private OwnPlayerStatus _ownPlayerStatusShown;
    private bool _firstFrameSeen;
    private bool _quitting;
    private double _dragStartWidth;
    private bool _dragMoved;
    private bool _updateBarDismissed;
    private int _updateProgressQueued;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        SetMinimumSize();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        BackButton.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        TitleBarButtons.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
        AppWindow.Title = AppName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Resonate.ico"));
        RestorePlacement();

        // The look's backdrop goes behind the content, and switching looks animates above it.
        var content = RootGrid;
        Content = null;
        var themeHost = new ThemeHost(content, services);
        Content = themeHost;
        services.Theme.AttachWindow(this, themeHost);
        services.Theme.Changed += (_, _) => ApplyCaptionButtonColors();
        ApplyCaptionButtonColors();
        SetUpAppSize();

        _messageTimer = DispatcherQueue.CreateTimer();
        _messageTimer.Interval = TimeSpan.FromSeconds(7);
        _messageTimer.IsRepeating = false;
        _messageTimer.Tick += (_, _) => MessageBar.IsOpen = false;

        SetUpSplitters();

        PlayerBar.Attach(services.Player);
        services.Player.StateChanged += OnNowPlayingChanged;
        PlayerBar.QueueRequested += (_, _) => ToggleQueue();
        QueuePane.CloseRequested += (_, _) => ShowQueue(false);
        services.Player.ErrorOccurred += (_, message) =>
            DispatcherQueue.TryEnqueue(() => ShowMessage(message, InfoBarSeverity.Warning));
        // Back to Windows' media controls starts the Spotify app hidden; Web API only closes it.
        services.ControlChannelChanged += (_, _) => _ = FollowSpotifyAppAsync(_lifetime.Token);
        services.Library.PlaylistsChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ShowPlaylists(_services.Library.Snapshot));
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);
        BuildPlaylistSortMenu();
        services.Account.SignedOut += (_, _) => DispatcherQueue.TryEnqueue(ShowSignIn);
        if (services.OwnPlayer is { } ownPlayer)
        {
            ownPlayer.StatusChanged += OnOwnPlayerStatusChanged;
        }

        services.Updates.UpdateReady += (_, _) => DispatcherQueue.TryEnqueue(ShowUpdateReady);
        services.Updates.ProgressChanged += OnUpdateProgressChanged;
        SetUpSettingsPane();
        services.Plugins.Notified += (_, note) =>
            DispatcherQueue.TryEnqueue(() => ShowMessage($"{note.PluginName}: {note.Text}", InfoBarSeverity.Informational));
        PlayerBar.AttachPlugins(services.Plugins);
        SetUpPlayerPlacement();
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += (_, args) => args.Cancel = !ReadyToClose();
        Closed += OnClosed;

        if (services.Account.IsSignedIn)
        {
            ShowShell();
        }
        else
        {
            ShowSignIn();
        }

        CompositionTarget.Rendering += OnFirstFrame;
        SetUpLocalFiles();
        SetUpClassicPlayer();
        SetUpBuiltInPlugins();
    }

    /// <summary>False while the window is minimised or hidden: clocks and endless animations rest then.</summary>
    public bool IsShown { get; private set; } = true;

    /// <summary>Raised on the interface thread when <see cref="IsShown"/> changes.</summary>
    public event EventHandler? ShownChanged;

    public ObservableCollection<NavItem> NavItems { get; } =
    [
        new(HomeKey, "\uE80F", "Home"),
        new(SearchKey, "\uE721", "Search"),
        new(LikedSongsKey, "\uEB51", "Liked Songs"),
        new(LocalFilesKey, "\uE8B7", "Local Files"),
        new(DjKey, "\uE7F6", "DJ"),
    ];

    public ObservableCollection<PlaylistNavItem> Playlists { get; } = [];

    /// <summary>Shows the library and pages, with what is known right away (the cached sidebar).</summary>
    public void ShowShell()
    {
        SignInFrame.Visibility = Visibility.Collapsed;
        SignInFrame.Content = null;
        ShellGrid.Visibility = Visibility.Visible;
        ShowTitleBarButtons();
        ApplyPlayerStyle();

        ShowPlaylists(_services.Library.Snapshot);
        _history.Clear();
        _currentKey = null;
        Open(HomeKey);

        if (!_backgroundStarted)
        {
            _backgroundStarted = true;
            _ = StartBackgroundWorkAsync();
        }
        else
        {
            // Signed in again, perhaps as someone else: their playlists and hearts, and the player on this PC.
            _ = RefreshLibraryAsync();
            _ = LoadLikesAsync(_lifetime.Token);
            _services.FollowOwnPlayer();
        }
    }

    /// <summary>Opens the queue pane next to the pages, or closes it (the player bar's queue button).</summary>
    public void ToggleQueue() => ShowQueue(!QueuePane.IsOpen);

    /// <summary>Opens Settings in the pane on the right, or closes it (the gear in the title bar).</summary>
    public void ToggleSettings() => ShowSettings(!SettingsPane.IsOpen);

    public void CloseSettings() => ShowSettings(false);

    /// <summary>The Settings page while the Settings pane is open.</summary>
    internal SettingsPage? SettingsPage => SettingsPane.Page;

    /// <summary>
    /// Settings and the mini player button in the title bar, while the library
    /// shows; the mini player button only while the user keeps it (Settings, Layout).
    /// </summary>
    internal void ShowTitleBarButtons()
    {
        var shell = ShellGrid.Visibility == Visibility.Visible;
        SettingsButton.Visibility = shell ? Visibility.Visible : Visibility.Collapsed;
        MiniPlayerButton.Visibility = shell && _services.Settings.ShowMiniPlayerButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleBarPassthrough();
    }

    private void SetUpSettingsPane()
    {
        SettingsPane.CloseRequested += (_, _) => ShowSettings(false);

        // The update bar stays inside a page narrowed by Settings or the queue
        // (16 px from each edge, inside the page's outline).
        ContentPanel.SizeChanged += (_, e) =>
        {
            var outline = ContentPanel.BorderThickness.Left + ContentPanel.BorderThickness.Right;
            UpdateBar.Width = Math.Clamp(e.NewSize.Width - outline - 32, 0, UpdateBarWidth);
        };
    }

    private void ShowQueue(bool open)
    {
        if (open == QueuePane.IsOpen)
        {
            return;
        }

        // One pane at a time on the right, so the page keeps its room.
        if (open)
        {
            ShowSettings(false);
            ShowLyrics(false);
        }

        QueuePane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            QueuePane.Open(_services.Player);
        }
        else
        {
            QueuePane.Close();
        }

        PlaceRightPane();
    }

    private void ShowSettings(bool open)
    {
        if (open == SettingsPane.IsOpen)
        {
            return;
        }

        if (open)
        {
            ShowQueue(false);
            ShowLyrics(false);
        }

        SettingsPane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            SettingsPane.Open();
        }
        else
        {
            SettingsPane.Close();
        }

        PlaceRightPane();
    }

    /// <summary>
    /// The queue and Settings share the column on the right. It exists only
    /// while one of them is open: the grid's spacing would otherwise leave a
    /// gap for an empty column at the right edge.
    /// </summary>
    private void PlaceRightPane()
    {
        var open = QueuePane.IsOpen || SettingsPane.IsOpen || LyricsPane.IsOpen;
        RightSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        RightSplitter.Label = SettingsPane.IsOpen ? "Resize Settings" : LyricsPane.IsOpen ? "Resize the lyrics" : "Resize the queue";
        var shown = ShellGrid.ColumnDefinitions.Contains(_paneColumn);
        if (open && !shown)
        {
            ShellGrid.ColumnDefinitions.Add(_paneColumn);
        }
        else if (!open && shown)
        {
            ShellGrid.ColumnDefinitions.Remove(_paneColumn);
        }

        LayOutPanes();
        QueueOpenChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Resizing the sidebar and the pane on the right ----

    private void SetUpSplitters()
    {
        SidebarSplitter.Label = "Resize the sidebar";
        SidebarSplitter.DragStarted += (_, _) => StartDrag(SidebarColumn);
        SidebarSplitter.Dragged += (_, moved) =>
        {
            _dragMoved = true;
            LayOutPanes(sidebar: _dragStartWidth + moved);
        };
        SidebarSplitter.DragCompleted += (_, _) => EndDrag();
        SidebarSplitter.Stepped += (_, step) =>
        {
            LayOutPanes(sidebar: SidebarColumn.Width.Value + step);
            KeepPaneWidths();
        };
        SidebarSplitter.ResetRequested += (_, _) =>
        {
            _services.Settings.SidebarWidth = null;
            _services.SaveSettings();
            LayOutPanes();
        };

        // The queue or Settings is on the right, so moving the grip right makes it narrower.
        RightSplitter.DragStarted += (_, _) => StartDrag(_paneColumn);
        RightSplitter.Dragged += (_, moved) =>
        {
            _dragMoved = true;
            LayOutPanes(pane: _dragStartWidth - moved);
        };
        RightSplitter.DragCompleted += (_, _) => EndDrag();
        RightSplitter.Stepped += (_, step) =>
        {
            LayOutPanes(pane: _paneColumn.Width.Value - step);
            KeepPaneWidths();
        };
        RightSplitter.ResetRequested += (_, _) =>
        {
            if (SettingsPane.IsOpen)
            {
                _services.Settings.SettingsPaneWidth = null;
            }
            else
            {
                _services.Settings.QueueWidth = null;
            }

            _services.SaveSettings();
            LayOutPanes();
        };

        // The gap between panels belongs to the look, which can change at any time.
        ShellGrid.RegisterPropertyChangedCallback(Grid.ColumnSpacingProperty, (_, _) => PlaceSplitters());
        ShellGrid.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width)
            {
                LayOutPanes();
            }
        };
        PlaceSplitters();
        LayOutPanes();
    }

    private void StartDrag(ColumnDefinition column)
    {
        _dragStartWidth = column.Width.Value;
        _dragMoved = false;
    }

    // A click on a grip is not a drag: it must not keep a width that a small window squeezed the panel to.
    private void EndDrag()
    {
        if (_dragMoved)
        {
            KeepPaneWidths();
        }
    }

    /// <summary>Centres each grip on the gap between its panels, however wide the look makes that gap.</summary>
    private void PlaceSplitters()
    {
        var reach = -((ShellGrid.ColumnSpacing / 2) + (PaneSplitter.GripWidth / 2));
        SidebarSplitter.Margin = new Thickness(0, 0, reach, 0);
        RightSplitter.Margin = new Thickness(reach, 0, 0, 0);
    }

    /// <summary>
    /// Sizes the sidebar and the pane on the right (the queue or Settings):
    /// as the user last dragged them (or <paramref name="sidebar"/> or
    /// <paramref name="pane"/> while dragging), within their limits, and never
    /// so wide that the page between them has less than <see cref="PageMinWidth"/>.
    /// The sidebar comes first; the pane takes what is left.
    /// </summary>
    private void LayOutPanes(double? sidebar = null, double? pane = null)
    {
        var settings = _services.Settings;
        var gap = ShellGrid.ColumnSpacing;

        // Before the first layout there is no room to measure: only the limits apply.
        var room = ShellGrid.ActualWidth > 0
            ? ShellGrid.ActualWidth - ShellGrid.Padding.Left - ShellGrid.Padding.Right
            : double.PositiveInfinity;
        var paneOpen = QueuePane.IsOpen || SettingsPane.IsOpen || LyricsPane.IsOpen;
        var (paneMin, paneMax, paneWanted) = SettingsPane.IsOpen
            ? (SettingsPane.MinimumWidth, SettingsMaxWidth, settings.SettingsPaneWidth ?? SettingsPane.DefaultWidth)
            : (QueueMinWidth, QueueMaxWidth, settings.QueueWidth ?? QueuePanel.PaneWidth);

        double sidebarWidth;
        if (pane is not null)
        {
            // Dragging the pane leaves the sidebar where it is.
            sidebarWidth = SidebarColumn.Width.Value;
        }
        else
        {
            var others = gap + PageMinWidth + (paneOpen ? gap + paneMin : 0);
            sidebarWidth = Fit(sidebar ?? settings.SidebarWidth ?? SidebarDefaultWidth, SidebarMinWidth, SidebarMaxWidth, room - others);
        }

        var paneWidth = Fit(pane ?? paneWanted, paneMin, paneMax, room - sidebarWidth - (2 * gap) - PageMinWidth);
        SetWidth(SidebarColumn, sidebarWidth);
        SetWidth(_paneColumn, paneWidth);

        // A window too small for everything gives each panel its least, so the page keeps what it can.
        static double Fit(double wanted, double min, double max, double room) =>
            Math.Round(Math.Clamp(wanted, min, Math.Clamp(room, min, max)));

        static void SetWidth(ColumnDefinition column, double width)
        {
            if (column.Width.Value != width || !column.Width.IsAbsolute)
            {
                column.Width = new GridLength(width);
            }
        }
    }

    /// <summary>A drag or an arrow key ended: the panels keep these widths, next time too.</summary>
    private void KeepPaneWidths()
    {
        var settings = _services.Settings;
        settings.SidebarWidth = SidebarColumn.Width.Value == SidebarDefaultWidth ? null : SidebarColumn.Width.Value;
        var paneWidth = _paneColumn.Width.Value;
        if (SettingsPane.IsOpen)
        {
            settings.SettingsPaneWidth = paneWidth == SettingsPane.DefaultWidth ? null : paneWidth;
        }
        else if (QueuePane.IsOpen || LyricsPane.IsOpen)
        {
            settings.QueueWidth = paneWidth == QueuePanel.PaneWidth ? null : paneWidth;
        }

        _services.SaveSettings();
    }

    public void ShowSignIn()
    {
        LeaveMiniPlayer();
        ShowQueue(false);
        ShowSettings(false);
        ShowLyrics(false);
        ShellGrid.Visibility = Visibility.Collapsed;
        ShowTitleBarButtons();
        ApplyPlayerStyle();
        SignInFrame.Visibility = Visibility.Visible;
        SignInFrame.Navigate(typeof(SignInPage), null, new SuppressNavigationTransitionInfo());
        _currentKey = null;

        // Signed out: Spotify's player on this PC goes too.
        _services.FollowOwnPlayer();

        // Back leads nowhere from the sign-in page; signing in starts again at Home.
        _history.Clear();
        BackButton.Visibility = Visibility.Collapsed;
        UpdateTitleBarPassthrough();
    }

    /// <summary>
    /// Opens a page by its key: "home", "search", "liked", "local", "dj",
    /// "settings", "album:&lt;id&gt;", "artist:&lt;id&gt;", or a playlist ID.
    /// Its sidebar entry is selected when it has one.
    /// </summary>
    public void Open(string key)
    {
        // A page needs this window: from the mini player (a song's album, say) it comes back.
        LeaveMiniPlayer();
        if (key == SettingsKey)
        {
            // Settings is a pane next to the page, not a page.
            ShowSettings(true);
            return;
        }

        if (key == NewSmartPlaylistKey)
        {
            // Not a page: it makes a smart playlist, then opens it (MainWindow.SmartPlaylists.cs).
            DispatcherQueue.TryEnqueue(OpenNewSmartPlaylist);
            return;
        }

        SelectNav(key);
        Navigate(key, remember: true);
    }

    public void OpenPlaylist(string playlistId) => Open(playlistId);

    public void OpenSettings()
    {
        LeaveMiniPlayer();
        ShowSettings(true);
    }

    public void OpenSearch() => Open(SearchKey);

    /// <summary>Goes back to the page before, like a browser's Back button.</summary>
    public void GoBack()
    {
        if (_history.Count == 0 || ShellGrid.Visibility != Visibility.Visible)
        {
            return;
        }

        var key = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        SelectNav(key);
        Navigate(key, remember: false);
    }

    /// <summary>The page on show, such as a <see cref="HomePage"/>.</summary>
    internal object? CurrentPage => ContentFrame.Content;

    /// <summary>A list was played; remembered for the "Recently played" playlist order, and so the player bar can open it.</summary>
    public void NoteListPlayed(string key, string? name)
    {
        _lastPlayed = (key, name);
        if (!Playlists.Any(p => p.Id == key))
        {
            return;
        }

        _services.Settings.PlaylistLastPlayed[key] = DateTimeOffset.UtcNow;
        _services.SaveSettings();
        if (_services.Settings.ParsedPlaylistSort == PlaylistSortMode.RecentlyPlayed)
        {
            ShowPlaylists(_services.Library.Snapshot);
        }
    }

    public void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageBar.ActionButton = null;
        MessageBar.Title = string.Empty;
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsClosable = true;
        MessageBar.IsOpen = true;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void Navigate(string key, bool remember)
    {
        if (_currentKey == key)
        {
            return;
        }

        if (remember && _currentKey is not null)
        {
            _history.Add(_currentKey);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }

        _currentKey = key;
        BackButton.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleBarPassthrough();

        NavigationTransitionInfo transition = remember
            ? new EntranceNavigationTransitionInfo()
            : new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft };
        switch (key)
        {
            case HomeKey:
                ContentFrame.Navigate(typeof(HomePage), null, transition);
                break;
            case SearchKey:
                ContentFrame.Navigate(typeof(SearchPage), null, transition);
                break;
            case DjKey:
                ContentFrame.Navigate(typeof(DjPage), null, transition);
                break;
            case not null when key.StartsWith(ArtistPrefix, StringComparison.Ordinal):
                ContentFrame.Navigate(typeof(ArtistPage), key[ArtistPrefix.Length..], transition);
                break;
            default:
                ContentFrame.Navigate(typeof(TracksPage), key, remember ? new DrillInNavigationTransitionInfo() : transition);
                break;
        }
    }

    private void SelectNav(string key)
    {
        _syncingSelection = true;
        try
        {
            NavList.SelectedItem = NavItems.FirstOrDefault(n => n.Key == key);
            PlaylistList.SelectedItem = Playlists.FirstOrDefault(p => p.Id == key);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Also when the window chose the link itself (back, or a page opened elsewhere).
        MoveNavPill(glide: true);
        if (_syncingSelection || NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        Open(item.Key);
    }

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || PlaylistList.SelectedItem is not PlaylistNavItem item)
        {
            return;
        }

        Open(item.Id);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ToggleSettings();

    private void OnBackClick(object sender, RoutedEventArgs e) => GoBack();

    private void OnNewPlaylistClick(object sender, RoutedEventArgs e) => _ = CreatePlaylistAsync();

    private async Task CreatePlaylistAsync()
    {
        if (await TrackActions.CreatePlaylistAsync() is { } playlist)
        {
            Open(playlist.Id);
        }
    }

    private void ShowPlaylists(LibrarySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            // Nothing known yet (first start, or just signed out).
            Playlists.Clear();
            return;
        }

        var settings = _services.Settings;
        var sorted = PlaylistSorter.Apply(snapshot.Playlists.ToList(), settings.ParsedPlaylistSort, settings.PlaylistOrder, settings.PlaylistLastPlayed);
        var selected = _currentKey;
        _syncingSelection = true;
        try
        {
            Playlists.Clear();
            foreach (var playlist in sorted)
            {
                var item = new PlaylistNavItem(playlist);
                MarkNowPlaying(item);
                Playlists.Add(item);
            }

            PlaylistList.SelectedItem = Playlists.FirstOrDefault(p => p.Id == selected);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void BuildPlaylistSortMenu()
    {
        var current = _services.Settings.ParsedPlaylistSort;
        PlaylistSortMenu.Items.Clear();
        foreach (var (mode, name) in new[]
        {
            (PlaylistSortMode.Spotify, "Recently added"),
            (PlaylistSortMode.RecentlyPlayed, "Recently played"),
            (PlaylistSortMode.Alphabetical, "Alphabetical"),
            (PlaylistSortMode.Creator, "Creator"),
            (PlaylistSortMode.Custom, "Custom order (drag to arrange)"),
        })
        {
            var item = new RadioMenuFlyoutItem { Text = name, GroupName = "playlists", IsChecked = mode == current };
            item.Click += (_, _) => SetPlaylistSort(mode);
            PlaylistSortMenu.Items.Add(item);
        }
    }

    private void SetPlaylistSort(PlaylistSortMode mode)
    {
        var settings = _services.Settings;
        if (mode == PlaylistSortMode.Custom && settings.PlaylistOrder.Count == 0)
        {
            // Start the custom order from what is shown now.
            settings.PlaylistOrder = Playlists.Select(p => p.Id).ToList();
        }

        settings.PlaylistSort = mode.ToString();
        _services.SaveSettings();
        BuildPlaylistSortMenu();
        ShowPlaylists(_services.Library.Snapshot);
    }

    /// <summary>Dragging a playlist arranges the sidebar in the user's own order.</summary>
    private void OnPlaylistDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move)
        {
            return;
        }

        var settings = _services.Settings;
        settings.PlaylistOrder = Playlists.Select(p => p.Id).ToList();
        settings.PlaylistSort = PlaylistSortMode.Custom.ToString();
        _services.SaveSettings();
        BuildPlaylistSortMenu();

        _syncingSelection = true;
        try
        {
            PlaylistList.SelectedItem = Playlists.FirstOrDefault(p => p.Id == _currentKey);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async Task StartBackgroundWorkAsync()
    {
        var token = _lifetime.Token;

        // Let the first frame appear before doing anything else.
        await Task.Yield();

        // Follows Spotify; with "Spotify Web API only" it never listens to the Spotify app.
        await Task.Run(() => _services.Player.Spotify.StartAsync(token), token);

        // With "Spotify Web API only", Spotify's player on this PC (never in a timing or update run).
        if (StartupOptions.Current is { StartupBenchmarkFile: null, UpdateCheckFeed: null })
        {
            _services.AllowOwnPlayer();
        }

        // Starts the Spotify app hidden, or with "Spotify Web API only" closes
        // it, which can take a few seconds, so nothing waits for that.
        var spotifyApp = FollowSpotifyAppAsync(token);
        if (_services.UsesSpotifyApp)
        {
            await spotifyApp;
        }

        // Plugins that are on start in their helper, off the interface thread;
        // nothing is downloaded or started when none is on.
        _ = Task.Run(() => _services.Plugins.StartAsync(token), token);

        await RefreshLibraryAsync();
        _ = LoadLikesAsync(token);
        _ = KeepListeningHistoryAsync(token);

        // Updates last, quietly; an installed copy downloads them in the
        // background, then keeps looking while Resonate stays open. Never in
        // demo mode, which CI's checks use: an installed copy that downloaded a
        // real release would install it at its next start.
        if (_services.IsDemo)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(8), token);
        while (await Task.Run(() => _services.Updates.CheckAndDownloadAsync(token), token) != UpdateStatus.ReadyToRestart
            && _services.Updates.IsInstalled)
        {
            await Task.Delay(UpdateService.CheckInterval, token);
        }
    }

    /// <summary>
    /// Starts the Spotify app hidden when it is not running, or with "Spotify
    /// Web API only" closes it, and says so when that did not work.
    /// </summary>
    private async Task FollowSpotifyAppAsync(CancellationToken token)
    {
        try
        {
            var outcome = await Task.Run(() => _services.SpotifyApp.FollowAsync(token), token);
            if (outcome == SpotifyAppOutcome.NotInstalled)
            {
                ShowMessage(
                    "The Spotify app is not installed. Resonate plays music through it: install it from spotify.com/download or the Microsoft Store, sign in, then come back. (Or pick Spotify Web API only in Settings to play without it.)",
                    InfoBarSeverity.Error);
            }
            else if (outcome == SpotifyAppOutcome.CouldNotClose)
            {
                ShowMessage("The Spotify app did not close. Close it yourself if you want it gone.", InfoBarSeverity.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
    }

    private async Task RefreshLibraryAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => _services.Library.RefreshAsync(_lifetime.Token), _lifetime.Token);
            ShowPlaylists(snapshot);
        }
        catch (SpotifyAuthException)
        {
            // Handled by the SignedOut event.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowMessage(PlayerController.DescribeError(ex), InfoBarSeverity.Warning);
        }
    }

    /// <summary>Reads Liked Songs, so every heart is right (from the stored copy when it is current).</summary>
    private async Task LoadLikesAsync(CancellationToken token)
    {
        try
        {
            await Task.Run(() => _services.Likes.LoadAsync(token), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hearts fill in as songs are liked; nothing to tell the user.
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Saves new plays (and makes the day's mixes when the day changed) now
    /// and every half hour, and reads Liked Songs again if that failed before.
    /// </summary>
    private async Task KeepListeningHistoryAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                try
                {
                    await Task.Run(() => _services.Home.RefreshAsync(token), token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Offline, signed out or refused: tried again next round, and Home shows what is stored.
                }

                await Task.Delay(ListeningHistoryInterval, token);

                // Offline at start, say: the hearts still need Liked Songs.
                if (!_services.Likes.IsLoaded)
                {
                    await LoadLikesAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnUpdateProgressChanged(object? sender, EventArgs e)
    {
        // Reported on a background thread; draw the newest progress once.
        if (Interlocked.Exchange(ref _updateProgressQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateProgressQueued, 0);
                ShowUpdateProgress();
            });
        }
    }

    /// <summary>A new version downloading: a progress bar with the speed and the time left, in the corner.</summary>
    private void ShowUpdateProgress()
    {
        if (_services.Updates.PendingVersion is not null)
        {
            // Downloaded: the bar says so (ShowUpdateReady).
            return;
        }

        if (_services.Updates.Progress is not { } progress)
        {
            UpdateBar.IsOpen = false;
            UpdateProgressBar.IsIndeterminate = false;
            _updateBarDismissed = false;
            return;
        }

        UpdateBar.Title = progress.Title;
        UpdateBar.Message = string.Empty;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.ActionButton = null;
        UpdateBarProgress.Visibility = Visibility.Visible;
        // The endless "almost ready" animation only runs while it is on show.
        UpdateProgressBar.IsIndeterminate = progress.Preparing && !_updateBarDismissed;
        UpdateProgressBar.Value = progress.Fraction;
        UpdateProgressText.Text = progress.Describe();
        UpdateBar.IsOpen = !_updateBarDismissed;
    }

    private void ShowUpdateReady()
    {
        var restart = new Button { Content = "Restart now" };
        restart.Click += (_, _) => _services.Updates.RestartToUpdate();
        UpdateBar.Title = "Update ready";
        UpdateBar.Message = $"Resonate {_services.Updates.PendingVersion} is downloaded.";
        UpdateBar.Severity = InfoBarSeverity.Success;
        UpdateBar.ActionButton = restart;
        UpdateBarProgress.Visibility = Visibility.Collapsed;
        UpdateProgressBar.IsIndeterminate = false;
        UpdateBar.IsOpen = true;
    }

    /// <summary>Closing the download's bar hides it until the download is done; Settings still shows it.</summary>
    private void OnUpdateBarCloseClick(InfoBar sender, object args)
    {
        _updateBarDismissed = true;
        UpdateProgressBar.IsIndeterminate = false;
    }

    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space || ShellGrid.Visibility != Visibility.Visible)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        if (focused is TextBox or PasswordBox or AutoSuggestBox)
        {
            return;
        }

        e.Handled = true;
        _ = _services.Player.TogglePlayPauseAsync();
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusSearch();
    }

    private void OnNextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.NextAsync();
    }

    private void OnPreviousAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.PreviousAsync();
    }

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        GoBack();
    }

    private void OnShuffleAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.SetShuffleAsync(!_services.Player.State.Shuffle);
    }

    private void OnRepeatAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.SetRepeatAsync(NextRepeat(_services.Player.State.Repeat));
    }

    private void OnVolumeUpAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.SetVolumeAsync(Math.Min(1, _services.Player.State.Volume + 0.05));
    }

    private void OnVolumeDownAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = _services.Player.SetVolumeAsync(Math.Max(0, _services.Player.State.Volume - 0.05));
    }

    private void OnNewPlaylistAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ShellGrid.Visibility == Visibility.Visible)
        {
            _ = CreatePlaylistAsync();
        }
    }

    /// <summary>Off, then the whole list, then this song, then off again (as in Spotify).</summary>
    public static RepeatMode NextRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    /// <summary>The mouse's back button goes back.</summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed)
        {
            e.Handled = true;
            GoBack();
        }
    }

    private void OnFirstFrame(object? sender, object e)
    {
        if (_firstFrameSeen)
        {
            return;
        }

        _firstFrameSeen = true;
        CompositionTarget.Rendering -= OnFirstFrame;

        using var process = Process.GetCurrentProcess();
        var startup = DateTime.Now - process.StartTime;
        _services.Settings.LastStartupMilliseconds = Math.Round(startup.TotalMilliseconds);
        _services.SaveSettings();

        var options = StartupOptions.Current;
        if (options.StartupBenchmarkFile is { } file)
        {
            File.WriteAllText(file, ((int)startup.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Application.Current.Exit();
        }
        else if (options.ScreenshotFolder is { } folder)
        {
            _ = new ScreenshotTour(this, (FrameworkElement)Content, folder).RunAsync();
        }
        else if (options.PerformanceFolder is { } perf)
        {
            _ = new PerformanceTour(this, (FrameworkElement)Content, perf, startup.TotalMilliseconds).RunAsync();
        }
        else if (options is { UpdateCheckFeed: { } feed, UpdateCheckResultFile: { } result })
        {
            _ = CheckForUpdateAndQuitAsync(feed, result);
        }
        else if (options is { PluginCheckFeed: { } pluginFeed, PluginCheckResultFile: { } pluginResult })
        {
            _ = CheckPluginsAndQuitAsync(pluginFeed, pluginResult);
        }
        else if (options.WebPlayerCheckResultFile is { } webPlayerResult)
        {
            _ = CheckWebPlayerAndQuitAsync(webPlayerResult);
        }

        // Covers nobody has looked at for a while make room, once the window is up.
        if (_services.Covers.Store is { } covers)
        {
            _ = Task.Run(covers.Prune);
        }
    }

    private static async Task CheckPluginsAndQuitAsync(string feed, string resultFile)
    {
        try
        {
            var work = Path.Combine(Path.GetTempPath(), "resonate-plugin-check");
            var outcome = await Task.Run(() => PluginSelfTest.RunAsync(
                AppServices.LoadPluginCatalog(),
                feed,
                work,
                new ProcessPluginHostLauncher(),
                TimeSpan.FromMinutes(2)));
            await File.WriteAllTextAsync(resultFile, outcome);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultFile, $"Error {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    /// <summary>CI's check that Spotify's web player can run in the installed copy (see <see cref="WebPlayerPage.CheckAsync"/>).</summary>
    /// <remarks>
    /// Writes each step as a line as it happens (from any thread, so a stuck
    /// interface thread still shows), and the outcome as the last line.
    /// </remarks>
    private async Task CheckWebPlayerAndQuitAsync(string resultFile)
    {
        var clock = Stopwatch.StartNew();
        var gate = new Lock();
        var dispatcher = DispatcherQueue;
        File.Delete(resultFile);
        void Write(string line)
        {
            lock (gate)
            {
                try
                {
                    File.AppendAllText(resultFile, $"{clock.Elapsed.TotalSeconds:0.0} s: {line}{Environment.NewLine}");
                }
                catch (IOException)
                {
                    // A step less in the log.
                }
            }
        }

        string outcome;
        try
        {
            // Finishes off the interface thread, so the outcome is written even if that thread hangs.
            var work = Path.Combine(Path.GetTempPath(), "resonate-web-player-check");
            outcome = await WebPlayerPage.CheckAsync(dispatcher, work, TimeSpan.FromMinutes(2), Write).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome = $"Error {ex.GetType().Name}: {ex.Message}";
        }

        Write("closing Resonate");
        lock (gate)
        {
            try
            {
                File.AppendAllText(resultFile, outcome + Environment.NewLine);
            }
            catch (IOException)
            {
                // CI says the check wrote no outcome.
            }
        }

        dispatcher.TryEnqueue(() => Application.Current.Exit());
        await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        Write("Resonate did not end by itself; ending it");
        Environment.Exit(0);
    }

    /// <summary>Says once when Resonate's own player cannot play until the user does something.</summary>
    private void OnOwnPlayerStatusChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_services.OwnPlayer is not { } own || own.Status == _ownPlayerStatusShown)
        {
            return;
        }

        _ownPlayerStatusShown = own.Status;
        var text = own.Status switch
        {
            OwnPlayerStatus.NeedsSignIn => "To play music on this PC, sign in again: Settings, Sign out, then sign in.",
            OwnPlayerStatus.NeedsPremium => "Spotify only plays on this PC with Premium.",
            OwnPlayerStatus.Unsupported => "This PC cannot run Spotify's web player. It needs Microsoft's WebView2 Runtime (and on Windows N the Media Feature Pack). Or switch to Windows media controls in Settings.",
            _ => null,
        };
        if (text is not null)
        {
            ShowMessage(text, InfoBarSeverity.Warning);
        }
    });

    private static async Task CheckForUpdateAndQuitAsync(string feed, string resultFile)
    {
        try
        {
            var updates = new UpdateService(new Velopack.Sources.SimpleFileSource(new DirectoryInfo(feed)));
            var status = await Task.Run(() => updates.CheckAndDownloadAsync(CancellationToken.None));
            await File.WriteAllTextAsync(resultFile, $"{status} {updates.PendingVersion}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultFile, "Error " + ex.GetType().Name);
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    /// <summary>Closes Resonate from one of its own buttons (see <see cref="ReadyToClose"/>).</summary>
    public void Quit()
    {
        if (ReadyToClose())
        {
            Close();
        }
    }

    /// <summary>
    /// Before Resonate closes, Spotify's player on this PC says goodbye, so
    /// Spotify drops "Resonate" from its devices at once; the window hides
    /// meanwhile, and it waits 3 s at most.
    /// </summary>
    /// <returns>True when the window may close now; else it closes itself shortly.</returns>
    private bool ReadyToClose()
    {
        if (_quitting || _services.OwnPlayer is not { Status: OwnPlayerStatus.Ready or OwnPlayerStatus.Starting } own)
        {
            return true;
        }

        _quitting = true;
        AppWindow.Hide();
        var dispatcher = DispatcherQueue;
        _ = own.StopAsync().WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(
            _ => dispatcher.TryEnqueue(Close),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return false;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // Quitting from the mini player: it goes first, while the services it uses are still there.
        if (_miniPlayer is { } mini)
        {
            _miniPlayer = null;
            mini.CloseForGood();
        }

        _lifetime.Cancel();
        _services.SaveSettings();
        _services.Dispose();
    }

    private void ApplyCaptionButtonColors()
    {
        var palette = _services.Theme.Palette;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = palette.TextSecondary.ToColor();
        titleBar.ButtonInactiveForegroundColor = palette.TextTertiary.ToColor();
        titleBar.ButtonHoverBackgroundColor = palette.Hover.ToColor();
        titleBar.ButtonHoverForegroundColor = palette.TextPrimary.ToColor();
        titleBar.ButtonPressedBackgroundColor = palette.Pressed.ToColor();
        titleBar.ButtonPressedForegroundColor = palette.TextPrimary.ToColor();
    }

    /// <summary>
    /// The title bar drags the window, so clicks only reach its buttons (back,
    /// and Settings and the mini player on the right) through "passthrough"
    /// areas, kept in step with where the buttons are. The buttons on the right
    /// keep clear of the window's own buttons.
    /// </summary>
    private void UpdateTitleBarPassthrough()
    {
        if (AppTitleBar.XamlRoot is { RasterizationScale: > 0 } titleRoot)
        {
            var margin = new Thickness(0, 0, (AppWindow.TitleBar.RightInset / titleRoot.RasterizationScale) + 4, 0);
            if (!TitleBarButtons.Margin.Equals(margin))
            {
                TitleBarButtons.Margin = margin;

                // The buttons move once laid out; their areas follow then.
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdateTitleBarPassthrough);
            }
        }

        var rects = new List<RectInt32>(4);
        AddPassthrough(BackButton, rects);
        AddPassthrough(SettingsButton, rects);
        AddPassthrough(MiniPlayerButton, rects);

        // The window shapes button (Window shapes plugin), while it is there, left of Settings.
        if (_shapeButton is not null)
        {
            AddPassthrough(_shapeButton, rects);
        }

        var input = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        if (rects.Count == 0)
        {
            input.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        else
        {
            input.SetRegionRects(NonClientRegionKind.Passthrough, [.. rects]);
        }
    }

    private static void AddPassthrough(FrameworkElement button, List<RectInt32> rects)
    {
        if (button.Visibility != Visibility.Visible || button.ActualWidth == 0 || button.XamlRoot is not { } root)
        {
            return;
        }

        var scale = root.RasterizationScale;
        var box = button.TransformToVisual(null).TransformBounds(new global::Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
        rects.Add(new RectInt32((int)Math.Round(box.X * scale), (int)Math.Round(box.Y * scale), (int)Math.Round(box.Width * scale), (int)Math.Round(box.Height * scale)));
    }

    /// <summary>Sizes the window in device-independent pixels and centres it on its screen.</summary>
    private void PlaceWindow(int width, int height)
    {
        var scale = GetDpiForWindow(Hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = Math.Min((int)(width * scale), area.Width);
        var h = Math.Min((int)(height * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        NotePlacement(args);
        NoteScreen(args);
        var shown = sender.IsVisible && !IsIconic(Hwnd);
        if (shown != IsShown)
        {
            IsShown = shown;
            TellPlayersShown();
            ShownChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);
}
