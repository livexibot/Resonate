using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Services;
using Resonate.App.Themes;
using Resonate.Spotify.Playback;

namespace Resonate.App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = App.Services;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private bool _loading;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        BuildThemeChoices();

        _loading = true;
        ChannelChoice.SelectedIndex = _services.Player.Spotify.Channel == ControlChannel.WebApi ? 1 : 0;
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

    private void BuildThemeChoices()
    {
        ThemeChoices.Children.Clear();
        foreach (var preset in ThemePreset.All)
        {
            var swatch = new Grid
            {
                Width = 120,
                Height = 72,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(preset.Background),
                BorderBrush = new SolidColorBrush(preset.Border),
                BorderThickness = new Thickness(1),
            };
            swatch.Children.Add(new Border
            {
                Width = 36,
                Height = 10,
                Margin = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(preset.Accent),
            });
            swatch.Children.Add(new TextBlock
            {
                Text = preset.Name,
                Margin = new Thickness(12, 10, 12, 0),
                FontSize = 13,
                Foreground = new SolidColorBrush(preset.TextPrimary),
            });

            var button = new Button
            {
                Content = swatch,
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(2),
                BorderBrush = preset.Id == _services.Theme.Current.Id
                    ? _services.Theme.GetBrush("ResonateAccentBrush")
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = preset.Id,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, preset.Name + " theme");
            button.Click += OnThemeClick;
            ThemeChoices.Children.Add(button);
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            var preset = ThemePreset.ById(id);
            _services.Theme.Apply(preset);
            App.MainWindow?.ApplyCaptionButtonColors();
            _services.Settings.ThemeId = preset.Id;
            _services.SaveSettings();
            BuildThemeChoices();
        }
    }

    private void OnChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ChannelChoice.SelectedIndex < 0)
        {
            return;
        }

        var channel = ChannelChoice.SelectedIndex == 1 ? ControlChannel.WebApi : ControlChannel.Local;
        _services.Player.Spotify.Channel = channel;
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

        // The next account to sign in must not see this one's playlists, songs or listening.
        _services.Library.Forget();
        _services.Home.Forget();
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
