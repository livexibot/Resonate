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
}

public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = App.Services;
    private SettingsSection? _pendingSection;
    private bool _animateSection;

    public SettingsPage()
    {
        InitializeComponent();
        PluginsHost.Children.Add(new PluginsPanel(_services.Plugins));
        Loaded += OnLoaded;
    }

    private bool _loading;

    /// <summary>Scrolls to the theme customizer, opening it.</summary>
    internal void ShowCustomize() => Studio.ShowCustomize();

    /// <summary>Scrolls to the plugins.</summary>
    internal void ShowPlugins() =>
        PluginsHost.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });

    /// <summary>Scrolls to a section, near the top of the page (once the page is laid out, if it is still opening).</summary>
    internal void ShowSection(SettingsSection section, bool animate = false)
    {
        _pendingSection = section;
        _animateSection = animate;
        if (IsLoaded)
        {
            BringSectionIntoView();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        FrameworkElement target = section switch
        {
            SettingsSection.Equalizer => EqualizerSection,
            _ => ClassicPlayerSection,
        };
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _animateSection, VerticalAlignmentRatio = 0.1 });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        ChannelChoice.SelectedIndex = _services.Player.Spotify.Channel == ControlChannel.WebApi ? 1 : 0;
        KeepHiddenSwitch.IsOn = _services.SpotifyWindow.KeepHidden;
        SaveResourcesSwitch.IsOn = _services.SpotifyWindow.SaveResources;
        ShowChannelOptions();
        _loading = false;

        var user = _services.Library.Snapshot?.User;
        AccountText.Text = _services.IsDemo
            ? "Demo mode: made-up music, nothing is sent to Spotify."
            : user is null
                ? "Signed in."
                : $"Signed in as {user.DisplayName ?? user.Id}, through your Spotify app {_services.Settings.ClientId}.";
        SignOutButton.IsEnabled = !_services.IsDemo;

        VersionText.Text = _services.Updates.IsInstalled
            ? $"Resonate {AppInfo.Version}. New versions download in the background; you choose when to restart."
            : $"Resonate {AppInfo.Version}. This copy was not installed with the installer, so it does not update itself.";
        CheckUpdatesButton.IsEnabled = _services.Updates.IsInstalled;
        RestartButton.Visibility = _services.Updates.PendingVersion is null ? Visibility.Collapsed : Visibility.Visible;

        StartupText.Text = _services.Settings.LastStartupMilliseconds is { } ms
            ? $"Last start: {ms.ToString("N0", CultureInfo.CurrentCulture)} ms from launch to the first frame."
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
        WebApiOnlyNote.Visibility = usesApp ? Visibility.Collapsed : Visibility.Visible;
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
        var status = await Task.Run(() => _services.Updates.CheckAndDownloadAsync(CancellationToken.None));
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
}
