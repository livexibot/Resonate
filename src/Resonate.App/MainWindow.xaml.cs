using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Resonate.App.Pages;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.App.ViewModels;
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

    private readonly AppServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _messageTimer;
    private readonly List<string> _history = [];
    private string? _currentPage;
    private bool _syncingSelection;
    private bool _backgroundStarted;
    private bool _firstFrameSeen;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "Resonate";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Resonate.ico"));
        PlaceWindow(1280, 820);

        services.Theme.AttachRoot(RootGrid);
        ApplyCaptionButtonColors();

        _messageTimer = DispatcherQueue.CreateTimer();
        _messageTimer.Interval = TimeSpan.FromSeconds(7);
        _messageTimer.IsRepeating = false;
        _messageTimer.Tick += (_, _) => MessageBar.IsOpen = false;

        PlayerBar.Attach(services.Player);
        services.Player.ErrorOccurred += (_, message) =>
            DispatcherQueue.TryEnqueue(() => ShowMessage(message, InfoBarSeverity.Warning));
        services.Library.PlaylistsChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ShowPlaylists(_services.Library.Snapshot));
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);
        BuildPlaylistSortMenu();
        services.Account.SignedOut += (_, _) => DispatcherQueue.TryEnqueue(ShowSignIn);
        services.Updates.UpdateReady += (_, _) => DispatcherQueue.TryEnqueue(ShowUpdateReady);
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
    }

    public ObservableCollection<NavItem> NavItems { get; } =
    [
        new(HomeKey, "\uE80F", "Home"),
        new(SearchKey, "\uE721", "Search"),
        new(LikedSongsKey, "\uEB52", "Liked Songs"),
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
        PlayerBar.Visibility = Visibility.Visible;

        ShowPlaylists(_services.Library.Snapshot);
        _history.Clear();
        _currentPage = null;
        Open(HomeKey);

        if (!_backgroundStarted)
        {
            _backgroundStarted = true;
            _ = StartBackgroundWorkAsync();
        }
        else
        {
            _ = RefreshLibraryAsync();
        }
    }

    public void ShowSignIn()
    {
        ShellGrid.Visibility = Visibility.Collapsed;
        PlayerBar.Visibility = Visibility.Collapsed;
        SignInFrame.Visibility = Visibility.Visible;
        SignInFrame.Navigate(typeof(SignInPage), null, new SuppressNavigationTransitionInfo());
        _currentPage = null;
    }

    /// <summary>
    /// Opens a page by its key: "home", "search", "liked", "local", "dj",
    /// "settings", "album:&lt;id&gt;", "artist:&lt;id&gt;", or a playlist ID.
    /// Its sidebar entry is selected when it has one.
    /// </summary>
    public void Open(string key)
    {
        SelectNav(key);
        Navigate(key, remember: true);
    }

    public void OpenPlaylist(string playlistId) => Open(playlistId);

    public void OpenSettings() => Open(SettingsKey);

    public void OpenSearch() => Open(SearchKey);

    /// <summary>Goes back to the page before, like a browser's Back button.</summary>
    public void GoBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var key = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        SelectNav(key);
        Navigate(key, remember: false);
    }

    /// <summary>A list was played; remembered for the "Recently played" playlist order.</summary>
    public void NoteListPlayed(string key)
    {
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
        if (_currentPage == key)
        {
            return;
        }

        if (remember && _currentPage is not null)
        {
            _history.Add(_currentPage);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }

        _currentPage = key;
        BackButton.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
            case SettingsKey:
                ContentFrame.Navigate(typeof(SettingsPage), null, transition);
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

    private void OnSettingsClick(object sender, RoutedEventArgs e) => Open(SettingsKey);

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
            return;
        }

        var settings = _services.Settings;
        var sorted = PlaylistSorter.Apply(snapshot.Playlists.ToList(), settings.ParsedPlaylistSort, settings.PlaylistOrder, settings.PlaylistLastPlayed);
        var selected = _currentPage;
        _syncingSelection = true;
        try
        {
            Playlists.Clear();
            foreach (var playlist in sorted)
            {
                Playlists.Add(new PlaylistNavItem(playlist));
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
            PlaylistList.SelectedItem = Playlists.FirstOrDefault(p => p.Id == _currentPage);
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

        try
        {
            await Task.Run(() => _services.Player.Spotify.StartAsync(token), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowMessage("Resonate could not reach Windows' media controls; playback goes through Spotify's servers instead.", InfoBarSeverity.Informational);
        }

        var spotify = await Task.Run(() => _services.Launcher.EnsureRunningAsync(token), token);
        if (spotify == SpotifyAppStatus.NotInstalled)
        {
            ShowMessage(
                "The Spotify app is not installed. Resonate plays music through it: install it from spotify.com/download or the Microsoft Store, sign in, then come back.",
                InfoBarSeverity.Error);
        }

        await RefreshLibraryAsync();
        _ = LoadLikesAsync(token);

        // Updates last, quietly; an installed copy downloads them in the
        // background, then keeps looking while Resonate stays open.
        await Task.Delay(TimeSpan.FromSeconds(8), token);
        while (await Task.Run(() => _services.Updates.CheckAndDownloadAsync(token), token) != UpdateStatus.ReadyToRestart
            && _services.Updates.IsInstalled)
        {
            await Task.Delay(UpdateService.CheckInterval, token);
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

    /// <summary>Reads Liked Songs once, so every heart is right (from the stored copy when it is current).</summary>
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

    private void ShowUpdateReady()
    {
        var restart = new Button { Content = "Restart now" };
        restart.Click += (_, _) => _services.Updates.RestartToUpdate();
        MessageBar.Title = "Update ready";
        MessageBar.Message = $"Resonate {_services.Updates.PendingVersion} is downloaded. Restart to start using it.";
        MessageBar.Severity = InfoBarSeverity.Success;
        MessageBar.ActionButton = restart;
        MessageBar.IsClosable = true;
        MessageBar.IsOpen = true;
        _messageTimer.Stop();
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
        if (ShellGrid.Visibility == Visibility.Visible)
        {
            OpenSearch();
            SearchPage.FocusSearchBox(ContentFrame);
        }
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
            _ = new ScreenshotTour(this, RootGrid, folder).RunAsync();
        }
        else if (options is { UpdateCheckFeed: { } feed, UpdateCheckResultFile: { } result })
        {
            _ = CheckForUpdateAndQuitAsync(feed, result);
        }
    }

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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _lifetime.Cancel();
        _services.SaveSettings();
        _services.Dispose();
    }

    internal void ApplyCaptionButtonColors()
    {
        var theme = _services.Theme.Current;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = theme.TextSecondary;
        titleBar.ButtonInactiveForegroundColor = theme.TextTertiary;
        titleBar.ButtonHoverBackgroundColor = theme.SurfaceHover;
        titleBar.ButtonHoverForegroundColor = theme.TextPrimary;
        titleBar.ButtonPressedBackgroundColor = theme.SurfacePressed;
        titleBar.ButtonPressedForegroundColor = theme.TextPrimary;
    }

    /// <summary>Sizes the window in device-independent pixels and centres it on its screen.</summary>
    private void PlaceWindow(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = Math.Min((int)(width * scale), area.Width);
        var h = Math.Min((int)(height * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h));
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
