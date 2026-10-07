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
using Resonate.App.Controls;
using Resonate.App.Pages;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.App.ViewModels;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Windows.Graphics;
using Launcher = Windows.System.Launcher;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App;

public sealed partial class MainWindow : Window
{
    public const string LikedSongsKey = "liked";
    public const string SearchKey = "search";
    public const string SettingsKey = "settings";

    private readonly AppServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueueTimer _messageTimer;
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

        // The look's backdrop goes behind the content, and switching looks animates above it.
        var content = RootGrid;
        Content = null;
        var themeHost = new ThemeHost(content, services);
        Content = themeHost;
        services.Theme.AttachWindow(this, themeHost, themeHost.Scene, themeHost.Overlay);
        services.Theme.Changed += (_, _) => ApplyCaptionButtonColors();
        ApplyCaptionButtonColors();

        _messageTimer = DispatcherQueue.CreateTimer();
        _messageTimer.Interval = TimeSpan.FromSeconds(7);
        _messageTimer.IsRepeating = false;
        _messageTimer.Tick += (_, _) => MessageBar.IsOpen = false;

        PlayerBar.Attach(services.Player);
        services.Player.ErrorOccurred += (_, message) =>
            DispatcherQueue.TryEnqueue(() => ShowMessage(message, InfoBarSeverity.Warning));
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
        new(SearchKey, "", "Search"),
        new(LikedSongsKey, "", "Liked Songs"),
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
        SelectNav(LikedSongsKey);
        Navigate(LikedSongsKey);

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

    /// <summary>Opens a playlist page, selecting it in the sidebar when it is there.</summary>
    public void OpenPlaylist(string playlistId)
    {
        SelectNav(playlistId);
        Navigate(playlistId);
    }

    public void OpenSettings()
    {
        SelectNav(SettingsKey);
        Navigate(SettingsKey);
    }

    /// <summary>The page on show, such as a <see cref="SettingsPage"/>.</summary>
    internal object? CurrentPage => ContentFrame.Content;

    public void OpenSearch()
    {
        SelectNav(SearchKey);
        Navigate(SearchKey);
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

    private void Navigate(string key)
    {
        if (_currentPage == key)
        {
            return;
        }

        _currentPage = key;
        switch (key)
        {
            case SearchKey:
                ContentFrame.Navigate(typeof(SearchPage), null, new EntranceNavigationTransitionInfo());
                break;
            case SettingsKey:
                ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
                break;
            default:
                ContentFrame.Navigate(typeof(PlaylistPage), key, new DrillInNavigationTransitionInfo());
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

        SelectNav(item.Key);
        Navigate(item.Key);
    }

    private void OnPlaylistSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || PlaylistList.SelectedItem is not PlaylistNavItem item)
        {
            return;
        }

        SelectNav(item.Id);
        Navigate(item.Id);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        SelectNav(SettingsKey);
        Navigate(SettingsKey);
    }

    private void ShowPlaylists(LibrarySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            // Nothing known yet (first start, or just signed out).
            Playlists.Clear();
            return;
        }

        var selected = _currentPage;
        _syncingSelection = true;
        try
        {
            Playlists.Clear();
            foreach (var playlist in snapshot.Playlists)
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

    private async Task StartBackgroundWorkAsync()
    {
        var token = _lifetime.Token;

        // Let the first frame appear before doing anything else.
        await Task.Yield();

        try
        {
            await Task.Run(() => _services.Player.StartAsync(token), token);
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
