using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Controls;
using Resonate.App.Services;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = App.Services;

    public SettingsPage()
    {
        InitializeComponent();
        PluginsHost.Children.Add(new PluginsPanel(_services.Plugins));
    }

    private bool _loading;

    /// <summary>Scrolls to the theme customizer, opening it.</summary>
    internal void ShowCustomize() => Studio.ShowCustomize();

    /// <summary>Scrolls to the plugins.</summary>
    internal void ShowPlugins() =>
        PluginsHost.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _loading = true;
        ChannelChoice.SelectedIndex = _services.Player.Channel == ControlChannel.WebApi ? 1 : 0;
        KeepHiddenSwitch.IsOn = _services.SpotifyWindow.KeepHidden;
        SaveResourcesSwitch.IsOn = _services.SpotifyWindow.SaveResources;
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

        var channel = ChannelChoice.SelectedIndex == 1 ? ControlChannel.WebApi : ControlChannel.Local;
        _services.Player.Channel = channel;
        _services.Settings.ControlChannel = channel == ControlChannel.WebApi ? "webapi" : "local";
        _services.SaveSettings();
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

        // The next account to sign in must not see this one's playlists.
        _services.Library.Forget();
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
