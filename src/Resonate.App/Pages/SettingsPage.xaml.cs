using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

/// <summary>A part of Settings that other places can open Settings at.</summary>
public enum SettingsSection
{
    Equalizer,
    ClassicPlayer,
    Plugins,
}

/// <summary>The tabs along the top of Settings.</summary>
public enum SettingsTab
{
    Themes,
    Player,
    Layout,
    Plugins,
    Playback,
    About,
}

public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = App.Services;
    private SettingsSection? _pendingSection;
    private bool _animateSection;

    /// <summary>Room under the last setting for a hovering player that lies over the pane (0 for none).</summary>
    internal void SetPlayerRoom(double height) => ScrollContent.Padding = new Thickness(24, 8, 24, Math.Max(40, height));

    public SettingsPage()
    {
        InitializeComponent();
        PluginsHost.Children.Add(new PluginsPanel(_services.Plugins, _services));
        ShortcutsHost.Child = new KeyShortcutsList(_services);
        ShowTab(LastTab);
        Loaded += OnLoaded;

        // Only while on show, so the updater and the player never keep a closed page alive.
        Unloaded += (_, _) =>
        {
            _services.Updates.ProgressChanged -= OnUpdateProgressChanged;
            if (_services.OwnPlayer is { } own)
            {
                own.StatusChanged -= OnOwnPlayerStatusChanged;
            }
        };
    }

    private int _updateProgressQueued;

    private bool _loading;
    private bool _choosingTab;

    /// <summary>The tab Settings opens on: the one used last while Resonate runs.</summary>
    public static SettingsTab LastTab { get; private set; } = SettingsTab.Themes;

    /// <summary>The tab on show.</summary>
    public SettingsTab Tab { get; private set; } = SettingsTab.Themes;

    /// <summary>Opens the Themes tab at the theme customizer, opening it.</summary>
    internal void ShowCustomize()
    {
        ShowTab(SettingsTab.Themes);
        if (IsLoaded)
        {
            // After this turn's layout, so the tab's settings have their heights.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Studio.ShowCustomize);
        }
        else
        {
            Studio.ShowCustomize();
        }
    }

    /// <summary>Opens the Plugins tab.</summary>
    internal void ShowPlugins() => ShowTab(SettingsTab.Plugins);

    /// <summary>Opens the About tab, where the updates are.</summary>
    internal void ShowUpdates() => ShowTab(SettingsTab.About);

    /// <summary>Opens a section's tab and scrolls to the section, near the top (once the page is laid out, if it is still opening).</summary>
    internal void ShowSection(SettingsSection section, bool animate = false)
    {
        var tab = TabOf(section);
        var switching = tab != Tab;
        ShowTab(tab);
        if (section == SettingsSection.Plugins)
        {
            return;
        }

        _pendingSection = section;

        // A tab that just came into view has nothing to glide from.
        _animateSection = animate && !switching;
        if (IsLoaded)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, BringSectionIntoView);
        }
    }

    private static SettingsTab TabOf(SettingsSection section) => section switch
    {
        SettingsSection.Equalizer => SettingsTab.Playback,
        SettingsSection.Plugins => SettingsTab.Plugins,
        _ => SettingsTab.Player,
    };

    /// <summary>Shows one tab's settings, from the top, and lights its tab.</summary>
    public void ShowTab(SettingsTab tab)
    {
        var changed = tab != Tab;
        Tab = tab;
        LastTab = tab;
        Studio.Visibility = tab == SettingsTab.Themes ? Visibility.Visible : Visibility.Collapsed;
        PlayerPanel.Visibility = tab == SettingsTab.Player ? Visibility.Visible : Visibility.Collapsed;
        LayoutPanel.Visibility = tab == SettingsTab.Layout ? Visibility.Visible : Visibility.Collapsed;
        PluginsHost.Visibility = tab == SettingsTab.Plugins ? Visibility.Visible : Visibility.Collapsed;
        PlaybackPanel.Visibility = tab == SettingsTab.Playback ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tab == SettingsTab.About ? Visibility.Visible : Visibility.Collapsed;

        _choosingTab = true;
        try
        {
            ThemesTab.IsChecked = tab == SettingsTab.Themes;
            PlayerTab.IsChecked = tab == SettingsTab.Player;
            LayoutTab.IsChecked = tab == SettingsTab.Layout;
            PluginsTab.IsChecked = tab == SettingsTab.Plugins;
            PlaybackTab.IsChecked = tab == SettingsTab.Playback;
            AboutTab.IsChecked = tab == SettingsTab.About;
        }
        finally
        {
            _choosingTab = false;
        }

        if (changed)
        {
            Scroller.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_choosingTab)
        {
            return;
        }

        SettingsTab? tab = ReferenceEquals(sender, ThemesTab) ? SettingsTab.Themes
            : ReferenceEquals(sender, PlayerTab) ? SettingsTab.Player
            : ReferenceEquals(sender, LayoutTab) ? SettingsTab.Layout
            : ReferenceEquals(sender, PluginsTab) ? SettingsTab.Plugins
            : ReferenceEquals(sender, PlaybackTab) ? SettingsTab.Playback
            : ReferenceEquals(sender, AboutTab) ? SettingsTab.About
            : null;
        if (tab is { } chosen)
        {
            ShowTab(chosen);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can come twice in a row; each handler is held once.
        _services.Updates.ProgressChanged -= OnUpdateProgressChanged;
        _services.Updates.ProgressChanged += OnUpdateProgressChanged;
        ShowUpdateProgress();
        if (_services.OwnPlayer is { } own)
        {
            own.StatusChanged -= OnOwnPlayerStatusChanged;
            own.StatusChanged += OnOwnPlayerStatusChanged;
        }

        ShowOwnPlayerStatus();

        if (_pendingSection is not null)
        {
            // After this turn's layout, so the sections above have their heights.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, BringSectionIntoView);
        }
    }

    private void BringSectionIntoView()
    {
        if (_pendingSection is not { } section)
        {
            return;
        }

        _pendingSection = null;
        if (TabOf(section) != Tab)
        {
            // Another tab was chosen meanwhile.
            return;
        }

        FrameworkElement target;
        if (section == SettingsSection.Equalizer)
        {
            EqualizerSection.Open();
            target = EqualizerSection;
        }
        else
        {
            ClassicPlayerSection.Open();
            target = ClassicPlayerSection;
        }

        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _animateSection, VerticalAlignmentRatio = 0.1 });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        ChannelChoice.SelectedIndex = _services.Player.Spotify.Channel == ControlChannel.WebApi ? 1 : 0;
        KeepHiddenSwitch.IsOn = _services.SpotifyWindow.KeepHidden;
        SaveResourcesSwitch.IsOn = _services.SpotifyWindow.SaveResources;
        PlayHereSwitch.IsOn = _services.Settings.WebApiPlayHere;
        PlayHereSwitch.IsEnabled = _services.OwnPlayer is not null;
        ShowChannelOptions();
        AutoUpdateSwitch.IsOn = _services.Settings.AutoUpdate;
        AutoUpdateSwitch.IsEnabled = _services.Updates.IsInstalled;
        StartWithWindowsSwitch.IsOn = _services.Settings.StartWithWindows;
        _loading = false;

        var user = _services.Library.Snapshot?.User;
        AccountRow.Header = _services.IsDemo
            ? "Demo mode"
            : user is null
                ? "Signed in"
                : $"Signed in as {user.DisplayName ?? user.Id}";
        AccountRow.Description = _services.IsDemo ? "Made-up music, nothing is sent to Spotify." : string.Empty;
        SignOutButton.IsEnabled = !_services.IsDemo;

        VersionRow.Header = $"Resonate {AppInfo.Version}";
        VersionRow.Description = _services.Updates.IsInstalled ? string.Empty : "Not installed, so it does not update itself.";
        CheckUpdatesButton.IsEnabled = _services.Updates.IsInstalled;
        RestartButton.Visibility = _services.Updates.PendingVersion is null ? Visibility.Collapsed : Visibility.Visible;

        StartupText.Text = _services.Settings.LastStartupMilliseconds is { } ms
            ? $"Last start: {ms.ToString("N0", CultureInfo.CurrentCulture)} ms"
            : string.Empty;
    }

    private void OnChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ChannelChoice.SelectedIndex < 0)
        {
            return;
        }

        _services.SetControlChannel(ChannelChoice.SelectedIndex == 1 ? ControlChannel.WebApi : ControlChannel.Local);
        ShowChannelOptions();
    }

    /// <summary>The Spotify app's options only while Resonate works with it; with Web API only, what that mode means.</summary>
    private void ShowChannelOptions()
    {
        var usesApp = _services.UsesSpotifyApp;
        SpotifyAppOptions.Visibility = usesApp ? Visibility.Visible : Visibility.Collapsed;
        WebApiOptions.Visibility = usesApp ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPlayHereToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.SetWebApiPlayHere(PlayHereSwitch.IsOn);
        ShowOwnPlayerStatus();
    }

    private void OnOwnPlayerStatusChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ShowOwnPlayerStatus);

    /// <summary>How Resonate's own player is doing, under "Play on this PC".</summary>
    private void ShowOwnPlayerStatus()
    {
        var text = _services.OwnPlayer is not { } own ? "Demo mode: nothing plays here."
            : _services.OwnPlayerHeld && PlayHereSwitch.IsOn ? "Paused after Resonate closed unexpectedly. Switch it off and on to try again."
            : AppServices.DescribeOwnPlayer(own.Status);
        PlayHereStatus.Text = text ?? string.Empty;
        PlayHereStatus.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnKeepHiddenToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.SpotifyWindow.KeepHidden = KeepHiddenSwitch.IsOn;
        _services.Settings.KeepSpotifyHidden = KeepHiddenSwitch.IsOn;
        _services.SaveSettings();
    }

    private void OnSaveResourcesToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.SpotifyWindow.SaveResources = SaveResourcesSwitch.IsOn;
        _services.Settings.SaveSpotifyResources = SaveResourcesSwitch.IsOn;
        _services.SaveSettings();
    }

    private void OnShowSpotifyClick(object sender, RoutedEventArgs e)
    {
        if (!_services.SpotifyWindow.ShowSpotify())
        {
            App.MainWindow?.ShowMessage(
                "The Spotify app could not be opened. Install it from spotify.com/download or the Microsoft Store, sign in, then come back.",
                InfoBarSeverity.Error);
        }
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        _services.Account.SignOut();

        // The next account to sign in must not see this one's playlists, songs or listening.
        _services.Library.Forget();
        _services.Likes.Forget();
        _services.Home.Forget();
        if (_services.Covers.Store is { } covers)
        {
            _ = Task.Run(covers.Clear);
        }

        App.MainWindow?.ShowSignIn();
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";
        UpdateStatus status;
        try
        {
            // Joins the background check if one is already downloading.
            status = await Task.Run(() => _services.Updates.CheckAndDownloadAsync(CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
            // Resonate is closing.
            return;
        }

        UpdateStatusText.Text = status switch
        {
            UpdateStatus.UpToDate => "Resonate is up to date.",
            UpdateStatus.ReadyToRestart => $"Resonate {_services.Updates.PendingVersion} is ready.",
            UpdateStatus.Failed => "Could not reach GitHub. Try again later.",
            _ => string.Empty,
        };
        RestartButton.Visibility = status == UpdateStatus.ReadyToRestart ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = true;
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => _services.Updates.RestartToUpdate();

    private void OnAutoUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.Settings.AutoUpdate = AutoUpdateSwitch.IsOn;
        _services.SaveSettings();
        App.MainWindow?.KeepUpdating();
    }

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            App.MainWindow?.SetStartWithWindows(StartWithWindowsSwitch.IsOn);
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

    /// <summary>A new version downloading: a progress bar, the speed and the time left.</summary>
    private void ShowUpdateProgress()
    {
        if (_services.Updates.PendingVersion is not null)
        {
            RestartButton.Visibility = Visibility.Visible;
        }

        if (_services.Updates.Progress is not { } progress)
        {
            // Stops the endless "almost ready" animation too.
            UpdateProgressBar.IsIndeterminate = false;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateStatusText.Text = string.Empty;
        UpdateProgressBar.IsIndeterminate = progress.Preparing;
        UpdateProgressBar.Value = progress.Fraction;
        UpdateProgressText.Text = progress.Preparing ? $"{progress.Title}…" : $"{progress.Title}: {progress.Describe()}";
    }
}
